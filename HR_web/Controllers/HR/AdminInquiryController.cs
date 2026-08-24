using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.HR;

/// <summary>
/// Dành cho Admin: toàn quyền trên tất cả inquiry.
///   - Send: senderType = "ADMIN" (bypass lock)
///   - Close: đóng bất kỳ conversation nào
///   - Unlock: mở khóa conversation bị HR giữ
/// </summary>
[Authorize(Roles = "Admin,CSR")]
public class AdminInquiryController : HR_web.Controllers.Inquiry.InquiryBaseController
{
    public AdminInquiryController(InquiryService inquiry) : base(inquiry) { }

    private bool IsAdmin => CurrentUser?.RoleName == "Admin";

    // Report/GetReport/ExportReport (thống kê, read-only) mở thêm cho CSR xem — các action khác vẫn Admin-only
    private bool CanViewReport => IsAdmin || CurrentUser?.RoleName == "CSR";

    // Tin nhắn phía HR/Admin gõ qua rich-text editor nên CONTENT lưu kèm thẻ HTML (<p>..</p>) —
    // bỏ thẻ cho dễ đọc khi xuất raw data ra Excel.
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        string text = Regex.Replace(html, "<[^>]+>", " ");
        text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

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
            senderType:   "ADMIN",          // bypass lock check ở API
            senderName:   CurrentUser.RoleName,
            assignedName: CurrentUser.FullName,
            content:      req.Content,
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
    // AJAX: Xoá hẳn conversation — CHỈ ADMIN (dọn dữ liệu test/phá ảnh hưởng KPI)
    // POST /AdminInquiry/Delete
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] IdRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Chỉ Admin mới có quyền xoá" });
        if (req.Id <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        var raw = await _inquiry.DeleteConversationRawAsync(req.Id);
        return Content(raw, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Báo cáo thống kê theo tuần / tháng
    // GET /AdminInquiry/Report
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Report()
    {
        if (!CanViewReport) return Forbid();
        ViewBag.IsAdmin = IsAdmin;
        return View();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Lấy dữ liệu báo cáo
    // GET /AdminInquiry/GetReport?from=YYYY-MM-DD&to=YYYY-MM-DD
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetReport(string? from = null, string? to = null)
    {
        if (!CanViewReport) return Json(new { success = false, message = "Không có quyền" });

        var result = await _inquiry.GetReportAsync(from, to);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Chi tiết đánh giá (sao + nội dung complain) — chỉ các hội thoại đã có đánh giá
    // GET /AdminInquiry/GetRatingDetail?from=YYYY-MM-DD&to=YYYY-MM-DD
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetRatingDetail(string? from = null, string? to = null)
    {
        if (!CanViewReport) return Json(new { success = false, message = "Không có quyền" });

        var rawResult = await _inquiry.GetReportRawAsync(from, to);
        if (!rawResult.success)
            return Json(new { success = false, message = rawResult.message ?? "Không tải được dữ liệu" });

        var data = rawResult.data
            .Where(d => d.rating.HasValue)
            .OrderByDescending(d => d.closedDt)
            .Select(d => new
            {
                id           = d.id,
                inquiryNo    = d.inquiryNo,
                topicName    = d.topicName,
                empCd        = d.empCd,
                empDisplay   = d.empDisplay,
                deptName     = d.deptName,
                assignedName = d.assignedName ?? d.assignedTo,
                closedDt     = d.closedDt,
                rating       = d.rating,
                ratingNote   = StripHtml(d.ratingNote)
            });

        return Json(new { success = true, data });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Xuất Excel báo cáo
    // GET /AdminInquiry/ExportReport?from=YYYY-MM-DD&to=YYYY-MM-DD
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ExportReport(string? from = null, string? to = null)
    {
        if (!CanViewReport) return Forbid();

        var result = await _inquiry.GetReportAsync(from, to);
        if (!result.success)
            return BadRequest(result.message ?? "Không tải được dữ liệu báo cáo");

        var rawResult = await _inquiry.GetReportRawAsync(from, to);

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
        AddRowOpt("Đánh giá TB",         s.avgRating,    2);
        AddRowOpt("Tin nhắn TB/HT",      s.avgMsg,       1);
        AddRowOpt("TG xử lý TB (phút)",  s.avgHandleMin, 1);

        var sumHdr = wsSum.Range(5, 1, r - 1, 1);
        sumHdr.Style.Font.Bold = true;
        sumHdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef2f2");

        // ─── Section: Người đóng hội thoại (kèm %) ────────────────────────
        int tc = s.closedByHr + s.closedByEmp + s.closedByAdmin;
        string Pct(int part) => tc > 0 ? $" ({Math.Round(part * 100.0 / tc)}%)" : "";

        r++;
        wsSum.Cell(r, 1).Value = "── NGƯỜI ĐÓNG HỘI THOẠI ──";
        wsSum.Cell(r, 1).Style.Font.Bold = true;
        wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        wsSum.Cell(r, 1).Style.Font.FontColor = XLColor.White;
        wsSum.Range(r, 1, r, 2).Merge();
        r++;

        wsSum.Cell(r, 1).Value = "HR đóng";        wsSum.Cell(r, 2).Value = s.closedByHr    + Pct(s.closedByHr);    r++;
        wsSum.Cell(r, 1).Value = "NV tự đóng";     wsSum.Cell(r, 2).Value = s.closedByEmp   + Pct(s.closedByEmp);   r++;
        wsSum.Cell(r, 1).Value = "Admin đóng";     wsSum.Cell(r, 2).Value = s.closedByAdmin + Pct(s.closedByAdmin); r++;
        wsSum.Cell(r, 1).Value = "Tổng đã đóng";   wsSum.Cell(r, 2).Value = tc;                                     r++;

        // ─── Section: Đánh giá cao nhất / thấp nhất ───────────────────────
        var rated = result.byHr.Where(h => h.avgRating.HasValue)
                              .OrderByDescending(h => h.avgRating!.Value).ToList();
        var topRated    = rated.FirstOrDefault();
        var bottomRated = rated.Count > 1 ? rated.Last() : null;

        r++;
        wsSum.Cell(r, 1).Value = "── TOP ĐÁNH GIÁ ──";
        wsSum.Cell(r, 1).Style.Font.Bold = true;
        wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        wsSum.Cell(r, 1).Style.Font.FontColor = XLColor.White;
        wsSum.Range(r, 1, r, 2).Merge();
        r++;

        if (topRated != null)
        {
            wsSum.Cell(r, 1).Value = "🏆 Cao nhất";
            wsSum.Cell(r, 2).Value = $"{topRated.hrName} ({topRated.hrCd}) — ★ {Math.Round(topRated.avgRating!.Value, 2)} / {topRated.handled} hội thoại";
            wsSum.Cell(r, 2).Style.Font.FontName = "Vnitbi__";
            wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#dcfce7");
            r++;
        }
        else
        {
            wsSum.Cell(r, 1).Value = "🏆 Cao nhất";
            wsSum.Cell(r, 2).Value = "Chưa có dữ liệu";
            r++;
        }
        if (bottomRated != null)
        {
            wsSum.Cell(r, 1).Value = "📉 Thấp nhất";
            wsSum.Cell(r, 2).Value = $"{bottomRated.hrName} ({bottomRated.hrCd}) — ★ {Math.Round(bottomRated.avgRating!.Value, 2)} / {bottomRated.handled} hội thoại";
            wsSum.Cell(r, 2).Style.Font.FontName = "Vnitbi__";
            wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
            r++;
        }
        else
        {
            wsSum.Cell(r, 1).Value = "📉 Thấp nhất";
            wsSum.Cell(r, 2).Value = "Chưa đủ dữ liệu (cần ≥ 2 HR có đánh giá)";
            r++;
        }

        wsSum.Column(1).Width = 30;
        wsSum.Column(2).Width = 55;

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

        // ─── Sheet 3: Workload ────────────────────────────────────────────
        var wsHr = wb.Worksheets.Add("Workload");
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

        // ─── Sheet 4: Raw Data (chi tiết từng hội thoại) ─────────────────────
        var wsRaw = wb.Worksheets.Add("Raw Data");
        string[] rawHeaders =
        {
            "STT", "ID chat", "Chủ đề", "Loại hội thoại", "Trạng thái", "Thời gian tạo", "Ngày đóng",
            "Người đóng", "Ghi chú đóng",
            "Mã NV", "Họ và tên", "Phòng ban", "Line", "Work",
            "Tin nhắn đầu tiên", "Tổng số tin nhắn", "Người xử lý", "Tin nhắn cuối (người xử lý)",
            "TG phản hồi (phút)", "Đánh giá", "Ghi chú đánh giá"
        };
        for (int i = 0; i < rawHeaders.Length; i++)
        {
            var c = wsRaw.Cell(1, i + 1);
            c.Value = rawHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        row = 2;
        foreach (var rw in rawResult.data)
        {
            wsRaw.Cell(row, 1).Value = row - 1;
            wsRaw.Cell(row, 2).Value = rw.inquiryNo;
            wsRaw.Cell(row, 3).Value = rw.topicName ?? "";
            wsRaw.Cell(row, 4).Value = rw.chatType == "ANON" ? "Ẩn danh" : "Trực tiếp";
            wsRaw.Cell(row, 5).Value = rw.status == "OPEN" ? "Đang mở" : rw.status == "CLOSED" ? "Đã đóng" : rw.status;
            if (rw.instDt.HasValue) wsRaw.Cell(row, 6).Value = rw.instDt.Value; else wsRaw.Cell(row, 6).Value = "—";
            if (rw.closedDt.HasValue) wsRaw.Cell(row, 7).Value = rw.closedDt.Value; else wsRaw.Cell(row, 7).Value = "—";
            wsRaw.Cell(row, 6).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            wsRaw.Cell(row, 7).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            string closedByLabel = rw.closedByType switch
            {
                "HR"    => "HR",
                "EMP"   => "NV tự đóng",
                "ADMIN" => "Admin",
                _       => ""
            };
            var closedByCell = wsRaw.Cell(row, 8);
            closedByCell.Value = string.IsNullOrEmpty(rw.closedByName) ? closedByLabel : $"{rw.closedByName} ({closedByLabel})";
            closedByCell.Style.Font.FontName = "Vnitbi__";
            wsRaw.Cell(row, 9).Value = StripHtml(rw.closeNote);

            wsRaw.Cell(row, 10).Value = rw.empCd ?? "";

            var nameCell = wsRaw.Cell(row, 11);
            nameCell.Value = rw.empDisplay ?? "";
            nameCell.Style.Font.FontName = "Vnitbi__";

            wsRaw.Cell(row, 12).Value = rw.deptName ?? "";
            wsRaw.Cell(row, 13).Value = rw.lineName ?? "";
            wsRaw.Cell(row, 14).Value = rw.workName ?? "";
            wsRaw.Range(row, 12, row, 14).Style.Font.FontName = "Vnitbi__";

            // Nội dung tin nhắn (chat content) là UTF-8 chuẩn — KHÔNG dùng font Vnitbi__ (chỉ áp cho tên/dept nguồn từ ECM100)
            wsRaw.Cell(row, 15).Value = StripHtml(rw.firstMsg);

            wsRaw.Cell(row, 16).Value = rw.msgCount;

            var handlerCell = wsRaw.Cell(row, 17);
            handlerCell.Value = (rw.assignedName ?? rw.assignedTo ?? "");
            handlerCell.Style.Font.FontName = "Vnitbi__";

            wsRaw.Cell(row, 18).Value = StripHtml(rw.lastHandlerMsg);

            if (rw.responseMin.HasValue) wsRaw.Cell(row, 19).Value = Math.Round(rw.responseMin.Value, 1);
            else                          wsRaw.Cell(row, 19).Value = "—";

            wsRaw.Cell(row, 20).Value = rw.rating.HasValue ? $"{rw.rating.Value} sao" : "Chưa đánh giá";
            wsRaw.Cell(row, 21).Value = StripHtml(rw.ratingNote);

            row++;
        }
        if (rawResult.data.Count > 0)
            wsRaw.Range(1, 1, 1, rawHeaders.Length).SetAutoFilter();
        wsRaw.SheetView.FreezeRows(1);
        wsRaw.Columns().AdjustToContents();
        // Giới hạn độ rộng cột nội dung dài để tránh sheet quá khổ khi mở
        wsRaw.Column(9).Width  = 40;
        wsRaw.Column(15).Width = 60;
        wsRaw.Column(18).Width = 60;
        wsRaw.Column(21).Width = 40;

        // ─── Sheet 5: Chi tiết đánh giá (chỉ hội thoại đã được NV đánh giá) ──
        var wsRating = wb.Worksheets.Add("Chi tiết đánh giá");
        string[] ratingHeaders =
        {
            "STT", "ID chat", "Ngày đóng", "Chủ đề",
            "Mã NV", "Họ và tên", "Phòng ban", "Người xử lý",
            "Số sao", "Nội dung đánh giá"
        };
        for (int i = 0; i < ratingHeaders.Length; i++)
        {
            var c = wsRating.Cell(1, i + 1);
            c.Value = ratingHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        row = 2;
        foreach (var rw in rawResult.data.Where(d => d.rating.HasValue).OrderByDescending(d => d.closedDt))
        {
            wsRating.Cell(row, 1).Value = row - 1;
            wsRating.Cell(row, 2).Value = rw.inquiryNo;
            if (rw.closedDt.HasValue) wsRating.Cell(row, 3).Value = rw.closedDt.Value; else wsRating.Cell(row, 3).Value = "—";
            wsRating.Cell(row, 3).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            wsRating.Cell(row, 4).Value = rw.topicName ?? "";
            wsRating.Cell(row, 5).Value = rw.empCd ?? "";

            var ratingNameCell = wsRating.Cell(row, 6);
            ratingNameCell.Value = rw.empDisplay ?? "";
            ratingNameCell.Style.Font.FontName = "Vnitbi__";

            wsRating.Cell(row, 7).Value = rw.deptName ?? "";
            wsRating.Cell(row, 7).Style.Font.FontName = "Vnitbi__";

            var handlerCell = wsRating.Cell(row, 8);
            handlerCell.Value = rw.assignedName ?? rw.assignedTo ?? "";
            handlerCell.Style.Font.FontName = "Vnitbi__";

            wsRating.Cell(row, 9).Value = rw.rating!.Value;
            wsRating.Cell(row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            wsRating.Cell(row, 10).Value = StripHtml(rw.ratingNote);

            row++;
        }
        if (rawResult.data.Any(d => d.rating.HasValue))
            wsRating.Range(1, 1, 1, ratingHeaders.Length).SetAutoFilter();
        wsRating.SheetView.FreezeRows(1);
        wsRating.Columns().AdjustToContents();
        wsRating.Column(10).Width = 60;

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
