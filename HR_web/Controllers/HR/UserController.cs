using HR_web.API.Service;
using HR_web.Models.Account;
using HR_web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using X.PagedList;

namespace HR_web.Controllers.HR;

[Authorize]
public class UserController : BaseController
{
    private readonly AccountService _service;

    public UserController(AccountService service)
    {
        _service = service;
    }

    // ─────────────────────────────────────────────
    // GET: /User/UserManager
    // ─────────────────────────────────────────────
    public async Task<IActionResult> UserManager(
        string? fullName = null,
        string? deptCd = null,
        string? lineCd = null,
        string? workCd = null,
        int? roleId = null,
        string? empCd = null,
        int page = 1,
        int pageSize = 50)
    {
        var modelPaged = await _service.GetUserListAsync(
            fullName: fullName,
            deptCd: deptCd,
            lineCd: lineCd,
            workCd: workCd,
            roleId: roleId,
            empCd: empCd,
            page: page,
            pageSize: pageSize
        );

        // Dùng X.PagedList thay PagedList.Mvc cũ
        var pagedList = new StaticPagedList<UserInfoModel>(
            modelPaged.Data,
            page,
            pageSize,
            modelPaged.Total
        );

        ViewBag.FullName = fullName;
        ViewBag.DeptCd = deptCd;
        ViewBag.LineCd = lineCd;
        ViewBag.WorkCd = workCd;
        ViewBag.RoleId = roleId;
        ViewBag.EmpCd = empCd;
        ViewBag.PageSize = pageSize;

        return View(pagedList);
    }

    // ─────────────────────────────────────────────
    // GET: /User/UserDetail?empCd=xxx
    // ─────────────────────────────────────────────
    public async Task<IActionResult> UserDetail(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd))
        {
            TempData["ErrorMessage"] = "Mã nhân viên không hợp lệ!";
            return RedirectToAction("UserManager");
        }

        var model = await _service.GetUserDetailAsync(empCd);

        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy thông tin nhân viên!";
            return RedirectToAction("UserManager");
        }

        return View(model);
    }

    // ─────────────────────────────────────────────
    // GET: /User/DisableUser?empCd=xxx
    // ─────────────────────────────────────────────
    public async Task<IActionResult> DisableUser(string empCd)
    {
        try
        {
            var result = await _service.DisableUserAsync(empCd, CurrentUser!.EmpCd);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] =
                result ? $"Nhân viên {empCd} đã bị vô hiệu hóa!" : $"Vô hiệu hóa nhân viên {empCd} thất bại!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
        }
        return RedirectToAction("UserManager");
    }

    // ─────────────────────────────────────────────
    // GET: /User/EnableUser?empCd=xxx
    // ─────────────────────────────────────────────
    public async Task<IActionResult> EnableUser(string empCd)
    {
        try
        {
            var result = await _service.EnableUserAsync(empCd, CurrentUser!.EmpCd);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] =
                result ? $"Nhân viên {empCd} đã được mở khoá!" : $"Mở khoá nhân viên {empCd} thất bại!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
        }
        return RedirectToAction("UserManager");
    }

    // ─────────────────────────────────────────────
    // GET: /User/ResetPassword?empCd=xxx
    // ─────────────────────────────────────────────
    public async Task<IActionResult> ResetPassword(string empCd)
    {
        try
        {
            var result = await _service.ResetPasswordAsync(empCd, CurrentUser!.EmpCd);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] =
                result ? $"Password nhân viên {empCd} đã được reset!" : $"Reset password nhân viên {empCd} thất bại!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
        }
        return RedirectToAction("UserManager");
    }

    // ─────────────────────────────────────────────
    // POST: /User/ChangePassword
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ChangePassword(string empCd, string oldPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(empCd) || string.IsNullOrWhiteSpace(newPassword))
        {
            TempData["ErrorMessage"] = "Dữ liệu không hợp lệ!";
            return RedirectToAction("UserManager");
        }
        try
        {
            var result = await _service.ChangePasswordAsync(empCd, oldPassword, newPassword);
            TempData[result ? "SuccessMessage" : "ErrorMessage"] =
                result ? "Đổi mật khẩu thành công!" : "Đổi mật khẩu thất bại!";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
        }
        return RedirectToAction("UserManager");
    }

    // ─────────────────────────────────────────────
    // GET+POST: /User/CreateUser
    // ─────────────────────────────────────────────
    public IActionResult CreateUser()
    {
        return View(new CreateUserModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.EmpCd))
        {
            TempData["ErrorMessage"] = "Dữ liệu không hợp lệ!";
            TempData["OpenModal"] = "createUserModal";
            return RedirectToAction("UserManager");
        }
        try
        {
            model.LoginUser = CurrentUser!.EmpCd;
            var (success, message) = await _service.CreateUserAsync(model);

            TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
            TempData["OpenModal"] = "createUserModal";
            return RedirectToAction("UserManager");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
            TempData["OpenModal"] = "createUserModal";
            return RedirectToAction("UserManager");
        }
    }
}
