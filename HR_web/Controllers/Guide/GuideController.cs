using HR_web.API.Service;
using HR_web.Models.Guide;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Guide;

[Authorize]
public class GuideController : BaseController
{
    private readonly GuideService _service;
    private readonly IWebHostEnvironment _env;

    private static readonly string[] AllowedVideoExts = { ".mp4", ".webm", ".mov" };
    private const long MaxVideoBytes = 500 * 1024 * 1024; // 500 MB

    public GuideController(GuideService service, IWebHostEnvironment env)
    {
        _service = service;
        _env = env;
    }

    // GET /Guide/Index  — tất cả đều xem được (kể cả chưa login)
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var list = await _service.GetListAsync();
        return View(list);
    }

    // GET /Guide/Detail?id=
    [AllowAnonymous]
    public async Task<IActionResult> Detail(int id)
    {
        var model = await _service.GetByIdAsync(id);
        if (model == null || model.IS_ACTIVE != 1)
            return RedirectToAction("Index");
        return View(model);
    }

    // GET /Guide/Manage  — chỉ Admin
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Manage()
    {
        var list = await _service.GetAdminListAsync();
        return View(list);
    }

    // GET /Guide/Edit?id=
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null || id == 0)
            return View(new GuideModel { IS_ACTIVE = 1 });

        var model = await _service.GetByIdAsync(id.Value);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy mục hướng dẫn!";
            return RedirectToAction("Manage");
        }
        return View(model);
    }

    // POST /Guide/Edit
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(524_288_000)] // 500 MB
    public async Task<IActionResult> Edit(GuideModel model, IFormFile? VideoFile)
    {
        if (string.IsNullOrWhiteSpace(model.TITLE))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập tiêu đề!";
            return View(model);
        }

        // Xử lý upload video
        if (VideoFile != null && VideoFile.Length > 0)
        {
            if (VideoFile.Length > MaxVideoBytes)
            {
                TempData["ErrorMessage"] = "File video không được vượt quá 500 MB!";
                return View(model);
            }

            var ext = Path.GetExtension(VideoFile.FileName).ToLower();
            if (!AllowedVideoExts.Contains(ext))
            {
                TempData["ErrorMessage"] = "Chỉ chấp nhận file MP4, WebM, hoặc MOV!";
                return View(model);
            }

            var guidesFolder = Path.Combine(_env.WebRootPath, "video", "guides");
            Directory.CreateDirectory(guidesFolder);

            // Xóa video cũ nếu có
            if (!string.IsNullOrEmpty(model.VIDEO_PATH))
            {
                var oldFile = Path.Combine(_env.WebRootPath, model.VIDEO_PATH.TrimStart('/'));
                if (System.IO.File.Exists(oldFile))
                    System.IO.File.Delete(oldFile);
            }

            var fileName = $"{Guid.NewGuid()}{ext}";
            var savePath = Path.Combine(guidesFolder, fileName);
            using (var stream = new FileStream(savePath, FileMode.Create))
                await VideoFile.CopyToAsync(stream);

            model.VIDEO_PATH = $"/video/guides/{fileName}";
        }

        var request = new SaveGuideRequest
        {
            ID            = model.ID == 0 ? null : model.ID,
            CATEGORY      = model.CATEGORY?.Trim(),
            TITLE         = model.TITLE.Trim(),
            CONTENT       = model.CONTENT,
            VIDEO_PATH    = model.VIDEO_PATH,
            DISPLAY_ORDER = model.DISPLAY_ORDER,
            IS_ACTIVE     = model.IS_ACTIVE,
            LOGIN_USER    = CurrentUser!.EmpCd
        };

        var (success, message) = await _service.SaveAsync(request);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }

    // POST /Guide/RemoveVideo?id=
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveVideo(int id)
    {
        var guide = await _service.GetByIdAsync(id);
        if (guide == null)
            return Json(new { success = false, message = "Không tìm thấy mục hướng dẫn" });

        if (!string.IsNullOrEmpty(guide.VIDEO_PATH))
        {
            var oldFile = Path.Combine(_env.WebRootPath, guide.VIDEO_PATH.TrimStart('/'));
            if (System.IO.File.Exists(oldFile))
                System.IO.File.Delete(oldFile);
        }

        var request = new SaveGuideRequest
        {
            ID            = guide.ID,
            CATEGORY      = guide.CATEGORY,
            TITLE         = guide.TITLE,
            CONTENT       = guide.CONTENT,
            VIDEO_PATH    = null,
            DISPLAY_ORDER = guide.DISPLAY_ORDER,
            IS_ACTIVE     = guide.IS_ACTIVE,
            LOGIN_USER    = CurrentUser!.EmpCd
        };

        var (success, message) = await _service.SaveAsync(request);
        return Json(new { success, message });
    }

    // POST /Guide/Toggle?id=
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Toggle(int id)
    {
        var (success, message) = await _service.ToggleAsync(id, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }

    // POST /Guide/Delete?id=
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var (success, message, videoPath) = await _service.DeleteAsync(id);

        if (success && !string.IsNullOrEmpty(videoPath))
        {
            var filePath = Path.Combine(_env.WebRootPath, videoPath.TrimStart('/'));
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }

        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }
}
