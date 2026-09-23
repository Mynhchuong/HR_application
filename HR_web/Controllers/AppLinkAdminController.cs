using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

[Authorize(Roles = "Admin")]
public class AppLinkAdminController : BaseController
{
    private readonly AppLinkService _service;

    public AppLinkAdminController(AppLinkService service)
    {
        _service = service;
    }

    public async Task<IActionResult> Index(string? status)
    {
        var (list, stats) = await _service.GetAdminListAsync(status);
        ViewBag.Stats = stats;
        ViewBag.Status = status;
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string linksText)
    {
        var links = (linksText ?? "")
            .Split('\n', '\r')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var (success, message, inserted, skipped) = await _service.ImportAsync(links, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Index");
    }
}
