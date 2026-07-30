using HR_api.Helpers;
using HR_api.Models.Training;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Controllers;

// HR/Admin endpoints cho Training (Course + Class + Enrollment + Group).
// Xem training_plan.md §4.3 để biết đầy đủ endpoint list.
[ApiController]
[Route("apiHR/[controller]")]
public partial class TrainingAdminController : ControllerBase
{
    private readonly TrainingCourseService _course;
    private readonly TrainingClassService _class;
    private readonly TrainingEnrollmentService _enroll;
    private readonly TrainingSessionService _session;
    private readonly TrainingMaterialService _material;
    private readonly TrainingTestService _test;
    private readonly TrainingAttemptService _attempt;
    private readonly TrainingReviewService _review;
    private readonly TrainingCompletionService _completion;
    private readonly TrainingReportService _report;
    private readonly TrainingAuthHelper _auth;
    private readonly TrainingTeamService _team;

    public TrainingAdminController(
        TrainingCourseService course,
        TrainingClassService cls,
        TrainingEnrollmentService enroll,
        TrainingSessionService session,
        TrainingMaterialService material,
        TrainingTestService test,
        TrainingAttemptService attempt,
        TrainingReviewService review,
        TrainingCompletionService completion,
        TrainingReportService report,
        TrainingAuthHelper auth,
        TrainingTeamService team)
    {
        _course = course;
        _class = cls;
        _enroll = enroll;
        _session = session;
        _material = material;
        _test = test;
        _attempt = attempt;
        _review = review;
        _completion = completion;
        _report = report;
        _auth = auth;
        _team = team;
    }

    [HttpGet("course/list")]
    public async Task<IActionResult> CourseList(
        [FromQuery] string? mode,
        [FromQuery] int? active,
        [FromQuery] string? search)
    {
        var data = await _course.ListAsync(mode, active, search);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/course/detail?id=1
    [HttpGet("course/detail")]
    public async Task<IActionResult> CourseDetail([FromQuery] int id)
    {
        var c = await _course.GetDetailAsync(id);
        if (c == null) return Ok(new { success = false, message = "Không tìm thấy khoá học" });
        return Ok(new { success = true, data = c });
    }

    // POST /apiHR/TrainingAdmin/course/save
    [HttpPost("course/save")]
    public async Task<IActionResult> CourseSave([FromBody] SaveCourseRequest req)
    {
        try
        {
            var id = await _course.SaveAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/course/archive → IS_ACTIVE=0 (ẩn khỏi dropdown khi tạo Class mới)
    [HttpPost("course/archive")]
    public async Task<IActionResult> CourseArchive([FromBody] ArchiveCourseRequest req)
    {
        try
        {
            await _course.ArchiveAsync(req, active: false);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/course/unarchive → IS_ACTIVE=1
    [HttpPost("course/unarchive")]
    public async Task<IActionResult> CourseUnarchive([FromBody] ArchiveCourseRequest req)
    {
        try
        {
            await _course.ArchiveAsync(req, active: true);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/course/delete
    [HttpPost("course/delete")]
    public async Task<IActionResult> CourseDelete([FromBody] ArchiveCourseRequest req)
    {
        try
        {
            await _course.DeleteAsync(req.ID, req.LOGIN_USER ?? "HR");
            return Ok(new { success = true, message = "Đã xóa khóa học thành công." });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingAdmin/course/session-templates?courseId=1
    [HttpGet("course/session-templates")]
    public async Task<IActionResult> SessionTemplateList([FromQuery] int courseId)
    {
        var data = await _course.GetSessionTemplatesAsync(courseId);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/course/session-templates/save — bulk replace
    [HttpPost("course/session-templates/save")]
    public async Task<IActionResult> SessionTemplateSave([FromBody] SaveSessionTemplateRequest req)
    {
        try
        {
            var count = await _course.SaveSessionTemplatesAsync(req);
            return Ok(new { success = true, data = new { count } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLASS
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/TrainingAdmin/class/list?status=DRAFT&courseId=1&search=
    [HttpGet("class/list")]
    public async Task<IActionResult> ClassList(
        [FromQuery] string? status,
        [FromQuery] int? courseId,
        [FromQuery] string? search)
    {
        var data = await _class.ListAsync(status, courseId, search);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/class/detail?id=1
    [HttpGet("class/detail")]
    public async Task<IActionResult> ClassDetail([FromQuery] int id)
    {
        var c = await _class.GetDetailAsync(id);
        if (c == null) return Ok(new { success = false, message = "Không tìm thấy lớp" });
        var sessions    = await _class.GetSessionsAsync(id);
        var teachers    = await _class.GetTeachersAsync(id);
        var groups      = await _class.GetGroupsAsync(id);
        var enrollments = await _enroll.ListAsync(id, null, null);
        var materials   = await _material.ListByClassAsync(id);
        return Ok(new { success = true, data = new { cls = c, sessions, teachers, groups, enrollments, materials } });
    }

    // POST /apiHR/TrainingAdmin/class/save
    [HttpPost("class/save")]
    public async Task<IActionResult> ClassSave([FromBody] SaveClassRequest req)
    {
        try
        {
            var id = await _class.SaveAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
        catch (OracleException)
        {
            // Không để lộ chi tiết Oracle (VD ORA-02290 check constraint) ra ngoài — trả 400 thân thiện.
            return StatusCode(400, new { success = false, message = "Dữ liệu lớp học không hợp lệ (vi phạm ràng buộc dữ liệu)." });
        }
    }

    // POST /apiHR/TrainingAdmin/class/publish-registration — DRAFT → OPEN_FOR_REGISTRATION (OPEN mode only)
    // Idempotent (§4.2): bấm lại khi đã publish rồi → success:true, ALREADY_PUBLISHED:true, không lỗi.
    [HttpPost("class/publish-registration")]
    public async Task<IActionResult> ClassPublishRegistration([FromBody] ChangeClassStatusRequest req)
    {
        try
        {
            var result = await _class.PublishRegistrationAsync(req.ID, req.LOGIN_USER, req.REGISTER_URL);
            var message = result.ALREADY_PUBLISHED
                ? "Lớp đã publish trước đó."
                : "Publish thành công.";
            return Ok(new { success = true, message, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/{id}/remind-review — nhắc mềm học viên ENROLLED chưa nộp review
    [HttpPost("class/{id}/remind-review")]
    public async Task<IActionResult> ClassRemindReview(int id)
    {
        try
        {
            var count = await _review.RemindPendingReviewsAsync(id);
            return Ok(new { success = true, message = count > 0 ? $"Đã nhắc {count} học viên chưa đánh giá." : "Tất cả học viên đã đánh giá rồi!", data = new { count } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/finalize-enrollment — chốt DS → SCHEDULED
    // Idempotent (§4.2): bấm lại khi Class đã SCHEDULED/IN_PROGRESS → không lỗi, không đổi status,
    // chỉ re-enqueue TRAINING_ASSIGNED cho học viên chưa nhận noti nào.
    [HttpPost("class/finalize-enrollment")]
    public async Task<IActionResult> ClassFinalize([FromBody] ChangeClassStatusRequest req)
    {
        try
        {
            var result = await _class.FinalizeEnrollmentAsync(req.ID, req.LOGIN_USER);
            var message = result.ALREADY_PUBLISHED
                ? $"Đã publish trước đó — đã gửi lại thông báo cho {result.NOTIFIED_COUNT} học viên chưa nhận."
                : $"Chốt danh sách thành công — đã gửi thông báo cho {result.NOTIFIED_COUNT} học viên.";
            return Ok(new { success = true, message, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/cancel
    [HttpPost("class/cancel")]
    public async Task<IActionResult> ClassCancel([FromBody] ChangeClassStatusRequest req)
    {
        try
        {
            await _class.CancelAsync(req.ID, req.LOGIN_USER);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/close — COMPLETED → CLOSED
    [HttpPost("class/close")]
    public async Task<IActionResult> ClassClose([FromBody] ChangeClassStatusRequest req)
    {
        try
        {
            await _class.CloseAsync(req.ID, req.LOGIN_USER);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/delete — xóa vĩnh viễn Lớp học
    [HttpPost("class/delete")]
    public async Task<IActionResult> ClassDelete([FromBody] ChangeClassStatusRequest req)
    {
        try
        {
            await _class.DeleteAsync(req.ID, req.LOGIN_USER);
            return Ok(new { success = true, message = "Đã xóa vĩnh viễn lớp học!" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─── Teacher ──────────────────────────────────────────────────

    // POST /apiHR/TrainingAdmin/class/assign-teacher
    [HttpPost("class/assign-teacher")]
    public async Task<IActionResult> AssignTeacher([FromBody] AssignTeacherRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền thực hiện chức năng này" });
        }

        try
        {
            await _class.AssignTeacherAsync(req);
            _auth.InvalidateActiveTeacher(req.EMPCD);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/remove-teacher
    [HttpPost("class/remove-teacher")]
    public async Task<IActionResult> RemoveTeacher([FromBody] RemoveTeacherRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền thực hiện chức năng này" });
        }

        await _class.RemoveTeacherAsync(req);
        _auth.InvalidateActiveTeacher(req.EMPCD);
        return Ok(new { success = true });
    }

    // ─── Group (§5b) ───────────────────────────────────────────────

    // GET /apiHR/TrainingAdmin/class/{id}/groups
    [HttpGet("class/{id}/groups")]
    public async Task<IActionResult> GroupList(int id)
    {
        var data = await _class.GetGroupsAsync(id);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/class/group/save
    [HttpPost("class/group/save")]
    public async Task<IActionResult> GroupSave([FromBody] SaveGroupRequest req)
    {
        try
        {
            var id = await _class.SaveGroupAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/group/delete
    [HttpPost("class/group/delete")]
    public async Task<IActionResult> GroupDelete([FromBody] DeleteGroupRequest req)
    {
        try
        {
            await _class.DeleteGroupAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/class/group/auto-split
    [HttpPost("class/group/auto-split")]
    public async Task<IActionResult> GroupAutoSplit([FromBody] AutoSplitGroupRequest req)
    {
        try
        {
            var updated = await _class.AutoSplitGroupAsync(req);
            return Ok(new { success = true, data = new { updated } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ENROLLMENT
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/TrainingAdmin/class/{classId}/enrollments?status=&groupId=
    [HttpGet("class/{classId}/enrollments")]
    public async Task<IActionResult> EnrollmentList(int classId, [FromQuery] string? status, [FromQuery] int? groupId)
    {
        var data = await _enroll.ListAsync(classId, status, groupId);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/enrollment/assign — bulk assign SOURCE='ASSIGNED', STATUS='ENROLLED'
    [HttpPost("enrollment/assign")]
    public async Task<IActionResult> EnrollmentAssign([FromBody] BulkAssignRequest req)
    {
        try
        {
            var result = await _enroll.BulkAssignAsync(req);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/enrollment/pre-assign — §3.3 (giống assign, semantic khác cho OPEN mode)
    [HttpPost("enrollment/pre-assign")]
    public async Task<IActionResult> EnrollmentPreAssign([FromBody] BulkPreAssignRequest req)
    {
        try
        {
            var result = await _enroll.BulkPreAssignAsync(req);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/enrollment/approve — PENDING_APPROVAL → ENROLLED
    [HttpPost("enrollment/approve")]
    public async Task<IActionResult> EnrollmentApprove([FromBody] ApproveEnrollmentRequest req)
    {
        try
        {
            await _enroll.ApproveAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/enrollment/reject — PENDING_APPROVAL → REJECTED
    [HttpPost("enrollment/reject")]
    public async Task<IActionResult> EnrollmentReject([FromBody] RejectEnrollmentRequest req)
    {
        try
        {
            await _enroll.RejectAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/enrollment/assign-group — bulk gán GROUP_ID
    [HttpPost("enrollment/assign-group")]
    public async Task<IActionResult> EnrollmentAssignGroup([FromBody] AssignGroupRequest req)
    {
        try
        {
            var updated = await _enroll.AssignGroupAsync(req);
            return Ok(new { success = true, data = new { updated } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/enrollment/remove — xóa học viên khỏi lớp
    [HttpPost("enrollment/remove")]
    public async Task<IActionResult> EnrollmentRemove([FromBody] RemoveEnrollmentRequest req)
    {
        try
        {
            await _enroll.RemoveAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SESSION (admin)
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/TrainingAdmin/class/{classId}/sessions
    [HttpGet("class/{classId}/sessions")]
    public async Task<IActionResult> SessionList(int classId)
    {
        var data = await _session.ListByClassAsync(classId);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/session/save
    [HttpPost("session/save")]
    public async Task<IActionResult> SessionSave([FromBody] SaveSessionRequest req)
    {
        try
        {
            var id = await _session.SaveAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/session/bulk-import — import Excel nhiều buổi 1 lần (HR_web đã parse file)
    [HttpPost("session/bulk-import")]
    public async Task<IActionResult> SessionBulkImport([FromBody] BulkImportSessionsRequest req)
    {
        var result = await _session.BulkImportAsync(req);
        return Ok(new { success = true, data = result });
    }

    // POST /apiHR/TrainingAdmin/session/reschedule
    [HttpPost("session/reschedule")]
    public async Task<IActionResult> SessionReschedule([FromBody] RescheduleSessionRequest req)
    {
        try
        {
            await _session.RescheduleAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/session/cancel
    [HttpPost("session/cancel")]
    public async Task<IActionResult> SessionCancel([FromBody] CancelSessionRequest req)
    {
        try
        {
            await _session.CancelAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingAdmin/session/{id}/attendance
    [HttpGet("session/{id}/attendance")]
    public async Task<IActionResult> SessionAttendance(int id)
    {
        var v = await _session.GetAttendanceViewAsync(id);
        if (v.SESSION == null) return Ok(new { success = false, message = "Không tìm thấy session" });
        return Ok(new { success = true, data = v });
    }

    // ═══════════════════════════════════════════════════════════════
    //  MATERIAL (admin — CRUD cả 2 level)
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/TrainingAdmin/course/{id}/materials
    [HttpGet("course/{id}/materials")]
    public async Task<IActionResult> CourseMaterials(int id)
    {
        var data = await _material.ListByCourseAsync(id);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/class/{id}/materials
    [HttpGet("class/{id}/materials")]
    public async Task<IActionResult> ClassMaterials(int id)
    {
        var data = await _material.ListByClassAsync(id, null);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/material/save
    [HttpPost("material/save")]
    public async Task<IActionResult> MaterialSave([FromBody] SaveMaterialRequest req)
    {
        try
        {
            var id = await _material.SaveAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/material/delete
    [HttpPost("material/delete")]
    public async Task<IActionResult> MaterialDelete([FromBody] DeleteMaterialRequest req)
    {
        try
        {
            await _material.DeleteAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEST — HR quản lý test template + report
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/TrainingAdmin/test/list?classId=&templateCourseId=&isTemplate=&status=
    [HttpGet("test/list")]
    public async Task<IActionResult> TestList(
        [FromQuery] int? classId,
        [FromQuery] int? templateCourseId,
        [FromQuery] int? isTemplate,
        [FromQuery] string? status)
    {
        var data = await _test.ListAsync(classId, templateCourseId, isTemplate, status);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/test/detail?id=
    [HttpGet("test/detail")]
    public async Task<IActionResult> TestDetail([FromQuery] int id)
    {
        var t = await _test.GetDetailAsync(id);
        if (t == null) return Ok(new { success = false, message = "Không tìm thấy test" });
        var questions = await _test.GetQuestionsAsync(id, forStudent: false);   // admin thấy IS_CORRECT
        return Ok(new { success = true, data = new { test = t, questions } });
    }

    // POST /apiHR/TrainingAdmin/test/save
    [HttpPost("test/save")]
    public async Task<IActionResult> TestSave([FromBody] SaveTestRequest req)
    {
        try
        {
            var id = await _test.SaveAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/test/questions/save — bulk replace Q + O
    [HttpPost("test/questions/save")]
    public async Task<IActionResult> TestQuestionsSave([FromBody] SaveTestQuestionsRequest req)
    {
        try
        {
            await _test.SaveQuestionsAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/test/publish — DRAFT → PUBLISHED
    [HttpPost("test/publish")]
    public async Task<IActionResult> TestPublish([FromBody] ChangeTestStatusRequest req)
    {
        try
        {
            await _test.PublishAsync(req.ID, req.LOGIN_USER, req.AVAILABLE_FROM, req.AVAILABLE_TO);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/test/close — PUBLISHED/OPEN → CLOSED
    [HttpPost("test/close")]
    public async Task<IActionResult> TestClose([FromBody] ChangeTestStatusRequest req)
    {
        try
        {
            await _test.CloseAsync(req.ID, req.LOGIN_USER);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingAdmin/test/auto-submit-expired — chạy tay cho tới khi Phase 5 làm batch job
    [HttpPost("test/auto-submit-expired")]
    public async Task<IActionResult> TestAutoSubmitExpired()
    {
        var n = await _attempt.AutoSubmitExpiredAsync();
        return Ok(new { success = true, data = new { finalized = n } });
    }

    // ═══════════════════════════════════════════════════════════════
    //  REVIEW REPORT (§14.4) + FINALIZE + CERTIFICATE
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/TrainingAdmin/review/report?classId=
    [HttpGet("review/report")]
    public async Task<IActionResult> ReviewReport([FromQuery] int classId)
    {
        var data = await _review.GetReportAsync(classId);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/class/finalize — chốt lớp: compute completion cho từng enrollment (§7)
    [HttpPost("class/finalize")]
    public async Task<IActionResult> ClassFinalize([FromBody] FinalizeClassRequest req)
    {
        try
        {
            var result = await _completion.FinalizeClassAsync(req);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingAdmin/class/{id}/failed-students — DS học viên FAILED + lý do rớt cụ thể (§14.1 HR yêu cầu)
    [HttpGet("class/{id}/failed-students")]
    public async Task<IActionResult> ClassFailedStudents(int id)
    {
        var data = await _completion.GetFailedStudentsAsync(id);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/certificate/list?classId=&courseId=&deptcd=&linecd=&workcd=&from=&to=&empcd=&loginUser=&page=&page_size=
    [HttpGet("certificate/list")]
    public async Task<IActionResult> CertificateList(
        [FromQuery] int? classId,
        [FromQuery] int? courseId,
        [FromQuery] string? deptcd,
        [FromQuery] string? linecd,
        [FromQuery] string? workcd,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? empcd,
        [FromQuery] string? loginUser,
        [FromQuery] int page = 1,
        [FromQuery] int page_size = 50)
    {
        string? scopeSql = null;
        List<OracleParameter>? scopeParams = null;
        string? searchEmpcd = empcd;

        if (!string.IsNullOrEmpty(loginUser))
        {
            var isHrOrAdmin = await _auth.IsHrOrAdminAsync(loginUser);
            if (!isHrOrAdmin)
            {
                var hasScope = await _team.HasScopeAsync(loginUser);
                if (hasScope)
                {
                    var scope = OTScopeFilterHelper.ForScopeByTuple(loginUser, empAlias: "EC", prefix: "CF");
                    scopeSql = scope.SqlClause;
                    scopeParams = scope.Params;
                }
                else
                {
                    searchEmpcd = loginUser;
                }
            }
        }

        var (total, data) = await _completion.ListCertificatesAsync(
            classId, courseId, deptcd, linecd, workcd, from, to, searchEmpcd, scopeSql, scopeParams, page, page_size);

        return Ok(new
        {
            success = true,
            total,
            page,
            page_size,
            total_pages = page_size > 0 ? (int)Math.Ceiling((double)total / page_size) : 0,
            data
        });
    }

    // POST /apiHR/TrainingAdmin/certificate/revoke — HR recall certificate
    [HttpPost("certificate/revoke")]
    public async Task<IActionResult> CertificateRevoke([FromBody] RevokeCertificateRequest req)
    {
        try
        {
            await _completion.RevokeCertificateAsync(req.CLASS_ID, req.EMPCD, req.LOGIN_USER);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}

public class RevokeCertificateRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

// ═════════════════════════════════════════════════════════════════════
// Partial — new endpoints (T1 clone, T2 express, T3 recover, T5 reports)
// ═════════════════════════════════════════════════════════════════════

public partial class TrainingAdminController
{
    // POST /apiHR/TrainingAdmin/class/clone-from-course — §15b Cách 1
    [HttpPost("class/clone-from-course")]
    public async Task<IActionResult> ClassCloneFromCourse([FromBody] CloneFromCourseRequest req)
    {
        try
        {
            var id = await _class.CloneFromCourseAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
        catch (OracleException)
        {
            return StatusCode(400, new { success = false, message = "Không tạo được lớp từ giáo trình (vi phạm ràng buộc dữ liệu)." });
        }
    }

    // POST /apiHR/TrainingAdmin/class/clone-from-class — §15b Cách 2: sao chép từ Class trước sang đợt mới
    [HttpPost("class/clone-from-class")]
    public async Task<IActionResult> ClassCloneFromClass([FromBody] CloneFromClassRequest req)
    {
        try
        {
            var id = await _class.CloneFromClassAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
        catch (OracleException ex)
        {
            // Trước đây catch (OracleException) không bind biến — message ORA-xxxxx thật (tên
            // constraint bị vi phạm) bị nuốt mất hoàn toàn, không cách nào debug được nguyên nhân
            // thật. Giờ trả kèm ex.Message (an toàn vì đây là API nội bộ cho Admin/HR).
            return StatusCode(400, new { success = false, message = $"Không sao chép được lớp học (vi phạm ràng buộc dữ liệu): {ex.Message}" });
        }
    }

    // POST /apiHR/TrainingAdmin/class/express-create — §4.2 modal 1 form
    [HttpPost("class/express-create")]
    public async Task<IActionResult> ClassExpressCreate([FromBody] ExpressCreateRequest req)
    {
        try
        {
            var id = await _class.ExpressCreateAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
        catch (OracleException)
        {
            return StatusCode(400, new { success = false, message = "Không tạo được lớp học nhanh (vi phạm ràng buộc dữ liệu)." });
        }
    }

    // POST /apiHR/TrainingAdmin/enrollment/recover — §16 chuyển DROPPED → ENROLLED
    [HttpPost("enrollment/recover")]
    public async Task<IActionResult> EnrollmentRecover([FromBody] RecoverEnrollmentRequest req)
    {
        try
        {
            await _enroll.RecoverAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─── §14 Reports ───────────────────────────────────────────

    // GET /apiHR/TrainingAdmin/report/class/{id} — §14.1
    [HttpGet("report/class/{id}")]
    public async Task<IActionResult> ReportClass(int id)
    {
        var data = await _report.GetClassReportAsync(id);
        if (data == null) return Ok(new { success = false, message = "Không tìm thấy Class" });
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/report/course/{id} — bảng từng lớp thuộc khóa + tổng cộng
    [HttpGet("report/course/{id}")]
    public async Task<IActionResult> ReportCourse(int id)
    {
        var data = await _report.GetCourseReportAsync(id);
        if (data == null) return Ok(new { success = false, message = "Không tìm thấy Khóa học" });
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/report/attendance/{classId} — §14.2
    [HttpGet("report/attendance/{classId}")]
    public async Task<IActionResult> ReportAttendance(int classId)
    {
        var data = await _report.GetAttendanceMatrixAsync(classId);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingAdmin/report/test/{testId} — §14.3
    [HttpGet("report/test/{testId}")]
    public async Task<IActionResult> ReportTest(int testId)
    {
        var data = await _report.GetTestReportAsync(testId);
        if (data == null) return Ok(new { success = false, message = "Không tìm thấy Test" });
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingAdmin/test/retake-grant — HR cấp thêm 1 lượt thi
    // (bận / lỗi kỹ thuật lúc thi / thi rớt) cho cả bài thi thường lẫn bài thi cuối khóa.
    [HttpPost("test/retake-grant")]
    public async Task<IActionResult> GrantRetake([FromBody] GrantRetakeRequest req)
    {
        var (ok, err) = await _attempt.GrantRetakeAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // POST /apiHR/TrainingAdmin/test/retake-grant-all-failed — cấp thi lại 1 lượt cho
    // TẤT CẢ học viên đang rớt bài thi này (lượt gần nhất đã nộp, IS_PASS=0, chưa có PENDING grant).
    [HttpPost("test/retake-grant-all-failed")]
    public async Task<IActionResult> GrantRetakeAllFailed([FromBody] GrantRetakeAllRequest req)
    {
        var (ok, err, granted) = await _attempt.GrantRetakeAllFailedAsync(req);
        return Ok(new { success = ok, message = err, granted });
    }
}
