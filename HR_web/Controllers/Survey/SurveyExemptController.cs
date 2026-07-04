using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Survey;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Survey;

// HR/Admin: quản lý blacklist survey (ILLITERATE / NO_PHONE / BOTH)
[Authorize(Roles = "Admin,HR")]
public class SurveyExemptController : BaseController
{
    private readonly SurveyExemptService _exempt;
    private readonly DropdownService _dropdown;

    public SurveyExemptController(SurveyExemptService exempt, DropdownService dropdown)
    {
        _exempt = exempt;
        _dropdown = dropdown;
    }

    // GET /SurveyExempt
    public async Task<IActionResult> Index(string? empcd, string? type, int? isActive, string? name,
        string? deptcd, string? linecd, string? workcd)
    {
        ViewBag.FEmpcd  = empcd;
        ViewBag.FType   = type;
        ViewBag.FActive = isActive;
        ViewBag.FName   = name;
        ViewBag.FDept   = deptcd;
        ViewBag.FLine   = linecd;
        ViewBag.FWork   = workcd;
        ViewBag.Depts   = await _dropdown.GetDeptAsync();

        var list = await _exempt.ListAsync(empcd, type, isActive, name, deptcd, linecd, workcd);
        return View(list);
    }

    // POST /SurveyExempt/Save  (AJAX upsert)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveVm req)
    {
        var payload = new
        {
            EMPCD          = req.EmpCd,
            EXEMPT_TYPE    = req.ExemptType,
            NOTE           = req.Note,
            EFFECTIVE_DATE = req.EffectiveDate,
            IS_ACTIVE      = req.IsActive,
            LOGIN_USER     = CurrentUser?.EmpCd,
        };
        var (ok, msg) = await _exempt.SaveAsync(payload);
        return Json(new { success = ok, message = msg });
    }

    // POST /SurveyExempt/Delete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] DeleteVm req)
    {
        var (ok, msg) = await _exempt.DeleteAsync(req.EmpCd, req.ExemptType, CurrentUser?.EmpCd);
        return Json(new { success = ok, message = msg });
    }

    // GET /SurveyExempt/Template  → Excel trống
    [HttpGet]
    public IActionResult Template()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Exempt");
        string[] headers = { "EMPCD", "EXEMPT_TYPE", "NOTE", "EFFECTIVE_DATE (yyyy-MM-dd)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
            c.Style.Font.FontColor = XLColor.White;
        }
        // Ví dụ + ghi chú
        ws.Cell(2, 1).Value = "99090701";
        ws.Cell(2, 2).Value = "ILLITERATE";
        ws.Cell(2, 3).Value = "Ghi chú tuỳ chọn";
        ws.Cell(2, 4).Value = DateTime.Today.ToString("yyyy-MM-dd");

        ws.Cell(4, 1).Value = "* EXEMPT_TYPE hợp lệ: ILLITERATE | NO_PHONE | BOTH";
        ws.Cell(5, 1).Value = "* Nếu bỏ trống EFFECTIVE_DATE → dùng ngày hôm nay";
        ws.Range(4, 1, 5, 1).Style.Font.Italic = true;
        ws.Range(4, 1, 5, 1).Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauImportSurveyExempt.xlsx");
    }

    // GET /SurveyExempt/Export  → Excel list (theo filter hiện tại)
    [HttpGet]
    public async Task<IActionResult> Export(string? empcd, string? type, int? isActive, string? name,
        string? deptcd, string? linecd, string? workcd)
    {
        var list = await _exempt.ListAsync(empcd, type, isActive, name, deptcd, linecd, workcd);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Exempt");
        string[] headers = { "STT", "EmpCd", "Loại", "Ghi chú", "Áp dụng từ", "Active", "Họ tên", "Dept", "Line", "Work", "Ngày thêm", "Ngày sửa" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
            c.Style.Font.FontColor = XLColor.White;
        }
        int row = 2;
        foreach (var e in list)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = e.EMPCD;
            ws.Cell(row, 3).Value = e.EXEMPT_TYPE;
            ws.Cell(row, 4).Value = e.NOTE ?? "";
            ws.Cell(row, 5).Value = e.EFFECTIVE_DATE.ToString("yyyy-MM-dd");
            ws.Cell(row, 6).Value = e.IS_ACTIVE == 1 ? "Y" : "N";
            ws.Cell(row, 7).Value = e.FULL_NAME ?? "";
            ws.Cell(row, 8).Value = e.DEPTCD ?? "";
            ws.Cell(row, 9).Value = e.LINECD ?? "";
            ws.Cell(row, 10).Value = e.WORKCD ?? "";
            ws.Cell(row, 11).Value = e.INST_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            ws.Cell(row, 12).Value = e.UPDT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            row++;
        }
        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"SurveyExempt_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // POST /SurveyExempt/Import  (file upload)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });

        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ILLITERATE", "NO_PHONE", "BOTH" };

        var items = new List<object>();
        int skipped = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int r = 2; r <= lastRow; r++)
            {
                var empcd = ws.Cell(r, 1).GetString()?.Trim() ?? "";
                var type  = ws.Cell(r, 2).GetString()?.Trim().ToUpperInvariant() ?? "";
                var note  = ws.Cell(r, 3).GetString()?.Trim();
                var dateS = ws.Cell(r, 4).GetString()?.Trim();

                if (string.IsNullOrEmpty(empcd) || string.IsNullOrEmpty(type)) { skipped++; continue; }
                if (!validTypes.Contains(type)) { skipped++; continue; }

                DateTime? effDate = null;
                if (!string.IsNullOrEmpty(dateS) && DateTime.TryParse(dateS, out var d))
                    effDate = d;

                items.Add(new
                {
                    EMPCD          = empcd,
                    EXEMPT_TYPE    = type,
                    NOTE           = note,
                    EFFECTIVE_DATE = effDate,
                    IS_ACTIVE      = 1,
                });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi đọc file: " + ex.Message });
        }

        if (items.Count == 0)
            return Json(new { success = false, message = "Không có dòng hợp lệ trong file" });

        var (ok, count, msg) = await _exempt.ImportAsync(items, CurrentUser?.EmpCd);
        return Json(new
        {
            success = ok,
            message = ok ? $"Đã import {count} dòng ({skipped} dòng bị bỏ qua)" : msg,
        });
    }

    public class SaveVm
    {
        public string    EmpCd         { get; set; } = "";
        public string    ExemptType    { get; set; } = "";
        public string?   Note          { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public int       IsActive      { get; set; } = 1;
    }
    public class DeleteVm
    {
        public string EmpCd      { get; set; } = "";
        public string ExemptType { get; set; } = "";
    }
}
