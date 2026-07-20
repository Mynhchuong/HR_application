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

    public async Task<List<DropdownModel>> GetWorkByLineAsync(string? lineCd, string? deptCd = null)
    {
        if (string.IsNullOrEmpty(lineCd) && string.IsNullOrEmpty(deptCd)) return new();
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(lineCd)) parts.Add($"lineCd={Uri.EscapeDataString(lineCd)}");
        if (!string.IsNullOrEmpty(deptCd)) parts.Add($"deptCd={Uri.EscapeDataString(deptCd)}");
        return await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/work-by-line", string.Join("&", parts)) ?? new();
    }

    public async Task<List<DropdownModel>> GetDeptByScopeAsync(string empcd)
        => await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/dept-by-scope", $"empcd={Uri.EscapeDataString(empcd)}") ?? new();

    public async Task<List<DropdownModel>> GetLineByScopeAsync(string empcd, string? deptCd = null)
    {
        var q = $"empcd={Uri.EscapeDataString(empcd)}";
        if (!string.IsNullOrEmpty(deptCd)) q += $"&deptCd={Uri.EscapeDataString(deptCd)}";
        return await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/line-by-scope", q) ?? new();
    }

    public async Task<List<DropdownModel>> GetWorkByScopeAsync(string empcd, string? lineCd = null, string? deptCd = null)
    {
        var q = $"empcd={Uri.EscapeDataString(empcd)}";
        if (!string.IsNullOrEmpty(lineCd)) q += $"&lineCd={Uri.EscapeDataString(lineCd)}";
        if (!string.IsNullOrEmpty(deptCd)) q += $"&deptCd={Uri.EscapeDataString(deptCd)}";
        return await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/work-by-scope", q) ?? new();
    }

    public async Task<List<DropdownModel>> GetEmpAsync(string term)
    {
        if (string.IsNullOrEmpty(term) || term.Length < 2) return new();
        return await _api.GetAsync<List<DropdownModel>>($"{BaseEndpoint}/emp", $"term={Uri.EscapeDataString(term)}") ?? new();
    }

    public async Task<List<EmpScopeModel>> GetEmpByScopeAsync(string empcd)
        => await _api.GetAsync<List<EmpScopeModel>>($"{BaseEndpoint}/emp-by-scope", $"empcd={Uri.EscapeDataString(empcd)}") ?? new();
}
