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

            if (model == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy thông tin người dùng!";
                return RedirectToAction("UserManager", "User");
            }

            model.EmpCd = CurrentUser.EmpCd;
            return View(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
            return RedirectToAction("UserManager", "User");
        }
    }

    // ─────────────────────────────────────────────
    // POST: /Profile/ChangePassword
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(oldPassword) ||
            string.IsNullOrWhiteSpace(newPassword) ||
            newPassword != confirmPassword)
        {
            TempData["ErrorMessage"] = "Dữ liệu không hợp lệ hoặc mật khẩu mới không khớp!";
            return RedirectToAction("ProfileUser");
        }

        try
        {
            var result = await _service.ChangePasswordAsync(CurrentUser!.EmpCd, oldPassword, newPassword);

            if (result)
            {
                TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";

                // Cập nhật flag trong Cookie (thay AuthHelper.UpdateUserSession cũ)
                var updatedUser = CurrentUser;
                updatedUser!.RequirePasswordChange = false;
                await AuthHelper.UpdateUserSessionAsync(HttpContext, updatedUser);
            }
            else
            {
                TempData["ErrorMessage"] = "Đổi mật khẩu thất bại!";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
        }

        return RedirectToAction("ProfileUser");
    }
}
