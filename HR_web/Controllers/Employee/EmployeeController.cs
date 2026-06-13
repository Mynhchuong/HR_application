using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using HR_web.API.Service;

namespace HR_web.Controllers.Employee;

[Microsoft.AspNetCore.Authorization.Authorize]
public class EmployeeController : BaseController
{
    private readonly EmployeeService _svc;
    public EmployeeController(EmployeeService svc) { _svc = svc; }

    public IActionResult MyTeam() => View();

    [HttpGet]
    public async Task<IActionResult> GetMyTeamData(
        string? search = null, string? deptcd = null,
        string? linecd = null, string? workcd = null)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false, message = "Chưa đăng nhập" });
        var result = await _svc.GetMyTeamAsync(CurrentUser.EmpCd, search, deptcd, linecd, workcd);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        string? search = null, string? deptcd = null,
        string? linecd = null, string? workcd = null)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Unauthorized();

        var result = await _svc.GetMyTeamAsync(CurrentUser.EmpCd, search, deptcd, linecd, workcd);
        var data   = result.data ?? new();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh sách nhân viên");

        string[] headers = { "STT", "Mã NV", "Họ & Tên", "Bộ phận", "Tên Bộ phận", "Line", "Tên Line", "Nhóm việc", "Tên Nhóm việc" };
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
            ws.Cell(row, 4).Value = d.DEPTCD;
            ws.Cell(row, 5).Value = d.DEPT_NAME;
            ws.Cell(row, 6).Value = d.LINECD;
            ws.Cell(row, 7).Value = d.LINE_NAME;
            ws.Cell(row, 8).Value = d.WORKCD;
            ws.Cell(row, 9).Value = d.WORK_NAME;

            if (i % 2 == 1)
                ws.Row(row).Cells(1, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DanhSachNhanVien_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
