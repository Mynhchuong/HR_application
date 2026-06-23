using HR_web.Models.Notification;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class NotificationService
{
    private readonly ApiService _api;

    public NotificationService(ApiService api) { _api = api; }

    public async Task<List<NotificationItem>> GetMyAsync(string empCd, int page = 1, int pageSize = 20)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empCd)}&page={page}&page_size={pageSize}";
            var r = await _api.GetAsync_Raw("Notification/my", q);
            if (r == null || !r.IsSuccessStatusCode) return new();
            var json  = await r.Content.ReadAsStringAsync();
            var resp  = JsonConvert.DeserializeObject<NotificationPagedResponse>(json);
            return resp?.data ?? new();
        }
        catch { return new(); }
    }

    public async Task<int> GetUnreadCountAsync(string empCd)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empCd)}";
            var r = await _api.GetAsync_Raw("Notification/unread-count", q);
            if (r == null || !r.IsSuccessStatusCode) return 0;
            var json = await r.Content.ReadAsStringAsync();
            var obj  = JsonConvert.DeserializeAnonymousType(json, new { success = false, count = 0 });
            return obj?.count ?? 0;
        }
        catch { return 0; }
    }

    public async Task<bool> MarkReadAsync(decimal notiId, string empCd)
    {
        try
        {
            var r = await _api.PostAsync($"Notification/mark-read?notiId={notiId}&empcd={Uri.EscapeDataString(empCd)}", new { });
            return r?.IsSuccessStatusCode == true;
        }
        catch { return false; }
    }

    public async Task<bool> MarkAllReadAsync(string empCd)
    {
        try
        {
            var r = await _api.PostAsync($"Notification/mark-all-read?empcd={Uri.EscapeDataString(empCd)}", new { });
            return r?.IsSuccessStatusCode == true;
        }
        catch { return false; }
    }

    public async Task<bool> RegisterTokenAsync(string empCd, string token, string osType)
    {
        try
        {
            var r = await _api.PostAsync("Notification/register-token", new { EMPCD = empCd, TOKEN = token, OS_TYPE = osType });
            return r?.IsSuccessStatusCode == true;
        }
        catch { return false; }
    }

    // ─── ADMIN COMPOSE: send multi-target ────────────────────────
    public async Task<string> SendMultiRawAsync(object payload)
    {
        try
        {
            var r = await _api.PostAsync("Notification/send-multi", payload);
            if (r == null) return "{\"success\":false,\"message\":\"Không kết nối được API\"}";
            return await r.Content.ReadAsStringAsync();
        }
        catch (Exception ex) { return "{\"success\":false,\"message\":\"" + ex.Message + "\"}"; }
    }

    public async Task<string> SearchEmpRawAsync(string? q, string? dept, string? line, string? work, int page = 1, int pageSize = 50)
    {
        try
        {
            var qs = $"page={page}&page_size={pageSize}";
            if (!string.IsNullOrEmpty(q))    qs += "&q="    + Uri.EscapeDataString(q);
            if (!string.IsNullOrEmpty(dept)) qs += "&dept=" + Uri.EscapeDataString(dept);
            if (!string.IsNullOrEmpty(line)) qs += "&line=" + Uri.EscapeDataString(line);
            if (!string.IsNullOrEmpty(work)) qs += "&work=" + Uri.EscapeDataString(work);
            var r = await _api.GetAsync_Raw("Notification/search-emp", qs);
            if (r == null || !r.IsSuccessStatusCode) return "{\"success\":false}";
            return await r.Content.ReadAsStringAsync();
        }
        catch { return "{\"success\":false}"; }
    }

    public async Task<string> SearchEmpCodesRawAsync(string? q, string? dept, string? line, string? work)
    {
        try
        {
            var qs = "";
            if (!string.IsNullOrEmpty(q))    qs += "q="    + Uri.EscapeDataString(q);
            if (!string.IsNullOrEmpty(dept)) qs += (qs.Length>0?"&":"") + "dept=" + Uri.EscapeDataString(dept);
            if (!string.IsNullOrEmpty(line)) qs += (qs.Length>0?"&":"") + "line=" + Uri.EscapeDataString(line);
            if (!string.IsNullOrEmpty(work)) qs += (qs.Length>0?"&":"") + "work=" + Uri.EscapeDataString(work);
            var r = await _api.GetAsync_Raw("Notification/search-emp-codes", qs);
            if (r == null || !r.IsSuccessStatusCode) return "{\"success\":false}";
            return await r.Content.ReadAsStringAsync();
        }
        catch { return "{\"success\":false}"; }
    }

    public async Task<string> LookupRawAsync(string type)
    {
        try
        {
            var r = await _api.GetAsync_Raw("Notification/lookup", "type=" + Uri.EscapeDataString(type));
            if (r == null || !r.IsSuccessStatusCode) return "{\"success\":false}";
            return await r.Content.ReadAsStringAsync();
        }
        catch { return "{\"success\":false}"; }
    }

    public async Task<string> AdminSentRawAsync(int page = 1, int pageSize = 30)
    {
        try
        {
            var r = await _api.GetAsync_Raw("Notification/admin/sent", $"page={page}&page_size={pageSize}");
            if (r == null || !r.IsSuccessStatusCode) return "{\"success\":false}";
            return await r.Content.ReadAsStringAsync();
        }
        catch { return "{\"success\":false}"; }
    }
}
