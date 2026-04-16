using HR_web.Models;

namespace HR_web.API.Service;

public class DropdownService
{
    private readonly ApiService _api;
    private const string BaseEndpoint = "Account/dropdown";

    public DropdownService(ApiService api)
    {
        _api = api;
    }

    public async Task<List<DropdownModel>> GetDeptAsync()
        => await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/dept") ?? new();

    public async Task<List<DropdownModel>> GetLineAsync()
        => await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/line") ?? new();

    public async Task<List<DropdownModel>> GetWorkAsync()
        => await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/work") ?? new();

    public async Task<List<DropdownModel>> GetRoleAsync()
        => await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/role") ?? new();

    public async Task<List<DropdownModel>> GetLineByDeptAsync(string deptCd)
    {
        if (string.IsNullOrEmpty(deptCd)) return new();
        return await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/line-by-dept", $"deptcd={Uri.EscapeDataString(deptCd)}") ?? new();
    }
}
