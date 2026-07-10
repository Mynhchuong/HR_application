using HR_api.Models.Training;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// Teacher-specific endpoints. Route guard đơn giản: gọi service check EMPCD có thuộc HR_TRAINING_CLASS_TEACHER
// của target Class không. Chi tiết auth pattern xem training_plan.md §7 (TrainingAuthHelper — sẽ làm Phase 5).
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

    public TrainingTeachController(
        TrainingSessionService session,
        TrainingMaterialService material,
        TrainingQAService qa,
        TrainingClassService cls,
        TrainingTestService test,
        TrainingAttemptService attempt)
    {
        _session  = session;
        _material = material;
        _qa       = qa;
        _class    = cls;
        _test     = test;
        _attempt  = attempt;
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
        var mine = all.Where(c => classIds.Contains(c.ID)).ToList();
        return Ok(new { success = true, data = mine });
    }

    // GET /apiHR/TrainingTeach/session/{id}/attendance — teacher view
    [HttpGet("session/{id}/attendance")]
    public async Task<IActionResult> SessionAttendance(int id)
    {
        var v = await _session.GetAttendanceViewAsync(id);
        if (v.SESSION == null) return Ok(new { success = false, message = "Không tìm thấy session" });
        return Ok(new { success = true, data = v });
    }

    // POST /apiHR/TrainingTeach/session/confirm
    [HttpPost("session/confirm")]
    public async Task<IActionResult> ConfirmAttendance([FromBody] ConfirmAttendanceRequest req)
    {
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

    // GET /apiHR/TrainingTeach/class/{id}/absent-stats
    [HttpGet("class/{id}/absent-stats")]
    public async Task<IActionResult> AbsentStats(int id)
    {
        var data = await _session.GetAbsentStatsAsync(id);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingTeach/enrollment/drop
    [HttpPost("enrollment/drop")]
    public async Task<IActionResult> DropStudent([FromBody] DropStudentRequest req)
    {
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
        try
        {
            await _test.PublishAsync(req.ID, req.LOGIN_USER);
            return Ok(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/TrainingTeach/test/{id}/pending-grade — attempts có ESSAY chưa chấm
    [HttpGet("test/{id}/pending-grade")]
    public async Task<IActionResult> PendingGrade(int id)
    {
        var data = await _attempt.GetPendingGradeAsync(id);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/TrainingTeach/test/grade — chấm 1 câu ESSAY
    [HttpPost("test/grade")]
    public async Task<IActionResult> Grade([FromBody] GradeAnswerRequest req)
    {
        var (ok, err) = await _attempt.GradeAnswerAsync(req);
        return Ok(new { success = ok, message = err });
    }
}
