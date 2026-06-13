using HR_web.Models.Guide;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class GuideService
{
    private readonly ApiService _api;

    public GuideService(ApiService api)
    {
        _api = api;
    }

    private class GuideResponse<T>
    {
        public bool success { get; set; }
        public T? data { get; set; }
        public string? message { get; set; }
        public string? videoPath { get; set; }
    }

    private async Task<T?> Parse<T>(HttpResponseMessage? response)
    {
        if (response == null || !response.IsSuccessStatusCode) return default;
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
    }

    public async Task<List<GuideModel>> GetListAsync()
    {
        var result = await _api.GetAsync<GuideResponse<List<GuideModel>>>("guide/list");
        return result?.success == true ? result.data ?? new() : new();
    }

    public async Task<List<GuideModel>> GetAdminListAsync()
    {
        var result = await _api.GetAsync<GuideResponse<List<GuideModel>>>("guide/admin/list");
        return result?.success == true ? result.data ?? new() : new();
    }

    public async Task<GuideModel?> GetByIdAsync(int id)
    {
        var result = await _api.GetAsync<GuideResponse<GuideModel>>($"guide/{id}");
        return result?.success == true ? result.data : null;
    }

    // Trả về savedId — API nên trả { success, message, data: <id> } để lấy ID khi tạo mới
    public async Task<(bool success, string message, int savedId)> SaveAsync(SaveGuideRequest model)
    {
        var response = await _api.PostAsync("guide/save", model);
        var parsed = await Parse<GuideResponse<int?>>(response);
        var id = parsed?.data ?? model.ID ?? 0;
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", id);
    }

    public async Task<(bool success, string message)> ToggleAsync(int id, string loginUser)
    {
        var response = await _api.PostAsync($"guide/toggle?id={id}&loginUser={loginUser}", new { });
        var parsed = await Parse<GuideResponse<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    public async Task<(bool success, string message, string? videoPath)> DeleteAsync(int id)
    {
        var response = await _api.PostAsync($"guide/delete?id={id}", new { });
        var parsed = await Parse<GuideResponse<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server", parsed?.videoPath);
    }
}
