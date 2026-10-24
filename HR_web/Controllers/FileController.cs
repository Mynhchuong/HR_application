using HR_web.API.Service;
using HR_web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Upload/serve file (không phải ảnh) — training material (PDF/DOCX/MP4).
// Pattern giống ImageController nhưng cho file docs/video.
// Network share: \\192.168.1.5\vserp_picture\TRAINING\{classId hoặc courseId}\
[Authorize]
public class FileController : BaseController
{
    private readonly TrainingService _training;
    private readonly IConfiguration _config;

    public FileController(TrainingService training, IConfiguration config)
    {
        _training = training;
        _config   = config;
    }

    // Reuse cùng share credential với ImageController
    internal const string ShareRoot = @"\\192.168.1.5\vserp_picture";
    internal static readonly System.Net.NetworkCredential ShareCred =
        new("localfileserver", "!samh0!!");

    private const string TrainingFolder = ShareRoot + @"\TRAINING";

    private static readonly string[] DocExts   = { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".txt" };
    private static readonly string[] VideoExts = { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
    private static readonly string[] ImgExts   = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxBytes = 500L * 1024 * 1024;   // 500 MB (cho video)

    // ─────────────────────────────────────────────────────────────
    // POST /File/UploadTrainingMaterial
    // multipart: file, level (COURSE|CLASS|SESSION), courseId|classId (+ sessionId nếu SESSION)
    // level SESSION = video 1 buổi học (đào tạo online) — vẫn cần classId để check quyền giáo viên.
    // Return: { success, url, fileName, fileType }
    // ─────────────────────────────────────────────────────────────
    [HttpPost]
    [Authorize]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadTrainingMaterial(
        IFormFile? file, string level, int? courseId = null, int? classId = null, int? sessionId = null)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file" });
        if (level != "COURSE" && level != "CLASS" && level != "SESSION")
            return Json(new { success = false, message = "level phải COURSE, CLASS hoặc SESSION" });
        if (level == "COURSE" && !courseId.HasValue)
            return Json(new { success = false, message = "COURSE level cần courseId" });
        if (level == "CLASS" && !classId.HasValue)
            return Json(new { success = false, message = "CLASS level cần classId" });
        if (level == "SESSION" && (!classId.HasValue || !sessionId.HasValue))
            return Json(new { success = false, message = "SESSION level cần classId + sessionId" });
        if (file.Length > MaxBytes)
            return Json(new { success = false, message = "File quá 500 MB" });

        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd))
            return Json(new { success = false, message = "Chưa đăng nhập" });

        bool isHrOrAdminOrCsr = CurrentUser?.RoleName is "HR" or "Admin" or "CSR";
        if (!isHrOrAdminOrCsr)
        {
            // Level COURSE chỉ HR/Admin/CSR (quản lý khóa học) — level CLASS/SESSION thì giáo viên
            // đứng lớp đó cũng được upload, dùng cùng check-access với UploadMaterial page.
            if (level == "COURSE")
                return Json(new { success = false, message = "Bạn không có quyền upload tài liệu cấp khóa học" });

            if (!await _training.CheckClassAccessAsync(classId!.Value, empcd))
                return Json(new { success = false, message = "Bạn không phải giáo viên của lớp này" });
        }

        var ext = Path.GetExtension(file.FileName).ToLower();
        string fileType;
        if (DocExts.Contains(ext))       fileType = ext == ".pdf" ? "PDF" : "DOCX";
        else if (VideoExts.Contains(ext)) fileType = "MP4";
        else if (ImgExts.Contains(ext))   fileType = "IMG";
        else return Json(new { success = false, message = "Định dạng file không hỗ trợ" });

        var subFolder = level switch
        {
            "COURSE"  => $"COURSE_{courseId}",
            "SESSION" => $"CLASS_{classId}_SESSION_{sessionId}",
            _         => $"CLASS_{classId}",
        };
        var folder    = Path.Combine(TrainingFolder, subFolder);
        var savedName = Guid.NewGuid().ToString("N") + ext;
        var savePath  = Path.Combine(folder, savedName);

        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(folder);
                await using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }
            var url = Url.Action("GetTrainingMaterial", "File", new { subFolder, fileName = savedName });
            return Json(new
            {
                success  = true,
                fileName = savedName,
                fileType,
                url,
                originalName = file.FileName,
                size = file.Length,
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi lưu file: {ex.Message}" });
        }
    }

    // ─────────────────────────────────────────────────────────────
    // GET /File/GetTrainingMaterial?subFolder=CLASS_5&fileName=xxx.pdf[&exp=&sig=]
    // Chấp nhận HOẶC cookie session (như cũ) HOẶC token ký sẵn (exp+sig, xem MaterialUrlTokenHelper) —
    // token cần thiết vì trình phát video native trong WKWebView (app My Samho iOS, có khả năng cả
    // Android) không chắc mang theo cookie khi tự gọi mạng để stream, khiến [Authorize] redirect về
    // trang Login thay vì trả video (báo lỗi thật 2026-10-24: chỉ app My Samho lỗi, trình duyệt mobile
    // + desktop đều xem được — đúng dấu hiệu cookie không tới được trình phát). [AllowAnonymous] ở
    // đây rồi tự check lại 1 trong 2 điều kiện, KHÔNG bỏ hẳn xác thực.
    // ─────────────────────────────────────────────────────────────
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetTrainingMaterial(string subFolder, string fileName, long exp = 0, string? sig = null)
    {
        if (string.IsNullOrWhiteSpace(subFolder) || string.IsNullOrWhiteSpace(fileName))
            return BadRequest();
        if (subFolder.Contains("..") || subFolder.Contains('/') || subFolder.Contains('\\'))
            return NotFound();
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();

        bool hasValidToken = exp > 0 && !string.IsNullOrEmpty(sig)
            && MaterialUrlTokenHelper.Verify(subFolder, fileName, exp, sig, _config["MaterialTokenSecret"] ?? "");
        if (!hasValidToken && User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var filePath = Path.Combine(TrainingFolder, subFolder, fileName);
        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                if (!System.IO.File.Exists(filePath)) return NotFound();
                var bytes = System.IO.File.ReadAllBytes(filePath);
                var ext = Path.GetExtension(fileName).ToLower();
                var mime = ext switch
                {
                    ".pdf"  => "application/pdf",
                    ".doc"  => "application/msword",
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    ".xls"  => "application/vnd.ms-excel",
                    ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    ".ppt"  => "application/vnd.ms-powerpoint",
                    ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    ".mp4"  => "video/mp4",
                    ".mov"  => "video/quicktime",
                    ".webm" => "video/webm",
                    ".png"  => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _       => "application/octet-stream",
                };
                // enableRangeProcessing: true — iOS Safari/WebKit BẮT BUỘC server trả 206 Partial
                // Content theo Range header mới chịu phát <video>, không chấp nhận full 200 OK như
                // Chrome/Edge desktop vẫn chịu được (báo lỗi thật 2026-10-24: video xem được trên máy
                // tính, không xem được trên iPhone). Dữ liệu đã buffer đủ (bytes) nên set cờ này an
                // toàn — không cần đổi sang streaming trực tiếp từ network share (rủi ro share bị
                // NetworkShareHelper.Dispose() cắt kết nối giữa lúc response đang ghi dần).
                return File(bytes, mime, enableRangeProcessing: true);
            }
        }
        catch { return NotFound(); }
    }
}
