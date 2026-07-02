namespace HR_web.API.Service;

// Wrapper cho /apiHR/Home/admin/*
public class HomeAdminApiService
{
    private readonly ApiService _api;

    public HomeAdminApiService(ApiService api) { _api = api; }

    // Passthrough raw JSON (giống AdminNotiService) — HR_web trả trực tiếp về client
    public Task<string> BannerListRawAsync()      => GetRawAsync("home/admin/banner/list", "");
    public Task<string> GreetingListRawAsync(string? slot) => GetRawAsync("home/admin/greeting/list",
                                                     string.IsNullOrEmpty(slot) ? "" : $"slot={Uri.EscapeDataString(slot)}");
    public Task<string> RuleListRawAsync()        => GetRawAsync("home/admin/rule/list", "");
    public Task<string> AuditRawAsync(int page, int pageSize, string? entity, string? actor, string? dateFrom, string? dateTo)
    {
        var parts = new List<string>
        {
            $"page={page}",
            $"page_size={pageSize}",
        };
        if (!string.IsNullOrEmpty(entity))   parts.Add($"entity={Uri.EscapeDataString(entity)}");
        if (!string.IsNullOrEmpty(actor))    parts.Add($"actor={Uri.EscapeDataString(actor)}");
        if (!string.IsNullOrEmpty(dateFrom)) parts.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
        if (!string.IsNullOrEmpty(dateTo))   parts.Add($"date_to={Uri.EscapeDataString(dateTo)}");
        return GetRawAsync("home/admin/audit", string.Join("&", parts));
    }

    public Task<string> BannerSaveRawAsync(object payload)   => PostRawAsync("home/admin/banner/save",   payload);
    public Task<string> BannerDeleteRawAsync(object payload) => PostRawAsync("home/admin/banner/delete", payload);
    public Task<string> GreetingSaveRawAsync(object payload)   => PostRawAsync("home/admin/greeting/save",   payload);
    public Task<string> GreetingDeleteRawAsync(object payload) => PostRawAsync("home/admin/greeting/delete", payload);
    public Task<string> RuleSaveRawAsync(object payload)     => PostRawAsync("home/admin/rule/save",     payload);

    private async Task<string> GetRawAsync(string path, string qs)
    {
        try
        {
            var res = await _api.GetAsync_Raw(path, qs);
            if (res == null) return @"{""success"":false,""message"":""API unreachable""}";
            return await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"message\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
        }
    }

    private async Task<string> PostRawAsync(string path, object payload)
    {
        try
        {
            var res = await _api.PostAsync(path, payload);
            if (res == null) return @"{""success"":false,""message"":""API unreachable""}";
            return await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"{{\"success\":false,\"message\":\"{ex.Message.Replace("\"", "\\\"")}\"}}";
        }
    }
}
