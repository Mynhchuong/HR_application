using HR_web.Models.Employee;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class EmployeeService
{
    private readonly ApiService _api;
    public EmployeeService(ApiService api) { _api = api; }

    public async Task<TeamMemberResponse> GetMyTeamAsync(
        string  approverEmpcd,
        string? search    = null,
        string? deptcd    = null,
        string? linecd    = null,
        string? workcd    = null,
        string? shiftcd   = null,
        string? jikwicd   = null,
        string? interest  = null,
        bool    noLogin   = false,
        int     page      = 1,
        int     pageSize  = 50,
        bool    all       = false)
    {
        var q = new List<string> { $"approver_empcd={Uri.EscapeDataString(approverEmpcd)}" };
        if (!string.IsNullOrEmpty(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrEmpty(deptcd)) q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd)) q.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd)) q.Add($"workcd={Uri.EscapeDataString(workcd)}");
        if (!string.IsNullOrEmpty(shiftcd)) q.Add($"shiftcd={Uri.EscapeDataString(shiftcd)}");
        if (!string.IsNullOrEmpty(jikwicd)) q.Add($"jikwicd={Uri.EscapeDataString(jikwicd)}");
        if (!string.IsNullOrEmpty(interest)) q.Add($"interest={Uri.EscapeDataString(interest)}");
        if (noLogin) q.Add("no_login=true");
        if (all)
        {
            q.Add("all=true");
        }
        else
        {
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
        }

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

    public async Task<TeamScopeFiltersResponse> GetMyTeamFiltersAsync(string approverEmpcd)
    {
        try
        {
            var res = await _api.GetAsync_Raw("Employee/my-team-filters", $"approver_empcd={Uri.EscapeDataString(approverEmpcd)}");
            if (res != null && res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<TeamScopeFiltersResponse>(json)
                       ?? new TeamScopeFiltersResponse { success = false, message = "Lỗi parse" };
            }
            return new TeamScopeFiltersResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex)
        {
            return new TeamScopeFiltersResponse { success = false, message = ex.Message };
        }
    }

    // Bản không giới hạn scope của GetMyTeamAsync — dùng cho trang "toàn công ty" của HR/Admin
    // (yêu cầu 2026-10-16). requesterEmpcd chỉ dùng để API kiểm tra role HR/Admin, không dùng để lọc.
    public async Task<TeamMemberResponse> GetAllEmployeesAsync(
        string  requesterEmpcd,
        string? search    = null,
        string? deptcd    = null,
        string? linecd    = null,
        string? workcd    = null,
        string? shiftcd   = null,
        string? jikwicd   = null,
        string? interest  = null,
        bool    noLogin   = false,
        int     page      = 1,
        int     pageSize  = 50,
        bool    all       = false)
    {
        var q = new List<string> { $"requester_empcd={Uri.EscapeDataString(requesterEmpcd)}" };
        if (!string.IsNullOrEmpty(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrEmpty(deptcd)) q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd)) q.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd)) q.Add($"workcd={Uri.EscapeDataString(workcd)}");
        if (!string.IsNullOrEmpty(shiftcd)) q.Add($"shiftcd={Uri.EscapeDataString(shiftcd)}");
        if (!string.IsNullOrEmpty(jikwicd)) q.Add($"jikwicd={Uri.EscapeDataString(jikwicd)}");
        if (!string.IsNullOrEmpty(interest)) q.Add($"interest={Uri.EscapeDataString(interest)}");
        if (noLogin) q.Add("no_login=true");
        if (all)
        {
            q.Add("all=true");
        }
        else
        {
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
        }

        try
        {
            var res = await _api.GetAsync_Raw("Employee/all-team", string.Join("&", q));
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

    public async Task<TeamScopeFiltersResponse> GetAllEmployeesFiltersAsync(string requesterEmpcd)
    {
        try
        {
            var res = await _api.GetAsync_Raw("Employee/all-team-filters", $"requester_empcd={Uri.EscapeDataString(requesterEmpcd)}");
            if (res != null && res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<TeamScopeFiltersResponse>(json)
                       ?? new TeamScopeFiltersResponse { success = false, message = "Lỗi parse" };
            }
            return new TeamScopeFiltersResponse { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex)
        {
            return new TeamScopeFiltersResponse { success = false, message = ex.Message };
        }
    }
}
