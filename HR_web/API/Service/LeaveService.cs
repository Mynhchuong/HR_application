using HR_web.Models.Leave;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class LeaveService
{
    private readonly ApiService _api;

    public LeaveService(ApiService api)
    {
        _api = api;
    }

    public async Task<LeaveMyRequestsPagedResponse> GetMyRequestsAsync(
        string empcd, string? source = null, int page = 1, int pageSize = 20,
        string? dateFrom = null, string? dateTo = null)
    {
        try
        {
            var q = new List<string> { $"empcd={Uri.EscapeDataString(empcd)}" };
            if (!string.IsNullOrEmpty(source))   q.Add($"source={Uri.EscapeDataString(source)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
            var response = await _api.GetAsync_Raw("leave/my-requests", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LeaveMyRequestsPagedResponse>(json);
                if (result != null) return result;
            }
            return new LeaveMyRequestsPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveMyRequestsPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<LeaveActionResponse> CreateAsync(LeaveCreateRequest request)
    {
        try
        {
            var response = await _api.PostAsync("leave/submit", request);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<LeaveActionResponse>(json)
                       ?? new LeaveActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new LeaveActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveActionResponse> UpdateAsync(LeaveUpdateRequest request)
    {
        try
        {
            var response = await _api.PutAsync<LeaveActionResponse>("leave/update", request);
            return response ?? new LeaveActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveActionResponse> DeleteAsync(string requestId, string empcd)
    {
        try
        {
            var q  = $"request_id={Uri.EscapeDataString(requestId)}&empcd={Uri.EscapeDataString(empcd)}";
            var ok = await _api.DeleteAsync("leave/delete", q);
            return ok ? new LeaveActionResponse { success = true,  message = "Đã xoá đơn nghỉ" }
                      : new LeaveActionResponse { success = false, message = "Lỗi xoá đơn" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveActionResponse> ConfirmAsync(string requestId, string empcd)
    {
        try
        {
            var payload  = new { REQUEST_ID = requestId, EMPCD = empcd };
            var response = await _api.PostAsync("leave/confirm", payload);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<LeaveActionResponse>(json)
                       ?? new LeaveActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new LeaveActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveListPagedResponse> GetApprovalListAsync(
        string approverEmpcd,
        string? status = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string> { $"approver_empcd={Uri.EscapeDataString(approverEmpcd)}" };
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
            var response = await _api.GetAsync_Raw("leave/approval-list", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LeaveListPagedResponse>(json);
                if (result != null) return result;
            }
            return new LeaveListPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveListPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<LeaveActionResponse> ApproveAsync(string requestId, string approverEmpcd, string? comment = null)
    {
        try
        {
            var payload  = new { REQUEST_ID = requestId, APPROVER_EMPCD = approverEmpcd, COMMENT = comment };
            var response = await _api.PostAsync("leave/approve", payload);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<LeaveActionResponse>(json)
                       ?? new LeaveActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new LeaveActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveActionResponse> RejectAsync(string requestId, string approverEmpcd, string? comment = null)
    {
        try
        {
            var payload  = new { REQUEST_ID = requestId, APPROVER_EMPCD = approverEmpcd, COMMENT = comment };
            var response = await _api.PostAsync("leave/reject", payload);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<LeaveActionResponse>(json)
                       ?? new LeaveActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new LeaveActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveScheduleResponse> GetTeamScheduleAsync(string approverEmpcd, int? month = null, int? year = null)
    {
        try
        {
            var q = new List<string> { $"approver_empcd={Uri.EscapeDataString(approverEmpcd)}" };
            if (month.HasValue) q.Add($"month={month}");
            if (year.HasValue)  q.Add($"year={year}");
            var response = await _api.GetAsync_Raw("leave/team-schedule", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LeaveScheduleResponse>(json);
                if (result != null) return result;
            }
            return new LeaveScheduleResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveScheduleResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<LeaveActionResponse> AssignAsync(LeaveAssignRequest request)
    {
        try
        {
            var response = await _api.PostAsync("leave/assign", request);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<LeaveActionResponse>(json)
                       ?? new LeaveActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new LeaveActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new LeaveActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<LeaveAssignPagedResponse> GetMyAssignmentsAsync(
        string assigner_empcd,
        string? status = null, string? search = null,
        string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 20)
    {
        try
        {
            var q = new List<string> { $"assigner_empcd={Uri.EscapeDataString(assigner_empcd)}" };
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
            var response = await _api.GetAsync_Raw("leave/my-assignments", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<LeaveAssignPagedResponse>(json);
                if (result != null) return result;
            }
            return new LeaveAssignPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveAssignPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<LeaveAssignmentLogPagedResponse> GetAssignmentLogAsync(
        string? assignerCd = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        string? status = null, string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(assignerCd)) q.Add($"assigner_cd={Uri.EscapeDataString(assignerCd)}");
            if (!string.IsNullOrEmpty(search))     q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))     q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))     q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))     q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(status))     q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(dateFrom))   q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))     q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
            var response = await _api.GetAsync_Raw("leave/assignment-log", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<LeaveAssignmentLogPagedResponse>(json);
                if (result != null) return result;
            }
            return new LeaveAssignmentLogPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveAssignmentLogPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<LeaveListPagedResponse> GetHRListAsync(
        string? status = null, string? source = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(source))   q.Add($"source={Uri.EscapeDataString(source)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
            var response = await _api.GetAsync_Raw("leave/hr-list", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LeaveListPagedResponse>(json);
                if (result != null) return result;
            }
            return new LeaveListPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveListPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<LeaveListPagedResponse> GetClerkListAsync(
        string  clerkEmpCd,
        string? status   = null, string? source  = null, string? search  = null,
        string? deptId   = null, string? lineId   = null, string? workId  = null,
        string? dateFrom = null, string? dateTo   = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string>();
            q.Add($"clerk_empcd={Uri.EscapeDataString(clerkEmpCd)}");
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(source))   q.Add($"source={Uri.EscapeDataString(source)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");
            var response = await _api.GetAsync_Raw("leave/clerk", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<LeaveListPagedResponse>(json);
                if (result != null) return result;
            }
            return new LeaveListPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new LeaveListPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<object?> GetAnnualLeaveBalanceAsync(string approverEmpcd)
    {
        try
        {
            var response = await _api.GetAsync_Raw("leave/annual-balance",
                $"approver_empcd={Uri.EscapeDataString(approverEmpcd)}");
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            }
            return new { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new { success = false, message = ex.Message }; }
    }

    public async Task<object?> GetMyBalanceAsync(string empcd)
    {
        try
        {
            var response = await _api.GetAsync_Raw("leave/my-balance",
                $"empcd={Uri.EscapeDataString(empcd)}");
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return Newtonsoft.Json.JsonConvert.DeserializeObject(json);
            }
            return new { success = false, message = "Lỗi kết nối API" };
        }
        catch (Exception ex) { return new { success = false, message = ex.Message }; }
    }

    public async Task<AdminEmpListResponse> GetAdminEmpListAsync(
        string? search = null, string? deptId = null, string? lineId = null, string? workId = null)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(search))  q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))  q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))  q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))  q.Add($"work_id={Uri.EscapeDataString(workId)}");
            var response = await _api.GetAsync_Raw("leave/admin-emp-list", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<AdminEmpListResponse>(json);
                if (result != null) return result;
            }
            return new AdminEmpListResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new AdminEmpListResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<AdminAssignResponse> AdminAssignAsync(LeaveAssignRequest request)
    {
        try
        {
            var response = await _api.PostAsync("leave/admin-assign", request);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<AdminAssignResponse>(json)
                       ?? new AdminAssignResponse { success = false, message = "Lỗi parse response" };
            }
            return new AdminAssignResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new AdminAssignResponse { success = false, message = ex.Message }; }
    }

    public async Task<AdminConfirmedLeavePagedResponse> GetAdminConfirmedLeavesAsync(
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null, string? status = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            q.Add($"page={page}"); q.Add($"page_size={pageSize}");
            var res = await _api.GetAsync_Raw("leave/admin-confirmed-leaves", string.Join("&", q));
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<AdminConfirmedLeavePagedResponse>(await res.Content.ReadAsStringAsync())
                       ?? new AdminConfirmedLeavePagedResponse { success = false };
            return new AdminConfirmedLeavePagedResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new AdminConfirmedLeavePagedResponse { success = false, message = ex.Message }; }
    }

    public async Task<AdminBulkDeleteResponse> AdminDeleteLeavesAsync(AdminBulkDeleteRequest request)
    {
        try
        {
            var res = await _api.PostAsync("leave/admin-delete-leaves", request);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<AdminBulkDeleteResponse>(await res.Content.ReadAsStringAsync())
                       ?? new AdminBulkDeleteResponse { success = false };
            return new AdminBulkDeleteResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new AdminBulkDeleteResponse { success = false, message = ex.Message }; }
    }
}
