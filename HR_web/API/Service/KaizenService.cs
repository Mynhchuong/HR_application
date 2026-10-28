namespace HR_web.API.Service;

/// <summary>Đăng ký Kaizen — chuyển tiếp nguyên JSON của HR_api/Controllers/KaizenController.cs.</summary>
public class KaizenService
{
    private readonly ApiService _api;
    public KaizenService(ApiService api) { _api = api; }

    private const string Fail = "{\"success\":false,\"message\":\"Không kết nối được máy chủ\"}";

    public async Task<string> SubmitRawAsync(string empcd, string? phone, string? idea)
    {
        try
        {
            var r = await _api.PostAsync("Kaizen/submit", new { EMPCD = empcd, PHONE = phone, IDEA = idea });
            return r == null ? Fail : await r.Content.ReadAsStringAsync();
        }
        catch { return Fail; }
    }

    public async Task<string> MyRawAsync(string empcd)
    {
        try
        {
            var r = await _api.GetAsync_Raw("Kaizen/my", $"empcd={Uri.EscapeDataString(empcd)}");
            return r == null ? Fail : await r.Content.ReadAsStringAsync();
        }
        catch { return Fail; }
    }

    public async Task<string> AdminListRawAsync(string? month, string? deptcd, string? search, int page, int pageSize)
    {
        try
        {
            var qs = $"page={page}&page_size={pageSize}";
            if (!string.IsNullOrWhiteSpace(month))  qs += $"&month={Uri.EscapeDataString(month)}";
            if (!string.IsNullOrWhiteSpace(deptcd)) qs += $"&deptcd={Uri.EscapeDataString(deptcd)}";
            if (!string.IsNullOrWhiteSpace(search)) qs += $"&search={Uri.EscapeDataString(search)}";
            var r = await _api.GetAsync_Raw("Kaizen/admin/list", qs);
            return r == null ? Fail : await r.Content.ReadAsStringAsync();
        }
        catch { return Fail; }
    }

    // ── Email báo cáo hằng tuần (Lean quản lý danh sách người nhận) ──
    public async Task<string> MailListRawAsync()
    {
        try
        {
            var r = await _api.GetAsync_Raw("Kaizen/mail/list");
            return r == null ? Fail : await r.Content.ReadAsStringAsync();
        }
        catch { return Fail; }
    }

    private async Task<string> MailPostAsync(string path, object body)
    {
        try
        {
            var r = await _api.PostAsync(path, body);
            return r == null ? Fail : await r.Content.ReadAsStringAsync();
        }
        catch { return Fail; }
    }

    public Task<string> MailAddRawAsync(string email, string? loginUser) => MailPostAsync("Kaizen/mail/add", new { EMAIL = email, LOGIN_USER = loginUser });
    public Task<string> MailRemoveRawAsync(int id) => MailPostAsync("Kaizen/mail/remove", new { ID = id });
    public Task<string> MailTestRawAsync(string email) => MailPostAsync("Kaizen/mail/test", new { EMAIL = email });
}
