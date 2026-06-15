using HR_web.API.Service;
using HR_web.Models.Guide;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Guide;

[Authorize]
public class GuideController : BaseController
{
    private readonly GuideService _service;
    private readonly VideoFileService _videoSvc;

    private const string VideoFolder = "GUIDE_VIDEO";

    public GuideController(GuideService service, VideoFileService videoSvc)
    {
        _service  = service;
        _videoSvc = videoSvc;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var list = await _service.GetListAsync();
        return View(list);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Detail(int id)
    {
        var model = await _service.GetByIdAsync(id);
        if (model == null || model.IS_ACTIVE != 1)
            return RedirectToAction("Index");
        return View(model);
    }

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
    // Video upload chỉ xử lý ở đây khi tạo mới (ID=0) — cần save trước để có ID.
    // Item đã có ID dùng UploadVideo (AJAX) để upload mà không reload trang.
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Edit(GuideModel model, IFormFile? VideoFile)
    {
        if (string.IsNullOrWhiteSpace(model.TITLE))
        {
            TempData["ErrorMessage"] = "Vui lòng nhập tiêu đề!";
            return View(model);
        }

        string? newExt = null;
        if (VideoFile != null && VideoFile.Length > 0)
        {
            var ext = Path.GetExtension(VideoFile.FileName).ToLower();
            if (!VideoFileService.AllowedExts.Contains(ext))
            {
                TempData["ErrorMessage"] = "Chỉ chấp nhận file MP4, WebM, hoặc MOV!";
                return View(model);
            }
            if (VideoFile.Length > VideoFileService.MaxBytes)
            {
                TempData["ErrorMessage"] = "File video không được vượt quá 500 MB!";
                return View(model);
            }
            newExt = ext;
        }

        var request = new SaveGuideRequest
        {
            ID            = model.ID == 0 ? null : model.ID,
            CATEGORY      = model.CATEGORY?.Trim(),
            TITLE         = model.TITLE.Trim(),
            CONTENT       = model.CONTENT,
            VIDEO_PATH    = newExt ?? (string.IsNullOrEmpty(model.VIDEO_PATH) ? null : model.VIDEO_PATH),
            DISPLAY_ORDER = model.DISPLAY_ORDER,
            IS_ACTIVE     = model.IS_ACTIVE,
            LOGIN_USER    = CurrentUser!.EmpCd
        };

        var (success, message, savedId) = await _service.SaveAsync(request);

        if (success && newExt != null && savedId > 0)
            await _videoSvc.SaveAsync(VideoFolder, savedId, VideoFile!);

        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }

    // POST /Guide/UploadVideo?id=
    // AJAX upload cho item đã có ID — lưu file rồi update VIDEO_PATH trong DB
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadVideo(int id, IFormFile file)
    {
        var guide = await _service.GetByIdAsync(id);
        if (guide == null)
            return Json(new { success = false, message = "Không tìm thấy mục hướng dẫn" });

        var (ok, message, ext) = await _videoSvc.SaveAsync(VideoFolder, id, file);
        if (!ok) return Json(new { success = false, message });

        var request = new SaveGuideRequest
        {
            ID            = guide.ID,
            CATEGORY      = guide.CATEGORY,
            TITLE         = guide.TITLE,
            CONTENT       = guide.CONTENT,
            VIDEO_PATH    = ext,
            DISPLAY_ORDER = guide.DISPLAY_ORDER,
            IS_ACTIVE     = guide.IS_ACTIVE,
            LOGIN_USER    = CurrentUser!.EmpCd
        };

        var (saved, saveMsg, _) = await _service.SaveAsync(request);
        return Json(new { success = saved, message = saveMsg, videoUrl = $"/Video/{VideoFolder}/{id}" });
    }

    // POST /Guide/RemoveVideo?id=
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveVideo(int id)
    {
        var guide = await _service.GetByIdAsync(id);
        if (guide == null)
            return Json(new { success = false, message = "Không tìm thấy mục hướng dẫn" });

        _videoSvc.DeleteAll(VideoFolder, id);

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

        var (success, message, _) = await _service.SaveAsync(request);
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
        var (success, message, _) = await _service.DeleteAsync(id);
        if (success) _videoSvc.DeleteAll(VideoFolder, id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }
}
