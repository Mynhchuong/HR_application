using HR_web.Models.GatePass;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class GatePassService
{
    private readonly ApiService _api;

    public GatePassService(ApiService api)
    {
        _api = api;
    }

    public async Task<GpShiftInfoModel?> GetShiftInfoAsync(string empcd, string? regDate)
    {
        try
        {
            var q = string.IsNullOrEmpty(regDate)
                ? $"empcd={Uri.EscapeDataString(empcd)}"
                : $"empcd={Uri.EscapeDataString(empcd)}&reg_date={Uri.EscapeDataString(regDate)}";
            var result = await _api.GetAsync<GpShiftResponse>("gatepass/shift-info", q);
            return (result != null && result.success) ? result.data : null;
        }
        catch (Exception ex) { Console.WriteLine($"[GatePassService] GetShiftInfoAsync error: {ex.Message}"); return null; }
    }

    public async Task<GpActionResponse> SubmitAsync(GpSubmitRequest request)
    {
        try
        {
            var response = await _api.PostAsync("gatepass/submit", request);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<GpActionResponse>(json)
                       ?? new GpActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new GpActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new GpActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<GpMyRequestsPagedResponse> GetMyRequestsAsync(string empcd, int page = 1, int pageSize = 20, string? dateFrom = null, string? dateTo = null)
    {
        try
        {
            var q = new List<string>
            {
                $"empcd={Uri.EscapeDataString(empcd)}",
                $"page={page}",
                $"page_size={pageSize}"
            };
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            var response = await _api.GetAsync_Raw("gatepass/my-requests", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<GpMyRequestsPagedResponse>(json);
                if (result != null) return result;
            }
            return new GpMyRequestsPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new GpMyRequestsPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<GpActionResponse> UpdateAsync(GpUpdateRequest request)
    {
        try
        {
            var response = await _api.PutAsync<GpActionResponse>("gatepass/update", request);
            return response ?? new GpActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new GpActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<GpActionResponse> DeleteAsync(string requestId, string empcd)
    {
        try
        {
            var q = $"request_id={Uri.EscapeDataString(requestId)}&empcd={Uri.EscapeDataString(empcd)}";
            var ok = await _api.DeleteAsync("gatepass/delete", q);
            return ok ? new GpActionResponse { success = true, message = "Đã xoá yêu cầu" }
                      : new GpActionResponse { success = false, message = "Lỗi xoá yêu cầu" };
        }
        catch (Exception ex) { return new GpActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<GpListPagedResponse> GetSupervisorListAsync(
        string supervisorEmpcd,
        string? status = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string> { $"supervisor_empcd={Uri.EscapeDataString(supervisorEmpcd)}" };
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");

            var response = await _api.GetAsync_Raw("gatepass/supervisor", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<GpListPagedResponse>(json);
                if (result != null) return result;
            }
            return new GpListPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new GpListPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<GpActionResponse> ApproveAsync(string requestId, string approverEmpcd, string? comment = null)
    {
        try
        {
            var payload = new { REQUEST_ID = requestId, APPROVER_EMPCD = approverEmpcd, COMMENT = comment };
            var response = await _api.PostAsync("gatepass/approve", payload);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<GpActionResponse>(json)
                       ?? new GpActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new GpActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new GpActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<GpActionResponse> RejectAsync(string requestId, string approverEmpcd, string? comment = null)
    {
        try
        {
            var payload = new { REQUEST_ID = requestId, APPROVER_EMPCD = approverEmpcd, COMMENT = comment };
            var response = await _api.PostAsync("gatepass/reject", payload);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<GpActionResponse>(json)
                       ?? new GpActionResponse { success = false, message = "Lỗi parse response" };
            }
            return new GpActionResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new GpActionResponse { success = false, message = ex.Message }; }
    }

    public async Task<GpListPagedResponse> GetClerkListAsync(
        string clerkEmpcd,
        string? status = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string> { $"clerk_empcd={Uri.EscapeDataString(clerkEmpcd)}" };
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");

            var response = await _api.GetAsync_Raw("gatepass/clerk", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<GpListPagedResponse>(json);
                if (result != null) return result;
            }
            return new GpListPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new GpListPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<GpListPagedResponse> GetHRListAsync(
        string? status = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");

            var response = await _api.GetAsync_Raw("gatepass/hr", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<GpListPagedResponse>(json);
                if (result != null) return result;
            }
            return new GpListPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex) { return new GpListPagedResponse { success = false, message = ex.Message, data = new() }; }
    }

    public async Task<AdminConfirmedGpPagedResponse> GetAdminConfirmedGpAsync(
        string? deptId = null, string? lineId = null, string? workId = null,
        string? dateFrom = null, string? dateTo = null, int page = 1, int pageSize = 50)
    {
        try
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            if (!string.IsNullOrEmpty(dateFrom)) q.Add($"date_from={Uri.EscapeDataString(dateFrom)}");
            if (!string.IsNullOrEmpty(dateTo))   q.Add($"date_to={Uri.EscapeDataString(dateTo)}");
            q.Add($"page={page}"); q.Add($"page_size={pageSize}");
            var res = await _api.GetAsync_Raw("gatepass/admin-confirmed-gp", string.Join("&", q));
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<AdminConfirmedGpPagedResponse>(await res.Content.ReadAsStringAsync())
                       ?? new AdminConfirmedGpPagedResponse { success = false };
            return new AdminConfirmedGpPagedResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new AdminConfirmedGpPagedResponse { success = false, message = ex.Message }; }
    }

    public async Task<HR_web.Models.Leave.AdminBulkDeleteResponse> AdminDeleteGpAsync(HR_web.Models.Leave.AdminBulkDeleteRequest request)
    {
        try
        {
            var res = await _api.PostAsync("gatepass/admin-delete-gp", request);
            if (res?.IsSuccessStatusCode == true)
                return JsonConvert.DeserializeObject<HR_web.Models.Leave.AdminBulkDeleteResponse>(await res.Content.ReadAsStringAsync())
                       ?? new HR_web.Models.Leave.AdminBulkDeleteResponse { success = false };
            return new HR_web.Models.Leave.AdminBulkDeleteResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new HR_web.Models.Leave.AdminBulkDeleteResponse { success = false, message = ex.Message }; }
    }
}
