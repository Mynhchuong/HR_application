using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Inquiry;

/// <summary>
/// Dành cho nhân viên: tạo + chat inquiry (DIRECT và ANON).
/// Truy cập bởi tất cả user đã đăng nhập.
/// </summary>
[Authorize]
public class EmployeeInquiryController : InquiryBaseController
{
    public EmployeeInquiryController(InquiryService inquiry) : base(inquiry) { }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Danh sách conversation DIRECT của nhân viên đang đăng nhập
    // GET /EmployeeInquiry/Index
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Index() => View();

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Lấy danh sách conversation DIRECT
    // GET /EmployeeInquiry/GetMyList
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMyList()
    {
        var result = await _inquiry.GetMyListAsync(CurrentUser!.EmpCd);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Lấy danh sách conversation ANON theo token (JS đọc localStorage)
    // GET /EmployeeInquiry/GetAnonList?token=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAnonList(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Json(new { success = false, message = "Thiếu token" });
        var result = await _inquiry.GetAnonListAsync(token);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Tra cứu hội thoại ẩn danh bằng mã — KHÔNG cần đúng thiết bị/trình duyệt đã gửi,
    // KHÔNG dùng EMPCD của người đang tra cứu để xác định chủ hội thoại (yêu cầu khách hàng
    // 2026-11-04: hệ thống không được dùng thông tin tài khoản đăng nhập/thiết bị để xác định
    // người gửi ẩn danh — nhiều NV dùng chung 1 máy vẫn không tạo ra liên kết danh tính nào).
    // POST /EmployeeInquiry/LookupByCode
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LookupByCode([FromBody] LookupByCodeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return Json(new { success = false, message = "Vui lòng nhập mã tra cứu" });
        var result = await _inquiry.LookupByCodeAsync(req.Code.Trim());
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Màn hình tạo conversation mới (chọn loại + topic + nội dung đầu tiên)
    // GET /EmployeeInquiry/Create
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Create()
    {
        var topics = await _inquiry.GetTopicsAsync();
        return View(topics);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Tạo conversation mới
    // POST /EmployeeInquiry/Create
    //   DIRECT: dùng empCd từ session
    //   ANON:   dùng anonToken từ client (localStorage)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TopicCd))
            return Json(new { success = false, message = "Vui lòng chọn chủ đề" });
        // Nội dung KHÔNG còn bắt buộc (2026-11-12): bước nhập tiêu đề/nội dung đã bỏ, hội thoại
        // tạo rỗng rồi công nhân gõ tin đầu tiên ngay trong khung chat.
        if (req.Content?.Length > 4000)
            return Json(new { success = false, message = "Nội dung vượt quá 4000 ký tự" });

        if (req.ChatType != "DIRECT" && req.ChatType != "ANON")
            return Json(new { success = false, message = "Loại chat không hợp lệ" });
        if (req.ChatType == "ANON" && string.IsNullOrWhiteSpace(req.AnonToken))
            return Json(new { success = false, message = "Thiếu token ẩn danh" });
        // Loại báo cáo bắt buộc (bước chọn "Kiến nghị / Ý kiến" ở form tạo mới)
        var reportType = (req.ReportType ?? "").Trim().ToUpperInvariant();
        if (string.Equals(req.TopicCd, "COMPLAINT", StringComparison.OrdinalIgnoreCase)) reportType = "OPINION";   // phản ánh từ trang Hồ sơ = Góp ý
        if (reportType != "GRIEVANCE" && reportType != "OPINION")
            return Json(new { success = false, message = "Vui lòng chọn Loại báo cáo (Kiến nghị / Ý kiến)" });

        string? empCd     = req.ChatType == "DIRECT" ? CurrentUser!.EmpCd : null;
        string? anonToken = req.ChatType == "ANON"   ? req.AnonToken       : null;

        var result = await _inquiry.CreateAsync(
            chatType:  req.ChatType,
            topicCd:   req.TopicCd,
            subject:   req.Subject,
            empCd:     empCd,
            anonToken: anonToken,
            content:   req.Content,
            reportType: reportType);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Màn hình chat
    // GET /EmployeeInquiry/Chat?id=
    //   - Tự động mark read (EMP side) khi mở
    //   - ANON: JS bắt đầu poll mỗi 15 giây
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Chat(long id, string? anonToken = null)
    {
        if (id <= 0) return RedirectToAction("Index");

        var result = await _inquiry.GetMessagesAsync(id);
        if (!result.success || result.inquiry == null)
        {
            TempData["ErrorMessage"] = result.message ?? "Không tìm thấy hội thoại";
            return RedirectToAction("Index");
        }

        bool isAnon = result.inquiry.ChatType == "ANON";

        if (!isAnon && result.inquiry.EmpCd != CurrentUser!.EmpCd)
        {
            TempData["ErrorMessage"] = "Bạn không có quyền xem hội thoại này";
            return RedirectToAction("Index");
        }
        if (isAnon && result.inquiry.AnonToken != anonToken)
        {
            TempData["ErrorMessage"] = "Bạn không có quyền xem hội thoại này";
            return RedirectToAction("Index");
        }

        // Mark read phía employee (fire-and-forget, không block render)
        string? tokenForRead = isAnon ? anonToken : null;
        _ = _inquiry.MarkReadAsync(id, "EMP", tokenForRead);

        ViewBag.IsAnon     = isAnon;
        ViewBag.AnonToken  = anonToken;   // truyền vào View để JS dùng cho polling/send
        ViewBag.EmpCd      = CurrentUser!.EmpCd;
        ViewBag.EmpName    = CurrentUser.FullName;

        return View(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Load tin nhắn (ANON polling: JS gọi mỗi 15s với afterMsgId)
    // GET /EmployeeInquiry/GetMessages?id=&afterMsgId=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMessages(long id, long afterMsgId = 0, string? anonToken = null)
    {
        if (id <= 0) return Json(new { success = false, message = "Thiếu ID" });
        var result = await _inquiry.GetMessagesAsync(id, afterMsgId);

        // Bug thật phát hiện 2026-11-04 (code review): endpoint AJAX polling này KHÔNG hề kiểm tra
        // chủ sở hữu — khác hẳn Chat() ngay bên trên có đủ 2 check (EmpCd cho DIRECT, AnonToken cho
        // ANON). Vì ID tăng dần đoán được, bất kỳ NV đăng nhập nào gọi thẳng URL này với 1 id bất kỳ
        // là đọc được toàn bộ nội dung chat (kể cả phản ánh nhạy cảm) của người khác. Thêm đúng 2
        // check y hệt Chat() trước khi trả dữ liệu.
        if (result.inquiry != null)
        {
            bool isAnon = result.inquiry.ChatType == "ANON";
            bool denied = isAnon
                ? result.inquiry.AnonToken != anonToken
                : result.inquiry.EmpCd != CurrentUser!.EmpCd;
            if (denied)
                return Json(new { success = false, message = "Bạn không có quyền xem hội thoại này" });

            // Không trả token về client để tránh lộ qua AJAX polling
            result.inquiry.AnonToken = null;
        }
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Gửi tin nhắn
    // POST /EmployeeInquiry/Send
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromBody] EmpSendRequest req)
    {
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
            empCd:      string.IsNullOrEmpty(req.AnonToken) ? CurrentUser!.EmpCd : null,
            anonToken:  req.AnonToken,
            senderType: "EMP",
            senderName: string.IsNullOrEmpty(req.AnonToken) ? CurrentUser!.FullName : null,
            content:    req.Content,
            files:      finalFiles.Count > 0 ? finalFiles : null);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đánh dấu đã đọc (EMP side)
    // POST /EmployeeInquiry/MarkRead
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead([FromBody] EmpAnonRequest req)
    {
        var result = await _inquiry.MarkReadAsync(req.InquiryId, "EMP", req.AnonToken);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Thu hồi tin nhắn của chính mình
    // POST /EmployeeInquiry/Recall
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Recall([FromBody] EmpRecallRequest req)
    {
        if (req.MsgId <= 0) return Json(new { success = false, message = "Thiếu ID tin nhắn" });

        string? empCd     = string.IsNullOrEmpty(req.AnonToken) ? CurrentUser!.EmpCd : null;
        string? anonToken = req.AnonToken;

        var result = await _inquiry.RecallAsync(req.MsgId, "EMP", empCd, anonToken);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đóng conversation (EMP side)
    // POST /EmployeeInquiry/Close
    //   API sẽ validate EMPCD/ANON_TOKEN là chủ conversation
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close([FromBody] EmpCloseRequest req)
    {
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        bool isAnon       = !string.IsNullOrEmpty(req.AnonToken);
        string? empCd     = isAnon ? null : CurrentUser!.EmpCd;
        string? anonToken = req.AnonToken;

        var result = await _inquiry.CloseAsync(
            inquiryId:  req.InquiryId,
            empCd:      empCd,
            anonToken:  anonToken,
            closerType: "EMP",
            closerName: isAnon ? "Ẩn danh" : CurrentUser!.FullName,
            closeNote:  req.CloseNote);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Đánh giá sau khi conversation CLOSED
    // POST /EmployeeInquiry/Rate
    //   Chỉ DIRECT mới bắt buộc, ANON cũng cho phép nếu có token
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rate([FromBody] RateRequest req)
    {
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });
        if (req.Rating < 1 || req.Rating > 5)
            return Json(new { success = false, message = "Đánh giá phải từ 1 đến 5 sao" });
        // 1–2 sao (= Không hài lòng) → bắt buộc nêu lý do (Phase 4). Kiểm ở web để HR_api vẫn nhận được client cũ.
        if (req.Rating <= 2 && (req.RatingNote?.Trim().Length ?? 0) < 3)
            return Json(new { success = false, message = "Vui lòng cho biết lý do chưa hài lòng" });

        string? empCd     = string.IsNullOrEmpty(req.AnonToken) ? CurrentUser!.EmpCd : null;
        string? anonToken = req.AnonToken;

        var result = await _inquiry.RateAsync(req.InquiryId, empCd, anonToken, req.Rating, req.RatingNote);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner request models (Employee-specific)
    // ─────────────────────────────────────────────────────────────────────────

    public class CreateRequest
    {
        public string  ChatType  { get; set; } = "DIRECT";
        public string  TopicCd   { get; set; } = "";
        public string? Subject   { get; set; }
        public string? AnonToken { get; set; }
        public string? Content   { get; set; }        // null = tạo hội thoại rỗng, gõ tin trong chat
        public string? ReportType { get; set; }          // GRIEVANCE (Kiến nghị) | OPINION (Ý kiến)
    }

    public class LookupByCodeRequest
    {
        public string Code { get; set; } = "";
    }

    public class EmpSendRequest
    {
        public long                   InquiryId  { get; set; }
        public string?                InquiryNo  { get; set; }
        public string?                AnonToken  { get; set; }
        public string?                Content    { get; set; }
        public List<InquiryFileInfo>? Files      { get; set; }
    }

    public class EmpAnonRequest
    {
        public long    InquiryId  { get; set; }
        public string? AnonToken  { get; set; }
    }

    public class EmpRecallRequest
    {
        public long    MsgId      { get; set; }
        public string? AnonToken  { get; set; }
    }

    public class EmpCloseRequest
    {
        public long    InquiryId  { get; set; }
        public string? AnonToken  { get; set; }
        public string? CloseNote  { get; set; }
    }

    public class RateRequest
    {
        public long    InquiryId  { get; set; }
        public string? AnonToken  { get; set; }
        public int     Rating     { get; set; }
        public string? RatingNote { get; set; }
    }
}
