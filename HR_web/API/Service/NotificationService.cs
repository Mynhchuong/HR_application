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
}
