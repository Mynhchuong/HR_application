using HR_web.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace HR_web.Controllers;

[Authorize]
public class PopupGateController : BaseController
{
    private readonly ApiService _api;

    public PopupGateController(ApiService api) => _api = api;

    // GET: /PopupGate/Next (AJAX) — popup nhắc việc ưu tiên cao nhất của NV đang đăng nhập (xem PopupGateService bên HR_api)
    [HttpGet]
    public async Task<IActionResult> Next()
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new { success = true, data = (object?)null });

        var res = await _api.GetAsync<JObject>("PopupGate/next", $"empcd={Uri.EscapeDataString(empcd)}");
        return Content(res?.ToString(Newtonsoft.Json.Formatting.None) ?? "{\"success\":false}", "application/json");
    }
}
