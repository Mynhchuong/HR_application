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

        // Prev / Next trong cùng category
        var all = await _service.GetListAsync();
        var siblings = all
            .Where(g => g.IS_ACTIVE == 1 && g.CATEGORY == model.CATEGORY)
            .OrderBy(g => g.DISPLAY_ORDER)
            .ThenBy(g => g.ID)
            .ToList();

        var idx = siblings.FindIndex(g => g.ID == id);
        if (idx > 0)
        {
            ViewBag.PrevId    = siblings[idx - 1].ID;
            ViewBag.PrevTitle = siblings[idx - 1].TITLE;
        }
        if (idx >= 0 && idx < siblings.Count - 1)
        {
            ViewBag.NextId    = siblings[idx + 1].ID;
            ViewBag.NextTitle = siblings[idx + 1].TITLE;
        }

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

        bool hasNewVideo = false;
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
            hasNewVideo = true;
        }

        var request = new SaveGuideRequest
        {
            ID            = model.ID == 0 ? null : model.ID,
            CATEGORY      = model.CATEGORY?.Trim(),
            TITLE         = model.TITLE.Trim(),
            CONTENT       = model.CONTENT,
            DISPLAY_ORDER = model.DISPLAY_ORDER,
            IS_ACTIVE     = model.IS_ACTIVE,
            LOGIN_USER    = CurrentUser!.EmpCd
        };

        var (success, message, savedId) = await _service.SaveAsync(request);

        if (success && hasNewVideo && savedId > 0)
        {
            var (fileOk, _, _) = await _videoSvc.SaveAsync(VideoFolder, savedId, VideoFile!);
            if (fileOk) await _service.SetVideoAsync(savedId, "Y", CurrentUser!.EmpCd);
        }

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
        var (ok, message, ext) = await _videoSvc.SaveAsync(VideoFolder, id, file);
        if (!ok) return Json(new { success = false, message });

        await _service.SetVideoAsync(id, "Y", CurrentUser!.EmpCd);
        return Json(new { success = true, message = "Upload thành công", videoUrl = Url.Action("Stream", "Video", new { folder = VideoFolder, id }) });
    }

    // POST /Guide/SaveChunk?id=
    // Chunked upload: client splits file into 4MB chunks, each request is small → bypasses IIS maxAllowedContentLength
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 6_000_000)]
    public async Task<IActionResult> SaveChunk(
        int id, string sessionId, int chunkIndex, int totalChunks, string originalExt,
        IFormFile chunk)
    {
        if (string.IsNullOrEmpty(sessionId) || sessionId.Length > 64 ||
            !sessionId.All(c => char.IsLetterOrDigit(c)))
            return Json(new { success = false, message = "Session không hợp lệ" });

        var ext = originalExt.ToLowerInvariant();
        if (!VideoFileService.AllowedExts.Contains(ext))
            return Json(new { success = false, message = "Định dạng không hỗ trợ" });

        var tempDir = Path.Combine(Path.GetTempPath(), "hr_video_chunks", sessionId);
        Directory.CreateDirectory(tempDir);

        var chunkPath = Path.Combine(tempDir, $"{chunkIndex:D6}");
        await using (var f = new FileStream(chunkPath, FileMode.Create))
            await chunk.CopyToAsync(f);

        if (chunkIndex < totalChunks - 1)
            return Json(new { success = true, done = false });

        // Last chunk — assemble all chunks into final file
        var guide = await _service.GetByIdAsync(id);
        if (guide == null)
        {
            TryCleanTemp(tempDir);
            return Json(new { success = false, message = "Không tìm thấy mục hướng dẫn" });
        }

        _videoSvc.DeleteAll(VideoFolder, id);
        var savePath = _videoSvc.FilePath(VideoFolder, id, ext);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

        await using (var output = new FileStream(savePath, FileMode.Create))
        {
            for (int i = 0; i <= chunkIndex; i++)
            {
                await using var input = new FileStream(Path.Combine(tempDir, $"{i:D6}"), FileMode.Open, FileAccess.Read);
                await input.CopyToAsync(output);
            }
        }

        TryCleanTemp(tempDir);

        await _service.SetVideoAsync(id, "Y", CurrentUser!.EmpCd);
        return Json(new { success = true, message = "Upload thành công", done = true, videoUrl = Url.Action("Stream", "Video", new { folder = VideoFolder, id }) });
    }

    private static void TryCleanTemp(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* ignore */ }
    }

    // POST /Guide/RemoveVideo?id=
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveVideo(int id)
    {
        _videoSvc.DeleteAll(VideoFolder, id);
        await _service.SetVideoAsync(id, null, CurrentUser!.EmpCd);
        return Json(new { success = true, message = "Đã xóa video" });
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
        var (success, message) = await _service.DeleteAsync(id);
        if (success) _videoSvc.DeleteAll(VideoFolder, id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }
}
