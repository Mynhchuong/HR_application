using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public class ReplyBody     { public long ChatId { get; set; } public string? Content { get; set; } }
    public class ChatIdBody    { public long ChatId { get; set; } }
    public class SetStatusBody { public long ChatId { get; set; } public string Status { get; set; } = "CLOSED"; }
}
