using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
