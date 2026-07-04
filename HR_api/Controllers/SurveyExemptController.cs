using HR_api.Models.Survey;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// CRUD HR_SURVEY_EXEMPT (blacklist mù chữ / không smartphone).
// Import/export Excel + template Phase 5.
[ApiController]
[Route("apiHR/[controller]")]
public class SurveyExemptController : ControllerBase
{
    private readonly SurveyExemptService _exempt;

    public SurveyExemptController(SurveyExemptService exempt) { _exempt = exempt; }

    // GET /apiHR/SurveyExempt/list?empcd=&type=ILLITERATE&isActive=1&name=&deptcd=&linecd=&workcd=
    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] string? empcd, [FromQuery] string? type, [FromQuery] int? isActive,
        [FromQuery] string? name,  [FromQuery] string? deptcd,
        [FromQuery] string? linecd, [FromQuery] string? workcd)
    {
        var list = await _exempt.ListAsync(empcd, type, isActive, name, deptcd, linecd, workcd);
        return Ok(new { success = true, data = list });
    }

    // POST /apiHR/SurveyExempt/save  (upsert)
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveExemptRequest req)
    {
        if (string.IsNullOrEmpty(req.EMPCD) || string.IsNullOrEmpty(req.EXEMPT_TYPE))
            return Ok(new { success = false, message = "Thiếu EMPCD hoặc EXEMPT_TYPE" });
        await _exempt.SaveAsync(req);
        return Ok(new { success = true });
    }

    // POST /apiHR/SurveyExempt/delete  (soft delete IS_ACTIVE=0)
    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeleteExemptRequest req)
    {
        if (string.IsNullOrEmpty(req.EMPCD) || string.IsNullOrEmpty(req.EXEMPT_TYPE))
            return Ok(new { success = false, message = "Thiếu EMPCD hoặc EXEMPT_TYPE" });
        await _exempt.DeleteAsync(req);
        return Ok(new { success = true });
    }

    // POST /apiHR/SurveyExempt/import  (bulk MERGE — HR_web đã parse Excel)
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportRequest req)
    {
        if (req.ITEMS == null || req.ITEMS.Count == 0)
            return Ok(new { success = false, message = "Không có dữ liệu" });
        var count = await _exempt.ImportAsync(req.ITEMS, req.LOGIN_USER);
        return Ok(new { success = true, data = new { count } });
    }

    public class ImportRequest
    {
        public string? LOGIN_USER { get; set; }
        public List<SaveExemptRequest> ITEMS { get; set; } = new();
    }
}
