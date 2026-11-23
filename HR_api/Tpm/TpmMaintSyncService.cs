using System.Text.Json;
using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Tpm;

/// <summary>Giữ bảng HRMS.HR_TPM_MAINT (danh sách công nhân bảo trì) khớp với TPM: mỗi ngày (mặc định 1440 phút) hỏi TPM danh sách thẻ bảo trì đang
/// hoạt động (GET api/mysamho/maintenance/members) rồi thêm thẻ mới / bỏ thẻ không còn. TPM thêm hoặc khóa bảo trì → My Samho tự theo,
/// không ai phải sửa tay. An toàn: TPM lỗi/chưa có endpoint/trả rỗng → KHÔNG đụng bảng; sẽ xóa quá nửa danh sách một lần → bỏ qua và ghi log
/// (tránh mất quyền cả loạt vì lỗi bên TPM). Chạy trong tiến trình HR_api (IIS Application Pool AlwaysRunning, giống các service nền khác).</summary>
public class TpmMaintSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TpmMaintSyncService> _log;

    public TpmMaintSyncService(IServiceScopeFactory scopeFactory, IConfiguration cfg, ILogger<TpmMaintSyncService> log)
    { _scopeFactory = scopeFactory; _cfg = cfg; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var minutes = int.TryParse(_cfg["Tpm:MaintSyncMinutes"], out var m) && m > 0 ? m : 1440;
        try { await Task.Delay(TimeSpan.FromSeconds(45), stop); } catch (TaskCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await SyncOnceAsync(scope.ServiceProvider.GetRequiredService<TpmService>(), scope.ServiceProvider.GetRequiredService<OracleService>());
            }
            catch (Exception ex) { _log.LogError(ex, "TpmMaintSync failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task SyncOnceAsync(TpmService tpm, OracleService db)
    {
        if (!tpm.IsConfigured) return;

        var (reached, status, body) = await tpm.CallAsync(HttpMethod.Get, "/api/mysamho/maintenance/members");
        if (!reached || status != 200 || string.IsNullOrWhiteSpace(body)) return;   // TPM chưa có endpoint / đang lỗi → giữ nguyên

        var remote = ParseEmpCds(body);
        if (remote.Count == 0) { _log.LogWarning("TpmMaintSync: TPM trả danh sách rỗng, bỏ qua"); return; }

        List<string> local;
        try { local = await db.ExecuteQueryAsync("SELECT EMP_CD FROM HRMS.HR_TPM_MAINT", r => r["EMP_CD"]?.ToString() ?? ""); }
        catch { return; }   // bảng chưa tạo
        var localSet = new HashSet<string>(local, StringComparer.OrdinalIgnoreCase);

        var toAdd = remote.Where(c => !localSet.Contains(c)).ToList();
        var toRemove = local.Where(c => !remote.Contains(c)).ToList();

        if (toRemove.Count > Math.Max(5, local.Count / 2))
        {
            _log.LogWarning("TpmMaintSync: sẽ xóa {n}/{t} thẻ — nghi dữ liệu TPM bất thường, bỏ qua lần này", toRemove.Count, local.Count);
            toRemove.Clear();
        }

        foreach (var c in toAdd)
            await db.ExecuteNonQueryAsync("INSERT INTO HRMS.HR_TPM_MAINT (EMP_CD) VALUES (:E)", new OracleParameter("E", c));
        foreach (var c in toRemove)
            await db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TPM_MAINT WHERE EMP_CD = :E", new OracleParameter("E", c));

        if (toAdd.Count + toRemove.Count > 0)
            _log.LogInformation("TpmMaintSync: +{a} / -{r} (tổng {t})", toAdd.Count, toRemove.Count, local.Count + toAdd.Count - toRemove.Count);
    }

    /// <summary>Chấp nhận {"items":[ "0123", ...]} hoặc {"items":[{"EmpCd":"0123"}, ...]} (PascalCase/camelCase), hoặc mảng trần.</summary>
    private static HashSet<string> ParseEmpCds(string json)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement arr = root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Array)) return set;
                arr = root.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value;
            }
            foreach (var e in arr.EnumerateArray())
            {
                string? c = e.ValueKind == JsonValueKind.String ? e.GetString()
                    : e.ValueKind == JsonValueKind.Object
                        ? e.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "EmpCd", StringComparison.OrdinalIgnoreCase)).Value.GetString()
                        : null;
                if (!string.IsNullOrWhiteSpace(c) && c.Length <= 20) set.Add(c.Trim());
            }
        }
        catch { /* JSON lạ → trả rỗng, caller bỏ qua */ }
        return set;
    }
}
