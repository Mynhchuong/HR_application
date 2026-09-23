using Microsoft.AspNetCore.Mvc;
using HR_api.Models.AppLink;
using HR_api.Services;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class AppLinkController : ControllerBase
{
    private readonly AppLinkService _service;

    public AppLinkController(AppLinkService service)
    {
        _service = service;
    }

    // GET /apiHR/AppLink/admin/list?status=
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminList(string? status)
    {
        try
        {
            var list = await _service.GetListAsync(status);
            var stats = await _service.GetStatsAsync();
            return Ok(new { success = true, data = list, stats });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/AppLink/admin/import
    [HttpPost("admin/import")]
    public async Task<IActionResult> Import([FromBody] ImportAppLinkRequest req)
    {
        try
        {
            var (success, message, inserted, skipped) = await _service.ImportAsync(req);
            return Ok(new { success, message, inserted, skipped });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/AppLink/request — nhân viên nhập mã thẻ để nhận link
    [HttpPost("request")]
    public async Task<IActionResult> RequestLink([FromBody] RequestAppLinkRequest req)
    {
        try
        {
            var result = await _service.RequestLinkAsync(req.EMPCD);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new AppLinkResult { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/AppLink/reassign — "đổi máy mới": đóng link cũ, phát link mới
    [HttpPost("reassign")]
    public async Task<IActionResult> Reassign([FromBody] ReassignAppLinkRequest req)
    {
        try
        {
            var result = await _service.ReassignAsync(req);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new AppLinkResult { success = false, message = ex.Message });
        }
    }
}
