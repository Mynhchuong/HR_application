using ClosedXML.Excel;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace HR_web.Controllers.Kaizen;

/// <summary>Quản lý đăng ký Kaizen — do bộ phận Lean (role dept Lean) và Admin xem log toàn công ty.</summary>
[Authorize(Roles = "Admin,Lean")]
public class KaizenAdminController : BaseController
{
    private readonly KaizenService _svc;
    public KaizenAdminController(KaizenService svc) { _svc = svc; }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> GetList(string? month, string? deptcd, string? search, int page = 1, int page_size = 50)
        => Content(await _svc.AdminListRawAsync(month, deptcd, search, page, page_size), "application/json");

    // ── Email nhận báo cáo Excel tự động mỗi thứ Sáu 08:00 ──
    [HttpGet]
    public async Task<IActionResult> MailList() => Content(await _svc.MailListRawAsync(), "application/json");

    public class MailBody { public string? Email { get; set; } public int Id { get; set; } }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MailAdd([FromBody] MailBody body)
        => Content(await _svc.MailAddRawAsync(body?.Email ?? "", CurrentUser?.EmpCd), "application/json");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MailRemove([FromBody] MailBody body)
        => Content(await _svc.MailRemoveRawAsync(body?.Id ?? 0), "application/json");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MailTest([FromBody] MailBody body)
        => Content(await _svc.MailTestRawAsync(body?.Email ?? ""), "application/json");

    // GET /KaizenAdmin/Export — xuất Excel đúng theo bộ lọc đang chọn
    [HttpGet]
    public async Task<IActionResult> Export(string? month, string? deptcd, string? search)
    {
        var rows = new List<JToken>();
        for (int page = 1; page <= 50; page++)   // trần 50 x 1000 = 50.000 dòng
        {
            var j = JObject.Parse(await _svc.AdminListRawAsync(month, deptcd, search, page, 1000));
            if (j["success"]?.Value<bool>() != true) { TempData["ErrorMessage"] = j["message"]?.ToString() ?? "Không xuất được"; return RedirectToAction("Index"); }
            var data = j["data"] as JArray;
            if (data == null || data.Count == 0) break;
            rows.AddRange(data);
            if (page >= (j["total_pages"]?.Value<int>() ?? 0)) break;
        }

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Kaizen");
        string[] heads = { "Ngày nộp", "Tháng", "Mã NV", "Họ tên", "Dept", "Line", "Work", "SĐT / Zalo", "Ý tưởng" };
        for (int c = 0; c < heads.Length; c++) { ws.Cell(1, c + 1).Value = heads[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
        int r = 2;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x["submitDt"]?.Value<DateTime>().ToString("dd/MM/yyyy HH:mm") ?? "";
            ws.Cell(r, 2).Value = x["month"]?.ToString() ?? "";
            ws.Cell(r, 3).Value = x["empcd"]?.ToString() ?? "";
            ws.Cell(r, 4).Value = x["empName"]?.ToString() ?? "";
            ws.Cell(r, 5).Value = x["deptName"]?.ToString() ?? x["deptcd"]?.ToString() ?? "";
            ws.Cell(r, 6).Value = x["lineName"]?.ToString() ?? x["linecd"]?.ToString() ?? "";
            ws.Cell(r, 7).Value = x["workName"]?.ToString() ?? x["workcd"]?.ToString() ?? "";
            ws.Cell(r, 8).Value = "'" + (x["phone"]?.ToString() ?? "");   // giữ số 0 đầu
            ws.Cell(r, 9).Value = x["idea"]?.ToString() ?? "";
            r++;
        }
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
        ws.Column(9).Width = 70;
        ws.Column(9).Style.Alignment.WrapText = true;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Kaizen_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }
}
