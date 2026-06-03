using ClosedXML.Excel;
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
        string? deptCd = null,
        string? lineCd = null,
        string? workCd = null,
        int? roleId = null,
        string? empCd = null,
        int page = 1,
        int pageSize = 50)
    {
        await _service.SyncResignedUsersAsync();

        var modelPaged = await _service.GetUserListAsync(
            fullName: null,
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
    // POST: /User/UpdateRole
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRole(string empCd, int roleId)
    {
        try
        {
            var (success, message) = await _service.UpdateRoleAsync(empCd, roleId, CurrentUser!.EmpCd);
            TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
        }
        return RedirectToAction("UserManager");
    }

    // ─────────────────────────────────────────────
    // GET: /User/ExportExcel
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        string? deptCd = null, string? lineCd = null, string? workCd = null,
        int? roleId = null, string? empCd = null)
    {
        var result = await _service.GetUserListAsync(
            fullName: null, deptCd: deptCd, lineCd: lineCd, workCd: workCd,
            roleId: roleId, empCd: empCd, page: 1, pageSize: 9999);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("User Manager");

        string[] headers = { "STT", "Mã NV", "Họ & Tên", "Phòng Ban", "Line", "Work", "Role", "Trạng Thái", "Lần Cuối Đăng Nhập" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#217346");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var u in result.Data)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = u.EmpCd;
            ws.Cell(row, 3).Value = u.FullName;
            ws.Cell(row, 4).Value = u.DeptCd ?? "";
            ws.Cell(row, 5).Value = u.LineCd ?? "";
            ws.Cell(row, 6).Value = u.WorkCd ?? "";
            ws.Cell(row, 7).Value = u.RoleName ?? "";
            ws.Cell(row, 8).Value = u.IsActive == 1 ? "Active" : "Inactive";
            ws.Cell(row, 9).Value = u.LastedLogin?.ToString("dd/MM/yyyy HH:mm") ?? "";
            row++;
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"UserManager_{DateTime.Now:yyyyMMdd}.xlsx");
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
