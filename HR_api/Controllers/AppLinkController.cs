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

    // GET /apiHR/AppLink/admin/list?status=&search=&page=&page_size=
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminList(string? status, string? search, int page = 1, int page_size = 50)
    {
        try
        {
            var (data, total) = await _service.GetListAsync(status, search, page, page_size);
            var stats = await _service.GetStatsAsync();
            return Ok(new
            {
                success = true,
                data,
                stats,
                total,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)total / page_size) : 0
            });
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

    // POST /apiHR/AppLink/admin/request — HR tự lấy link giùm NV (từ AppLinkAdmin), bỏ qua giới
    // hạn 3 lần/mã thẻ — chính là lối "nhắn Nhân sự" mà NV được hướng dẫn khi hết quota tự lấy.
    [HttpPost("admin/request")]
    public async Task<IActionResult> AdminRequestLink([FromBody] RequestAppLinkRequest req)
    {
        try
        {
            var result = await _service.RequestLinkAsync(req.EMPCD, bypassLimit: true);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new AppLinkResult { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/AppLink/admin/reassign — HR cấp lại link mới giùm NV, bỏ qua giới hạn 3 lần.
    [HttpPost("admin/reassign")]
    public async Task<IActionResult> AdminReassign([FromBody] ReassignAppLinkRequest req)
    {
        try
        {
            var result = await _service.ReassignAsync(req, bypassLimit: true);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new AppLinkResult { success = false, message = ex.Message });
        }
    }
}
