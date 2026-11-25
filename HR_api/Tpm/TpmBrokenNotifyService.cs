using System.Text.Json;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Notification;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Tpm;

/// <summary>Báo phiếu BÁO HƯ MỚI cho công nhân bảo trì ĐÚNG KHU VỰC qua thông báo My Samho (chuông + push Firebase).
/// Hai đường kích hoạt:
///  1) TPM gọi webhook POST apiHR/Tpm/notify/broken {BrokenId} ngay khi có phiếu mới → báo tức thì, không phải hỏi liên tục.
///  2) Lưới an toàn: HR_api hỏi TPM thưa (Tpm:BrokenNotify:PollSeconds, mặc định 300s) để bắt phiếu webhook bị lỡ và để báo "bảo trì chung" khi
///     phiếu chưa ai nhận sau EscalateMinutes.
/// So vị trí máy với vùng phụ trách của từng thẻ bảo trì (TPM maintenance/areas, nhớ 6 giờ).
///
/// Quy tắc:
///  - Có người phụ trách đúng khu vực (tên Nhà/Group trùng một đoạn của LocationPath): báo NGAY cho họ.
///  - "Bảo trì chung" (không gắn khu vực) chỉ được báo khi (a) máy không thuộc khu vực của ai, hoặc (b) phiếu vẫn chưa ai nhận sau
///    EscalateMinutes (mặc định 15) — tránh 100 người cùng bị dội mỗi phiếu.
///  - Chỉ xét phiếu báo trong MaxAgeMinutes (mặc định 120) gần đây → bật tính năng lần đầu/khởi động lại không dội phiếu cũ.
///  - Không gửi trùng: dò HR_NOTIFICATIONS (LINK_ACTION='TPM_BROKEN:{id}' + người nhận) — không cần bảng mới.
/// Mặc định TẮT (Tpm:BrokenNotify:Enabled=false). Bật + OnlyEmpCds=[...] để chạy thử cho vài người trước. Chạy trong tiến trình HR_api
/// (IIS Application Pool AlwaysRunning).</summary>
public class TpmBrokenNotifyService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<TpmBrokenNotifyService> _log;

    private sealed record Member(string EmpCd, HashSet<string> Areas, bool General);
    private List<Member> _members = new();
    private DateTime _membersAt = DateTime.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);   // webhook + vòng hỏi thưa không chạy chồng → không gửi trùng

    public TpmBrokenNotifyService(IServiceScopeFactory scopeFactory, IConfiguration cfg, ILogger<TpmBrokenNotifyService> log)
    { _scopeFactory = scopeFactory; _cfg = cfg; _log = log; }

    private int Int(string key, int def) => int.TryParse(_cfg[$"Tpm:BrokenNotify:{key}"], out var v) && v > 0 ? v : def;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!string.Equals(_cfg["Tpm:BrokenNotify:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInformation("TpmBrokenNotify: tắt (Tpm:BrokenNotify:Enabled != true)");
            return;
        }
        _log.LogInformation("TpmBrokenNotify started");
        try { await Task.Delay(TimeSpan.FromSeconds(60), stop); } catch (TaskCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await TickAsync(scope.ServiceProvider);
            }
            catch (Exception ex) { _log.LogError(ex, "TpmBrokenNotify tick failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(Int("PollSeconds", 300)), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private sealed record Ticket(long BrokenId, string Status, DateTime ReportedAt, string Code, string Name, string Path, string Desc);

    private bool Enabled => string.Equals(_cfg["Tpm:BrokenNotify:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Webhook từ TPM: có phiếu báo hư mới → báo ngay (chỉ cần BrokenId; chi tiết lấy lại từ TPM, không tin dữ liệu gửi kèm).</summary>
    public async Task NotifyBrokenAsync(long brokenId)
    {
        if (!Enabled) return;
        using var scope = _scopeFactory.CreateScope();
        await RunAsync(scope.ServiceProvider, brokenId);
    }

    private async Task TickAsync(IServiceProvider sp) => await RunAsync(sp, null);

    private async Task RunAsync(IServiceProvider sp, long? onlyId)
    {
        await _gate.WaitAsync();
        try
        {
            var tpm = sp.GetRequiredService<TpmService>();
            var db = sp.GetRequiredService<OracleService>();
            if (!tpm.IsConfigured) return;

            await RefreshMembersAsync(tpm, db);
            if (_members.Count == 0) return;

            // 1 người bất kỳ trong danh sách để hỏi toàn bộ phiếu (myAreaOnly=false)
            var (reached, status, body) = await tpm.CallAsync(HttpMethod.Get,
                $"/api/mysamho/maintenance/broken?empCd={Uri.EscapeDataString(_members[0].EmpCd)}&myAreaOnly=false");
            if (!reached || status != 200 || string.IsNullOrWhiteSpace(body)) return;

            var tickets = new List<Ticket>();
            using (var doc = JsonDocument.Parse(body))
            {
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;
                foreach (var it in items.EnumerateArray())
                {
                    if (!it.TryGetProperty("BrokenId", out var idEl) || !idEl.TryGetInt64(out var id)) continue;
                    if (onlyId.HasValue && id != onlyId.Value) continue;
                    if (!DateTime.TryParse(Str(it, "ReportedAt"), out var at)) continue;
                    tickets.Add(new Ticket(id, Str(it, "Status") ?? "", at, Str(it, "ItemCode") ?? "", Str(it, "NameEn") ?? "",
                                           Str(it, "LocationPath") ?? "", (Str(it, "Description") ?? "").Replace('\n', ' ')));
                }
            }
            await ProcessAsync(sp, db, tickets);
        }
        finally { _gate.Release(); }
    }

    private async Task ProcessAsync(IServiceProvider sp, OracleService db, List<Ticket> tickets)
    {
        var maxAge = TimeSpan.FromMinutes(Int("MaxAgeMinutes", 120));
        var escalate = TimeSpan.FromMinutes(Int("EscalateMinutes", 15));
        var only = new HashSet<string>(_cfg.GetSection("Tpm:BrokenNotify:OnlyEmpCds").Get<string[]>() ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var helper = sp.GetRequiredService<NotificationHelper>();
        bool dryRun = string.Equals(_cfg["Tpm:BrokenNotify:DryRun"], "true", StringComparison.OrdinalIgnoreCase);   // chỉ ghi log ai SẼ nhận, không gửi

        int sent = 0;
        foreach (var t in tickets)
        {
            if (t.Status != "REPORTED") continue;
            var age = DateTime.Now - t.ReportedAt;
            if (age > maxAge || age < TimeSpan.FromMinutes(-5)) continue;

            var segs = t.Path.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matched = _members.Where(m => !m.General && m.Areas.Overlaps(segs)).Select(m => m.EmpCd).ToList();
            var general = _members.Where(m => m.General).Select(m => m.EmpCd).ToList();

            // Báo ngay cho đúng khu vực; không ai phụ trách khu vực này → bảo trì chung nhận luôn; còn không thì đợi EscalateMinutes
            var recipients = new List<string>(matched);
            if (matched.Count == 0 || age >= escalate) recipients.AddRange(general);
            if (only.Count > 0) recipients = recipients.Where(only.Contains).ToList();
            if (recipients.Count == 0) continue;

            var link = $"TPM_BROKEN:{t.BrokenId}";
            var already = (await db.ExecuteQueryAsync(
                "SELECT TARGET_VAL FROM HRMS.HR_NOTIFICATIONS WHERE NOTI_TYPE = 'EMPCD' AND LINK_ACTION = :L",
                r => r["TARGET_VAL"]?.ToString() ?? "", new OracleParameter("L", link))).ToHashSet();

            var desc = t.Desc.Length > 80 ? t.Desc[..80] + "…" : t.Desc;
            var title = $"Máy hư: {t.Code}";
            var text = $"{t.Name} · {t.Path}" + (desc.Length > 0 ? $" — {desc}" : "");

            foreach (var emp in recipients.Distinct())
            {
                if (already.Contains(emp)) continue;
                if (dryRun) { _log.LogInformation("TpmBrokenNotify[DRY] phiếu {id} {code} ({path}) → {emp}", t.BrokenId, t.Code, t.Path, emp); sent++; continue; }
                await helper.SendNotificationAsync(new SendNotificationRequest
                {
                    TITLE = title, BODY = text, TITLE_EN = $"Machine broken: {t.Code}", BODY_EN = $"{t.Name} · {t.Path}",
                    NOTI_TYPE = "EMPCD", TARGET_VAL = emp, LINK_ACTION = link,
                    CREATED_BY = "SYSTEM", PRIORITY = "HIGH", SOURCE = "SYSTEM",
                });
                sent++;
            }
        }
        if (sent > 0) _log.LogInformation("TpmBrokenNotify: đã gửi {n} thông báo{m}", sent, only.Count > 0 ? " (CHẠY THỬ, danh sách giới hạn)" : "");
    }

    // Vùng phụ trách của từng thẻ bảo trì (TPM maintenance/areas) — nhớ 6 giờ, ~108 lời gọi mỗi lần làm mới
    private async Task RefreshMembersAsync(TpmService tpm, OracleService db)
    {
        if ((DateTime.Now - _membersAt).TotalHours < 6 && _members.Count > 0) return;
        List<string> codes;
        try { codes = await db.ExecuteQueryAsync("SELECT EMP_CD FROM HRMS.HR_TPM_MAINT", r => r["EMP_CD"]?.ToString() ?? ""); }
        catch { return; }

        var list = new List<Member>();
        foreach (var c in codes.Where(c => c.Length > 0))
        {
            var (reached, status, body) = await tpm.CallAsync(HttpMethod.Get, $"/api/mysamho/maintenance/areas?empCd={Uri.EscapeDataString(c)}");
            if (!reached || status != 200) continue;   // 401/403 (đã khóa bên TPM) hoặc lỗi → bỏ qua người này
            try
            {
                using var d = JsonDocument.Parse(body);
                var areas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (d.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var a in arr.EnumerateArray()) { var n = Str(a, "LocationNm"); if (!string.IsNullOrWhiteSpace(n)) areas.Add(n.Trim()); }
                list.Add(new Member(c, areas, areas.Count == 0));
            }
            catch { }
        }
        if (list.Count > 0) { _members = list; _membersAt = DateTime.Now; _log.LogInformation("TpmBrokenNotify: {n} thẻ bảo trì ({g} bảo trì chung)", list.Count, list.Count(m => m.General)); }
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
