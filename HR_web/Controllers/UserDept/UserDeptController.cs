using ClosedXML.Excel;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

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
    public async Task<IActionResult> GetList(string? empcd, string? deptcd, string? linecd, string? workcd, int page = 1, int page_size = 50)
    {
        var result = await _service.GetListAsync(empcd, deptcd, linecd, workcd, page, page_size);
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

            if (CurrentUser == null)
                return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

            var (success, message, inserted, skipped) = await _service.ImportAsync(rows, CurrentUser.EmpCd);
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

    [HttpGet]
    public async Task<IActionResult> GetFilterScope()
    {
        // Admin xem toàn công ty → không truyền empcd (API sẽ query EAM410)
        // Role khác → truyền empcd để lấy scope từ HR_USERS_DEPT
        var empcd = CurrentUser?.RoleName == "Admin" ? null : CurrentUser?.EmpCd;
        var result = await _service.GetFilterScopeAsync(empcd ?? "");
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetDeptOptions()
        => Json(await _service.GetDeptOptionsAsync());

    [HttpGet]
    public async Task<IActionResult> GetLineOptions(string? deptcds)
        => Json(await _service.GetLineOptionsAsync(deptcds ?? ""));

    [HttpGet]
    public async Task<IActionResult> GetWorkOptions(string? deptcds, string? linecds)
        => Json(await _service.GetWorkOptionsAsync(deptcds ?? "", linecds ?? ""));

    [HttpPost]
    public async Task<IActionResult> ManualCreate(
        [FromForm] string empcd,
        [FromForm] string deptcds,
        [FromForm] string linecds,
        [FromForm] string workcds)
    {
        if (CurrentUser == null)
            return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

        var dList = deptcds?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList() ?? [];
        var lList = linecds?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList() ?? [];
        var wList = workcds?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList() ?? [];
        var (success, message, inserted, skipped) = await _service.ManualCreateAsync(empcd, CurrentUser.EmpCd, dList, lList, wList);
        return Json(new { success, message, inserted, skipped });
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string? empcd, string? deptcd, string? linecd, string? workcd)
    {
        var raw  = await _service.GetListAsync(empcd, deptcd, linecd, workcd, 1, 9999);
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(raw);
        var root = JObject.Parse(json);
        var data = root["data"] as JArray ?? new JArray();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Phân Quyền Phạm Vi");

        string[] headers = { "STT", "Mã NV", "Họ & Tên", "Role", "Bộ Phận", "Tên BP", "Line", "Tên Line", "Nhóm Việc", "Tên Nhóm Việc", "Ngày Tạo", "Tạo Bởi", "Ngày Cập Nhật", "Cập Nhật Bởi" };
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
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value  = row - 1;
            ws.Cell(row, 2).Value  = item["EMPCD"]?.ToString()       ?? "";
            ws.Cell(row, 3).Value  = item["EMP_NAME"]?.ToString()    ?? "";
            ws.Cell(row, 4).Value  = item["ROLE_NAME"]?.ToString()   ?? "";
            ws.Cell(row, 5).Value  = item["DEPTCD"]?.ToString()      ?? "";
            ws.Cell(row, 6).Value  = item["DEPT_NAME"]?.ToString()   ?? "";
            ws.Cell(row, 7).Value  = item["LINECD"]?.ToString()      ?? "";
            ws.Cell(row, 8).Value  = item["LINE_NAME"]?.ToString()   ?? "";
            ws.Cell(row, 9).Value  = item["WORKCD"]?.ToString()      ?? "";
            ws.Cell(row, 10).Value = item["WORK_NAME"]?.ToString()   ?? "";
            ws.Cell(row, 11).Value = item["CREATEDATE"]?.ToString()  ?? "";
            ws.Cell(row, 12).Value = item["CREATEBY"]?.ToString()    ?? "";
            ws.Cell(row, 13).Value = item["UPDATEDATE"]?.ToString()  ?? "";
            ws.Cell(row, 14).Value = item["UPDATEBY"]?.ToString()    ?? "";
            row++;
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PhanQuyenPhamVi_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(string empcd, string? deptcd, string? linecd, string? workcd)
    {
        var ok = await _service.DeleteAsync(empcd, deptcd, linecd, workcd);
        return Json(new { success = ok });
    }
}
