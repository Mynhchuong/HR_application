using HR_web.Models.Directory;

namespace HR_web.API.Service;

public class DirectoryService
{
    private readonly ApiService _api;
    private const string EmployeeEndpoint = "Directory/employee";
    private const string ChangeHistoryEndpoint = "Directory/change-history";
    private const string WorkCdListEndpoint = "Directory/workcd-list";
    private const string DeptListEndpoint = "Directory/dept-list";
    private const string LineListEndpoint = "Directory/line-list";

    public DirectoryService(ApiService api)
    {
        _api = api;
    }

    public async Task<EmployeeDirectoryModel?> GetEmployeeAsync(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return null;
        return await _api.GetAsync<EmployeeDirectoryModel>(EmployeeEndpoint, $"empCd={Uri.EscapeDataString(empCd)}");
    }

    public async Task<List<EmployeeChangeHistoryModel>> GetChangeHistoryAsync(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return new List<EmployeeChangeHistoryModel>();
        var result = await _api.GetAsync<List<EmployeeChangeHistoryModel>>(ChangeHistoryEndpoint, $"empCd={Uri.EscapeDataString(empCd)}");
        return result ?? new List<EmployeeChangeHistoryModel>();
    }

    public async Task<WorkCdListResult> GetWorkCdListAsync(string? deptCd, string? lineCd, int page, int pageSize)
    {
        var q = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(deptCd)) q.Add($"deptCd={Uri.EscapeDataString(deptCd)}");
        if (!string.IsNullOrWhiteSpace(lineCd)) q.Add($"lineCd={Uri.EscapeDataString(lineCd)}");

        var result = await _api.GetAsync<WorkCdListResult>(WorkCdListEndpoint, string.Join("&", q));
        return result ?? new WorkCdListResult();
    }

    public async Task<List<DeptOptionModel>> GetDeptListAsync()
    {
        var result = await _api.GetAsync<List<DeptOptionModel>>(DeptListEndpoint);
        return result ?? new List<DeptOptionModel>();
    }

    public async Task<List<LineOptionModel>> GetLineListAsync(string? deptCd)
    {
        var q = string.IsNullOrWhiteSpace(deptCd) ? "" : $"deptCd={Uri.EscapeDataString(deptCd)}";
        var result = await _api.GetAsync<List<LineOptionModel>>(LineListEndpoint, q);
        return result ?? new List<LineOptionModel>();
    }
}
