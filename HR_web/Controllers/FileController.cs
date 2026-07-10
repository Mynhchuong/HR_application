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
    // multipart: file, level (COURSE|CLASS), courseId|classId
    // Return: { success, url, fileName, fileType }
    // ─────────────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadTrainingMaterial(
        IFormFile? file, string level, int? courseId = null, int? classId = null)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file" });
        if (level != "COURSE" && level != "CLASS")
            return Json(new { success = false, message = "level phải COURSE hoặc CLASS" });
        if (level == "COURSE" && !courseId.HasValue)
            return Json(new { success = false, message = "COURSE level cần courseId" });
        if (level == "CLASS" && !classId.HasValue)
            return Json(new { success = false, message = "CLASS level cần classId" });
        if (file.Length > MaxBytes)
            return Json(new { success = false, message = "File quá 500 MB" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        string fileType;
        if (DocExts.Contains(ext))       fileType = ext == ".pdf" ? "PDF" : "DOCX";
        else if (VideoExts.Contains(ext)) fileType = "MP4";
        else if (ImgExts.Contains(ext))   fileType = "IMG";
        else return Json(new { success = false, message = "Định dạng file không hỗ trợ" });

        var subFolder = level == "COURSE" ? $"COURSE_{courseId}" : $"CLASS_{classId}";
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
    // GET /File/GetTrainingMaterial?subFolder=CLASS_5&fileName=xxx.pdf
    // ─────────────────────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetTrainingMaterial(string subFolder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(subFolder) || string.IsNullOrWhiteSpace(fileName))
            return BadRequest();
        if (subFolder.Contains("..") || subFolder.Contains('/') || subFolder.Contains('\\'))
            return NotFound();
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();

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
                return File(bytes, mime);
            }
        }
        catch { return NotFound(); }
    }
}
