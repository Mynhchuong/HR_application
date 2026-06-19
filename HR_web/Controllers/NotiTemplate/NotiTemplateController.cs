using HR_web.API.Service;
using HR_web.Models.NotiTemplate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.NotiTemplate;

[Authorize]
public class NotiTemplateController : BaseController
{
    private readonly NotiTemplateService _svc;

    public NotiTemplateController(NotiTemplateService svc) { _svc = svc; }

    // GET /NotiTemplate/Index
    public IActionResult Index() => View();

    // GET /NotiTemplate/GetList  (AJAX)
    [HttpGet]
    public async Task<IActionResult> GetList()
    {
        var list = await _svc.GetListAsync();
        return Json(new { success = true, data = list });
    }

    // POST /NotiTemplate/Save  (AJAX)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveNotiTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TEMPLATE_KEY) || string.IsNullOrWhiteSpace(req.TITLE_VI))
            return Json(new { success = false, message = "Thiếu thông tin bắt buộc" });

        var (ok, msg) = await _svc.SaveAsync(req, CurrentUser?.EmpCd ?? "");
        return Json(new { success = ok, message = msg });
    }

    // POST /NotiTemplate/Toggle  (AJAX)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle([FromBody] ToggleRequest req)
    {
        var (ok, msg) = await _svc.ToggleAsync(req.Key, CurrentUser?.EmpCd ?? "");
        return Json(new { success = ok, message = msg });
    }

    public class ToggleRequest { public string Key { get; set; } = ""; }
}
