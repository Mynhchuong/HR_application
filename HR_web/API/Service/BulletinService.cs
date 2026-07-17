using HR_web.Models.Bulletin;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class BulletinService
{
    private readonly ApiService _api;

    public BulletinService(ApiService api) { _api = api; }

    private class R<T>
    {
        public bool    success  { get; set; }
        public T?      data     { get; set; }
        public string? message  { get; set; }
        public int?    id       { get; set; }
        public bool?   fcmSent  { get; set; }
        public string? action   { get; set; }
        public int?    isPinned { get; set; }
        public string? fileName { get; set; }
    }

    private static async Task<T?> Parse<T>(HttpResponseMessage? response)
    {
        if (response == null || !response.IsSuccessStatusCode) return default;
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
    }

    // ─── Bài đăng ────────────────────────────────────────────────────────────

    public async Task<List<BulletinModel>> GetListAsync(string? empcd = null)
    {
        var url = string.IsNullOrEmpty(empcd)
            ? "bulletin/list"
            : $"bulletin/list?empcd={Uri.EscapeDataString(empcd)}";
        var r = await _api.GetAsync<R<List<BulletinModel>>>(url);
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<List<BulletinModel>> GetAdminListAsync()
    {
        var r = await _api.GetAsync<R<List<BulletinModel>>>("bulletin/admin/list");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<int> GetUnreadCountAsync(string empcd)
    {
        try
        {
            var r = await _api.GetAsync<UnreadCountResponse>("bulletin/unread-count", $"empcd={Uri.EscapeDataString(empcd)}");
            return r?.count ?? 0;
        }
        catch { return 0; }
    }

    private class UnreadCountResponse { public int count { get; set; } }

    public async Task<BulletinModel?> GetByIdAsync(int id, string? empcd = null)
    {
        string url = $"bulletin/{id}";
        if (!string.IsNullOrEmpty(empcd)) url += $"?empcd={Uri.EscapeDataString(empcd)}";
        var r = await _api.GetAsync<R<BulletinModel>>(url);
        return r?.success == true ? r.data : null;
    }

    public async Task<(bool success, string message, int id)> SaveAsync(SaveBulletinRequest model)
    {
        var response = await _api.PostAsync("bulletin/save", model);
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.id ?? 0);
    }

    public async Task<(bool success, string message, bool fcmSent)> PublishAsync(int id, string loginUser)
    {
        var response = await _api.PostAsync($"bulletin/publish?id={id}&loginUser={Uri.EscapeDataString(loginUser)}", new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.fcmSent ?? false);
    }

    public async Task<(bool success, string message)> UnpublishAsync(int id, string loginUser)
    {
        var response = await _api.PostAsync($"bulletin/unpublish?id={id}&loginUser={Uri.EscapeDataString(loginUser)}", new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    public async Task<(bool success, string message, int isPinned)> TogglePinAsync(int id, string loginUser)
    {
        var response = await _api.PostAsync($"bulletin/toggle-pin?id={id}&loginUser={Uri.EscapeDataString(loginUser)}", new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.isPinned ?? 0);
    }

    public async Task<(bool success, string message)> SetPinOrderAsync(int id, int pinOrder, string loginUser)
    {
        var response = await _api.PostAsync($"bulletin/set-pin-order?id={id}&pinOrder={pinOrder}&loginUser={Uri.EscapeDataString(loginUser)}", new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    public async Task<(bool success, string message)> DeleteAsync(int id, string loginUser)
    {
        var response = await _api.PostAsync($"bulletin/delete?id={id}&loginUser={Uri.EscapeDataString(loginUser)}", new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    // ─── Media ───────────────────────────────────────────────────────────────

    public async Task<(bool success, string message, int id, string fileName)> AddMediaAsync(AddMediaRequest model)
    {
        var response = await _api.PostAsync("bulletin/media/add", model);
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.id ?? 0, parsed?.fileName ?? "");
    }

    public async Task<(bool success, string message)> DeleteMediaAsync(int id)
    {
        var response = await _api.PostAsync($"bulletin/media/delete?id={id}", new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    // ─── Comment ─────────────────────────────────────────────────────────────

    public async Task<List<BulletinCommentDto>> GetCommentsAsync(int bulletinId)
    {
        var r = await _api.GetAsync<R<List<BulletinCommentDto>>>($"bulletin/{bulletinId}/comments");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<(bool success, string message, int id)> AddCommentAsync(AddCommentRequest model)
    {
        var response = await _api.PostAsync("bulletin/comment/add", model);
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.id ?? 0);
    }

    public async Task<(bool success, string message)> DeleteCommentAsync(int id, string empcd, bool isHrAdmin)
    {
        var response = await _api.PostAsync(
            $"bulletin/comment/delete?id={id}&empcd={Uri.EscapeDataString(empcd)}&isHrAdmin={isHrAdmin.ToString().ToLower()}",
            new { });
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    // ─── Reaction ────────────────────────────────────────────────────────────

    public async Task<(bool success, string message, string action)> ReactAsync(ReactRequest model)
    {
        var response = await _api.PostAsync("bulletin/react", model);
        var parsed   = await Parse<R<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.action ?? "");
    }

    // ─── Stats ───────────────────────────────────────────────────────────────

    public async Task<BulletinStatsModel?> GetStatsAsync(int bulletinId)
    {
        var r = await _api.GetAsync<R<BulletinStatsModel>>($"bulletin/{bulletinId}/stats");
        return r?.success == true ? r.data : null;
    }
}
