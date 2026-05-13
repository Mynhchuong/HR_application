using Newtonsoft.Json;

namespace HR_web.API.Service;

public class UserDeptService
{
    private readonly ApiService _api;

    public UserDeptService(ApiService api)
    {
        _api = api;
    }

    public async Task<object?> GetListAsync(string? empcd, string? deptcd, int page, int pageSize)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(empcd))  q.Add($"empcd={Uri.EscapeDataString(empcd)}");
            if (!string.IsNullOrEmpty(deptcd)) q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");

            var res = await _api.GetAsync_Raw("UserDept", string.Join("&", q));
            if (res == null || !res.IsSuccessStatusCode) return null;
            return JsonConvert.DeserializeObject(await res.Content.ReadAsStringAsync());
        }
        catch { return null; }
    }

    public async Task<(bool success, string message, int inserted, int skipped)> ImportAsync(
        List<Dictionary<string, string?>> rows, string? createdBy = null)
    {
        try
        {
            var payload = new { CreatedBy = createdBy, Rows = rows };
            var res = await _api.PostAsync("UserDept/import", payload);
            if (res == null || !res.IsSuccessStatusCode)
                return (false, "Lỗi kết nối API", 0, 0);

            var json = JsonConvert.DeserializeObject<dynamic>(await res.Content.ReadAsStringAsync());
            bool ok  = (bool)(json?.success ?? false);
            return (ok,
                    (string)(json?.message ?? ""),
                    (int)(json?.inserted   ?? 0),
                    (int)(json?.skipped    ?? 0));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0, 0);
        }
    }

    public async Task<bool> DeleteAsync(string empcd, string? deptcd, string? linecd, string? workcd)
    {
        try
        {
            var q = new List<string> { $"empcd={Uri.EscapeDataString(empcd)}" };
            if (!string.IsNullOrEmpty(deptcd)) q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
            if (!string.IsNullOrEmpty(linecd)) q.Add($"linecd={Uri.EscapeDataString(linecd)}");
            if (!string.IsNullOrEmpty(workcd)) q.Add($"workcd={Uri.EscapeDataString(workcd)}");

            return await _api.DeleteAsync("UserDept", string.Join("&", q));
        }
        catch { return false; }
    }
}
