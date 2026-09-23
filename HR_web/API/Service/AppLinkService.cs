using HR_web.Models.AppLink;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class AppLinkService
{
    private readonly ApiService _api;

    public AppLinkService(ApiService api)
    {
        _api = api;
    }

    private class ListResponse
    {
        public bool success { get; set; }
        public List<AppLinkModel>? data { get; set; }
        public AppLinkStatsModel? stats { get; set; }
        public string? message { get; set; }
        public int total { get; set; }
        public int page { get; set; }
        public int page_size { get; set; }
        public int total_pages { get; set; }
    }

    private class ImportResponse
    {
        public bool success { get; set; }
        public string? message { get; set; }
        public int inserted { get; set; }
        public int skipped { get; set; }
    }

    private async Task<T?> Parse<T>(HttpResponseMessage? response)
    {
        if (response == null) return default;
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
    }

    public async Task<(List<AppLinkModel> list, AppLinkStatsModel stats, int total, int totalPages)> GetAdminListAsync(
        string? status, string? search, int page, int pageSize)
    {
        var qs = $"?page={page}&page_size={pageSize}";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        var result = await _api.GetAsync<ListResponse>($"AppLink/admin/list{qs}");
        return (result?.data ?? new(), result?.stats ?? new(), result?.total ?? 0, result?.total_pages ?? 0);
    }

    public async Task<(bool success, string message, int inserted, int skipped)> ImportAsync(List<string> links, string loginUser)
    {
        var response = await _api.PostAsync("AppLink/admin/import", new ImportAppLinkRequest { LINKS = links, LOGIN_USER = loginUser });
        var parsed = await Parse<ImportResponse>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.inserted ?? 0, parsed?.skipped ?? 0);
    }

    public async Task<AppLinkResult> RequestLinkAsync(string empcd)
    {
        var response = await _api.PostAsync("AppLink/request", new { EMPCD = empcd });
        var parsed = await Parse<AppLinkResult>(response);
        return parsed ?? new AppLinkResult { success = false, message = "Lỗi server" };
    }

    public async Task<AppLinkResult> ReassignAsync(string empcd, string? actorEmpcd)
    {
        var response = await _api.PostAsync("AppLink/reassign", new ReassignAppLinkRequest { EMPCD = empcd, ACTOR_EMPCD = actorEmpcd });
        var parsed = await Parse<AppLinkResult>(response);
        return parsed ?? new AppLinkResult { success = false, message = "Lỗi server" };
    }

    // HR tự lấy/cấp lại link giùm NV từ AppLinkAdmin — bỏ qua giới hạn 3 lần/mã thẻ.
    public async Task<AppLinkResult> AdminRequestLinkAsync(string empcd)
    {
        var response = await _api.PostAsync("AppLink/admin/request", new { EMPCD = empcd });
        var parsed = await Parse<AppLinkResult>(response);
        return parsed ?? new AppLinkResult { success = false, message = "Lỗi server" };
    }

    public async Task<AppLinkResult> AdminReassignAsync(string empcd, string? actorEmpcd)
    {
        var response = await _api.PostAsync("AppLink/admin/reassign", new ReassignAppLinkRequest { EMPCD = empcd, ACTOR_EMPCD = actorEmpcd });
        var parsed = await Parse<AppLinkResult>(response);
        return parsed ?? new AppLinkResult { success = false, message = "Lỗi server" };
    }
}
