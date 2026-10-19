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
    public async Task<IActionResult> Chat()
    {
        // Chặn truy cập thẳng URL khi HR/CSR đã tắt tính năng (yêu cầu 2026-10-19) — không chỉ ẩn
        // lối vào ở EmployeeInquiry, phòng NV đã bookmark/lưu link cũ.
        if (!await IsEnabledAsync())
        {
            TempData["InfoMessage"] = "Tính năng Hỏi AI hiện đang tạm ngưng, vui lòng gửi câu hỏi qua Hộp thư phản ánh.";
            return RedirectToAction("Index", "EmployeeInquiry");
        }
        ViewBag.EmpCd   = Empcd;
        ViewBag.EmpName = EmpName;
        return View();
    }

    // GET /AiCsr/IsEnabled — dùng cho EmployeeInquiry Index/Create quyết định có hiện lối vào AI
    // hay không, không cần tải cả trang Chat mới biết.
    [HttpGet]
    public async Task<IActionResult> IsEnabled()
        => Content(await _svc.GetConfigRawAsync(), "application/json");

    private async Task<bool> IsEnabledAsync()
    {
        try
        {
            var json = await _svc.GetConfigRawAsync();
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            // Chỉ khoá khi API trả về THẬT SỰ tắt (success=true, enabled=false) — API lỗi/mất kết
            // nối (success=false) không được coi là "tắt" (bug thật phát hiện lúc review: trước
            // đây `!success` cũng bị tính là tắt, khiến 1 lần API chập chờn là NV bị đá khỏi trang
            // dù chưa ai bấm tắt gì cả).
            if ((bool?)obj["success"] != true) return true;
            return (bool?)obj["enabled"] != false;
        }
        catch { return true; } // lỗi đọc config -> mặc định không khoá nhầm
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
