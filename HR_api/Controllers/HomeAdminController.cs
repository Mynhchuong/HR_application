using HR_api.Models.Home;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

// Admin panel endpoints — HR_web gọi qua HomeAdminApiService.
// Role check ở HR_web layer (session-aware). API tin caller đã pass role.
[ApiController]
[Route("apiHR/Home/admin")]
public class HomeAdminController : ControllerBase
{
    private readonly HomeAdminService _svc;

    public HomeAdminController(HomeAdminService svc) { _svc = svc; }

    // ─── BANNER ─────────────────────────────────────────────────────
    [HttpGet("banner/list")]
    public async Task<IActionResult> BannerList()
    {
        try
        {
            var data = await _svc.ListBannersAsync();
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("banner/save")]
    public async Task<IActionResult> BannerSave([FromBody] SaveBannerRequest req)
    {
        try
        {
            var (ok, msg, id) = await _svc.SaveBannerAsync(req);
            return Ok(new { success = ok, message = msg, id });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("banner/delete")]
    public async Task<IActionResult> BannerDelete([FromBody] BannerActionRequest req)
    {
        try
        {
            var (ok, msg) = await _svc.DeleteBannerAsync(req);
            return Ok(new { success = ok, message = msg });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─── GREETING ───────────────────────────────────────────────────
    [HttpGet("greeting/list")]
    public async Task<IActionResult> GreetingList([FromQuery] string? slot = null)
    {
        try
        {
            var data = await _svc.ListGreetingsAsync(slot);
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("greeting/save")]
    public async Task<IActionResult> GreetingSave([FromBody] SaveGreetingRequest req)
    {
        try
        {
            var (ok, msg, id) = await _svc.SaveGreetingAsync(req);
            return Ok(new { success = ok, message = msg, id });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("greeting/delete")]
    public async Task<IActionResult> GreetingDelete([FromBody] GreetingActionRequest req)
    {
        try
        {
            var (ok, msg) = await _svc.DeleteGreetingAsync(req);
            return Ok(new { success = ok, message = msg });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─── RULE ───────────────────────────────────────────────────────
    [HttpGet("rule/list")]
    public async Task<IActionResult> RuleList()
    {
        try
        {
            var data = await _svc.ListRulesAsync();
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("rule/save")]
    public async Task<IActionResult> RuleSave([FromBody] SaveRuleRequest req)
    {
        try
        {
            var (ok, msg) = await _svc.SaveRuleAsync(req);
            return Ok(new { success = ok, message = msg });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─── AUDIT ──────────────────────────────────────────────────────
    [HttpGet("audit")]
    public async Task<IActionResult> Audit(
        int page = 1, int page_size = 30,
        string? entity = null, string? actor = null,
        string? date_from = null, string? date_to = null)
    {
        try
        {
            DateTime? df = DateTime.TryParse(date_from, out var _df) ? _df : null;
            DateTime? dt = DateTime.TryParse(date_to,   out var _dt) ? _dt : null;

            var data = await _svc.ListAuditAsync(page, page_size, entity, actor, df, dt);
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
