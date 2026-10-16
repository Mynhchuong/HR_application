using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using HR_web.API.Service;
using HR_web.Models.Employee;

namespace HR_web.Controllers.Employee;

[Microsoft.AspNetCore.Authorization.Authorize]
public class EmployeeController : BaseController
{
    private readonly EmployeeService _svc;
    public EmployeeController(EmployeeService svc) { _svc = svc; }

    public IActionResult MyTeam() => View();

    // GET: /Employee/MyTeamForExpat — bản tiếng Anh cho role Expat, cùng pattern
    // OtListForExpat/GpListForExpat/LeaveApprovalForExpat (dùng chung data/export endpoint,
    // chỉ khác view hiển thị + header Excel theo CurrentUser.RoleName == "Expat").
    public IActionResult MyTeamForExpat() => View();

    [HttpGet]
    public async Task<IActionResult> GetMyTeamData(
        string? search = null, string? deptcd = null,
        string? linecd = null, string? workcd = null, string? shiftcd = null,
        string? jikwicd = null, string? interest = null,
        bool    no_login = false, int page = 1, int page_size = 50)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
        {
            var isExpat0 = CurrentUser?.RoleName == "Expat";
            return Json(new { success = false, message = isExpat0 ? "Not logged in" : "Chưa đăng nhập" });
        }
        var result = await _svc.GetMyTeamAsync(CurrentUser.EmpCd, search, deptcd, linecd, workcd, shiftcd, jikwicd, interest, no_login, page, page_size);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyTeamFilters()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
        {
            var isExpat0 = CurrentUser?.RoleName == "Expat";
            return Json(new { success = false, message = isExpat0 ? "Not logged in" : "Chưa đăng nhập" });
        }
        var result = await _svc.GetMyTeamFiltersAsync(CurrentUser.EmpCd);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        string? search = null, string? deptcd = null,
        string? linecd = null, string? workcd = null, string? shiftcd = null,
        string? jikwicd = null, string? interest = null,
        bool    no_login = false)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Unauthorized();

        var isExpat = CurrentUser?.RoleName == "Expat";
        var result = await _svc.GetMyTeamAsync(CurrentUser.EmpCd, search, deptcd, linecd, workcd, shiftcd, jikwicd, interest, no_login, all: true);
        var fileName = isExpat ? $"EmployeeList_{DateTime.Now:yyyyMMdd}.xlsx" : $"DanhSachNhanVien_{DateTime.Now:yyyyMMdd}.xlsx";
        return BuildTeamExcel(result.data ?? new(), isExpat, fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Danh sách TOÀN CÔNG TY — chỉ HR/Admin, không giới hạn theo phạm vi phụ trách
    // (yêu cầu 2026-10-16: "hr cũng cần 1 page lấy all danh sách công nhân").
    // GET /Employee/AllEmployees
    // ─────────────────────────────────────────────────────────────────────────
    private bool IsHrOrAdmin => CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";

    public IActionResult AllEmployees()
    {
        if (!IsHrOrAdmin) return Forbid();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetAllEmployeesData(
        string? search = null, string? deptcd = null,
        string? linecd = null, string? workcd = null, string? shiftcd = null,
        string? jikwicd = null, string? interest = null,
        bool    no_login = false, int page = 1, int page_size = 50)
    {
        if (!IsHrOrAdmin || string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false, message = "Không có quyền" });
        var result = await _svc.GetAllEmployeesAsync(CurrentUser.EmpCd, search, deptcd, linecd, workcd, shiftcd, jikwicd, interest, no_login, page, page_size);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAllEmployeesFilters()
    {
        if (!IsHrOrAdmin || string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false, message = "Không có quyền" });
        var result = await _svc.GetAllEmployeesFiltersAsync(CurrentUser.EmpCd);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ExportAllEmployeesExcel(
        string? search = null, string? deptcd = null,
        string? linecd = null, string? workcd = null, string? shiftcd = null,
        string? jikwicd = null, string? interest = null,
        bool    no_login = false)
    {
        if (!IsHrOrAdmin || string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Unauthorized();

        var result = await _svc.GetAllEmployeesAsync(CurrentUser.EmpCd, search, deptcd, linecd, workcd, shiftcd, jikwicd, interest, no_login, all: true);
        return BuildTeamExcel(result.data ?? new(), isExpat: false, $"DanhSachToanBoNhanVien_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // Dùng chung cho ExportExcel (MyTeam) và ExportAllEmployeesExcel (AllEmployees) — cùng cấu trúc
    // cột, chỉ khác nguồn dữ liệu (có/không giới hạn scope).
    private IActionResult BuildTeamExcel(List<TeamMemberModel> data, bool isExpat, string fileName)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(isExpat ? "Employee List" : "Danh sách nhân viên");

        string[] headers = isExpat
            ? new[] { "No.", "Emp. Code", "Full Name", "Position", "Work performed", "Dept.", "Dept. Name", "Line", "Line Name", "Work Code", "Work Name", "Phone", "Email", "OT Hours (Month)", "OT Hours (Year)", "Leave Left", "Shift" }
            : new[] { "STT", "Mã NV", "Họ & Tên", "Chức vụ", "Công việc thực hiện", "Bộ phận", "Tên Bộ phận", "Line", "Tên Line", "Nhóm việc", "Tên Nhóm việc", "Số điện thoại", "Email", "Giờ TC tháng", "Giờ TC năm", "Phép năm còn lại", "Ca làm việc" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < data.Count; i++)
        {
            int row = i + 2;
            var d = data[i];
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = d.EMPCD;
            ws.Cell(row, 3).Value = d.EMP_NAME;
            ws.Cell(row, 4).Value = d.POSITION_NAME;
            ws.Cell(row, 5).Value = d.HARDWORK_TEN;
            ws.Cell(row, 6).Value = d.DEPTCD;
            ws.Cell(row, 7).Value = d.DEPT_NAME;
            ws.Cell(row, 8).Value = d.LINECD;
            ws.Cell(row, 9).Value = d.LINE_NAME;
            ws.Cell(row, 10).Value = d.WORKCD;
            ws.Cell(row, 11).Value = d.WORK_NAME;
            ws.Cell(row, 12).Value = d.PHONE;
            ws.Cell(row, 13).Value = d.EMAIL;
            ws.Cell(row, 14).Value = d.SUM_MONTH;
            ws.Cell(row, 15).Value = d.SUM_YEAR;
            ws.Cell(row, 16).Value = d.LEAVE_LEFT;
            ws.Cell(row, 17).Value = string.IsNullOrEmpty(d.SHIFT_CD) ? "" : $"{d.SHIFT_CD} ({d.SHIFT_TIME})";
            ws.Cell(row, 14).Style.NumberFormat.Format = "#,##0.0";
            ws.Cell(row, 15).Style.NumberFormat.Format = "#,##0.0";

            if (i % 2 == 1)
                ws.Row(row).Cells(1, 17).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
