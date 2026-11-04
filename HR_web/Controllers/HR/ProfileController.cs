using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.HR;

[Authorize]
public class ProfileController : BaseController
{
    private readonly AccountService _service;

    public ProfileController(AccountService service)
    {
        _service = service;
    }

    // ─────────────────────────────────────────────
    // GET: /Profile/ProfileUser
    // ─────────────────────────────────────────────
    public async Task<IActionResult> ProfileUser()
    {
        try
        {
            var model = await _service.GetUserDetailAsync(CurrentUser!.EmpCd);

            // Tài khoản hệ thống không có trong ECM100 (vd: admin) → dùng thông tin từ session
            if (model == null)
            {
                model = new UserDetailModel
                {
                    EmpCd    = CurrentUser!.EmpCd,
                    FullName = CurrentUser.FullName ?? CurrentUser.EmpCd,
                };
            }

            model.EmpCd        = CurrentUser!.EmpCd;
            model.HasSignature = CurrentUser.SIGNATUREBLOB == "Y";
            // Ảnh chữ ký cache 1 ngày (GetSignature ResponseCache Duration=86400) — cùng 1 URL nên
            // sau khi NV upload chữ ký mới, trình duyệt vẫn trả ảnh CŨ từ cache tới hết 1 ngày, NV
            // tưởng lưu không thành công (bug thật báo 2026-11-03, test trên iPhone). Gắn thêm query
            // "v=<LastWriteTime file thật>" — ảnh không đổi thì URL giống hệt (vẫn hưởng cache), ảnh
            // vừa ghi đè thì URL đổi theo, trình duyệt tự tải lại.
            if (model.HasSignature)
            {
                long version = 0;
                try
                {
                    using (new HR_web.Helpers.NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
                    {
                        var sigPath = Path.Combine(ImageController.SignatureFolder, model.EmpCd + ".jpg");
                        if (System.IO.File.Exists(sigPath))
                            version = System.IO.File.GetLastWriteTimeUtc(sigPath).Ticks;
                    }
                }
                catch { /* lỗi đọc network share -> vẫn hiện ảnh, chỉ mất tác dụng cache-bust lần này */ }
                model.SignatureUrl = Url.Action("GetSignature", "Image", new { empCd = model.EmpCd, v = version });
            }

            if (CurrentUser.RoleName == "Expat")
                return View("ProfileUserExpat", model);

            return View(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
            return View(new UserDetailModel { EmpCd = CurrentUser?.EmpCd ?? "" });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /Profile/ChangePassword
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, string confirmPassword)
    {
        bool isExpat = CurrentUser?.RoleName == "Expat";

        if (string.IsNullOrWhiteSpace(oldPassword) ||
            string.IsNullOrWhiteSpace(newPassword) ||
            newPassword != confirmPassword)
        {
            TempData["ErrorMessage"] = isExpat
                ? "Invalid input or passwords do not match!"
                : "Dữ liệu không hợp lệ hoặc mật khẩu mới không khớp!";
            return RedirectToAction("ProfileUser");
        }

        if (newPassword == "123456")
        {
            TempData["ErrorMessage"] = isExpat
                ? "Cannot use the default password 123456!"
                : "Không được dùng mật khẩu mặc định 123456!";
            return RedirectToAction("ProfileUser");
        }

        if (newPassword == oldPassword)
        {
            TempData["ErrorMessage"] = isExpat
                ? "New password must be different from current password!"
                : "Mật khẩu mới phải khác mật khẩu cũ!";
            return RedirectToAction("ProfileUser");
        }

        if (CurrentUser == null)
        {
            TempData["ErrorMessage"] = isExpat
                ? "Session expired, please log in again!"
                : "Phiên đăng nhập hết hạn, vui lòng đăng nhập lại!";
            return RedirectToAction("ProfileUser");
        }

        try
        {
            var result = await _service.ChangePasswordAsync(CurrentUser.EmpCd, oldPassword, newPassword);

            if (result)
            {
                TempData["SuccessMessage"] = isExpat
                    ? "Password changed successfully!"
                    : "Đổi mật khẩu thành công!";

                var updatedUser = CurrentUser;
                updatedUser.RequirePasswordChange = false;
                await AuthHelper.UpdateUserSessionAsync(HttpContext, updatedUser);
            }
            else
            {
                TempData["ErrorMessage"] = isExpat
                    ? "Password change failed. Please check your current password."
                    : "Đổi mật khẩu thất bại!";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = isExpat
                ? $"An error occurred: {ex.Message}"
                : $"Có lỗi xảy ra: {ex.Message}";
        }

        return RedirectToAction("ProfileUser");
    }
}
