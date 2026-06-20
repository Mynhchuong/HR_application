using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Leave;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.HR;

[Authorize]
public class SundayLeaveController : BaseController
{
    private readonly LeaveService _leave;

    public SundayLeaveController(LeaveService leave)
    {
        _leave = leave;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> GetList(string? deptCd, string? lineCd, string? workCd, string? search)
    {
        var result = await _leave.GetSundayListAsync(deptCd, lineCd, workCd, search);
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([FromBody] SingleEmpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmpCd))
            return Json(new { success = false, message = "Mã nhân viên không hợp lệ" });
        return Json(await _leave.SundayAddAsync(req.EmpCd.Trim(), CurrentUser?.EmpCd ?? "HR"));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove([FromBody] SingleEmpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmpCd))
            return Json(new { success = false, message = "Mã nhân viên không hợp lệ" });
        return Json(await _leave.SundayRemoveAsync(req.EmpCd.Trim(), CurrentUser?.EmpCd ?? "HR"));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBulk([FromBody] BulkEmpRequest req)
    {
        if (req.EmpCds == null || req.EmpCds.Count == 0)
            return Json(new { success = false, message = "Chưa chọn nhân viên nào" });
        return Json(await _leave.SundayBulkRemoveAsync(req.EmpCds, CurrentUser?.EmpCd ?? "HR"));
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string? deptCd, string? lineCd, string? workCd, string? search)
    {
        var result = await _leave.GetSundayListAsync(deptCd, lineCd, workCd, search);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DS làm Chủ Nhật");

        string[] headers = { "STT", "Mã NV", "Họ & Tên", "Phòng Ban", "Line", "Work", "Ngày thêm" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var e in result.data)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = e.EMPCD;
            ws.Cell(row, 3).Value = e.EMP_NAME ?? "";
            ws.Cell(row, 4).Value = e.DEPT_NAME ?? e.DEPT_ID ?? "";
            ws.Cell(row, 5).Value = e.LINE_NAME ?? e.LINE_ID ?? "";
            ws.Cell(row, 6).Value = e.WORK_NAME ?? e.WORK_ID ?? "";
            ws.Cell(row, 7).Value = e.INST_DT ?? "";
            row++;
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"SundayLeave_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Import");
        ws.Cell(1, 1).Value = "Mã NV";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        ws.Cell(1, 1).Style.Font.FontColor = XLColor.White;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(2, 1).Value = "* Nhập mã nhân viên, mỗi mã một dòng";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        ws.Column(1).Width = 30;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauImportSundayLeave.xlsx");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });
        try
        {
            var empCds = new List<string>();
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var v = ws.Cell(r, 1).GetString()?.Trim();
                if (!string.IsNullOrEmpty(v)) empCds.Add(v);
            }
            if (empCds.Count == 0)
                return Json(new { success = false, message = "File không có dữ liệu" });
            return Json(await _leave.SundayImportAsync(empCds, CurrentUser?.EmpCd ?? "HR"));
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi đọc file: " + ex.Message });
        }
    }

    public class SingleEmpRequest { public string EmpCd  { get; set; } = ""; }
    public class BulkEmpRequest   { public List<string> EmpCds { get; set; } = new(); }
}
