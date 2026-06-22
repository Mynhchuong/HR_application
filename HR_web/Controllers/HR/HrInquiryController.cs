using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.HR;

/// <summary>
/// Dành cho HR: xem + reply tất cả inquiry.
/// Chỉ đóng được conversation mà mình đang phụ trách (ASSIGNED_TO = empCd).
/// Không có quyền Unlock.
/// </summary>
public class HrInquiryController : HR_web.Controllers.Inquiry.InquiryBaseController
{
    public HrInquiryController(InquiryService inquiry) : base(inquiry) { }

    private bool IsHr => CurrentUser?.RoleName == "HR";

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Danh sách tất cả inquiry
    // GET /HrInquiry/Index
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Index()
    {
        if (!IsHr) return Forbid();
        return View();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Màn hình chat
    // GET /HrInquiry/Chat?id=
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Chat(long id)
    {
        if (!IsHr) return Forbid();
        if (id <= 0) return RedirectToAction("Index");

        var result = await _inquiry.GetMessagesAsync(id);
        if (!result.success || result.inquiry == null)
        {
            TempData["ErrorMessage"] = result.message ?? "Không tìm thấy hội thoại";
            return RedirectToAction("Index");
        }

        ViewBag.CurrentEmpCd = CurrentUser!.EmpCd;
        ViewBag.CurrentName  = CurrentUser.FullName;

        // Mark read phía HR (fire-and-forget)
        _ = _inquiry.MarkReadAsync(id, "HR");

        return View(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Danh sách inquiry (có filter + phân trang)
    // GET /HrInquiry/GetList
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
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetHrListAsync(status, topicCd, chatType, assignedTo, search, page, pageSize);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Load tin nhắn (polling-safe)
    // GET /HrInquiry/GetMessages?id=&afterMsgId=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMessages(long id, long afterMsgId = 0)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (id <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });
        var result = await _inquiry.GetMessagesAsync(id, afterMsgId);
        if (result.inquiry != null) result.inquiry.AnonToken = null;
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Gửi tin nhắn (senderType = "HR")
    // POST /HrInquiry/Send
    //   Nếu ASSIGNED_TO != null và != mình → API từ chối (lock bởi HR khác)
    //   Nếu ASSIGNED_TO == null → API tự lock về mình khi gửi lần đầu
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromBody] HrSendRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
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
            senderType: "HR",
            senderName: CurrentUser.FullName,
            content:    req.Content,
            files:      finalFiles.Count > 0 ? finalFiles : null);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đánh dấu đã đọc (HR side)
    // POST /HrInquiry/MarkRead
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] IdRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.MarkReadAsync(req.Id, "HR");
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Thu hồi tin nhắn HR đã gửi
    // POST /HrInquiry/Recall
    //   API xác minh SENDER_CD = mình và bên kia chưa đọc
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Recall([FromBody] IdRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.RecallAsync(req.Id, "HR", CurrentUser!.EmpCd, null);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đóng conversation
    // POST /HrInquiry/Close
    //   Rule §4: HR chỉ đóng được nếu ASSIGNED_TO = empCd của mình
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close([FromBody] HrCloseRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        // Kiểm tra ownership trước khi gọi API (API không validate phía HR)
        var conv = await _inquiry.GetMessagesAsync(req.InquiryId);
        if (!conv.success || conv.inquiry == null)
            return Json(new { success = false, message = "Không tìm thấy hội thoại" });

        if (conv.inquiry.AssignedTo != CurrentUser!.EmpCd)
            return Json(new { success = false, message = "Bạn chỉ có thể đóng conversation mà mình đang phụ trách" });

        var result = await _inquiry.CloseAsync(
            inquiryId:  req.InquiryId,
            empCd:      CurrentUser.EmpCd,
            anonToken:  null,
            closerType: "HR",
            closerName: CurrentUser.FullName,
            closeNote:  req.CloseNote);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner request models (HR-specific)
    // ─────────────────────────────────────────────────────────────────────────

    public class HrSendRequest
    {
        public long                   InquiryId  { get; set; }
        public string?                InquiryNo  { get; set; }
        public string?                Content    { get; set; }
        public List<InquiryFileInfo>? Files      { get; set; }
    }

    public class HrCloseRequest
    {
        public long    InquiryId  { get; set; }
        public string? CloseNote  { get; set; }
    }
}
