using HR_web.API.Service;
using HR_web.Models.Directory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Trang quản lý hình minh hoạ theo work cd (Dept+Line+Work) - chỉ HR/Admin.
[Authorize(Roles = "Admin,HR")]
public class WorkCdImageController : BaseController
{
    private readonly DirectoryService _service;

    public WorkCdImageController(DirectoryService service)
    {
        _service = service;
    }

    // GET: /WorkCdImage/Index?deptCd=&lineCd=&missingOnly=&page=
    public async Task<IActionResult> Index(string? deptCd, string? lineCd, bool missingOnly = false, int page = 1)
    {
        const int pageSize = 60;

        var listTask = _service.GetWorkCdListAsync(deptCd, lineCd, page, pageSize);
        var deptsTask = _service.GetDeptListAsync();
        var linesTask = _service.GetLineListAsync(deptCd);
        await Task.WhenAll(listTask, deptsTask, linesTask);

        var result = listTask.Result;
        var existing = ImageController.CheckWorkCdImagesExist(result.Items.Select(i => i.ImageFileName));
        foreach (var item in result.Items)
            item.HasImage = existing.Contains(item.ImageFileName);

        if (missingOnly)
            result.Items = result.Items.Where(i => !i.HasImage).ToList();

        ViewBag.Depts = deptsTask.Result;
        ViewBag.Lines = linesTask.Result;
        ViewBag.DeptCd = deptCd;
        ViewBag.LineCd = lineCd;
        ViewBag.MissingOnly = missingOnly;
        ViewBag.TotalPages = (int)Math.Ceiling(result.Total / (double)pageSize);

        return View(result);
    }

    // POST: /WorkCdImage/UploadBulk
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadBulk(List<IFormFile> files, string? deptCd, string? lineCd, bool missingOnly = false, int page = 1)
    {
        var (savedCount, matched, skipped) = await ImageController.SaveWorkCdImagesAsync(files);

        TempData["UploadSavedCount"] = savedCount;
        TempData["UploadSkipped"] = System.Text.Json.JsonSerializer.Serialize(skipped);

        return RedirectToAction("Index", new { deptCd, lineCd, missingOnly, page });
    }
}
