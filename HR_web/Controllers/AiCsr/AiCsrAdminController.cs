using ClosedXML.Excel;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace HR_web.Controllers.AiCsr;

// "AI SAMHO - CSR" — phía quản lý. CHỈ CSR + Admin (yêu cầu 2026-09-10).
[Authorize(Roles = "CSR,Admin")]
public class AiCsrAdminController : HR_web.Controllers.BaseController
{
    private readonly AiCsrService _svc;
    public AiCsrAdminController(AiCsrService svc) { _svc = svc; }

    // GET /AiCsrAdmin/Index
    public IActionResult Index() => View();

    // GET /AiCsrAdmin/GetList
    [HttpGet]
    public async Task<IActionResult> GetList(string? status = null, string? search = null, int page = 1, int pageSize = 30)
        => Content(await _svc.ListRawAsync(status, search, page, pageSize), "application/json");

    // GET /AiCsrAdmin/Thread?id=
    public async Task<IActionResult> Thread(long id)
    {
        if (id <= 0) return RedirectToAction("Index");
        ViewBag.ChatId    = id;
        ViewBag.CsrEmpCd  = CurrentUser?.EmpCd;
        ViewBag.CsrName   = CurrentUser?.FullName;
        // đánh dấu CSR đã xem (fire-and-forget)
        _ = _svc.CsrMarkReadRawAsync(new { chatId = id });
        return View();
    }

    // GET /AiCsrAdmin/GetThread?id=
    [HttpGet]
    public async Task<IActionResult> GetThread(long id)
    {
        if (id <= 0) return Json(new { success = false, message = "Thiếu id" });
        return Content(await _svc.ThreadRawAsync(id), "application/json");
    }

    // GET /AiCsrAdmin/GetMessages?id=&afterMsgId=
    [HttpGet]
    public async Task<IActionResult> GetMessages(long id, long afterMsgId = 0)
    {
        if (id <= 0) return Json(new { success = false, message = "Thiếu id" });
        return Content(await _svc.ThreadMessagesRawAsync(id, afterMsgId), "application/json");
    }

    // POST /AiCsrAdmin/Reply  { chatId, content }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply([FromBody] ReplyBody req)
    {
        if (req == null || req.ChatId <= 0) return Json(new { success = false, message = "Thiếu chatId" });
        if (string.IsNullOrWhiteSpace(req.Content)) return Json(new { success = false, message = "Nội dung trống" });
        var payload = new
        {
            chatId   = req.ChatId,
            csrEmpcd = CurrentUser?.EmpCd,
            csrName  = CurrentUser?.FullName,
            content  = req.Content.Trim()
        };
        return Content(await _svc.CsrReplyRawAsync(payload), "application/json");
    }

    // POST /AiCsrAdmin/MarkRead  { chatId }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] ChatIdBody req)
    {
        if (req == null || req.ChatId <= 0) return Json(new { success = false, message = "Thiếu chatId" });
        return Content(await _svc.CsrMarkReadRawAsync(new { chatId = req.ChatId }), "application/json");
    }

    // POST /AiCsrAdmin/SetStatus  { chatId, status }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus([FromBody] SetStatusBody req)
    {
        if (req == null || req.ChatId <= 0) return Json(new { success = false, message = "Thiếu chatId" });
        var payload = new { chatId = req.ChatId, actorEmpcd = CurrentUser?.EmpCd, status = req.Status };
        return Content(await _svc.SetStatusRawAsync(payload), "application/json");
    }

    // GET /AiCsrAdmin/ExportExcel?status=&search=
    [HttpGet]
    public async Task<IActionResult> ExportExcel(string? status = null, string? search = null)
    {
        var json = await _svc.ListRawAsync(status, search, 1, 10000);
        var obj  = JObject.Parse(json);
        var rows = (obj["data"] as JArray) ?? new JArray();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Hội thoại AI");
        string[] headers = { "STT", "Mã NV", "Họ và tên", "Phòng ban", "Line", "Work",
                              "Câu hỏi", "Trạng thái", "Số tin nhắn", "Chưa đọc (CSR)",
                              "Ngày tạo", "Tin nhắn cuối" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var it in rows)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = (string?)it["empcd"] ?? "";
            var nameCell = ws.Cell(row, 3);
            nameCell.Value = (string?)it["empName"] ?? "";
            nameCell.Style.Font.FontName = "Vnitbi__";
            var chips = ws.Range(row, 4, row, 6);
            ws.Cell(row, 4).Value = (string?)it["deptName"] ?? "";
            ws.Cell(row, 5).Value = (string?)it["lineName"] ?? "";
            ws.Cell(row, 6).Value = (string?)it["workName"] ?? "";
            chips.Style.Font.FontName = "Vnitbi__";
            ws.Cell(row, 7).Value = (string?)it["title"] ?? "";
            ws.Cell(row, 8).Value = (string?)it["status"] == "OPEN" ? "Đang mở" : "Đã đóng";
            ws.Cell(row, 9).Value = (int?)it["msgCount"] ?? 0;
            ws.Cell(row, 10).Value = (int?)it["unreadCsr"] ?? 0;

            var instDt = (DateTime?)it["instDt"];
            if (instDt.HasValue) { ws.Cell(row, 11).Value = instDt.Value; ws.Cell(row, 11).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; }
            else ws.Cell(row, 11).Value = "—";

            var lastMsgDt = (DateTime?)it["lastMsgDt"];
            if (lastMsgDt.HasValue) { ws.Cell(row, 12).Value = lastMsgDt.Value; ws.Cell(row, 12).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; }
            else ws.Cell(row, 12).Value = "—";

            row++;
        }
        if (rows.Count > 0)
            ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        ws.Column(7).Width = 50;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        string fileName = $"AiCsrChat_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public class ReplyBody     { public long ChatId { get; set; } public string? Content { get; set; } }
    public class ChatIdBody    { public long ChatId { get; set; } }
    public class SetStatusBody { public long ChatId { get; set; } public string Status { get; set; } = "CLOSED"; }
}
