using HR_api.Helpers;
using HR_api.Models.Training;
using HR_api.Services;
using HR_api.Data;
using Oracle.ManagedDataAccess.Client;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// Teacher-specific endpoints.
[ApiController]
[Route("apiHR/[controller]")]
public class TrainingTeachController : ControllerBase
{
    private readonly TrainingSessionService _session;
    private readonly TrainingMaterialService _material;
    private readonly TrainingQAService _qa;
    private readonly TrainingClassService _class;
    private readonly TrainingTestService _test;
    private readonly TrainingAttemptService _attempt;
    private readonly TrainingAuthHelper _auth;
    private readonly OracleService _db;
    private readonly TrainingReportService _report;

    public TrainingTeachController(
        TrainingSessionService session,
        TrainingMaterialService material,
        TrainingQAService qa,
        TrainingClassService cls,
        TrainingTestService test,
        TrainingAttemptService attempt,
        TrainingAuthHelper auth,
        OracleService db,
        TrainingReportService report)
    {
        _session  = session;
        _material = material;
        _qa       = qa;
        _class    = cls;
        _test     = test;
        _attempt  = attempt;
        _auth     = auth;
        _db       = db;
        _report   = report;
    }

    // GET /apiHR/TrainingTeach/my-classes?empcd=
    [HttpGet("my-classes")]
    public async Task<IActionResult> MyClasses([FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        // List Class có EMPCD trong HR_TRAINING_CLASS_TEACHER — reuse _class.ListAsync filter theo teacher.
        var all = await _class.ListAsync(null, null, null);
        var teach = await _class.GetTeachersForEmpAsync(empcd);   // helper (thêm dưới đây)
        var classIds = teach.Select(t => t.CLASS_ID).ToHashSet();
        var mine = all.Where(c => classIds.Contains(c.ID) && c.STATUS != "CANCELLED").ToList();
        return Ok(new { success = true, data = mine });
    }

    // GET /apiHR/TrainingTeach/session/{id}/attendance?empcd= — teacher view
    [HttpGet("session/{id}/attendance")]
    public async Task<IActionResult> SessionAttendance(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        if (!await _auth.IsTeacherOfSessionAsync(empcd, id) && !await _auth.IsHrOrAdminAsync(empcd))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền truy cập buổi học này" });
        }

        var v = await _session.GetAttendanceViewAsync(id);
        if (v.SESSION == null) return Ok(new { success = false, message = "Không tìm thấy session" });
        return Ok(new { success = true, data = v });
    }

    // POST /apiHR/TrainingTeach/session/confirm
    [HttpPost("session/confirm")]
    public async Task<IActionResult> ConfirmAttendance([FromBody] ConfirmAttendanceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfSessionAsync(req.LOGIN_USER, req.SESSION_ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xác nhận điểm danh cho buổi học này" });
        }

        try
        {
            await _session.ConfirmAttendanceAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingTeach/session/confirm-batch
    [HttpPost("session/confirm-batch")]
    public async Task<IActionResult> ConfirmAttendanceBatch([FromBody] ConfirmAttendanceBatchRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfSessionAsync(req.LOGIN_USER, req.SESSION_ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xác nhận điểm danh cho buổi học này" });
        }

        try
        {
            var n = await _session.ConfirmAttendanceBatchAsync(req);
            return Ok(new { success = true, data = new { updated = n } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingTeach/class/{id}/absent-stats?empcd=
    [HttpGet("class/{id}/absent-stats")]
    public async Task<IActionResult> AbsentStats(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        if (!await _auth.IsTeacherAsync(empcd, id) && !await _auth.IsHrOrAdminAsync(empcd))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xem thống kê vắng của lớp học này" });
        }

        var data = await _session.GetAbsentStatsAsync(id);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingTeach/enrollment/drop
    [HttpPost("enrollment/drop")]
    public async Task<IActionResult> DropStudent([FromBody] DropStudentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherAsync(req.LOGIN_USER, req.CLASS_ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xoá học viên khỏi lớp học này" });
        }

        try
        {
            await _session.DropStudentAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingTeach/material/save — teacher upload material Class-level
    [HttpPost("material/save")]
    public async Task<IActionResult> MaterialSave([FromBody] SaveMaterialRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (req.CLASS_ID.HasValue)
        {
            if (!await _auth.IsTeacherAsync(req.LOGIN_USER, req.CLASS_ID.Value) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
            {
                return StatusCode(403, new { success = false, message = "Bạn không có quyền tải tài liệu lên lớp học này" });
            }
        }
        else
        {
            if (!await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
            {
                return StatusCode(403, new { success = false, message = "Chỉ có HR/Admin mới được tải tài liệu hệ thống" });
            }
        }

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

    // POST /apiHR/TrainingTeach/material/delete
    [HttpPost("material/delete")]
    public async Task<IActionResult> MaterialDelete([FromBody] DeleteMaterialRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfMaterialAsync(req.LOGIN_USER, req.ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xoá tài liệu này" });
        }

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

    // POST /apiHR/TrainingTeach/qa/answer
    [HttpPost("qa/answer")]
    public async Task<IActionResult> QAAnswer([FromBody] AnswerQuestionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfQuestionAsync(req.LOGIN_USER, req.ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền trả lời câu hỏi này" });
        }

        try
        {
            await _qa.AnswerAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingTeach/qa/delete
    [HttpPost("qa/delete")]
    public async Task<IActionResult> QADelete([FromBody] DeleteQuestionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfQuestionAsync(req.LOGIN_USER, req.ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xoá câu hỏi này" });
        }

        try
        {
            await _qa.DeleteAsync(req);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEST — teacher soạn + chấm ESSAY (§6)
    // ═══════════════════════════════════════════════════════════════

    // POST /apiHR/TrainingTeach/test/save — teacher soạn test cho Class mình dạy
    [HttpPost("test/save")]
    public async Task<IActionResult> TestSave([FromBody] SaveTestRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (req.CLASS_ID.HasValue)
        {
            if (!await _auth.IsTeacherAsync(req.LOGIN_USER, req.CLASS_ID.Value) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
            {
                return StatusCode(403, new { success = false, message = "Bạn không có quyền tạo/sửa bài kiểm tra cho lớp học này" });
            }
        }
        else
        {
            if (!await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
            {
                return StatusCode(403, new { success = false, message = "Chỉ có HR/Admin mới được tạo đề thi mẫu" });
            }
        }

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

    // POST /apiHR/TrainingTeach/test/questions/save
    [HttpPost("test/questions/save")]
    public async Task<IActionResult> TestQuestionsSave([FromBody] SaveTestQuestionsRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        // Phải là teacher CỦA CHÍNH Class chứa TEST_ID này (hoặc HR/Admin) — không dùng
        // IsActiveTeacherAsync (chỉ check "đang dạy lớp active nào đó", không gắn với TEST_ID
        // truyền vào) vì sẽ cho phép teacher lớp A sửa câu hỏi test của lớp B (đã phát hiện ở audit).
        if (!await _auth.IsTeacherOfTestAsync(req.LOGIN_USER, req.TEST_ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền sửa câu hỏi cho bài kiểm tra này" });
        }

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

    // POST /apiHR/TrainingTeach/test/publish
    [HttpPost("test/publish")]
    public async Task<IActionResult> TestPublish([FromBody] ChangeTestStatusRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfTestAsync(req.LOGIN_USER, req.ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xuất bản bài kiểm tra này" });
        }

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

    // POST /apiHR/TrainingTeach/test/update-schedule — sửa AVAILABLE_FROM/TO cho bài đã publish
    [HttpPost("test/update-schedule")]
    public async Task<IActionResult> TestUpdateSchedule([FromBody] ChangeTestStatusRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfTestAsync(req.LOGIN_USER, req.ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền sửa lịch bài kiểm tra này" });
        }

        try
        {
            await _test.UpdateScheduleAsync(req.ID, req.LOGIN_USER, req.AVAILABLE_FROM, req.AVAILABLE_TO);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingTeach/test/{id}/report?empcd= — điểm + chi tiết câu sai cho giáo viên đứng lớp
    [HttpGet("test/{id}/report")]
    public async Task<IActionResult> TestReport(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        if (!await _auth.IsTeacherOfTestAsync(empcd, id) && !await _auth.IsHrOrAdminAsync(empcd))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền xem báo cáo bài kiểm tra này" });
        }

        var data = await _report.GetTestReportAsync(id);
        if (data == null) return Ok(new { success = false, message = "Không tìm thấy bài kiểm tra" });
        return Ok(new { success = true, data });
    }

    // GET /apiHR/TrainingTeach/test/{id}/pending-grade?empcd= — attempts có ESSAY chưa chấm
    [HttpGet("test/{id}/pending-grade")]
    public async Task<IActionResult> PendingGrade(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        if (!await _auth.IsTeacherOfTestAsync(empcd, id) && !await _auth.IsHrOrAdminAsync(empcd))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền chấm bài kiểm tra này" });
        }

        // Truyền empcd để lọc theo nhóm GV phụ trách (GV nhóm chỉ thấy bài học viên nhóm mình)
        var data = await _attempt.GetPendingGradeAsync(id, empcd);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingTeach/test/grade — chấm 1 câu ESSAY
    [HttpPost("test/grade")]
    public async Task<IActionResult> Grade([FromBody] GradeAnswerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherOfAnswerAsync(req.LOGIN_USER, req.ANSWER_ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền chấm câu hỏi này" });
        }

        var (ok, err) = await _attempt.GradeAnswerAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // POST /apiHR/TrainingTeach/test/delete — chỉ xóa DRAFT
    [HttpPost("test/delete")]
    public async Task<IActionResult> TestDelete([FromBody] ChangeTestStatusRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        // Phải là teacher của Class chứa test này (hoặc HR/Admin) — trước đây dùng IsActiveTeacherAsync
        // (chỉ check "đang dạy lớp active nào đó") nên teacher lớp khác cũng xoá được test này.
        if (!await _auth.IsTeacherOfTestAsync(req.LOGIN_USER, req.ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
            return StatusCode(403, new { success = false, message = "Không có quyền xóa bài kiểm tra" });

        try
        {
            await _test.DeleteAsync(req.ID);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/TrainingTeach/class/set-final-test
    [HttpPost("class/set-final-test")]
    public async Task<IActionResult> SetFinalTest([FromBody] SetFinalTestRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LOGIN_USER))
            return Ok(new { success = false, message = "LOGIN_USER required" });

        if (!await _auth.IsTeacherAsync(req.LOGIN_USER, req.CLASS_ID) && !await _auth.IsHrOrAdminAsync(req.LOGIN_USER))
        {
            return StatusCode(403, new { success = false, message = "Bạn không có quyền sửa cấu hình lớp học này" });
        }

        try
        {
            // §16 ràng buộc code layer: TEST_ID phải thuộc đúng Class này + IS_TEMPLATE=0.
            await _class.ValidateFinalTestAsync(req.TEST_ID, req.CLASS_ID);

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_CLASS
                   SET FINAL_TEST_ID = :FTID
                 WHERE ID = :CID",
                new OracleParameter("FTID", (object?)req.TEST_ID ?? DBNull.Value),
                new OracleParameter("CID",  req.CLASS_ID));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingTeach/class/{id}/check-access
    [HttpGet("class/{id}/check-access")]
    public async Task<IActionResult> CheckClassAccess(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        var hasAccess = await _auth.IsTeacherAsync(empcd, id) || await _auth.IsHrOrAdminAsync(empcd);
        return Ok(new { success = true, data = hasAccess });
    }

    // GET /apiHR/TrainingTeach/session/{id}/check-access
    [HttpGet("session/{id}/check-access")]
    public async Task<IActionResult> CheckSessionAccess(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        var hasAccess = await _auth.IsTeacherOfSessionAsync(empcd, id) || await _auth.IsHrOrAdminAsync(empcd);
        return Ok(new { success = true, data = hasAccess });
    }

    // GET /apiHR/TrainingTeach/test/{id}/check-access
    [HttpGet("test/{id}/check-access")]
    public async Task<IActionResult> CheckTestAccess(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });

        var hasAccess = await _auth.IsTeacherOfTestAsync(empcd, id) || await _auth.IsHrOrAdminAsync(empcd);
        return Ok(new { success = true, data = hasAccess });
    }
}

public class SetFinalTestRequest
{
    public int CLASS_ID { get; set; }
    public int? TEST_ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}
