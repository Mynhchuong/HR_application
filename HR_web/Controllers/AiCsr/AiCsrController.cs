using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.AiCsr;

// "AI SAMHO - CSR" — phía NHÂN VIÊN. Trang chat riêng, tách khỏi Hộp thư (EmployeeInquiry).
// EMPCD luôn lấy từ session, KHÔNG tin client (giống EmployeeInquiryController).
[Authorize]
public class AiCsrController : HR_web.Controllers.BaseController
{
    private readonly AiCsrService _svc;
    public AiCsrController(AiCsrService svc) { _svc = svc; }

    private string Empcd   => CurrentUser?.EmpCd ?? "";
    private string EmpName => CurrentUser?.FullName ?? "";
    // Expat -> hỏi AI bằng tiếng Anh; còn lại tiếng Việt.
    private string Lang     => CurrentUser?.RoleName == "Expat" ? "en" : "vi";

    // GET /AiCsr/Chat
    public IActionResult Chat()
    {
        ViewBag.EmpCd   = Empcd;
        ViewBag.EmpName = EmpName;
        return View();
    }

    // GET /AiCsr/MyThread — đoạn chat OPEN hiện tại + tin nhắn
    [HttpGet]
    public async Task<IActionResult> MyThread()
    {
        if (string.IsNullOrEmpty(Empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        return Content(await _svc.MyThreadRawAsync(Empcd), "application/json");
    }

    // POST /AiCsr/Ask  { question }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] AskBody req)
    {
        if (string.IsNullOrEmpty(Empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        if (string.IsNullOrWhiteSpace(req?.Question))
            return Json(new { success = false, message = "Vui lòng nhập câu hỏi" });

        var payload = new { empcd = Empcd, empName = EmpName, question = req!.Question.Trim(), language = Lang };
        return Content(await _svc.AskRawAsync(payload), "application/json");
    }

    // GET /AiCsr/Messages?chatId=&afterMsgId=
    [HttpGet]
    public async Task<IActionResult> Messages(long chatId, long afterMsgId = 0)
    {
        if (chatId <= 0) return Json(new { success = false, message = "Thiếu chatId" });
        return Content(await _svc.MessagesRawAsync(chatId, Empcd, afterMsgId), "application/json");
    }

    // POST /AiCsr/MarkRead  { chatId }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] ChatIdBody req)
    {
        if (req == null || req.ChatId <= 0) return Json(new { success = false, message = "Thiếu chatId" });
        return Content(await _svc.MarkReadEmpRawAsync(new { chatId = req.ChatId, empcd = Empcd }), "application/json");
    }

    // POST /AiCsr/NewThread
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> NewThread()
    {
        if (string.IsNullOrEmpty(Empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        return Content(await _svc.NewThreadRawAsync(new { empcd = Empcd }), "application/json");
    }

    public class AskBody    { public string? Question { get; set; } }
    public class ChatIdBody { public long ChatId { get; set; } }
}
