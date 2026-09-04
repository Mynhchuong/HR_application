using HR_web.Models.Directory;

namespace HR_web.API.Service;

public class DirectoryService
{
    private readonly ApiService _api;
    private const string EmployeeEndpoint = "Directory/employee";
    private const string ChangeHistoryEndpoint = "Directory/change-history";
    private const string InterestListEndpoint = "Directory/interest-list";

    public DirectoryService(ApiService api)
    {
        _api = api;
    }

    public async Task<EmployeeDirectoryModel?> GetEmployeeAsync(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return null;
        return await _api.GetAsync<EmployeeDirectoryModel>(EmployeeEndpoint, $"empCd={Uri.EscapeDataString(empCd)}");
    }

    public async Task<EmployeeChangeHistoryListResult> GetChangeHistoryAsync(string empCd, int page = 1, int pageSize = 5)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return new EmployeeChangeHistoryListResult();
        var result = await _api.GetAsync<EmployeeChangeHistoryListResult>(
            ChangeHistoryEndpoint, $"empCd={Uri.EscapeDataString(empCd)}&page={page}&pageSize={pageSize}");
        return result ?? new EmployeeChangeHistoryListResult();
    }

    public async Task<WorkCdListResult> GetInterestListAsync(string? search, int page, int pageSize)
    {
        var q = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");

        var result = await _api.GetAsync<WorkCdListResult>(InterestListEndpoint, string.Join("&", q));
        return result ?? new WorkCdListResult();
    }
}
