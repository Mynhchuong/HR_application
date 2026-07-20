using HR_api.Models.Survey;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// Admin endpoints — HR/Admin quản lý survey.
[ApiController]
[Route("apiHR/[controller]")]
public class SurveyAdminController : ControllerBase
{
    private readonly SurveyAdminService _admin;
    private readonly SurveyReportService _report;
    private readonly SurveyRecipientService _recipient;
    private readonly SurveyScopeService _scope;
    private readonly NotificationService _noti;

    public SurveyAdminController(
        SurveyAdminService admin,
        SurveyReportService report,
        SurveyRecipientService recipient,
        SurveyScopeService scope,
        NotificationService noti)
    {
        _admin = admin;
        _report = report;
        _recipient = recipient;
        _scope = scope;
        _noti = noti;
    }

    // ═══════════════════════════════════════════════════════════════
    //  REPORT
    // ═══════════════════════════════════════════════════════════════

    // GET /apiHR/SurveyAdmin/report?id=  → overview (recipient counts + per-question)
    [HttpGet("report")]
    public async Task<IActionResult> Report([FromQuery] int id)
    {
        var data = await _report.GetOverviewAsync(id);
        if (data == null) return Ok(new { success = false, message = "Không tìm thấy survey" });
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/report/quiz?id= → score per user (QUIZ only)
    [HttpGet("report/quiz")]
    public async Task<IActionResult> ReportQuiz([FromQuery] int id)
    {
        var data = await _report.GetQuizReportAsync(id);
        if (data == null) return Ok(new { success = false, message = "Chỉ hỗ trợ QUIZ" });
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/report/illiterate?id=&deptcd=&linecd=&workcd=
    [HttpGet("report/illiterate")]
    public async Task<IActionResult> ReportIlliterate(
        [FromQuery] int? id,
        [FromQuery] string? deptcd,
        [FromQuery] string? linecd,
        [FromQuery] string? workcd)
    {
        var data = await _report.GetIlliterateListAsync(id, deptcd, linecd, workcd);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/report/text-answers?qid=&page=&pageSize=
    [HttpGet("report/text-answers")]
    public async Task<IActionResult> TextAnswers(
        [FromQuery] int qid,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 50000) pageSize = 50000;
        if (page < 1) page = 1;
        var data = await _report.GetTextAnswersAsync(qid, page, pageSize);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/report/option-respondents?optId=
    [HttpGet("report/option-respondents")]
    public async Task<IActionResult> OptionRespondents([FromQuery] int optId)
    {
        var data = await _report.GetOptionRespondentsAsync(optId);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/report/all-answers?id=
    [HttpGet("report/all-answers")]
    public async Task<IActionResult> ReportAllAnswers([FromQuery] int id)
    {
        var data = await _report.GetAllAnswersAsync(id);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/list?status=DRAFT&type=QUIZ&search=abc
    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? type, [FromQuery] string? search)
    {
        var list = await _admin.ListAsync(status, type, search);
        return Ok(new { success = true, data = list });
    }

    // GET /apiHR/SurveyAdmin/detail?id=1
    [HttpGet("detail")]
    public async Task<IActionResult> Detail([FromQuery] int id)
    {
        var s = await _admin.GetDetailAsync(id);
        if (s == null) return Ok(new { success = false, message = "Không tìm thấy survey" });
        return Ok(new { success = true, data = s });
    }

    // POST /apiHR/SurveyAdmin/save
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveSurveyRequest req)
    {
        try
        {
            var id = await _admin.SaveAsync(req);
            return Ok(new { success = true, data = new { id } });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/SurveyAdmin/change-status  (publish / pause / cancel / delete)
    [HttpPost("change-status")]
    public async Task<IActionResult> ChangeStatus([FromBody] ChangeStatusRequest req)
    {
        var (ok, err) = await _admin.ChangeStatusAsync(req.ID, req.NEW_STATUS, req.LOGIN_USER);
        return Ok(new { success = ok, message = err });
    }

    // POST /apiHR/SurveyAdmin/publish-stream — publish + snapshot batch 50, stream tiến độ NDJSON.
    //   Response: text/plain, mỗi dòng 1 JSON:
    //     {"phase":"schedule"} → đã DRAFT→SCHEDULED
    //     {"phase":"snapshot","inserted":N,"total":M}
    //     {"phase":"done","success":true,"total":M} hoặc {"phase":"error","message":"..."}
    [HttpPost("publish-stream")]
    public async Task PublishStream([FromBody] ChangeStatusRequest req)
    {
        Response.StatusCode = 200;
        Response.ContentType = "application/x-ndjson";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task Emit(object obj)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(obj);
            await Response.WriteAsync(json + "\n");
            await Response.Body.FlushAsync();
        }

        try
        {
            var detail = await _admin.GetDetailAsync(req.ID);
            var current = detail?.STATUS ?? "";
            // Chỉ đổi status nếu đang DRAFT. Nếu đã SCHEDULED / ACTIVE → skip, chỉ re-snapshot.
            if (current == "DRAFT")
            {
                var (ok, err) = await _admin.ChangeStatusAsync(req.ID, "SCHEDULED", req.LOGIN_USER);
                if (!ok)
                {
                    await Emit(new { phase = "error", message = err ?? "Publish thất bại" });
                    return;
                }
            }
            else if (current != "SCHEDULED" && current != "ACTIVE")
            {
                await Emit(new { phase = "error", message = $"Không publish được từ trạng thái {current}" });
                return;
            }
            await Emit(new { phase = "schedule" });

            var count = await _recipient.SnapshotWithProgressAsync(req.ID, async (inserted, total) =>
            {
                await Emit(new { phase = "snapshot", inserted, total });
            });

            await Emit(new { phase = "done", success = true, total = count });
        }
        catch (Exception ex)
        {
            await Emit(new { phase = "error", message = ex.Message });
        }
    }

    // Shortcuts để front-end dùng gọn (đều gọi qua ChangeStatusAsync)
    [HttpPost("publish")] public Task<IActionResult> Publish([FromBody] ChangeStatusRequest r) { r.NEW_STATUS = "SCHEDULED"; return ChangeStatus(r); }
    [HttpPost("pause")]   public Task<IActionResult> Pause  ([FromBody] ChangeStatusRequest r) { r.NEW_STATUS = "PAUSED";    return ChangeStatus(r); }
    [HttpPost("resume")]  public Task<IActionResult> Resume ([FromBody] ChangeStatusRequest r) { r.NEW_STATUS = "ACTIVE";    return ChangeStatus(r); }
    [HttpPost("cancel")]  public Task<IActionResult> Cancel ([FromBody] ChangeStatusRequest r) { r.NEW_STATUS = "CANCELLED"; return ChangeStatus(r); }
    [HttpPost("delete")]  public Task<IActionResult> Delete ([FromBody] ChangeStatusRequest r) { r.NEW_STATUS = "DELETED";   return ChangeStatus(r); }

    // GET /apiHR/SurveyAdmin/preview?id=  → detail đầy đủ (kèm IS_CORRECT) cho HR preview.
    // Không check recipient, không tạo response.
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] int id)
    {
        var s = await _admin.GetDetailAsync(id);
        if (s == null) return Ok(new { success = false, message = "Không tìm thấy survey" });
        return Ok(new { success = true, data = s });
    }

    // POST /apiHR/SurveyAdmin/test-submit  → chấm điểm cho câu trả lời test, KHÔNG lưu response.
    // Payload: { SURVEY_ID, ANSWERS: [ { QUESTION_ID, ANSWER_OPTION_IDS?, ANSWER_TEXT?, ANSWER_NUMBER? } ] }
    [HttpPost("test-submit")]
    public async Task<IActionResult> TestSubmit(
        [FromBody] TestSubmitRequest req,
        [FromServices] HR_api.Data.OracleService db)
    {
        var s = await _admin.GetDetailAsync(req.SURVEY_ID);
        if (s == null) return Ok(new { success = false, message = "Không tìm thấy survey" });

        // Compute in-memory (không đụng HR_SURVEY_RESPONSE)
        decimal score = 0, max = 0;
        var isQuiz = s.SURVEY_TYPE == "QUIZ";

        foreach (var q in s.QUESTIONS)
        {
            if (!isQuiz) continue;
            if (q.QUESTION_TYPE is "SINGLE" or "MULTI" or "YESNO" or "DROPDOWN")
            {
                max += q.POINTS;
                var correct = q.OPTIONS.Where(o => o.IS_CORRECT == 1).Select(o => o.ID).ToHashSet();
                if (correct.Count == 0) continue;

                var answer = req.ANSWERS?.FirstOrDefault(a => a.QUESTION_ID == q.ID);
                var picked = ParseIds(answer?.ANSWER_OPTION_IDS);
                if (picked.SetEquals(correct)) score += q.POINTS;
            }
        }

        int? isPass = !isQuiz ? null
            : (s.PASS_SCORE == null ? null
               : (score >= s.PASS_SCORE ? 1 : 0));

        return Ok(new
        {
            success = true,
            data = new HR_api.Models.Survey.SurveySubmitResultModel
            {
                SUCCESS     = true,
                SCORE       = isQuiz ? score  : null,
                MAX_SCORE   = isQuiz ? max    : null,
                IS_PASS     = isPass,
                SURVEY_TYPE = s.SURVEY_TYPE,
            }
        });
    }

    // GET /apiHR/SurveyAdmin/participants?id=&deptcd=&linecd=&workcd=&empcd=&status=&page=&pageSize=
    [HttpGet("participants")]
    public async Task<IActionResult> Participants(
        [FromQuery] int id,
        [FromQuery] string? deptcd, [FromQuery] string? linecd,
        [FromQuery] string? workcd, [FromQuery] string? empcd,
        [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var data = await _report.GetParticipantsAsync(id, deptcd, linecd, workcd, empcd, status, page, pageSize);
        return Ok(new { success = true, data });
    }

    // GET /apiHR/SurveyAdmin/participant-answers?surveyId=&empcd=
    [HttpGet("participant-answers")]
    public async Task<IActionResult> ParticipantAnswers([FromQuery] int surveyId, [FromQuery] string empcd)
    {
        var data = await _report.GetParticipantAnswersAsync(surveyId, empcd);
        if (data == null) return Ok(new { success = false, message = "Không tìm thấy" });
        return Ok(new { success = true, data });
    }

    private static HashSet<int> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => int.TryParse(s, out _)).Select(int.Parse).ToHashSet();
    }

    // GET /apiHR/SurveyAdmin/scope-count?deptcd=X&linecd=Y&workcd=Z
    [HttpGet("scope-count")]
    public async Task<IActionResult> ScopeCount(
        [FromQuery] string deptcd,
        [FromQuery] string? linecd,
        [FromQuery] string? workcd)
    {
        if (string.IsNullOrWhiteSpace(deptcd)) return Ok(new { count = 0 });
        var count = await _scope.CountScopeAsync(deptcd, linecd, workcd);
        return Ok(new { count });
    }

    public class TestSubmitRequest
    {
        public int SURVEY_ID { get; set; }
        public List<TestAnswer> ANSWERS { get; set; } = new();
    }

    public class TestAnswer
    {
        public int      QUESTION_ID       { get; set; }
        public string?  ANSWER_OPTION_IDS { get; set; }
        public string?  ANSWER_TEXT       { get; set; }
        public decimal? ANSWER_NUMBER     { get; set; }
    }
}
