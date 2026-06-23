using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Inquiry;

/// <summary>
/// Dành cho nhân viên: tạo + chat inquiry (DIRECT và ANON).
/// Truy cập bởi tất cả user đã đăng nhập.
/// </summary>
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
        if (string.IsNullOrWhiteSpace(req.Content))
            return Json(new { success = false, message = "Nội dung không được để trống" });
        if (req.Content.Length > 4000)
            return Json(new { success = false, message = "Nội dung vượt quá 4000 ký tự" });

        if (req.ChatType != "DIRECT" && req.ChatType != "ANON")
            return Json(new { success = false, message = "Loại chat không hợp lệ" });
        if (req.ChatType == "ANON" && string.IsNullOrWhiteSpace(req.AnonToken))
            return Json(new { success = false, message = "Thiếu token ẩn danh" });

        string? empCd     = req.ChatType == "DIRECT" ? CurrentUser!.EmpCd : null;
        string? anonToken = req.ChatType == "ANON"   ? req.AnonToken       : null;

        var result = await _inquiry.CreateAsync(
            chatType:  req.ChatType,
            topicCd:   req.TopicCd,
            subject:   req.Subject,
            empCd:     empCd,
            anonToken: anonToken,
            content:   req.Content);

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
    public async Task<IActionResult> GetMessages(long id, long afterMsgId = 0)
    {
        if (id <= 0) return Json(new { success = false, message = "Thiếu ID" });
        var result = await _inquiry.GetMessagesAsync(id, afterMsgId);
        // Không trả token về client để tránh lộ qua AJAX polling
        if (result.inquiry != null) result.inquiry.AnonToken = null;
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
        public string  Content   { get; set; } = "";
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
