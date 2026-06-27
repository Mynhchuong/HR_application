using HR_web.API.Service;
using HR_web.Models.Bulletin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Bulletin;

[Authorize(Roles = "Admin,HR")]
public class BulletinAdminController : BaseController
{
    private readonly BulletinService     _service;
    private readonly VideoFileService    _videoService;

    private const string VIDEO_FOLDER = "BULLETIN_VIDEO";

    public BulletinAdminController(BulletinService service, VideoFileService videoService)
    {
        _service      = service;
        _videoService = videoService;
    }

    // ─────────────────────────────────────────────
    // GET /BulletinAdmin/Manage
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Manage()
    {
        var list = await _service.GetAdminListAsync();
        return View(list);
    }

    // ─────────────────────────────────────────────
    // GET /BulletinAdmin/Edit/{id?}
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null || id == 0)
        {
            return View(new BulletinEditViewModel
            {
                PUBLISH_FROM = DateTime.Now.Date,
                PUBLISH_TO   = DateTime.Now.Date.AddDays(30)
            });
        }

        var item = await _service.GetByIdAsync(id.Value);
        if (item == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy bản tin";
            return RedirectToAction("Manage");
        }

        var vm = new BulletinEditViewModel
        {
            ID           = item.ID,
            TITLE        = item.TITLE,
            CONTENT      = item.CONTENT ?? "",
            COVER_IMG    = item.COVER_IMG,
            PUBLISH_FROM = item.PUBLISH_FROM,
            PUBLISH_TO   = item.PUBLISH_TO,
            IS_PINNED    = item.IS_PINNED,
            PIN_ORDER    = item.PIN_ORDER
        };
        ViewBag.Media = item.MEDIA;
        return View(vm);
    }

    // ─────────────────────────────────────────────
    // POST /BulletinAdmin/Edit
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BulletinEditViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.TITLE) ||
            string.IsNullOrWhiteSpace(model.CONTENT))
        {
            TempData["ErrorMessage"] = "Vui lòng điền tiêu đề và nội dung";
            return View(model);
        }

        if (!model.PUBLISH_FROM.HasValue || !model.PUBLISH_TO.HasValue)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn ngày đăng và ngày kết thúc";
            return View(model);
        }

        var req = new SaveBulletinRequest
        {
            ID           = model.ID == 0 ? null : model.ID,
            TITLE        = model.TITLE.Trim(),
            CONTENT      = model.CONTENT,
            COVER_IMG    = string.IsNullOrWhiteSpace(model.COVER_IMG) ? null : model.COVER_IMG.Trim(),
            PUBLISH_FROM = model.PUBLISH_FROM.Value,
            PUBLISH_TO   = model.PUBLISH_TO.Value,
            IS_PINNED    = model.IS_PINNED,
            PIN_ORDER    = model.PIN_ORDER,
            LOGIN_USER   = CurrentUser?.EmpCd,
            LOGIN_NAME   = CurrentUser?.FullName
        };

        var (ok, msg, newId) = await _service.SaveAsync(req);
        if (!ok)
        {
            TempData["ErrorMessage"] = msg;
            return View(model);
        }

        TempData["SuccessMessage"] = msg;
        // Sau khi save xong, đi vào Preview để xem trước rồi quyết định đăng
        int targetId = newId > 0 ? newId : model.ID;
        return RedirectToAction("Preview", new { id = targetId });
    }

    // ─────────────────────────────────────────────
    // GET /BulletinAdmin/Preview/{id}
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Preview(int id)
    {
        var item = await _service.GetByIdAsync(id);   // KHÔNG truyền empcd → không tăng VIEW_COUNT
        if (item == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy bản tin";
            return RedirectToAction("Manage");
        }
        return View(item);
    }

    // ─────────────────────────────────────────────
    // Actions: Publish / Unpublish / TogglePin / SetPinOrder / Delete
    // ─────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id, string? returnUrl = null)
    {
        var (ok, msg, fcm) = await _service.PublishAsync(id, CurrentUser?.EmpCd ?? "");
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = msg;
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unpublish(int id)
    {
        var (ok, msg) = await _service.UnpublishAsync(id, CurrentUser?.EmpCd ?? "");
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePin(int id)
    {
        var (ok, msg, _) = await _service.TogglePinAsync(id, CurrentUser?.EmpCd ?? "");
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPinOrder(int id, int pinOrder)
    {
        var (ok, msg) = await _service.SetPinOrderAsync(id, pinOrder, CurrentUser?.EmpCd ?? "");
        return Json(new { success = ok, message = msg });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, msg) = await _service.DeleteAsync(id, CurrentUser?.EmpCd ?? "");
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = msg;
        return RedirectToAction("Manage");
    }

    // ─────────────────────────────────────────────
    // Media upload — chunked không cần, video thẳng vào share, ảnh đi qua TipTap → UploadBulletinImage
    // POST /BulletinAdmin/UploadVideo  — multipart, trả URL
    // ─────────────────────────────────────────────
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 500L * 1024 * 1024)]
    public async Task<IActionResult> UploadVideo(int bulletinId, IFormFile? file, int displayOrder = 0)
    {
        if (bulletinId <= 0 || file == null || file.Length == 0)
            return Json(new { success = false, message = "Thiếu file hoặc bulletinId" });

        // 1. Insert metadata — chỉ gửi ext, API tự build FILE_NAME = {ID}{ext}
        var ext = Path.GetExtension(file.FileName).ToLower();
        var addReq = new AddMediaRequest
        {
            BULLETIN_ID   = bulletinId,
            MEDIA_TYPE    = "VIDEO",
            FILE_NAME     = ext,                  // ".mp4" → API trả về fileName = "{ID}.mp4"
            DISPLAY_ORDER = displayOrder
        };
        var (ok, msg, mediaId, finalFileName) = await _service.AddMediaAsync(addReq);
        if (!ok || mediaId <= 0)
            return Json(new { success = false, message = msg });

        // 2. Lưu file vào share dưới tên {mediaId}{ext}
        var (saveOk, saveMsg, _) = await _videoService.SaveAsync(VIDEO_FOLDER, mediaId, file);
        if (!saveOk)
        {
            await _service.DeleteMediaAsync(mediaId);
            return Json(new { success = false, message = saveMsg });
        }

        var url = Url.Action("Stream", "Video", new { folder = VIDEO_FOLDER, id = mediaId });
        return Json(new { success = true, mediaId, url, fileName = finalFileName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVideo(int mediaId)
    {
        _videoService.DeleteAll(VIDEO_FOLDER, mediaId);
        var (ok, msg) = await _service.DeleteMediaAsync(mediaId);
        return Json(new { success = ok, message = msg });
    }
}
