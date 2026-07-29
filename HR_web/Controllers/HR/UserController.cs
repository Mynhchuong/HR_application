using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using X.PagedList;

namespace HR_web.Controllers.HR;

[Authorize(Roles = "Admin,HR")]
public class UserController : BaseController
{
    private readonly AccountService _service;
    private readonly DropdownService _dropdown;

    public UserController(AccountService service, DropdownService dropdown)
    {
        _service = service;
        _dropdown = dropdown;
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
        bool pwdResetToday = false,
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
            pwdResetToday: pwdResetToday,
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
        ViewBag.PwdResetToday = pwdResetToday;
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

    // ─────────────────────────────────────────────
    // GET: /User/DownloadRoleTemplate
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> DownloadRoleTemplate()
    {
        var roles = await _dropdown.GetRoleAsync();

        using var wb = new XLWorkbook();

        var ws1 = wb.Worksheets.Add("Import");
        string[] headers = { "STT", "Mã NV", "Role ID" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws1.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#217346");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws1.Cell(2, 1).Value = "* Role ID lấy từ sheet 'Danh sách Role'";
        ws1.Cell(2, 1).Style.Font.Italic = true;
        ws1.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        ws1.Range(2, 1, 2, 3).Merge();
        ws1.Columns().AdjustToContents();

        var ws2 = wb.Worksheets.Add("Danh sách Role");
        ws2.Cell(1, 1).Value = "Role ID";
        ws2.Cell(1, 2).Value = "Tên Role";
        ws2.Range(1, 1, 1, 2).Style.Font.Bold = true;
        ws2.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F497D");
        ws2.Range(1, 1, 1, 2).Style.Font.FontColor = XLColor.White;
        int r = 2;
        foreach (var role in roles.Where(x => x.text != "Admin"))
        {
            ws2.Cell(r, 1).Value = role.id;
            ws2.Cell(r, 2).Value = role.text;
            r++;
        }
        ws2.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauCapNhatRole.xlsx");
    }

    // ─────────────────────────────────────────────
    // POST: /User/ImportUpdateRole
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportUpdateRole(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn file Excel";
            return RedirectToAction("UserManager");
        }
        try
        {
            var items = new List<(string empCd, int roleId)>();
            var parseErrors = new List<AccountService.BulkUpdateRoleResult>();

            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int row = 3; row <= lastRow; row++)
            {
                var empCd = ws.Cell(row, 2).GetString()?.Trim();
                var roleIdStr = ws.Cell(row, 3).GetString()?.Trim();
                if (string.IsNullOrEmpty(empCd)) continue;

                if (!int.TryParse(roleIdStr, out int roleId))
                {
                    parseErrors.Add(new AccountService.BulkUpdateRoleResult
                    {
                        EmpCd = empCd,
                        Success = false,
                        Message = $"Role ID '{roleIdStr}' không hợp lệ"
                    });
                    continue;
                }
                items.Add((empCd, roleId));
            }

            var results = items.Count > 0
                ? await _service.BulkUpdateRoleAsync(items, CurrentUser!.EmpCd)
                : new List<AccountService.BulkUpdateRoleResult>();

            results.AddRange(parseErrors);

            TempData["ImportResults"] = JsonSerializer.Serialize(results);
            TempData["OpenModal"] = "importResultModal";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Lỗi đọc file: " + ex.Message;
        }
        return RedirectToAction("UserManager");
    }
}
