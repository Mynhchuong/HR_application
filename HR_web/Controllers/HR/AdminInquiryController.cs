using System.Globalization;
using ClosedXML.Excel;
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
        string? sort       = null,
        int     page       = 1,
        int     pageSize   = 30)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetHrListAsync(status, topicCd, chatType, assignedTo, search, sort, page, pageSize);
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
            senderType: "ADMIN",          // bypass lock check ở API
            senderName: CurrentUser.FullName,
            content:    req.Content,
            files:      finalFiles.Count > 0 ? finalFiles : null,
            refs:       req.Refs?.Count > 0 ? req.Refs : null);

        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Tìm Policy/Guide để chèn trích dẫn (Admin only)
    // GET /AdminInquiry/SearchRefs?type=POLICY|GUIDE&q=...
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> SearchRefs(string type = "POLICY", string? q = null)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var json = await _inquiry.SearchRefsRawAsync(type, q);
        return Content(json, "application/json");
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
    // GET /AdminInquiry/GetReport?from=YYYY-MM-DD&to=YYYY-MM-DD
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetReport(string? from = null, string? to = null)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });

        var result = await _inquiry.GetReportAsync(from, to);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Xuất Excel báo cáo
    // GET /AdminInquiry/ExportReport?from=YYYY-MM-DD&to=YYYY-MM-DD
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ExportReport(string? from = null, string? to = null)
    {
        if (!IsAdmin) return Forbid();

        var result = await _inquiry.GetReportAsync(from, to);
        if (!result.success)
            return BadRequest(result.message ?? "Không tải được dữ liệu báo cáo");

        string periodLabel = $"{result.from ?? "—"} → {result.to ?? "—"}";

        using var wb = new XLWorkbook();

        // ─── Sheet 1: Tổng quan ──────────────────────────────────────────────
        var wsSum = wb.Worksheets.Add("Tổng quan");
        var s     = result.summary ?? new InquiryReportSummary();

        wsSum.Cell(1, 1).Value = "BÁO CÁO HỘI THOẠI";
        wsSum.Cell(1, 1).Style.Font.Bold     = true;
        wsSum.Cell(1, 1).Style.Font.FontSize = 14;
        wsSum.Range(1, 1, 1, 2).Merge();

        wsSum.Cell(2, 1).Value = "Kỳ báo cáo";
        wsSum.Cell(2, 2).Value = periodLabel;
        wsSum.Cell(3, 1).Value = "Xuất lúc";
        wsSum.Cell(3, 2).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        int r = 5;
        void AddRowInt(string label, int value)
        {
            wsSum.Cell(r, 1).Value = label;
            wsSum.Cell(r, 2).Value = value;
            r++;
        }
        void AddRowOpt(string label, double? value, int digits)
        {
            wsSum.Cell(r, 1).Value = label;
            if (value.HasValue) wsSum.Cell(r, 2).Value = Math.Round(value.Value, digits);
            else                wsSum.Cell(r, 2).Value = "—";
            r++;
        }

        AddRowInt("Tổng hội thoại",      s.total);
        AddRowInt("Đang mở",             s.cntOpen);
        AddRowInt("Đã đóng",             s.cntClosed);
        AddRowInt("Hội thoại trực tiếp", s.cntDirect);
        AddRowInt("Hội thoại ẩn danh",   s.cntAnon);
        AddRowInt("HR đóng",             s.closedByHr);
        AddRowInt("NV tự đóng",          s.closedByEmp);
        AddRowInt("Admin đóng",          s.closedByAdmin);
        AddRowOpt("Đánh giá TB",         s.avgRating,    2);
        AddRowOpt("Tin nhắn TB/HT",      s.avgMsg,       1);
        AddRowOpt("TG xử lý TB (phút)",  s.avgHandleMin, 1);

        var sumHdr = wsSum.Range(5, 1, r - 1, 1);
        sumHdr.Style.Font.Bold = true;
        sumHdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef2f2");
        wsSum.Columns().AdjustToContents();

        // ─── Sheet 2: Theo chủ đề ────────────────────────────────────────────
        var wsTopic = wb.Worksheets.Add("Theo chủ đề");
        string[] topicHeaders = { "STT", "Chủ đề", "Tổng", "Đang mở", "Đã đóng", "Đánh giá TB" };
        for (int i = 0; i < topicHeaders.Length; i++)
        {
            var c = wsTopic.Cell(1, i + 1);
            c.Value = topicHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var t in result.byTopic)
        {
            wsTopic.Cell(row, 1).Value = row - 1;
            wsTopic.Cell(row, 2).Value = t.topicName ?? t.topicCd;
            wsTopic.Cell(row, 3).Value = t.total;
            wsTopic.Cell(row, 4).Value = t.cntOpen;
            wsTopic.Cell(row, 5).Value = t.cntClosed;
            if (t.avgRating.HasValue) wsTopic.Cell(row, 6).Value = Math.Round(t.avgRating.Value, 2);
            else                       wsTopic.Cell(row, 6).Value = "—";
            row++;
        }
        if (result.byTopic.Count > 0)
            wsTopic.Range(1, 1, 1, topicHeaders.Length).SetAutoFilter();
        wsTopic.Columns().AdjustToContents();

        // ─── Sheet 3: Workload HR ────────────────────────────────────────────
        var wsHr = wb.Worksheets.Add("Workload HR");
        string[] hrHeaders = { "STT", "Mã NS", "Họ và tên", "Tiếp nhận", "Đã đóng", "Đánh giá TB", "TG xử lý TB (phút)" };
        for (int i = 0; i < hrHeaders.Length; i++)
        {
            var c = wsHr.Cell(1, i + 1);
            c.Value = hrHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        row = 2;
        foreach (var h in result.byHr)
        {
            wsHr.Cell(row, 1).Value = row - 1;
            wsHr.Cell(row, 2).Value = h.hrCd   ?? "";
            var nameCell = wsHr.Cell(row, 3);
            nameCell.Value = h.hrName ?? "";
            nameCell.Style.Font.FontName = "Vnitbi__";
            wsHr.Cell(row, 4).Value = h.handled;
            wsHr.Cell(row, 5).Value = h.cntClosed;
            if (h.avgRating.HasValue)    wsHr.Cell(row, 6).Value = Math.Round(h.avgRating.Value, 2);
            else                          wsHr.Cell(row, 6).Value = "—";
            if (h.avgHandleMin.HasValue) wsHr.Cell(row, 7).Value = Math.Round(h.avgHandleMin.Value, 1);
            else                          wsHr.Cell(row, 7).Value = "—";
            row++;
        }
        if (result.byHr.Count > 0)
            wsHr.Range(1, 1, 1, hrHeaders.Length).SetAutoFilter();
        wsHr.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        string fileName = $"BaoCaoHoiThoai_{result.from ?? "tu"}_{result.to ?? "den"}.xlsx";

        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Quản lý chủ đề (Admin)
    // GET /AdminInquiry/Topics
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Topics()
    {
        if (!IsAdmin) return Forbid();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminTopics()
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetAdminTopicsAsync();
        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> SaveTopic([FromBody] AdminTopicSaveRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req == null) return Json(new { success = false, message = "Dữ liệu rỗng" });

        var payload = new
        {
            topicCd     = req.TopicCd,
            topicName   = req.TopicName,
            topicNameEn = req.TopicNameEn,
            icon        = req.Icon,
            color       = req.Color,
            sortOrder   = req.SortOrder,
            isActive    = req.IsActive,
            updtId      = CurrentUser?.EmpCd
        };
        var raw = await _inquiry.SaveTopicRawAsync(payload, req.IsEdit ? req.TopicCd : null);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> ToggleTopic([FromBody] AdminTopicToggleRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || string.IsNullOrWhiteSpace(req.TopicCd))
            return Json(new { success = false, message = "Thiếu mã chủ đề" });

        var raw = await _inquiry.ToggleTopicRawAsync(req.TopicCd, req.IsActive, CurrentUser?.EmpCd);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTopic([FromBody] AdminTopicDeleteRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || string.IsNullOrWhiteSpace(req.TopicCd))
            return Json(new { success = false, message = "Thiếu mã chủ đề" });

        var raw = await _inquiry.DeleteTopicRawAsync(req.TopicCd);
        return Content(raw, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner request models (Admin-specific)
    // ─────────────────────────────────────────────────────────────────────────

    public class AdminTopicSaveRequest
    {
        public bool    IsEdit      { get; set; }
        public string  TopicCd     { get; set; } = "";
        public string  TopicName   { get; set; } = "";
        public string? TopicNameEn { get; set; }
        public string? Icon        { get; set; }
        public string? Color       { get; set; }
        public int     SortOrder   { get; set; }
        public bool    IsActive    { get; set; } = true;
    }

    public class AdminTopicToggleRequest
    {
        public string TopicCd  { get; set; } = "";
        public bool   IsActive { get; set; }
    }

    public class AdminTopicDeleteRequest
    {
        public string TopicCd { get; set; } = "";
    }

    public class AdminSendRequest
    {
        public long                   InquiryId  { get; set; }
        public string?                InquiryNo  { get; set; }
        public string?                Content    { get; set; }
        public List<InquiryFileInfo>? Files      { get; set; }
        public List<InquiryRefInfo>?  Refs       { get; set; }
    }

    public class AdminCloseRequest
    {
        public long    InquiryId  { get; set; }
        public string? CloseNote  { get; set; }
    }
}
