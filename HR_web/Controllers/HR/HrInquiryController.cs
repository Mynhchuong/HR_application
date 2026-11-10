using HR_web.Models.Account;
using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HR_web.Controllers.HR;

/// <summary>
/// Dành cho HR: xem + reply tất cả inquiry.
/// Chỉ đóng được conversation mà mình đang phụ trách (ASSIGNED_TO = empCd).
/// Không có quyền Unlock.
/// </summary>
[Authorize(Roles = "HR,CSR,GA")]
public class HrInquiryController : HR_web.Controllers.Inquiry.InquiryBaseController
{
    private readonly AiCsrService _aiCsr;
    public HrInquiryController(InquiryService inquiry, AiCsrService aiCsr) : base(inquiry) { _aiCsr = aiCsr; }

    // "IsHr" = bất kỳ phòng nào được xử lý hội thoại (HR, CSR, GA).
    private bool IsHr => CurrentUser.HasAnyRole("HR", "CSR", "GA");

    // CSR thấy + phân loại TẤT CẢ; HR/GA chỉ thấy hội thoại CSR đã giao (PIC) cho phòng mình (yêu cầu 2026-11-06).
    private bool IsCsr => CurrentUser.HasRole("CSR");

    // Tên các phòng của người xem dùng để lọc theo PIC; null = không giới hạn (CSR).
    private string? ScopePicRoles => IsCsr ? null
        : string.Join(",", new[] { "HR", "GA" }.Where(r => CurrentUser.HasRole(r)));

    // Chặn xem/trả lời/đóng hội thoại không thuộc phòng mình (đi thẳng bằng URL/ID). CSR luôn được.
    private async Task<bool> CanAccessInquiryAsync(long id)
    {
        if (IsCsr) return true;
        try
        {
            var o = JObject.Parse(await _inquiry.GetCaseInfoRawAsync(id));
            var pic = o["data"]?["picDeptName"]?.Value<string>();
            return !string.IsNullOrEmpty(pic) && CurrentUser.HasRole(pic);
        }
        catch { return false; }
    }
    private const string NoAccessMsg = "Hội thoại này không thuộc phòng của bạn";

    // Chỉ CSR được phân loại case (yêu cầu 2026-11-06: "chỉ có csr mới dc phân loại thôi") — HR/GA/... chỉ xử lý.
    private bool CanClassify => IsCsr;

    // Vai trò xử lý hộp thư ("HR" | "CSR") — role chính HOẶC role dept. KHÔNG dùng RoleName thô: sau khi
    // đổi role chính của HR về Worker, RoleName = "Worker" sẽ làm sai câu trả lời mẫu và vi phạm
    // constraint CHK_INQUIRY_CLOSED_TYPE (chỉ nhận HR/CSR/ADMIN/EMP). GA được ghi nhận là "HR" (phòng xử lý);
    // tên người đóng/gửi vẫn lưu riêng.
    private string InquiryRole => CurrentUser.HasRole("HR") ? "HR" : CurrentUser.HasRole("CSR") ? "CSR" : "HR";

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Danh sách tất cả inquiry
    // GET /HrInquiry/Index
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Index()
    {
        if (!IsHr) return Forbid();
        ViewBag.IsCsr = IsCsr;
        return View();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Popup "Phân loại" trong khung chat (Views/HrInquiry/_CaseClassifyModal.cshtml): CHỈ CSR sửa Loại báo cáo, Chủ đề,
    // Mức độ, PIC, Hạn phản hồi (yêu cầu khách 2026-11-06: "CSR điều chỉnh nếu công nhân chọn chưa đúng").
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> CaseInfo(long id)
    {
        if (!CanClassify) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetCaseInfoRawAsync(id), "application/json");
    }

    // Danh mục để đổ dropdown: chủ đề (kể cả đang ẩn, để hiện đúng chủ đề của đơn cũ), mức độ, phòng ban PIC
    [HttpGet]
    public async Task<IActionResult> CaseCatalogs()
    {
        if (!CanClassify) return Json(new { success = false, message = "Không có quyền" });
        try
        {
            var topics = await _inquiry.GetAdminTopicsAsync();
            var sev    = JObject.Parse(await _inquiry.GetSeveritiesRawAsync());
            var pic    = JObject.Parse(await _inquiry.GetPicDeptRolesRawAsync());
            return Json(new { success = topics.success, topics = topics.data, severities = sev["data"], picRoles = pic["data"] });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Không tải được danh mục: " + ex.Message });
        }
    }

    public class CaseInfoBody
    {
        public long      Id            { get; set; }
        public string?   ReportType    { get; set; }
        public string?   TopicCd       { get; set; }
        public string?   SeverityCd    { get; set; }
        public int?      PicDeptRoleId { get; set; }
        public DateTime? DeadlineDt    { get; set; }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCaseInfo([FromBody] CaseInfoBody body)
    {
        if (!CanClassify) return Json(new { success = false, message = "Không có quyền" });
        if (body == null || body.Id <= 0) return Json(new { success = false, message = "Thiếu mã hội thoại" });
        var payload = new
        {
            reportType = body.ReportType, topicCd = body.TopicCd, severityCd = body.SeverityCd,
            picDeptRoleId = body.PicDeptRoleId, deadlineDt = body.DeadlineDt, updtId = CurrentUser?.EmpCd
        };
        return Content(await _inquiry.UpdateCaseInfoRawAsync(body.Id, payload), "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHÂN LOẠI NHANH (chỉ CSR) — hàng đợi hội thoại chưa xác nhận + gợi ý + gán hàng loạt.
    // GET /HrInquiry/Triage · GET /HrInquiry/TriageQueue · POST /HrInquiry/TriageConfirm
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Triage()
    {
        if (!CanClassify) return Forbid();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> TriageQueue(string scope = "open", string? chatType = null, bool noPic = false, string? topicCd = null, string? from = null, string? to = null, int page = 1, int pageSize = 30)
    {
        if (!CanClassify) return Json(new { success = false, message = "Không có quyền" });
        var q = $"scope={Uri.EscapeDataString(scope ?? "open")}&noPic={(noPic ? "true" : "false")}&page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(chatType)) q += $"&chatType={Uri.EscapeDataString(chatType)}";
        if (!string.IsNullOrEmpty(topicCd))  q += $"&topicCd={Uri.EscapeDataString(topicCd)}";
        if (!string.IsNullOrEmpty(from))     q += $"&from={Uri.EscapeDataString(from)}";
        if (!string.IsNullOrEmpty(to))       q += $"&to={Uri.EscapeDataString(to)}";
        return Content(await _inquiry.GetTriageQueueRawAsync(q), "application/json");
    }

    public class TriageConfirmBody
    {
        public List<long> Ids           { get; set; } = new();
        public string?    ReportType    { get; set; }
        public string?    TopicCd       { get; set; }
        public string?    SeverityCd    { get; set; }
        public int?       PicDeptRoleId { get; set; }
        public string?    DeadlineMode  { get; set; }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TriageConfirm([FromBody] TriageConfirmBody body)
    {
        if (!CanClassify) return Json(new { success = false, message = "Không có quyền" });
        if (body == null || body.Ids == null || body.Ids.Count == 0) return Json(new { success = false, message = "Chưa chọn hội thoại" });
        return Content(await _inquiry.TriageConfirmRawAsync(new
        {
            ids = body.Ids, reportType = body.ReportType, topicCd = body.TopicCd, severityCd = body.SeverityCd,
            picDeptRoleId = body.PicDeptRoleId, setPic = body.PicDeptRoleId.HasValue, deadlineMode = body.DeadlineMode, updtId = CurrentUser?.EmpCd
        }), "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Công tắc bật/tắt "Hỏi AI" — HR/CSR tự điều khiển (yêu cầu 2026-10-19).
    // GET /HrInquiry/GetAiConfig · POST /HrInquiry/SetAiConfig { enabled }
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAiConfig()
        => Content(await _aiCsr.GetConfigRawAsync(), "application/json");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAiConfig([FromBody] SetAiConfigRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var payload = new { enabled = req?.Enabled ?? true, actorEmpCd = CurrentUser!.EmpCd };
        return Content(await _aiCsr.SetConfigRawAsync(payload), "application/json");
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
        if (isAi && !IsCsr) return Forbid();
        if (!isAi && !await CanAccessInquiryAsync(id))
        {
            TempData["ErrorMessage"] = NoAccessMsg;
            return RedirectToAction("Index");
        }
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
        ViewBag.CanClassify  = CanClassify;
        ViewBag.IsCsr        = IsCsr;

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

        bool unreadEmp = (head?["UnreadEmp"]?.Value<int>() ?? 0) > 0;
        return new InquiryMessagesResponse { success = true, inquiry = inquiry, messages = MapAiMessages(msgs, id, unreadEmp) };
    }

    // unreadEmp: cờ ở MỨC CẢ THREAD (HR_AI_CHAT.UNREAD_EMP) — bảng HR_AI_CHAT_MSG không lưu cờ đọc
    // theo từng tin, nên dùng chung cho mọi tin CSR/HR trong thread để quyết định còn thu hồi được
    // không (đồng nhất với điều kiện check ở AiCsrController.CsrRecall).
    private static List<InquiryMsgDto> MapAiMessages(JArray msgs, long inquiryId, bool unreadEmp)
    {
        return msgs.Select(m =>
        {
            string senderType = m["senderType"]?.Value<string>() ?? "EMP";
            string mapped = senderType switch { "CSR" => "HR", "AI" => "SYS", _ => "EMP" };
            bool isDeleted = m["isDeleted"]?.Value<bool>() ?? false;
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
                IsReadEmp  = mapped == "HR" ? !unreadEmp : true,
                IsDeleted  = isDeleted,
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
        int     pageSize   = 30,
        string? draftPending = null)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetHrListAsync(status, topicCd, chatType, assignedTo, search, sort, CurrentUser?.EmpCd, page, pageSize, ScopePicRoles, draftPending);
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
        if (!IsCsr) return Json(new { success = false, message = "Không có quyền" });
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
        if (!await CanAccessInquiryAsync(id)) return Json(new { success = false, message = NoAccessMsg });
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
        if (!IsCsr) return Json(new { success = false, message = "Không có quyền" });
        if (id <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        JObject obj;
        try { obj = JObject.Parse(await _aiCsr.ThreadMessagesRawAsync(id, afterMsgId)); }
        catch { return Json(new { success = false, message = "Lỗi kết nối server" }); }

        if (obj["success"]?.Value<bool>() != true)
            return Json(new { success = false, message = obj["message"]?.Value<string>() });

        var msgs = (obj["messages"] as JArray) ?? new JArray();
        bool unreadEmp = (obj["unreadEmp"]?.Value<int>() ?? 0) > 0;
        return Json(new
        {
            success = true,
            inquiry = new { status = obj["status"]?.Value<string>() ?? "OPEN" },
            messages = MapAiMessages(msgs, id, unreadEmp)
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
        if (!await CanAccessInquiryAsync(req.InquiryId)) return Json(new { success = false, message = NoAccessMsg });

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

        // HR/GA (không phải CSR): KHÔNG gửi thẳng cho công nhân — lưu thành BẢN NHÁP chờ CSR duyệt/sửa (2026-11-06).
        if (!IsCsr)
        {
            var created = await _inquiry.DraftPostRawAsync("Inquiry/draft/create", new
            {
                inquiryId  = req.InquiryId,
                authorCd   = CurrentUser!.EmpCd,
                authorName = CurrentUser.FullName,
                authorRole = CurrentUser.HasRole("HR") ? "HR" : "GA",
                content    = hasContent ? req.Content : null,
                filesJson  = finalFiles.Count > 0 ? JsonConvert.SerializeObject(finalFiles) : null,
                refsJson   = hasRefs ? JsonConvert.SerializeObject(req.Refs) : null
            });
            var co = JObject.Parse(created);
            if (co["success"]?.Value<bool>() != true)
                return Json(new { success = false, message = co["message"]?.Value<string>() ?? "Không tạo được bản nháp" });
            return Json(new { success = true, draft = true, draftId = co["draftId"]?.Value<long>() ?? 0 });
        }

        // CSR sửa bản nháp trong khung soạn rồi bấm gửi → duyệt bản nháp đó với nội dung đã sửa (file/trích dẫn thêm mới được gộp)
        if (req.DraftId > 0)
            return await ApproveDraftAsync(req.DraftId, req.InquiryId, hasContent ? req.Content : null,
                                           finalFiles.Count > 0 ? finalFiles : null, hasRefs ? req.Refs : null);

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
        var json = await _inquiry.CannedRepliesRawAsync(q, InquiryRole);
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
        if (!IsCsr) return Json(new { success = false, message = "Không có quyền" });
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
        if (!IsCsr) return Json(new { success = false, message = "Không có quyền" });
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
        if (!await CanAccessInquiryAsync(req.Id)) return Json(new { success = false, message = NoAccessMsg });
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
    // AJAX: Thu hồi tin nhắn HR/CSR đã gửi trong chat AI SAMHO - CSR (HR_AI_CHAT, nguồn riêng,
    // không dùng chung InquiryService.RecallAsync của HR_INQUIRY).
    // POST /HrInquiry/AiRecall
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiRecall([FromBody] IdRequest req)
    {
        if (!IsCsr) return Json(new { success = false, message = "Không có quyền" });
        var payload = new { msgId = req.Id, actorEmpcd = CurrentUser?.EmpCd };
        return Content(await _aiCsr.CsrRecallRawAsync(payload), "application/json");
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
        if (!await CanAccessInquiryAsync(req.InquiryId)) return Json(new { success = false, message = NoAccessMsg });

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
            closerType: InquiryRole,
            closerName: CurrentUser.FullName,
            closeNote:  req.CloseNote);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BẢN NHÁP TRẢ LỜI (HR/GA soạn → CSR duyệt/sửa → gửi cho công nhân) — yêu cầu 2026-11-06.
    // Tin nhắn thật được gửi dưới danh nghĩa NGƯỜI SOẠN (để người soạn thành người phụ trách, đóng/thu hồi được);
    // CSR chỉ là người kiểm duyệt (lưu REVIEWER_* trong HR_INQUIRY_DRAFT).
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<IActionResult> ApproveDraftAsync(long draftId, long expectInquiryId, string? editedContent,
                                                        List<InquiryFileInfo>? extraFiles, List<InquiryRefInfo>? extraRefs)
    {
        var claim = JObject.Parse(await _inquiry.DraftPostRawAsync($"Inquiry/draft/{draftId}/claim",
            new { reviewerCd = CurrentUser!.EmpCd, reviewerName = CurrentUser.FullName, content = editedContent }));
        if (claim["success"]?.Value<bool>() != true)
            return Json(new { success = false, message = claim["message"]?.Value<string>() ?? "Không duyệt được" });

        try
        {
            var d = claim["data"]!;
            long inquiryId = d["inquiryId"]?.Value<long>() ?? 0;
            if (expectInquiryId > 0 && inquiryId != expectInquiryId)
            {
                await _inquiry.DraftPostRawAsync($"Inquiry/draft/{draftId}/revert", new { });
                return Json(new { success = false, message = "Bản nháp không thuộc hội thoại này" });
            }

            var files = new List<InquiryFileInfo>();
            var refs  = new List<InquiryRefInfo>();
            try
            {
                var fj = d["filesJson"]?.Value<string>(); if (!string.IsNullOrEmpty(fj)) files = JsonConvert.DeserializeObject<List<InquiryFileInfo>>(fj) ?? files;
                var rj = d["refsJson"]?.Value<string>();  if (!string.IsNullOrEmpty(rj)) refs  = JsonConvert.DeserializeObject<List<InquiryRefInfo>>(rj) ?? refs;
            }
            catch { /* JSON hỏng → gửi phần chữ */ }
            if (extraFiles != null) files.AddRange(extraFiles);
            if (extraRefs  != null) refs.AddRange(extraRefs);
            if (files.Count > 5) files = files.Take(5).ToList();

            string? authorCd   = d["authorCd"]?.Value<string>();
            string? authorName = d["authorName"]?.Value<string>();
            var res = await _inquiry.SendAsync(
                inquiryId: inquiryId, empCd: authorCd, anonToken: null,
                senderType: "HR", senderName: authorName,
                content: d["finalContent"]?.Value<string>(),
                files: files.Count > 0 ? files : null, refs: refs.Count > 0 ? refs : null,
                assignedName: authorName, skipLockCheck: true);

            if (!res.success)
            {
                await _inquiry.DraftPostRawAsync($"Inquiry/draft/{draftId}/revert", new { });
                return Json(new { success = false, message = res.message ?? "Gửi thất bại" });
            }
            await _inquiry.DraftPostRawAsync($"Inquiry/draft/{draftId}/finalize", new { msgId = res.msgId });
            return Json(new { success = true, msgId = res.msgId, approved = true });
        }
        catch (Exception ex)
        {
            // Lỗi bất ngờ sau khi đã "chốt" duyệt → trả bản nháp về chờ duyệt để CSR thử lại (không để kẹt ở APPROVED mà không có tin)
            try { await _inquiry.DraftPostRawAsync($"Inquiry/draft/{draftId}/revert", new { }); } catch { }
            return Json(new { success = false, message = "Lỗi khi gửi: " + ex.Message });
        }
    }

    // GET /HrInquiry/Drafts?id= — bản nháp còn hiệu lực của 1 hội thoại (CSR thấy để duyệt; HR/GA thấy để theo dõi)
    [HttpGet]
    public async Task<IActionResult> Drafts(long id)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (!await CanAccessInquiryAsync(id)) return Json(new { success = false, message = NoAccessMsg });
        try
        {
            var o = JObject.Parse(await _inquiry.GetDraftsRawAsync(id));
            if (o["success"]?.Value<bool>() != true) return Json(new { success = false, message = o["message"]?.Value<string>() });
            var list = new JArray();
            foreach (var d in (o["data"] as JArray) ?? new JArray())
            {
                var files = new JArray();
                try
                {
                    var fj = d["filesJson"]?.Value<string>();
                    if (!string.IsNullOrEmpty(fj))
                        foreach (var f in JArray.Parse(fj)) files.Add(new JObject { ["fileName"] = f["FileName"], ["fileType"] = f["FileType"] });
                }
                catch { }
                int refCount = 0;
                try { var rj = d["refsJson"]?.Value<string>(); if (!string.IsNullOrEmpty(rj)) refCount = JArray.Parse(rj).Count; } catch { }
                list.Add(new JObject
                {
                    ["id"] = d["id"], ["authorName"] = d["authorName"], ["authorRole"] = d["authorRole"],
                    ["content"] = d["content"], ["status"] = d["status"], ["reviewNote"] = d["reviewNote"],
                    ["reviewerName"] = d["reviewerName"], ["createdDt"] = d["createdDt"],
                    ["mine"] = d["authorCd"]?.Value<string>() == CurrentUser!.EmpCd,
                    ["files"] = files, ["refCount"] = refCount
                });
            }
            return Json(new { success = true, isCsr = IsCsr, drafts = JsonConvert.DeserializeObject<object>(list.ToString()) });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // POST /HrInquiry/ApproveDraft { id } — CSR duyệt NGUYÊN VĂN (muốn sửa chữ: sửa trong khung soạn rồi gửi)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDraft([FromBody] IdRequest req)
    {
        if (!IsCsr) return Json(new { success = false, message = "Chỉ CSR được duyệt" });
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu bản nháp" });
        return await ApproveDraftAsync(req.Id, 0, null, null, null);
    }

    public class DraftReturnRequest { public long Id { get; set; } public string? Note { get; set; } }

    // POST /HrInquiry/ReturnDraft { id, note } — CSR trả lại kèm góp ý
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnDraft([FromBody] DraftReturnRequest req)
    {
        if (!IsCsr) return Json(new { success = false, message = "Chỉ CSR được trả lại" });
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu bản nháp" });
        return Content(await _inquiry.DraftPostRawAsync($"Inquiry/draft/{req.Id}/return",
            new { reviewerCd = CurrentUser!.EmpCd, reviewerName = CurrentUser.FullName, note = req.Note }), "application/json");
    }

    // POST /HrInquiry/DismissDraft { id } — người soạn thu hồi bản nháp đang chờ / bỏ bản bị trả lại
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DismissDraft([FromBody] IdRequest req)
    {
        if (!IsHr) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu bản nháp" });
        return Content(await _inquiry.DraftPostRawAsync($"Inquiry/draft/{req.Id}/dismiss", new { actorCd = CurrentUser!.EmpCd }), "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Đóng hàng loạt — HR/CSR tự upload file Excel (1 cột ID chat) để gửi 1 tin nhắn
    // rồi đóng hết, thay cho việc trước đây phải nhờ xử lý thủ công qua script ngoài.
    // GET /HrInquiry/BulkClose
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult BulkClose()
    {
        if (!IsCsr) return Forbid();
        return View();
    }

    // GET /HrInquiry/BulkCloseTemplate — file mẫu Excel (1 cột "ID chat")
    [HttpGet]
    public IActionResult BulkCloseTemplate()
    {
        if (!IsHr) return Forbid();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("ID chat");
        var h = ws.Cell(1, 1);
        h.Value = "ID chat";
        h.Style.Font.Bold = true;
        h.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1d4ed8");
        h.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        ws.Cell(2, 1).Value = "INQ-20261014-0001";
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauDongHangLoat.xlsx");
    }

    // POST /HrInquiry/BulkCloseUpload — parse Excel (cột 1 = INQUIRY_NO), gửi nguyên danh sách +
    // nội dung tin nhắn sang HR_api/Inquiry/bulk-close xử lý (giữ đúng luật lock/ownership hiện có,
    // không bypass — HR/CSR chỉ đóng được hội thoại chưa ai nhận hoặc đang do chính mình phụ trách).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCloseUpload(IFormFile file, [FromForm] string message)
    {
        if (!IsCsr) return Json(new { success = false, message = "Không có quyền" });
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });
        if (string.IsNullOrWhiteSpace(message))
            return Json(new { success = false, message = "Vui lòng nhập nội dung tin nhắn" });

        var inquiryNos = new List<string>();
        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var v = ws.Cell(r, 1).GetString().Trim();
                if (!string.IsNullOrEmpty(v)) inquiryNos.Add(v);
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Không đọc được file Excel: " + ex.Message });
        }

        if (inquiryNos.Count == 0)
            return Json(new { success = false, message = "File không có ID chat nào" });

        var payload = new
        {
            inquiryNos,
            empCd      = CurrentUser!.EmpCd,
            closerName = CurrentUser.FullName,
            closerType = InquiryRole,
            message
        };
        var raw = await _inquiry.BulkCloseRawAsync(payload);
        return Content(raw, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner request models (HR-specific)
    // ─────────────────────────────────────────────────────────────────────────

    public class HrSendRequest
    {
        public long                   InquiryId  { get; set; }
        public long                   DraftId    { get; set; }   // CSR sửa bản nháp trong khung soạn rồi gửi → duyệt bản nháp này
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

    public class SetAiConfigRequest
    {
        public bool Enabled { get; set; }
    }
}
