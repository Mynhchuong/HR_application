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
                return View(ExpatViews.Profile, model);

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
        // UiLang là nguồn duy nhất quyết định ngôn ngữ (vi / en / ko) — trước đây chỉ phân biệt Expat hay không
        // nên Expat người Hàn vẫn nhận thông báo tiếng Anh.
        string uiLang = CurrentUser?.UiLang ?? "vi";

        if (string.IsNullOrWhiteSpace(oldPassword) ||
            string.IsNullOrWhiteSpace(newPassword) ||
            newPassword != confirmPassword)
        {
            TempData["ErrorMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang, "Dữ liệu không hợp lệ hoặc mật khẩu mới không khớp!", "Invalid input or passwords do not match!");
            return RedirectToAction("ProfileUser");
        }

        if (newPassword == "123456")
        {
            TempData["ErrorMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang, "Không được dùng mật khẩu mặc định 123456!", "Cannot use the default password 123456!");
            return RedirectToAction("ProfileUser");
        }

        if (newPassword == oldPassword)
        {
            TempData["ErrorMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang, "Mật khẩu mới phải khác mật khẩu cũ!", "New password must be different from current password!");
            return RedirectToAction("ProfileUser");
        }

        if (CurrentUser == null)
        {
            TempData["ErrorMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang, "Phiên đăng nhập hết hạn, vui lòng đăng nhập lại!", "Session expired, please log in again!");
            return RedirectToAction("ProfileUser");
        }

        try
        {
            var result = await _service.ChangePasswordAsync(CurrentUser.EmpCd, oldPassword, newPassword);

            if (result)
            {
                TempData["SuccessMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang, "Đổi mật khẩu thành công!", "Password changed successfully!");

                var updatedUser = CurrentUser;
                updatedUser.RequirePasswordChange = false;
                await AuthHelper.UpdateUserSessionAsync(HttpContext, updatedUser);
            }
            else
            {
                TempData["ErrorMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang, "Đổi mật khẩu thất bại!", "Password change failed. Please check your current password.");
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = HR_web.Helpers.ExpatText.Dual(uiLang,
                $"Có lỗi xảy ra: {ex.Message}", $"An error occurred: {ex.Message}");
        }

        return RedirectToAction("ProfileUser");
    }
}
