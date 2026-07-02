using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace HR_web.Controllers;


[Authorize]
public class ImageController : BaseController
{
    private readonly AccountService _accountService;

    // ── Network share config (dùng chung toàn app) ──────────────────────────
    internal const string ShareRoot = @"\\192.168.1.5\vserp_picture";
    internal static readonly System.Net.NetworkCredential ShareCred =
        new("localfileserver", "!samh0!!");

    // ── Thư mục từng loại ảnh ───────────────────────────────────────────────
    private const string EmployeeFolder = ShareRoot + @"\VSHRMS";
    private const string SignatureFolder = ShareRoot + @"\WRK_SIGN";
    private const string CantinFolder   = ShareRoot + @"\MY_SAMHO_CANTIN";
    private const string PolicyFolder   = ShareRoot + @"\POLICY";
    private const string BulletinFolder = ShareRoot + @"\BULLETIN\IMG";
    private const string HomeBannerFolder = ShareRoot + @"\MY_SAMHO_HOME";

    private static readonly string[] ImageExts    = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long               ImageMaxBytes = 10L * 1024 * 1024; // 10 MB

    public ImageController(AccountService accountService)
    {
        _accountService = accountService;
    }

    // ── Helper chung: đọc file từ network share và trả về ảnh ───────────────
    private IActionResult ServeNetworkImage(string filePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains(".."))
            return NotFound();
        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                if (!System.IO.File.Exists(filePath)) return NotFound();
                var bytes = System.IO.File.ReadAllBytes(filePath);
                var mime  = Path.GetExtension(fileName).ToLower() switch
                {
                    ".png"  => "image/png",
                    ".webp" => "image/webp",
                    _       => "image/jpeg"
                };
                return File(bytes, mime);
            }
        }
        catch { return NotFound(); }
    }

    // ── Ảnh nhân viên ───────────────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetEmployeeImage(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return BadRequest();
        var fileName = empCd + ".jpg";
        return ServeNetworkImage(Path.Combine(EmployeeFolder, fileName), fileName);
    }

    // ── Chữ ký ──────────────────────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetSignature(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return BadRequest();
        var fileName = empCd + ".jpg";
        return ServeNetworkImage(Path.Combine(SignatureFolder, fileName), fileName);
    }

    // ── Ảnh món ăn (cantin) ─────────────────────────────────────────────────
    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetFoodImage(string fileName)
        => ServeNetworkImage(Path.Combine(CantinFolder, fileName), fileName);

    // ── Ảnh Policy ──────────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadPolicyImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!ImageExts.Contains(ext))
            return Json(new { success = false, message = "Chỉ chấp nhận JPG, PNG, WebP, GIF!" });

        if (file.Length > ImageMaxBytes)
            return Json(new { success = false, message = "File không được vượt quá 10 MB!" });

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var savePath = Path.Combine(PolicyFolder, fileName);

        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(PolicyFolder);
                await using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }
            return Json(new { success = true, url = Url.Action("GetPolicyImage", "Image", new { fileName }) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi lưu file: {ex.Message}" });
        }
    }

    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetPolicyImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();
        return ServeNetworkImage(Path.Combine(PolicyFolder, fileName), fileName);
    }

    // ── Ảnh Bulletin (cover + ảnh trong nội dung TipTap) ────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadBulletinImage(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (!ImageExts.Contains(ext))
            return Json(new { success = false, message = "Chỉ chấp nhận JPG, PNG, WebP, GIF!" });

        if (file.Length > ImageMaxBytes)
            return Json(new { success = false, message = "File không được vượt quá 10 MB!" });

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var savePath = Path.Combine(BulletinFolder, fileName);

        try
        {
            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(BulletinFolder);
                await using var stream = new FileStream(savePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }
            return Json(new { success = true, url = Url.Action("GetBulletinImage", "Image", new { fileName }) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi lưu file: {ex.Message}" });
        }
    }

    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 86400)]
    public IActionResult GetBulletinImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();
        return ServeNetworkImage(Path.Combine(BulletinFolder, fileName), fileName);
    }

    // ── Home banner ─────────────────────────────────────────────────────────
    // Stream ảnh banner từ \\192.168.1.5\vserp_picture\MY_SAMHO_HOME\
    [HttpGet, AllowAnonymous]
    [ResponseCache(Duration = 300)] // 5 phút — banner đổi thường xuyên hơn bulletin
    public IActionResult GetHomeBanner(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return NotFound();
        return ServeNetworkImage(Path.Combine(HomeBannerFolder, fileName), fileName);
    }

    // ── Upload Home banner (chỉ HR/Admin) ──────────────────────────────────
    // Validate 1920×1080 (16:9, ±2%), 1200×675 ≤ w×h ≤ 4096×2304, ≤5MB, JPG/PNG/WebP
    // Auto-resize về 1920×1080 q=85 JPEG
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadHomeBanner(IFormFile? file)
    {
        // Role gate
        var role = CurrentUser?.RoleName;
        if (role is not ("HR" or "Admin"))
            return Json(new { success = false, message = "Bạn không có quyền upload banner" });

        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Chưa chọn file!" });

        // 1. MIME
        var allowedMime = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedMime.Contains(file.ContentType))
            return Json(new { success = false, message = "Chỉ chấp nhận JPG/PNG/WebP" });

        // 2. Size
        if (file.Length > 5L * 1024 * 1024)
            return Json(new { success = false, message = $"Ảnh {(file.Length / 1024 / 1024)}MB quá lớn, tối đa 5MB" });

        try
        {
            using var stream = file.OpenReadStream();
            using var image  = await SixLabors.ImageSharp.Image.LoadAsync(stream);

            int w = image.Width, h = image.Height;

            // 3. Dimensions
            if (w < 1200 || h < 675)
                return Json(new { success = false, message = $"Ảnh {w}×{h} quá nhỏ. Tối thiểu 1200×675 (khuyến nghị 1920×1080)" });
            if (w > 4096 || h > 2304)
                return Json(new { success = false, message = $"Ảnh {w}×{h} quá lớn. Tối đa 4096×2304" });

            // 4. Aspect ratio 16:9 ±2%
            double ratio  = (double)w / h;
            double target = 16.0 / 9.0;
            if (Math.Abs(ratio - target) > 0.02 * target)
                return Json(new { success = false, message = $"Tỉ lệ ảnh sai ({w}×{h}). Cần tỉ lệ 16:9 — crop lại 1920×1080" });

            // 5. Resize về 1920×1080 nếu khác
            if (w != 1920 || h != 1080)
            {
                image.Mutate(x => x.Resize(1920, 1080));
            }

            // 6. Save JPEG q=85 vào share folder
            var fileName = "banner_" + Guid.NewGuid().ToString("N") + ".jpg";
            var savePath = Path.Combine(HomeBannerFolder, fileName);

            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                Directory.CreateDirectory(HomeBannerFolder);
                await using var fs = new FileStream(savePath, FileMode.Create);
                await image.SaveAsJpegAsync(fs, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 });
            }

            return Json(new { success = true, fileName, url = Url.Action("GetHomeBanner", "Image", new { fileName }) });
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            return Json(new { success = false, message = "Không đọc được file — có phải ảnh hợp lệ không?" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Lỗi upload: {ex.Message}" });
        }
    }

    // ── Chữ ký ──────────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UploadSignature(string empCd, IFormFile? file)
    {
        bool isExpat = CurrentUser?.RoleName == "Expat";

        if (string.IsNullOrWhiteSpace(empCd) || file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = isExpat ? "Please select a file!" : "Chưa chọn file hoặc mã nhân viên trống!";
            return RedirectToAction("ProfileUser", "Profile");
        }

        string ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".jpg" && ext != ".jpeg")
        {
            TempData["ErrorMessage"] = isExpat ? "Only JPG files are accepted." : "Chỉ chấp nhận file JPG.";
            return RedirectToAction("ProfileUser", "Profile");
        }

        try
        {
            string savePath = Path.Combine(SignatureFolder, empCd + ".jpg");

            using (new NetworkShareHelper(ShareRoot, ShareCred))
            {
                using (var stream = new FileStream(savePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }

            if (CurrentUser != null)
            {
                var updatedUser = CurrentUser;
                updatedUser.SIGNATUREBLOB = "Y";

                await AuthHelper.UpdateUserSessionAsync(HttpContext, updatedUser);

                await _accountService.UpdateSignatureFlagAsync(empCd, true, CurrentUser.EmpCd);
            }

            TempData["SuccessMessage"] = isExpat ? "Signature updated successfully!" : "Cập nhật chữ ký thành công!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = isExpat ? $"File save error: {ex.Message}" : $"Lỗi lưu file: {ex.Message}";
        }

        return RedirectToAction("ProfileUser", "Profile");
    }
}
