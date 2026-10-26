using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace HR_web.Controllers.Inquiry;

/// <summary>
/// Abstract base — chứa UploadFile, GetFile, MoveFilesToFinal dùng chung cho cả 3 controller.
/// </summary>
[Authorize]
public abstract class InquiryBaseController : BaseController
{
    protected readonly InquiryService _inquiry;

    protected InquiryBaseController(InquiryService inquiry)
    {
        _inquiry = inquiry;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Upload file đính kèm → lưu temp, client gửi metadata khi send
    // POST /{controller}/UploadFile
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Không có file" });

        var ext    = Path.GetExtension(file.FileName).ToLower();
        bool isImg = ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
        bool isVid = ext is ".mp4" or ".webm" or ".mov" or ".m4v";
        bool isDoc = ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx";
        if (!isImg && !isVid && !isDoc)
            return Json(new { success = false, message = "Định dạng không hỗ trợ (jpg/png/gif/webp/mp4/webm/mov/pdf/doc/xls)" });

        // Video chụp từ điện thoại ở 1080p/4K dễ vượt 50MB chỉ sau 30-60 giây — nâng lên 100MB cho đỡ
        // chật (yêu cầu 2026-10-26: tối ưu upload ảnh/video/file từ điện thoại).
        long maxBytes = isVid ? 100L * 1024 * 1024 : 10L * 1024 * 1024;
        if (file.Length > maxBytes)
            return Json(new { success = false, message = isVid ? "Video vượt quá 100MB" : "File vượt quá 10MB" });

        try
        {
            string sessionId = HttpContext.Session.Id;
            string safeFile  = $"{Guid.NewGuid()}{ext}";
            string relDir    = Path.Combine("INQUIRY", "temp", sessionId);
            string fullDir   = Path.Combine(ImageController.ShareRoot, relDir);
            string fullPath  = Path.Combine(fullDir, safeFile);
            string relPath   = Path.Combine(relDir, safeFile).Replace('\\', '/');
            string? thumbRelPath = null;

            using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
            {
                Directory.CreateDirectory(fullDir);

                // Ảnh chụp thẳng từ camera điện thoại (jpg/jpeg — png/gif/webp thường là screenshot,
                // không có EXIF orientation nên giữ nguyên) lưu thô trước đây dễ bị NGHIÊNG/NGƯỢC khi
                // xem bản gốc (zoom/full-size trong chat) — cùng bug đã fix ở UploadSignature. Trước
                // đây chỉ thumbnail 200x200 được load qua ImageSharp (tự AutoOrient khi Load) nên đúng
                // chiều, còn ảnh gốc thì không (yêu cầu 2026-10-26).
                if (isImg && (ext == ".jpg" || ext == ".jpeg"))
                {
                    using var stream = file.OpenReadStream();
                    using var img = await Image.LoadAsync(stream);
                    img.Mutate(x => x.AutoOrient());

                    int w = img.Width, h = img.Height;
                    int maxSide = Math.Max(w, h);
                    if (maxSide > 1600)
                    {
                        double scale = 1600.0 / maxSide;
                        img.Mutate(x => x.Resize((int)Math.Round(w * scale), (int)Math.Round(h * scale)));
                    }

                    await using (var fs = System.IO.File.Create(fullPath))
                        await img.SaveAsJpegAsync(fs, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 });

                    try
                    {
                        string thumbName = "thumb_" + safeFile;
                        string thumbFull = Path.Combine(fullDir, thumbName);
                        thumbRelPath     = Path.Combine(relDir, thumbName).Replace('\\', '/');

                        using var thumb = img.Clone(x => x.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Max,
                            Size = new Size(200, 200)
                        }));
                        await thumb.SaveAsync(thumbFull);
                    }
                    catch { thumbRelPath = null; }
                }
                else
                {
                    await using (var fs = System.IO.File.Create(fullPath))
                        await file.CopyToAsync(fs);

                    if (isImg)
                    {
                        try
                        {
                            string thumbName = "thumb_" + safeFile;
                            string thumbFull = Path.Combine(fullDir, thumbName);
                            thumbRelPath     = Path.Combine(relDir, thumbName).Replace('\\', '/');

                            using var img = await Image.LoadAsync(fullPath);
                            img.Mutate(x => x.Resize(new ResizeOptions
                            {
                                Mode = ResizeMode.Max,
                                Size = new Size(200, 200)
                            }));
                            await img.SaveAsync(thumbFull);
                        }
                        catch { thumbRelPath = null; }
                    }
                }
            }

            return Json(new
            {
                success   = true,
                fileName  = file.FileName,
                filePath  = relPath,
                thumbPath = thumbRelPath,
                fileType  = isImg ? "IMAGE" : isVid ? "VIDEO" : "FILE",
                mimeType  = file.ContentType,
                fileSize  = file.Length
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi lưu file: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phục vụ file từ network share
    // GET /{controller}/GetFile?path=INQUIRY/2026/INQ-001/...[&exp=&sig=]
    // Chấp nhận HOẶC cookie session (như cũ) HOẶC token ký sẵn (exp+sig, chỉ InquiryService ký cho
    // attachment VIDEO — xem MaterialUrlTokenHelper, cùng lý do đã áp dụng cho training video: trình
    // phát video native trong WKWebView không chắc mang theo cookie). [AllowAnonymous] rồi tự check
    // lại 1 trong 2 điều kiện, KHÔNG bỏ hẳn xác thực.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetFile(string path, [FromServices] IConfiguration config, long exp = 0, string? sig = null)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..")) return NotFound();

        bool hasValidToken = exp > 0 && !string.IsNullOrEmpty(sig)
            && MaterialUrlTokenHelper.Verify(path, "", exp, sig, config?["MaterialTokenSecret"] ?? "");
        if (!hasValidToken && User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        try
        {
            string fullPath = Path.Combine(ImageController.ShareRoot, path.Replace('/', '\\'));
            if (!fullPath.StartsWith(ImageController.ShareRoot, StringComparison.OrdinalIgnoreCase))
                return NotFound();

            using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
            {
                if (!System.IO.File.Exists(fullPath)) return NotFound();
                var bytes = System.IO.File.ReadAllBytes(fullPath);
                var mime  = Path.GetExtension(path).ToLower() switch
                {
                    ".png"  => "image/png",
                    ".gif"  => "image/gif",
                    ".webp" => "image/webp",
                    ".mp4"  => "video/mp4",
                    ".webm" => "video/webm",
                    ".mov"  => "video/quicktime",
                    ".m4v"  => "video/x-m4v",
                    ".pdf"  => "application/pdf",
                    ".doc"  => "application/msword",
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    ".xls"  => "application/vnd.ms-excel",
                    ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    _       => "image/jpeg"
                };
                // enableRangeProcessing: true — video <video> tag trên iOS/Android WebView cần server
                // trả 206 Partial Content theo byte-range mới phát được (cùng lỗi/cùng fix đã xác nhận
                // thật ở training video, xem FileController.GetTrainingMaterial).
                return File(bytes, mime, enableRangeProcessing: true);
            }
        }
        catch { return NotFound(); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: di chuyển file từ temp → INQUIRY/{year}/{inquiryNo}/
    // ─────────────────────────────────────────────────────────────────────────
    protected static List<InquiryFileInfo> MoveFilesToFinal(
        List<InquiryFileInfo> tempFiles, string inquiryNo, string year)
    {
        if (string.IsNullOrWhiteSpace(inquiryNo)) return tempFiles;

        var    result      = new List<InquiryFileInfo>();
        string relFinalDir = Path.Combine("INQUIRY", year, inquiryNo);
        string fullFinalDir = Path.Combine(ImageController.ShareRoot, relFinalDir);

        try
        {
            using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
            {
                Directory.CreateDirectory(fullFinalDir);
                foreach (var f in tempFiles)
                {
                    string srcFull = Path.Combine(ImageController.ShareRoot, f.FilePath.Replace('/', '\\'));
                    if (!srcFull.StartsWith(ImageController.ShareRoot, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string destName = Path.GetFileName(f.FilePath);
                    string destFull = Path.Combine(fullFinalDir, destName);
                    string destRel  = Path.Combine(relFinalDir, destName).Replace('\\', '/');

                    if (!System.IO.File.Exists(srcFull)) continue;

                    System.IO.File.Move(srcFull, destFull, overwrite: true);

                    string? finalThumbRel = null;
                    if (!string.IsNullOrEmpty(f.ThumbPath))
                    {
                        string srcThumb = Path.Combine(ImageController.ShareRoot, f.ThumbPath.Replace('/', '\\'));
                        if (srcThumb.StartsWith(ImageController.ShareRoot, StringComparison.OrdinalIgnoreCase)
                            && System.IO.File.Exists(srcThumb))
                        {
                            string thumbName     = Path.GetFileName(f.ThumbPath);
                            string destThumbFull = Path.Combine(fullFinalDir, thumbName);
                            finalThumbRel        = Path.Combine(relFinalDir, thumbName).Replace('\\', '/');
                            System.IO.File.Move(srcThumb, destThumbFull, overwrite: true);
                        }
                    }

                    result.Add(new InquiryFileInfo
                    {
                        FileName  = f.FileName,
                        FilePath  = destRel,
                        FileType  = f.FileType,
                        MimeType  = f.MimeType,
                        FileSize  = f.FileSize,
                        ThumbPath = finalThumbRel
                    });
                }
            }
            return result;
        }
        catch
        {
            // Không lưu temp path vào DB — file sẽ bị cleanup service dọn sau
            return new List<InquiryFileInfo>();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: lấy InquiryNo (để move file) — fallback gọi API nếu client không truyền
    // ─────────────────────────────────────────────────────────────────────────
    protected async Task<string> ResolveInquiryNoAsync(long inquiryId, string? inquiryNo)
    {
        if (!string.IsNullOrWhiteSpace(inquiryNo)) return inquiryNo;
        var conv = await _inquiry.GetMessagesAsync(inquiryId);
        return conv.inquiry?.InquiryNo ?? "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Lấy danh sách topic (dùng chung cho cả 3 controller)
    // GET /{controller}/GetTopics
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetTopics()
    {
        var result = await _inquiry.GetTopicsAsync();
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared request models
    // ─────────────────────────────────────────────────────────────────────────

    public class IdRequest { public long Id { get; set; } }
}
