using HR_web.Models.AttendanceConfirm;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class AttendanceConfirmService
{
    private readonly ApiService _api;

    public AttendanceConfirmService(ApiService api)
    {
        _api = api;
    }

    public async Task<AttendanceMissingListResponse> GetMissingListAsync(
        string callerEmpcd, string? deptId = null, string? lineId = null, string? workId = null,
        string? search = null, string? status = null, string? missingType = null, string? fromDate = null, string? toDate = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var query = $"caller_empcd={callerEmpcd}&page={page}&page_size={pageSize}";
            if (!string.IsNullOrEmpty(deptId))     query += $"&dept_id={deptId}";
            if (!string.IsNullOrEmpty(lineId))     query += $"&line_id={lineId}";
            if (!string.IsNullOrEmpty(workId))     query += $"&work_id={workId}";
            if (!string.IsNullOrEmpty(search))     query += $"&search={Uri.EscapeDataString(search)}";
            if (!string.IsNullOrEmpty(status))     query += $"&status={status}";
            if (!string.IsNullOrEmpty(missingType))query += $"&missing_type={missingType}";
            if (!string.IsNullOrEmpty(fromDate))   query += $"&from_date={fromDate}";
            if (!string.IsNullOrEmpty(toDate))     query += $"&to_date={toDate}";

            var result = await _api.GetAsync<AttendanceMissingListResponse>("attendanceconfirm/missing-list", query);
            return result ?? new AttendanceMissingListResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new AttendanceMissingListResponse { success = false, message = ex.Message }; }
    }

    public async Task<MyPendingResponse> GetMyPendingAsync(string empcd, int days = 60)
    {
        try
        {
            var result = await _api.GetAsync<MyPendingResponse>("attendanceconfirm/my-pending", $"empcd={empcd}&days={days}");
            return result ?? new MyPendingResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new MyPendingResponse { success = false, message = ex.Message }; }
    }

    public async Task<MyDayResponse> GetMyDayAsync(string empcd, string workDate)
    {
        try
        {
            var result = await _api.GetAsync<MyDayResponse>("attendanceconfirm/my-day", $"empcd={empcd}&work_date={workDate}");
            return result ?? new MyDayResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new MyDayResponse { success = false, message = ex.Message }; }
    }

    public async Task<SimpleApiResponse> SubmitWorkerAsync(WorkerSubmitRequest req)
    {
        try
        {
            var response = await _api.PostAsync("attendanceconfirm/submit-worker", req);
            if (response != null)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<SimpleApiResponse>(json) ?? new SimpleApiResponse { success = false, message = "Lỗi parse response" };
            }
            return new SimpleApiResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new SimpleApiResponse { success = false, message = ex.Message }; }
    }

    public async Task<SimpleApiResponse> ManagerConfirmAsync(ManagerConfirmRequest req)
    {
        try
        {
            var response = await _api.PostAsync("attendanceconfirm/manager-confirm", req);
            if (response != null)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<SimpleApiResponse>(json) ?? new SimpleApiResponse { success = false, message = "Lỗi parse response" };
            }
            return new SimpleApiResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new SimpleApiResponse { success = false, message = ex.Message }; }
    }

    public async Task<AttendanceConfirmResponse> RequestConfirmAsync(RequestConfirmBulkRequest req)
    {
        try
        {
            var response = await _api.PostAsync("attendanceconfirm/request-confirm", req);
            if (response != null)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<AttendanceConfirmResponse>(json) ?? new AttendanceConfirmResponse { success = false, message = "Lỗi parse response" };
            }
            return new AttendanceConfirmResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new AttendanceConfirmResponse { success = false, message = ex.Message }; }
    }
}
