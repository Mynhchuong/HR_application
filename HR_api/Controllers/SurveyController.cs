using HR_api.Models.Survey;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// User-facing endpoints: NV làm survey.
[ApiController]
[Route("apiHR/[controller]")]
public class SurveyController : ControllerBase
{
    private readonly SurveyService _survey;
    private readonly SurveyRecipientService _recipient;

    public SurveyController(SurveyService survey, SurveyRecipientService recipient)
    {
        _survey = survey;
        _recipient = recipient;
    }

    // GET /apiHR/Survey/pending?empcd=xxx  → list survey ACTIVE mà user chưa làm
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new { success = false, message = "Thiếu empcd" });
        var list = await _survey.GetPendingAsync(empcd);
        return Ok(new { success = true, data = list });
    }

    // GET /apiHR/Survey/pending-id?empcd=xxx&roleId=  → ID survey nhỏ nhất chưa làm (dùng cho middleware chặn app)
    // roleId=7 (Expat) → chỉ trả EN survey. Còn lại → chỉ trả VI.
    [HttpGet("pending-id")]
    public async Task<IActionResult> GetPendingId([FromQuery] string empcd, [FromQuery] int? roleId = null)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new { success = true, data = (int?)null });
        var id = await _recipient.GetOldestPendingSurveyIdAsync(empcd, roleId);
        return Ok(new { success = true, data = id });
    }

    // GET /apiHR/Survey/detail?empcd=xxx&id=1
    [HttpGet("detail")]
    public async Task<IActionResult> GetDetail([FromQuery] string empcd, [FromQuery] int id)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new { success = false, message = "Thiếu empcd" });
        var (survey, response, error) = await _survey.GetDetailForUserAsync(empcd, id);
        if (error != null && survey == null) return Ok(new { success = false, message = error });
        return Ok(new
        {
            success = error == null,
            message = error,
            data = new { survey, response }
        });
    }

    // POST /apiHR/Survey/save-answer
    [HttpPost("save-answer")]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerRequest req)
    {
        if (string.IsNullOrEmpty(req.EMPCD)) return Ok(new { success = false, message = "Thiếu empcd" });
        var (ok, err) = await _survey.SaveAnswerAsync(req);
        return Ok(new { success = ok, message = err });
    }

    // POST /apiHR/Survey/submit
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitSurveyRequest req)
    {
        if (string.IsNullOrEmpty(req.EMPCD)) return Ok(new { success = false, message = "Thiếu empcd" });
        var (result, err) = await _survey.SubmitAsync(req.EMPCD, req.SURVEY_ID);
        return Ok(new { success = result.SUCCESS, message = err, data = result });
    }

    // POST /apiHR/Survey/skip-illiterate
    [HttpPost("skip-illiterate")]
    public async Task<IActionResult> SkipIlliterate([FromBody] SkipIlliterateRequest req)
    {
        if (string.IsNullOrEmpty(req.EMPCD)) return Ok(new { success = false, message = "Thiếu empcd" });
        var (ok, err) = await _survey.SkipIlliterateAsync(req.EMPCD, req.SURVEY_ID);
        return Ok(new { success = ok, message = err });
    }
}
