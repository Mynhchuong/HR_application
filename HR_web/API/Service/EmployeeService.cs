using HR_web.Models.Employee;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class EmployeeService
{
    private readonly ApiService _api;
    public EmployeeService(ApiService api) { _api = api; }

    public async Task<TeamMemberResponse> GetMyTeamAsync(
        string  approverEmpcd,
        string? search = null,
        string? deptcd = null,
        string? linecd = null,
        string? workcd = null)
    {
        var q = new List<string> { $"approver_empcd={Uri.EscapeDataString(approverEmpcd)}" };
        if (!string.IsNullOrEmpty(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrEmpty(deptcd)) q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd)) q.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd)) q.Add($"workcd={Uri.EscapeDataString(workcd)}");

        try
        {
            var res = await _api.GetAsync_Raw("Employee/my-team", string.Join("&", q));
            if (res != null && res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<TeamMemberResponse>(json)
                       ?? new TeamMemberResponse { success = false, message = "Lỗi parse" };
            }
            return new TeamMemberResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex)
        {
            return new TeamMemberResponse { success = false, message = ex.Message };
        }
    }
}
