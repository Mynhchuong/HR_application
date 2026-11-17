using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class PopupGateController : ControllerBase
{
    private readonly PopupGateService _svc;

    public PopupGateController(PopupGateService svc) => _svc = svc;

    // GET /apiHR/PopupGate/next?empcd=
    [HttpGet("next")]
    public async Task<IActionResult> Next([FromQuery] string empcd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });
            var item = await _svc.GetNextAsync(empcd);
            return Ok(new { success = true, data = item });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
