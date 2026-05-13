using ClosedXML.Excel;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.UserDept;

[Authorize]
public class UserDeptController : BaseController
{
    private readonly UserDeptService _service;

    public UserDeptController(UserDeptService service)
    {
        _service = service;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> GetList(string? empcd, string? deptcd, int page = 1, int page_size = 50)
    {
        var result = await _service.GetListAsync(empcd, deptcd, page, page_size);
        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> Import(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "Chưa chọn file" });

            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Chỉ hỗ trợ file .xlsx" });

            var rows = new List<Dictionary<string, string?>>();

            using var stream = file.OpenReadStream();
            using var wb     = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();

            // Dòng 1 là header, đọc từ dòng 2
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var empcd  = ws.Cell(r, 1).GetString().Trim();
                var deptcd = ws.Cell(r, 2).GetString().Trim();
                var linecd = ws.Cell(r, 3).GetString().Trim();
                var workcd = ws.Cell(r, 4).GetString().Trim();

                if (string.IsNullOrEmpty(empcd) && string.IsNullOrEmpty(deptcd)) continue;

                rows.Add(new Dictionary<string, string?>
                {
                    ["EMPCD"]  = string.IsNullOrEmpty(empcd)  ? null : empcd,
                    ["DEPTCD"] = string.IsNullOrEmpty(deptcd) ? null : deptcd,
                    ["LINECD"] = string.IsNullOrEmpty(linecd) ? null : linecd,
                    ["WORKCD"] = string.IsNullOrEmpty(workcd) ? null : workcd
                });
            }

            if (rows.Count == 0)
                return Json(new { success = false, message = "File không có dữ liệu" });

            var (success, message, inserted, skipped) = await _service.ImportAsync(rows, CurrentUser?.EmpCd);
            return Json(new { success, message, inserted, skipped });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("HR_USERS_DEPT");

        // Header
        string[] headers = { "EMPCD", "DEPTCD", "LINECD", "WORKCD" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4CAF50");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Sample row
        ws.Cell(2, 1).Value = "12345678";
        ws.Cell(2, 2).Value = "A01001";
        ws.Cell(2, 3).Value = "0907";
        ws.Cell(2, 4).Value = "1001";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "template_HR_USERS_DEPT.xlsx");
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(string empcd, string? deptcd, string? linecd, string? workcd)
    {
        var ok = await _service.DeleteAsync(empcd, deptcd, linecd, workcd);
        return Json(new { success = ok });
    }
}
