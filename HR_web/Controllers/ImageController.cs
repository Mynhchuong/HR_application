using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

/// <summary>
/// Phục vụ ảnh nhân viên và chữ ký từ network share.
/// Thay Server.MapPath bằng IWebHostEnvironment.WebRootPath trong .NET 8.
/// Thay Request.Files bằng IFormFile trong .NET 8.
/// </summary>
[Authorize]
public class ImageController : BaseController
{
    private readonly AccountService _accountService;
    private readonly IWebHostEnvironment _env;

    private readonly string _employeeImageFolder = @"\\192.168.1.5\vserp_picture\VSHRMS";
    private readonly string _signatureFolder = @"\\192.168.1.5\vserp_picture\WRK_SIGN";

    public ImageController(AccountService accountService, IWebHostEnvironment env)
    {
        _accountService = accountService;
        _env = env;
    }

    // ─────────────────────────────────────────────
    // GET: /Image/GetEmployeeImage?empCd=xxx
    // ─────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetEmployeeImage(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd))
            return BadRequest();

        string path = Path.Combine(_employeeImageFolder, empCd + ".jpg");

        if (!System.IO.File.Exists(path))
        {
            // Trả về ảnh placeholder từ wwwroot (thay Server.MapPath)
            string fallback = Path.Combine(_env.WebRootPath, "assets", "img", "illustrations", "danger-chat-ill.png");
            return PhysicalFile(fallback, "image/png");
        }

        return PhysicalFile(path, "image/jpeg");
    }

    // ─────────────────────────────────────────────
    // GET: /Image/GetSignature?empCd=xxx
    // ─────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetSignature(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd))
            return BadRequest();

        string path = Path.Combine(_signatureFolder, empCd + ".jpg");

        if (!System.IO.File.Exists(path))
        {
            string fallback = Path.Combine(_env.WebRootPath, "assets", "img", "illustrations", "danger-chat-ill.png");
            return PhysicalFile(fallback, "image/png");
        }

        return PhysicalFile(path, "image/jpeg");
    }

    // ─────────────────────────────────────────────
    // POST: /Image/UploadSignature
    // Dùng IFormFile thay Request.Files cũ
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UploadSignature(string empCd, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(empCd) || file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Chưa chọn file hoặc mã nhân viên trống!";
            return RedirectToAction("ProfileUser", "Profile");
        }

        string ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".jpg" && ext != ".jpeg")
        {
            TempData["ErrorMessage"] = "Chỉ chấp nhận file JPG.";
            return RedirectToAction("ProfileUser", "Profile");
        }

        try
        {
            string savePath = Path.Combine(_signatureFolder, empCd + ".jpg");

            // Ghi file lên network share (thay file.SaveAs)
            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Cập nhật trạng thái chữ ký trong Cookie và DB
            if (CurrentUser != null)
            {
                var updatedUser = CurrentUser;
                updatedUser.SIGNATUREBLOB = "Y";

                // Cập nhật Cookie (thay AuthHelper.UpdateUserSession cũ)
                await AuthHelper.UpdateUserSessionAsync(HttpContext, updatedUser);

                // Cập nhật DB
                await _accountService.UpdateSignatureFlagAsync(empCd, true, CurrentUser.EmpCd);
            }

            TempData["SuccessMessage"] = "Cập nhật chữ ký thành công!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Lỗi lưu file: {ex.Message}";
        }

        return RedirectToAction("ProfileUser", "Profile");
    }
}
