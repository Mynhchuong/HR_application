using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.HR;

/// <summary>
/// Dành cho HR: xem + reply tất cả inquiry.
/// Chỉ đóng được conversation mà mình đang phụ trách (ASSIGNED_TO = empCd).
/// Không có quyền Unlock.
/// </summary>
[Authorize(Roles = "HR,CSR")]
public class HrInquiryController : HR_web.Controllers.Inquiry.InquiryBaseController
{
    public HrInquiryController(InquiryService inquiry) : base(inquiry) { }

    private bool IsHr => CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "CSR";

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

        // Mark read phía HR — ghi mốc đã đọc RIÊNG cho tài khoản này (viewerEmpcd), khỏi ảnh hưởng
        // badge chưa đọc của các CSR/HR/Admin khác. AWAIT để chắc chắn ghi HR_INQUIRY_READER trước
        // khi user quay lại danh sách.
        await _inquiry.MarkReadAsync(id, "HR", viewerEmpcd: CurrentUser!.EmpCd);

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
        string? sort       = null,
        int     page       = 1,
        int     pageSize   = 30)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetHrListAsync(status, topicCd, chatType, assignedTo, search, sort, CurrentUser?.EmpCd, page, pageSize);
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
        bool hasRefs    = req.Refs?.Count > 0;
        if (!hasContent && !hasFiles && !hasRefs)
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
            senderType:   "HR",
            senderName:   CurrentUser.RoleName,
            assignedName: CurrentUser.FullName,
            content:      req.Content,
            files:      finalFiles.Count > 0 ? finalFiles : null,
            refs:       req.Refs?.Count > 0 ? req.Refs : null);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Tìm Policy/Guide để chèn trích dẫn (HR only)
    // GET /HrInquiry/SearchRefs?type=POLICY|GUIDE&q=...
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> SearchRefs(string type = "POLICY", string? q = null)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var json = await _inquiry.SearchRefsRawAsync(type, q);
        return Content(json, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Câu trả lời mẫu — chèn khi trả lời (quản lý thêm/sửa/xoá xem CannedReplies() bên dưới,
    // HR/CSR cũng có quyền — yêu cầu 2026-09-14)
    // GET /HrInquiry/GetCannedRepliesForPicker?q=...
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetCannedRepliesForPicker(string? q = null)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var json = await _inquiry.CannedRepliesRawAsync(q);
        return Content(json, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Quản lý câu trả lời mẫu (HR/CSR cũng được thêm/sửa/xoá — yêu cầu 2026-09-14,
    // trước đó chỉ Admin). Dùng chung view với AdminInquiry/CannedReplies.cshtml (view không
    // chỉ định controller trong Url.Action nên tự trỏ đúng controller đang xử lý request).
    // GET /HrInquiry/CannedReplies
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult CannedReplies()
    {
        if (!IsHr) return Forbid();
        return View("~/Views/AdminInquiry/CannedReplies.cshtml");
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminCannedReplies()
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var raw = await _inquiry.CannedRepliesAdminRawAsync(CurrentUser?.EmpCd);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveCannedReply([FromBody] HrCannedReplySaveRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Content))
            return Json(new { success = false, message = "Thiếu tiêu đề hoặc nội dung" });

        var payload = new
        {
            id           = req.Id,
            title        = req.Title,
            content      = req.Content,
            displayOrder = req.DisplayOrder,
            isActive     = req.IsActive,
            actorEmpCd   = CurrentUser?.EmpCd
        };
        var raw = await _inquiry.CannedReplySaveRawAsync(payload);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCannedReply([FromBody] HrCannedReplyDeleteRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu ID" });

        var payload = new { id = req.Id, actorEmpCd = CurrentUser?.EmpCd };
        var raw = await _inquiry.CannedReplyDeleteRawAsync(payload);
        return Content(raw, "application/json");
    }

    public class HrCannedReplySaveRequest
    {
        public long?  Id           { get; set; }
        public string Title        { get; set; } = "";
        public string Content      { get; set; } = "";
        public int    DisplayOrder { get; set; }
        public bool   IsActive     { get; set; } = true;
    }

    public class HrCannedReplyDeleteRequest
    {
        public long Id { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đánh dấu đã đọc (HR side)
    // POST /HrInquiry/MarkRead
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] IdRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.MarkReadAsync(req.Id, "HR", viewerEmpcd: CurrentUser?.EmpCd);
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
        public List<InquiryRefInfo>?  Refs       { get; set; }
    }

    public class HrCloseRequest
    {
        public long    InquiryId  { get; set; }
        public string? CloseNote  { get; set; }
    }
}
