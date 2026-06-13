using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Video;

/// <summary>
/// Controller chuyên stream và upload video từ network share.
/// Không đụng DB — dùng chung cho mọi module (Guide, Policy, v.v.)
///
/// Cách dùng từ module khác:
///   - Stream:  GET  /Video/GUIDE_VIDEO/5
///   - Upload:  POST /Video/Upload/GUIDE_VIDEO/5   (AJAX, trả { success, url })
///   - Delete:  POST /Video/Delete/GUIDE_VIDEO/5   (AJAX, file only — DB tự xử)
/// </summary>
[Authorize]
public class VideoController : BaseController
{
    private readonly VideoFileService _videoSvc;

    public VideoController(VideoFileService videoSvc) => _videoSvc = videoSvc;

    // GET /Video/{folder}/{id}
    // Stream video có Range support (browser seek được, không cần download hết)
    [AllowAnonymous]
    [HttpGet("/Video/{folder}/{id:int}")]
    public IActionResult Stream(string folder, int id)
    {
        if (!VideoFileService.IsValidFolder(folder)) return BadRequest();

        var found = _videoSvc.Find(folder, id);
        if (found == null) return NotFound();

        return PhysicalFile(found.Value.path,
                            VideoFileService.MimeType(found.Value.ext),
                            enableRangeProcessing: true);
    }

    // POST /Video/Upload/{folder}/{id}
    // AJAX upload — lưu file, trả { success, message, url }
    // Module gọi endpoint này xong tự update DB với ext trả về
    [HttpPost("/Video/Upload/{folder}/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(524_288_000)]
    public async Task<IActionResult> Upload(string folder, int id, IFormFile file)
    {
        if (!VideoFileService.IsValidFolder(folder)) return BadRequest();

        var (ok, message, _) = await _videoSvc.SaveAsync(folder, id, file);
        var url = ok ? $"/Video/{folder}/{id}" : null;
        return Json(new { success = ok, message, url });
    }

    // POST /Video/Delete/{folder}/{id}
    // AJAX xóa file — module tự update DB sau khi nhận success
    [HttpPost("/Video/Delete/{folder}/{id:int}")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(string folder, int id)
    {
        if (!VideoFileService.IsValidFolder(folder)) return BadRequest();

        _videoSvc.DeleteAll(folder, id);
        return Json(new { success = true });
    }
}
