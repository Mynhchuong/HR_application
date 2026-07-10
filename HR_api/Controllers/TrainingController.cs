using HR_api.Models.Training;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// User-facing endpoints (student view).
[ApiController]
[Route("apiHR/[controller]")]
public class TrainingController : ControllerBase
{
    private readonly TrainingEnrollmentService _enroll;
    private readonly TrainingSessionService _session;
    private readonly TrainingClassService _class;
    private readonly TrainingMaterialService _material;
    private readonly TrainingQAService _qa;
    private readonly TrainingAttemptService _attempt;
    private readonly TrainingReviewService _review;
    private readonly TrainingTeamService _team;

    public TrainingController(
        TrainingEnrollmentService enroll,
        TrainingSessionService session,
        TrainingClassService cls,
        TrainingMaterialService material,
        TrainingQAService qa,
        TrainingAttemptService attempt,
        TrainingReviewService review,
        TrainingTeamService team)
    {
        _enroll   = enroll;
        _session  = session;
        _class    = cls;
        _material = material;
        _qa       = qa;
        _attempt  = attempt;
        _review   = review;
        _team     = team;
    }

    // GET /apiHR/Training/my-classes?empcd=
    [HttpGet("my-classes")]
    public async Task<IActionResult> MyClasses([FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        var data = await _enroll.GetMyClassesAsync(empcd);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/Training/class/{id}/detail?empcd= — cho student xem lớp mình được assign
    [HttpGet("class/{id}/detail")]
    public async Task<IActionResult> ClassDetail(int id, [FromQuery] string? empcd)
    {
        var cls = await _class.GetDetailAsync(id);
        if (cls == null) return Ok(new { success = false, message = "Không tìm thấy lớp" });
        var sessions = await _class.GetSessionsAsync(id);
        var teachers = await _class.GetTeachersAsync(id);
        var groups   = await _class.GetGroupsAsync(id);
        var materials = await _material.ListByClassAsync(id, empcd);
        return Ok(new { success = true, data = new { cls, sessions, teachers, groups, materials } });
    }

    // POST /apiHR/Training/class/{id}/register — OPEN mode, NV tự đăng ký
    [HttpPost("class/{id}/register")]
    public async Task<IActionResult> Register(int id, [FromBody] SelfRegisterRequest req)
    {
        req.CLASS_ID = id;
        if (string.IsNullOrWhiteSpace(req.EMPCD))
            return Ok(new { success = false, message = "EMPCD required" });
        var (ok, err) = await _enroll.SelfRegisterAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // GET /apiHR/Training/session/{id}/detail
    [HttpGet("session/{id}/detail")]
    public async Task<IActionResult> SessionDetail(int id)
    {
        var s = await _session.GetDetailAsync(id);
        if (s == null) return Ok(new { success = false, message = "Không tìm thấy session" });
        return Ok(new { success = true, data = s });
    }

    // POST /apiHR/Training/session/checkin — NV bấm điểm danh
    [HttpPost("session/checkin")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInRequest req)
    {
        var (ok, err) = await _session.CheckInAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // GET /apiHR/Training/class/{id}/materials?empcd=
    [HttpGet("class/{id}/materials")]
    public async Task<IActionResult> Materials(int id, [FromQuery] string? empcd)
    {
        var data = await _material.ListByClassAsync(id, empcd);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/Training/material/view — track xem tài liệu
    [HttpPost("material/view")]
    public async Task<IActionResult> MaterialView([FromBody] MaterialViewRequest req)
    {
        var newView = await _material.TrackViewAsync(req);
        return Ok(new { success = true, data = new { newView } });
    }

    // GET /apiHR/Training/class/{id}/qa
    [HttpGet("class/{id}/qa")]
    public async Task<IActionResult> QAList(int id)
    {
        var data = await _qa.ListByClassAsync(id);
        return Ok(new { success = true, data });
    }

    // POST /apiHR/Training/qa/ask — student đặt câu hỏi
    [HttpPost("qa/ask")]
    public async Task<IActionResult> QAAsk([FromBody] AskQuestionRequest req)
    {
        try
        {
            var id = await _qa.AskAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEST — student làm bài (§6.2-6.5)
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/Training/test/{id}?empcd= — full data để làm bài (không lộ IS_CORRECT)
    [HttpGet("test/{id}")]
    public async Task<IActionResult> TestForStudent(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        var (view, err) = await _attempt.GetForStudentAsync(id, empcd);
        return Ok(new { success = view != null, message = err, data = view });
    }

    // POST /apiHR/Training/test/start — bấm "Bắt đầu" (idempotent — trả attempt cũ nếu có)
    [HttpPost("test/start")]
    public async Task<IActionResult> TestStart([FromBody] StartAttemptRequest req)
    {
        // Lấy IP + UserAgent để log
        req.IP_ADDRESS ??= HttpContext.Connection.RemoteIpAddress?.ToString();
        req.USER_AGENT ??= Request.Headers.UserAgent.ToString();
        var (att, err) = await _attempt.StartAttemptAsync(req);
        return Ok(new { success = att != null, message = err, data = att });
    }

    // POST /apiHR/Training/test/save-answer — auto-save mỗi câu
    [HttpPost("test/save-answer")]
    public async Task<IActionResult> TestSaveAnswer([FromBody] SaveAnswerRequest req)
    {
        var (ok, err) = await _attempt.SaveAnswerAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // POST /apiHR/Training/test/submit — nộp bài
    [HttpPost("test/submit")]
    public async Task<IActionResult> TestSubmit([FromBody] SubmitAttemptRequest req)
    {
        var (result, err) = await _attempt.SubmitAsync(req);
        return Ok(new { success = result != null, message = err, data = result });
    }

    // GET /apiHR/Training/test/{id}/my-result?empcd=
    [HttpGet("test/{id}/my-result")]
    public async Task<IActionResult> TestMyResult(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        var data = await _attempt.GetMyResultAsync(id, empcd);
        return Ok(new { success = data != null, data });
    }

    // ═══════════════════════════════════════════════════════════════
    //  REVIEW (§10) — student submit + xem review của chính mình
    // ═══════════════════════════════════════════════════════════════

    // POST /apiHR/Training/review/submit
    [HttpPost("review/submit")]
    public async Task<IActionResult> ReviewSubmit([FromBody] SubmitReviewRequest req)
    {
        var (ok, err) = await _review.SubmitAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // GET /apiHR/Training/class/{id}/my-review?empcd=
    [HttpGet("class/{id}/my-review")]
    public async Task<IActionResult> MyReview(int id, [FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        var data = await _review.GetMyReviewAsync(id, empcd);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEAM SCHEDULE (§14.5) — Manager/Supervisor/Clerk xem NV mình quản lý
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/Training/team/has-scope?empcd= — dùng cho menu builder (show/hide "Lịch training team")
    [HttpGet("team/has-scope")]
    public async Task<IActionResult> TeamHasScope([FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return Ok(new { success = false, data = false });
        var has = await _team.HasScopeAsync(empcd);
        return Ok(new { success = true, data = has });
    }

    // GET /apiHR/Training/team/schedule?empcd=&from=&to=&status=
    // Trả list session của NV thuộc scope. status filter: UPCOMING|ONGOING|COMPLETED|ALL (default ALL).
    [HttpGet("team/schedule")]
    public async Task<IActionResult> TeamSchedule(
        [FromQuery] string empcd,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? status)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        if (!await _team.HasScopeAsync(empcd))
            return Ok(new { success = false, message = "Bạn không có scope quản lý" });

        var data = await _team.GetScheduleAsync(empcd, from, to, status);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/Training/team/today?empcd= — shortcut: session hôm nay (dùng cho Home dashboard drill-down)
    [HttpGet("team/today")]
    public async Task<IActionResult> TeamToday([FromQuery] string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = false, message = "empcd required" });
        var today = DateTime.Today;
        var data = await _team.GetScheduleAsync(empcd, today, today, null);
        return Ok(new { success = true, data });
    }
}
