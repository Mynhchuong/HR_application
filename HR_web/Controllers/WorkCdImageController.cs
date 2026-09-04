using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Trang quản lý hình minh hoạ theo mã công việc (ECM100.INTEREST, VD Y80/QUÉT KEO) - chỉ HR/Admin.
[Authorize(Roles = "Admin,HR")]
public class WorkCdImageController : BaseController
{
    private readonly DirectoryService _service;

    public WorkCdImageController(DirectoryService service)
    {
        _service = service;
    }

    // GET: /WorkCdImage/Index?search=&missingOnly=&page=
    public async Task<IActionResult> Index(string? search, bool missingOnly = false, int page = 1)
    {
        const int pageSize = 60;

        var result = await _service.GetInterestListAsync(search, page, pageSize);

        var existing = ImageController.CheckWorkCdImagesExist(result.Items.Select(i => i.ImageFileName));
        foreach (var item in result.Items)
            item.HasImage = existing.Contains(item.ImageFileName);

        if (missingOnly)
            result.Items = result.Items.Where(i => !i.HasImage).ToList();

        ViewBag.Search = search;
        ViewBag.MissingOnly = missingOnly;
        ViewBag.TotalPages = (int)Math.Ceiling(result.Total / (double)pageSize);

        return View(result);
    }

    // POST: /WorkCdImage/UploadBulk
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadBulk(List<IFormFile> files, string? search, bool missingOnly = false, int page = 1)
    {
        var (savedCount, matched, skipped) = await ImageController.SaveWorkCdImagesAsync(files);

        TempData["UploadSavedCount"] = savedCount;
        TempData["UploadSkipped"] = System.Text.Json.JsonSerializer.Serialize(skipped);

        return RedirectToAction("Index", new { search, missingOnly, page });
    }
}
