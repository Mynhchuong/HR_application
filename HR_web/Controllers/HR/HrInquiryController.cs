using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace HR_web.Controllers.HR;

/// <summary>
/// Dành cho HR: xem + reply tất cả inquiry.
/// Chỉ đóng được conversation mà mình đang phụ trách (ASSIGNED_TO = empCd).
/// Không có quyền Unlock.
/// </summary>
[Authorize(Roles = "HR,CSR")]
public class HrInquiryController : HR_web.Controllers.Inquiry.InquiryBaseController
{
    private readonly AiCsrService _aiCsr;
    public HrInquiryController(InquiryService inquiry, AiCsrService aiCsr) : base(inquiry) { _aiCsr = aiCsr; }

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
    // PAGE: Màn hình chat — dùng CHUNG cho cả hội thoại thường (HR_INQUIRY) và AI SAMHO - CSR
    // (HR_AI_CHAT, source=AI) — yêu cầu 2026-10-01: "cùng 1 cơ chế mà chia 2 page cực quá".
    // AI chat được map tạm sang đúng shape InquiryMessagesResponse (xem BuildAiChatResponseAsync)
    // để tái dùng NGUYÊN view/JS (inquiry-chat.js) — không đụng data gốc HR_AI_CHAT.
    // GET /HrInquiry/Chat?id=&source=AI
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Chat(long id, string? source = null)
    {
        if (!IsHr) return Forbid();
        if (id <= 0) return RedirectToAction("Index");

        bool isAi = source == "AI";
        var result = isAi
            ? await BuildAiChatResponseAsync(id)
            : await _inquiry.GetMessagesAsync(id);

        if (!result.success || result.inquiry == null)
        {
            TempData["ErrorMessage"] = result.message ?? "Không tìm thấy hội thoại";
            return RedirectToAction("Index");
        }

        ViewBag.CurrentEmpCd = CurrentUser!.EmpCd;
        ViewBag.CurrentName  = CurrentUser.FullName;
        ViewBag.IsAiSource   = isAi;

        if (isAi)
        {
            // Không có per-viewer read tracking riêng cho AI chat (khác HR_INQUIRY_READER) — dùng
            // thẳng cờ chung LAST_CSR_READ_DT như UI cũ, fire-and-forget để không chặn trang.
            _ = _aiCsr.CsrMarkReadRawAsync(new { chatId = id });
        }
        else
        {
            // Mark read phía HR — ghi mốc đã đọc RIÊNG cho tài khoản này (viewerEmpcd), khỏi ảnh
            // hưởng badge chưa đọc của các CSR/HR/Admin khác. AWAIT để chắc chắn ghi
            // HR_INQUIRY_READER trước khi user quay lại danh sách.
            await _inquiry.MarkReadAsync(id, "HR", viewerEmpcd: CurrentUser!.EmpCd);
        }

        return View(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Map response thô từ apiHR/AiCsr/thread sang đúng shape InquiryMessagesResponse (dùng chung
    // DTO InquiryListItemDto/InquiryMsgDto với HR_INQUIRY) để Chat.cshtml/inquiry-chat.js tái dùng
    // được nguyên vẹn mà không cần biết nguồn dữ liệu thật. Mapping sender:
    //   EMP -> EMP (nhân viên) | CSR -> HR (CSR/HR thật trả lời) | AI -> SYS (bong bóng trung lập,
    //   không lẫn với 2 bên người thật — SenderName vẫn giữ "AI SAMHO - CSR" để phân biệt rõ).
    // AssignedTo luôn gán = người đang xem để mở khoá nút "Đóng hội thoại" — AI chat vốn không khoá
    // phụ trách thật (ai cũng trả lời được), gán tạm chỉ để tái dùng đúng UI điều kiện canClose.
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<InquiryMessagesResponse> BuildAiChatResponseAsync(long id)
    {
        JObject obj;
        try { obj = JObject.Parse(await _aiCsr.ThreadRawAsync(id)); }
        catch { return new InquiryMessagesResponse { success = false, message = "Lỗi kết nối server" }; }

        if (obj["success"]?.Value<bool>() != true)
            return new InquiryMessagesResponse { success = false, message = obj["message"]?.Value<string>() ?? "Không tìm thấy hội thoại" };

        var head = obj["head"];
        var msgs = (obj["messages"] as JArray) ?? new JArray();

        var inquiry = new InquiryListItemDto
        {
            Id           = id,
            InquiryNo    = $"AI-{id}",
            ChatType     = "AI",
            TopicCd      = "",
            TopicName    = "AI SAMHO - CSR",
            TopicColor   = "#7c3aed",
            Subject      = head?["Title"]?.Value<string>(),
            EmpCd        = head?["Empcd"]?.Value<string>(),
            EmpName      = head?["EmpName"]?.Value<string>(),
            DeptName     = head?["DeptName"]?.Value<string>(),
            LineName     = head?["LineName"]?.Value<string>(),
            WorkName     = head?["WorkName"]?.Value<string>(),
            Status       = head?["Status"]?.Value<string>() ?? "OPEN",
            AssignedTo   = CurrentUser?.EmpCd,
            AssignedName = CurrentUser?.FullName,
            UnreadHr     = 0,
            UnreadEmp    = head?["UnreadEmp"]?.Value<int>() ?? 0,
            MsgCount     = msgs.Count
        };

        return new InquiryMessagesResponse { success = true, inquiry = inquiry, messages = MapAiMessages(msgs, id) };
    }

    private static List<InquiryMsgDto> MapAiMessages(JArray msgs, long inquiryId)
    {
        return msgs.Select(m =>
        {
            string senderType = m["senderType"]?.Value<string>() ?? "EMP";
            string mapped = senderType switch { "CSR" => "HR", "AI" => "SYS", _ => "EMP" };
            return new InquiryMsgDto
            {
                Id         = m["id"]?.Value<long>() ?? 0,
                InquiryId  = inquiryId,
                SenderType = mapped,
                SenderCd   = m["senderCd"]?.Value<string>(),
                SenderName = senderType == "AI" ? "🤖 AI SAMHO - CSR" : m["senderName"]?.Value<string>(),
                MsgType    = "TEXT",
                Content    = m["content"]?.Value<string>(),
                IsReadHr   = true,
                IsReadEmp  = true,
                IsDeleted  = false,
                SentDt     = m["sentDt"]?.Value<DateTime?>() ?? DateTime.Now
            };
        }).ToList();
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
    // AJAX: Danh sách hội thoại "AI SAMHO - CSR" (HR_AI_CHAT — nguồn riêng, KHÔNG chung
    // HR_INQUIRY) — gộp hiển thị vào CÙNG trang Quản lý hội thoại qua filter "Loại hội thoại"
    // riêng, để HR/CSR check và trả lời đè khi AI trả lời sai (yêu cầu 2026-10-01). Giữ nguyên
    // bảng/API AI chat, chỉ thêm đường xem — xem chi tiết/trả lời dùng chung Chat?source=AI.
    // GET /HrInquiry/GetAiList?status=&search=&page=&pageSize=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAiList(string? status = null, string? search = null, int page = 1, int pageSize = 30)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _aiCsr.ListRawAsync(status, search, page, pageSize), "application/json");
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
    // AJAX: Poll tin nhắn mới cho AI chat — cùng contract JSON với GetMessages (inquiry-chat.js
    // refreshMessages() không phân biệt nguồn) nhưng tự lấy từ HR_AI_CHAT.
    // GET /HrInquiry/AiGetMessages?id=&afterMsgId=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> AiGetMessages(long id, long afterMsgId = 0)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (id <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        JObject obj;
        try { obj = JObject.Parse(await _aiCsr.ThreadMessagesRawAsync(id, afterMsgId)); }
        catch { return Json(new { success = false, message = "Lỗi kết nối server" }); }

        if (obj["success"]?.Value<bool>() != true)
            return Json(new { success = false, message = obj["message"]?.Value<string>() });

        var msgs = (obj["messages"] as JArray) ?? new JArray();
        return Json(new
        {
            success = true,
            inquiry = new { status = obj["status"]?.Value<string>() ?? "OPEN" },
            messages = MapAiMessages(msgs, id)
        });
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
            senderName:   CurrentUser.FullName,
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
        var json = await _inquiry.CannedRepliesRawAsync(q, CurrentUser?.RoleName);
        return Content(json, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Gửi tin nhắn trả lời AI chat — contract giống Send ({success,message}) nên
    // InquiryChat.sendMessage() dùng chung được, chỉ khác điểm đến (HR_AI_CHAT, không khoá phụ
    // trách, không đính kèm file/trích dẫn vì HR_AI_CHAT_MSG không có bảng lưu).
    // POST /HrInquiry/AiSend  { inquiryId, content }
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiSend([FromBody] HrSendRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });
        if (string.IsNullOrWhiteSpace(req.Content)) return Json(new { success = false, message = "Tin nhắn không được để trống" });

        var payload = new
        {
            chatId   = req.InquiryId,
            csrEmpcd = CurrentUser?.EmpCd,
            csrName  = CurrentUser?.FullName,
            content  = req.Content.Trim()
        };
        return Content(await _aiCsr.CsrReplyRawAsync(payload), "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đóng AI chat — contract giống Close ({success,message}).
    // POST /HrInquiry/AiClose  { inquiryId }
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiClose([FromBody] HrCloseRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        var payload = new { chatId = req.InquiryId, actorEmpcd = CurrentUser?.EmpCd, status = "CLOSED" };
        return Content(await _aiCsr.SetStatusRawAsync(payload), "application/json");
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
            targetRoles  = req.TargetRoles,
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
        public long?   Id           { get; set; }
        public string  Title        { get; set; } = "";
        public string  Content      { get; set; } = "";
        public int     DisplayOrder { get; set; }
        public bool    IsActive     { get; set; } = true;
        public string? TargetRoles  { get; set; }
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

        // closerType lấy đúng role thật (HR hoặc CSR) để báo cáo tách riêng được ai đóng — trước đây
        // gắn cứng "HR" khiến CSR bị tính chung vào bucket HR (bug báo 2026-10-01: "csr đâu").
        var result = await _inquiry.CloseAsync(
            inquiryId:  req.InquiryId,
            empCd:      CurrentUser.EmpCd,
            anonToken:  null,
            closerType: CurrentUser.RoleName ?? "HR",
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
