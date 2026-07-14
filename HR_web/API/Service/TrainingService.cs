using HR_web.Models.Training;

namespace HR_web.API.Service;

// Wrapper cho các endpoint apiHR/Training/team/* — team schedule view (§14.5).
public class TrainingService
{
    private readonly ApiService _api;

    public TrainingService(ApiService api) { _api = api; }

    public async Task<List<TeamScheduleItem>> GetTeamScheduleAsync(
        string empcd, DateTime? from, DateTime? to, string? status)
    {
        try
        {
            var q = $"empcd={empcd}";
            if (from.HasValue)                     q += $"&from={from.Value:yyyy-MM-dd}";
            if (to.HasValue)                       q += $"&to={to.Value:yyyy-MM-dd}";
            if (!string.IsNullOrWhiteSpace(status)) q += $"&status={status}";
            var res = await _api.GetAsync<TeamScheduleResponse>("Training/team/schedule", q);
            return res?.data ?? new();
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetTeamSchedule error: {ex.Message}"); return new(); }
    }

    public async Task<List<TeamScheduleItem>> GetTodayAsync(string empcd)
    {
        try
        {
            var res = await _api.GetAsync<TeamScheduleResponse>("Training/team/today", $"empcd={empcd}");
            return res?.data ?? new();
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetToday error: {ex.Message}"); return new(); }
    }

    // ═══════════════════════════════════════════════════════════════
    //  §14 REPORTS
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<ClassListItem>> GetClassListAsync(string? status = null, string? search = null, int? courseId = null)
    {
        try
        {
            var q = "";
            if (!string.IsNullOrWhiteSpace(status)) q += $"status={status}&";
            if (!string.IsNullOrWhiteSpace(search)) q += $"search={Uri.EscapeDataString(search)}&";
            if (courseId.HasValue)                 q += $"courseId={courseId.Value}&";
            var res = await _api.GetAsync<ClassListResponse>("TrainingAdmin/class/list", q);
            return res?.data ?? new();
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetClassList error: {ex.Message}"); return new(); }
    }

    public async Task<ReportClassModel?> GetClassReportAsync(int classId)
    {
        try
        {
            var res = await _api.GetAsync<ReportClassResponse>($"TrainingAdmin/report/class/{classId}", "");
            return res?.data;
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetClassReport error: {ex.Message}"); return null; }
    }

    public async Task<ReportAttendanceMatrix?> GetAttendanceMatrixAsync(int classId)
    {
        try
        {
            var res = await _api.GetAsync<AttendanceResponse>($"TrainingAdmin/report/attendance/{classId}", "");
            return res?.data;
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetAttendance error: {ex.Message}"); return null; }
    }

    public async Task<ReportTestModel?> GetTestReportAsync(int testId)
    {
        try
        {
            var res = await _api.GetAsync<ReportTestResponse>($"TrainingAdmin/report/test/{testId}", "");
            return res?.data;
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetTestReport error: {ex.Message}"); return null; }
    }

    public async Task<ReviewReportModel?> GetSatisfactionReportAsync(int classId)
    {
        try
        {
            var res = await _api.GetAsync<ReviewReportResponse>("TrainingAdmin/review/report", $"classId={classId}");
            return res?.data;
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] GetSatisfaction error: {ex.Message}"); return null; }
    }

    public async Task<bool> IsActiveTeacherAsync(string empcd)
    {
        try
        {
            var res = await _api.GetAsync<HasScopeResponse>("Training/is-active-teacher", $"empcd={empcd}");
            return res?.success == true && res.data;
        }
        catch (Exception ex) { Console.WriteLine($"[TrainingService] IsActiveTeacher error: {ex.Message}"); return false; }
    }

    public async Task<bool> CheckClassAccessAsync(int classId, string empcd)
    {
        try
        {
            var res = await _api.GetAsync<HasScopeResponse>($"TrainingTeach/class/{classId}/check-access", $"empcd={empcd}");
            return res?.success == true && res.data;
        }
        catch { return false; }
    }

    public async Task<bool> CheckSessionAccessAsync(int sessionId, string empcd)
    {
        try
        {
            var res = await _api.GetAsync<HasScopeResponse>($"TrainingTeach/session/{sessionId}/check-access", $"empcd={empcd}");
            return res?.success == true && res.data;
        }
        catch { return false; }
    }

    public async Task<bool> CheckTestAccessAsync(int testId, string empcd)
    {
        try
        {
            var res = await _api.GetAsync<HasScopeResponse>($"TrainingTeach/test/{testId}/check-access", $"empcd={empcd}");
            return res?.success == true && res.data;
        }
        catch { return false; }
    }

    public async Task<T?> GetFromApiAsync<T>(string endpoint, string q = "")
    {
        return await _api.GetAsync<T>(endpoint, q);
    }

    public async Task<HttpResponseMessage?> PostToApiAsync(string endpoint, object data)
    {
        return await _api.PostAsync(endpoint, data);
    }
}
