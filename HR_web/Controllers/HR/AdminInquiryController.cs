using System.Globalization;
using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.HR;

/// <summary>
/// Dành cho Admin: toàn quyền trên tất cả inquiry.
///   - Send: senderType = "ADMIN" (bypass lock)
///   - Close: đóng bất kỳ conversation nào
///   - Unlock: mở khóa conversation bị HR giữ
/// </summary>
public class AdminInquiryController : HR_web.Controllers.Inquiry.InquiryBaseController
{
    public AdminInquiryController(InquiryService inquiry) : base(inquiry) { }

    private bool IsAdmin => CurrentUser?.RoleName == "Admin";

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Danh sách tất cả inquiry
    // GET /AdminInquiry/Index
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Index()
    {
        if (!IsAdmin) return Forbid();
        return View();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Màn hình chat (có nút Unlock)
    // GET /AdminInquiry/Chat?id=
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Chat(long id)
    {
        if (!IsAdmin) return Forbid();
        if (id <= 0) return RedirectToAction("Index");

        var result = await _inquiry.GetMessagesAsync(id);
        if (!result.success || result.inquiry == null)
        {
            TempData["ErrorMessage"] = result.message ?? "Không tìm thấy hội thoại";
            return RedirectToAction("Index");
        }

        ViewBag.CurrentEmpCd = CurrentUser!.EmpCd;
        ViewBag.CurrentName  = CurrentUser.FullName;

        // Mark read phía HR (Admin dùng chung HR bucket)
        _ = _inquiry.MarkReadAsync(id, "HR");

        return View(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Danh sách inquiry (có filter + phân trang)
    // GET /AdminInquiry/GetList
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetList(
        string? status     = null,
        string? topicCd    = null,
        string? chatType   = null,
        string? assignedTo = null,
        string? search     = null,
        int     page       = 1,
        int     pageSize   = 30)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetHrListAsync(status, topicCd, chatType, assignedTo, search, page, pageSize);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Load tin nhắn (polling-safe)
    // GET /AdminInquiry/GetMessages?id=&afterMsgId=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMessages(long id, long afterMsgId = 0)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (id <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });
        var result = await _inquiry.GetMessagesAsync(id, afterMsgId);
        if (result.inquiry != null) result.inquiry.AnonToken = null;
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Gửi tin nhắn (senderType = "ADMIN" — bypass lock, ghi DB là "HR")
    // POST /AdminInquiry/Send
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromBody] AdminSendRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        bool hasContent = !string.IsNullOrWhiteSpace(req.Content);
        bool hasFiles   = req.Files?.Count > 0;
        if (!hasContent && !hasFiles)
            return Json(new { success = false, message = "Tin nhắn không được để trống" });
        if (req.Content?.Length > 4000)
            return Json(new { success = false, message = "Nội dung vượt quá 4000 ký tự" });
        if (req.Files?.Count > 5)
            return Json(new { success = false, message = "Tối đa 5 file mỗi lần gửi" });

        List<InquiryFileInfo> finalFiles = new();
        if (hasFiles)
        {
            string no = await ResolveInquiryNoAsync(req.InquiryId, req.InquiryNo);
            finalFiles = MoveFilesToFinal(req.Files!, no, DateTime.Now.Year.ToString());
        }

        var result = await _inquiry.SendAsync(
            inquiryId:  req.InquiryId,
            empCd:      CurrentUser!.EmpCd,
            anonToken:  null,
            senderType: "ADMIN",          // bypass lock check ở API
            senderName: CurrentUser.FullName,
            content:    req.Content,
            files:      finalFiles.Count > 0 ? finalFiles : null);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đánh dấu đã đọc (HR bucket — Admin dùng chung với HR)
    // POST /AdminInquiry/MarkRead
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] IdRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.MarkReadAsync(req.Id, "HR");
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Thu hồi tin nhắn
    // POST /AdminInquiry/Recall
    //   Tin Admin gửi được lưu DB với SENDER_TYPE = "HR" nên dùng senderType = "HR"
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Recall([FromBody] IdRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.RecallAsync(req.Id, "HR", CurrentUser!.EmpCd, null);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đóng conversation — Admin đóng được bất kỳ (không cần ASSIGNED_TO)
    // POST /AdminInquiry/Close
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close([FromBody] AdminCloseRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        var result = await _inquiry.CloseAsync(
            inquiryId:  req.InquiryId,
            empCd:      CurrentUser!.EmpCd,
            anonToken:  null,
            closerType: "ADMIN",
            closerName: CurrentUser.FullName,
            closeNote:  req.CloseNote);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Mở khóa conversation — CHỈ ADMIN
    // POST /AdminInquiry/Unlock
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock([FromBody] IdRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Chỉ Admin mới có quyền mở khóa" });
        var result = await _inquiry.UnlockAsync(req.Id, CurrentUser!.EmpCd, CurrentUser.FullName);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Báo cáo thống kê theo tuần / tháng
    // GET /AdminInquiry/Report
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Report()
    {
        if (!IsAdmin) return Forbid();
        return View();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Lấy dữ liệu báo cáo
    // GET /AdminInquiry/GetReport?type=WEEK&year=2026&week=25
    // GET /AdminInquiry/GetReport?type=MONTH&year=2026&month=6
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetReport(
        string type  = "WEEK",
        int    year  = 0,
        int    week  = 0,
        int    month = 0)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });

        if (year <= 0)  year  = DateTime.Now.Year;
        if (type == "WEEK"  && week  <= 0) week  = ISOWeek.GetWeekOfYear(DateTime.Now);
        if (type == "MONTH" && (month < 1 || month > 12)) month = DateTime.Now.Month;

        var result = await _inquiry.GetReportAsync(type, year, week, month);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner request models (Admin-specific)
    // ─────────────────────────────────────────────────────────────────────────

    public class AdminSendRequest
    {
        public long                   InquiryId  { get; set; }
        public string?                InquiryNo  { get; set; }
        public string?                Content    { get; set; }
        public List<InquiryFileInfo>? Files      { get; set; }
    }

    public class AdminCloseRequest
    {
        public long    InquiryId  { get; set; }
        public string? CloseNote  { get; set; }
    }
}
