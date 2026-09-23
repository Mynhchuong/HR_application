using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

[Authorize(Roles = "Admin,HR")]
public class AppLinkAdminController : BaseController
{
    private readonly AppLinkService _service;

    public AppLinkAdminController(AppLinkService service)
    {
        _service = service;
    }

    public async Task<IActionResult> Index()
    {
        var (_, stats, _, _) = await _service.GetAdminListAsync(null, null, 1, 1);
        ViewBag.Stats = stats;
        return View();
    }

    // GET /AppLinkAdmin/GetList?status=&search=&page=&page_size= — AJAX cho bảng + phân trang
    [HttpGet]
    public async Task<IActionResult> GetList(string? status, string? search, int page = 1, int page_size = 50)
    {
        var (list, _, total, totalPages) = await _service.GetAdminListAsync(status, search, page, page_size);
        return Json(new { success = true, data = list, total, page, page_size, total_pages = totalPages });
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

    // POST /AppLinkAdmin/RequestLink — HR nhập mã thẻ để lấy link gửi giùm NV, bỏ qua giới hạn 3 lần.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestLink(string empcd)
    {
        var result = await _service.AdminRequestLinkAsync(empcd);
        return Json(result);
    }

    // POST /AppLinkAdmin/ReassignLink — HR cấp lại link mới giùm NV (đã hết quota tự lấy).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReassignLink(string empcd)
    {
        var result = await _service.AdminReassignAsync(empcd, CurrentUser?.EmpCd);
        return Json(result);
    }
}
