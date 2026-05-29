using HR_web.Models.Policy;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class PolicyService
{
    private readonly ApiService _api;

    public PolicyService(ApiService api)
    {
        _api = api;
    }

    private class PolicyResponse<T>
    {
        public bool success { get; set; }
        public T? data { get; set; }
        public string? message { get; set; }
    }

    private async Task<T?> Parse<T>(HttpResponseMessage? response)
    {
        if (response == null || !response.IsSuccessStatusCode) return default;
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
    }

    public async Task<List<CompanyPolicyModel>> GetListAsync()
    {
        var result = await _api.GetAsync<PolicyResponse<List<CompanyPolicyModel>>>("policy/list");
        return result?.success == true ? result.data ?? new() : new();
    }

    public async Task<List<CompanyPolicyModel>> GetAdminListAsync()
    {
        var result = await _api.GetAsync<PolicyResponse<List<CompanyPolicyModel>>>("policy/admin/list");
        return result?.success == true ? result.data ?? new() : new();
    }

    public async Task<CompanyPolicyModel?> GetByIdAsync(int id)
    {
        var result = await _api.GetAsync<PolicyResponse<CompanyPolicyModel>>($"policy/{id}");
        return result?.success == true ? result.data : null;
    }

    public async Task<(bool success, string message)> SaveAsync(SavePolicyRequest model)
    {
        var response = await _api.PostAsync("policy/save", model);
        var parsed = await Parse<PolicyResponse<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }

    public async Task<(bool success, string message)> ToggleAsync(int id, string loginUser)
    {
        var response = await _api.PostAsync($"policy/toggle?id={id}&loginUser={loginUser}", new { });
        var parsed = await Parse<PolicyResponse<object>>(response);
        return (parsed?.success ?? false, parsed?.message ?? "Lỗi server");
    }
}
