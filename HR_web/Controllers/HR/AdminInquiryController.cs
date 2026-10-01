using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Inquiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

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
    private readonly AiCsrService _aiCsr;
    public AdminInquiryController(InquiryService inquiry, AiCsrService aiCsr) : base(inquiry) { _aiCsr = aiCsr; }

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
    // PAGE: Màn hình chat (có nút Unlock) — dùng CHUNG cho cả hội thoại thường và AI SAMHO - CSR
    // (source=AI), cùng cơ chế với HrInquiryController.Chat (yêu cầu 2026-10-01: "cùng 1 cơ chế
    // mà chia 2 page cực quá").
    // GET /AdminInquiry/Chat?id=&source=AI
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IActionResult> Chat(long id, string? source = null)
    {
        if (!IsAdmin) return Forbid();
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
            _ = _aiCsr.CsrMarkReadRawAsync(new { chatId = id });
        }
        else
        {
            // Mark read phía HR (Admin dùng chung HR bucket) — ghi mốc đã đọc RIÊNG cho tài khoản này
            // (viewerEmpcd), khỏi ảnh hưởng badge chưa đọc của các CSR/HR/Admin khác.
            // AWAIT (không fire-and-forget) để chắc chắn ghi được HR_INQUIRY_READER trước khi user
            // quay lại danh sách — nếu không, mở xong quay ra vẫn thấy "chưa đọc".
            await _inquiry.MarkReadAsync(id, "HR", viewerEmpcd: CurrentUser!.EmpCd);
        }

        return View(result);
    }

    // Xem BuildAiChatResponseAsync/MapAiMessages (HrInquiryController) để biết chi tiết mapping —
    // cố tình KHÔNG tách helper dùng chung giữa 2 controller để tránh thêm 1 lớp phụ thuộc chéo
    // giữa HR và Admin chỉ vì 1 đoạn mapping ngắn.
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
    // AJAX: Gửi tin nhắn trả lời AI chat / Đóng AI chat — xem HrInquiryController.AiSend/AiClose.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiSend([FromBody] AdminSendRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiClose([FromBody] AdminCloseRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req.InquiryId <= 0) return Json(new { success = false, message = "Thiếu ID hội thoại" });

        var payload = new { chatId = req.InquiryId, actorEmpcd = CurrentUser?.EmpCd, status = "CLOSED" };
        return Content(await _aiCsr.SetStatusRawAsync(payload), "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> AiGetMessages(long id, long afterMsgId = 0)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
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
        var result = await _inquiry.GetHrListAsync(status, topicCd, chatType, assignedTo, search, sort, CurrentUser?.EmpCd, page, pageSize);
        return Json(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Danh sách hội thoại "AI SAMHO - CSR" (HR_AI_CHAT — nguồn riêng, KHÔNG chung
    // HR_INQUIRY) — gộp hiển thị vào CÙNG trang qua filter "Loại hội thoại" riêng (yêu cầu
    // 2026-10-01, cùng cơ chế đã làm ở HrInquiry/Index để HR/CSR check). Xem chi tiết/trả lời vẫn
    // chung Chat?source=AI (controller AiCsrAdmin — cả Index lẫn Thread — đã bỏ hẳn).
    // GET /AdminInquiry/GetAiList?status=&search=&page=&pageSize=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAiList(string? status = null, string? search = null, int page = 1, int pageSize = 30)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _aiCsr.ListRawAsync(status, search, page, pageSize), "application/json");
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
            senderName:   CurrentUser.FullName,
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
        var result = await _inquiry.MarkReadAsync(req.Id, "HR", viewerEmpcd: CurrentUser?.EmpCd);
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

        // Gộp thêm "AI SAMHO - CSR" vào cùng báo cáo (yêu cầu 2026-10-01: "báo cáo tính luôn AI") —
        // nguồn (HR_AI_CHAT) tách hẳn khỏi HR_INQUIRY nên không nhồi vào summary/byTopic/byHr hiện
        // có (sẽ sai lệch số liệu cũ), mà thêm field "ai" riêng cho FE tự vẽ phần riêng.
        JObject? aiReport = null;
        try { aiReport = JObject.Parse(await _aiCsr.ReportRawAsync(from, to)); } catch { }

        var combined = JObject.FromObject(result);
        combined["ai"] = aiReport;
        return Content(combined.ToString(Newtonsoft.Json.Formatting.None), "application/json");
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
        int tc = s.closedByHr + s.closedByCsr + s.closedByEmp + s.closedByAdmin;
        string Pct(int part) => tc > 0 ? $" ({Math.Round(part * 100.0 / tc)}%)" : "";

        r++;
        wsSum.Cell(r, 1).Value = "── NGƯỜI ĐÓNG HỘI THOẠI ──";
        wsSum.Cell(r, 1).Style.Font.Bold = true;
        wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        wsSum.Cell(r, 1).Style.Font.FontColor = XLColor.White;
        wsSum.Range(r, 1, r, 2).Merge();
        r++;

        wsSum.Cell(r, 1).Value = "HR đóng";        wsSum.Cell(r, 2).Value = s.closedByHr    + Pct(s.closedByHr);    r++;
        wsSum.Cell(r, 1).Value = "CSR đóng";       wsSum.Cell(r, 2).Value = s.closedByCsr   + Pct(s.closedByCsr);   r++;
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
                "CSR"   => "CSR",
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

        // ─── Sheet 6: Nội dung chat (toàn bộ tin nhắn từng hội thoại — yêu cầu 2026-09-21,
        // HR/CSR cần xem lại NV nhắn gì, ai phản hồi lúc mấy giờ, ai bấm kết thúc) ─────
        var msgResult  = await _inquiry.GetReportMessagesAsync(from, to);
        var infoById   = rawResult.data.ToDictionary(d => d.id, d => d);
        var wsChat     = wb.Worksheets.Add("Nội dung chat");
        string[] chatHeaders =
        {
            "ID chat", "Chủ đề", "Mã NV", "Họ và tên",
            "Thời gian", "Người gửi", "Tên người gửi", "Nội dung",
            "Người kết thúc", "TG kết thúc"
        };
        for (int i = 0; i < chatHeaders.Length; i++)
        {
            var c = wsChat.Cell(1, i + 1);
            c.Value = chatHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        row = 2;
        long? prevInquiryId = null;
        foreach (var m in msgResult.data.OrderBy(x => x.inquiryId).ThenBy(x => x.sentDt))
        {
            infoById.TryGetValue(m.inquiryId, out var info);

            // Kẻ viền đậm phía trên khi sang hội thoại khác — thay cho gộp ô cho đơn giản
            if (prevInquiryId.HasValue && prevInquiryId.Value != m.inquiryId)
                wsChat.Range(row, 1, row, chatHeaders.Length).Style.Border.TopBorder = XLBorderStyleValues.Medium;

            wsChat.Cell(row, 1).Value = info?.inquiryNo ?? m.inquiryId.ToString();
            wsChat.Cell(row, 2).Value = info?.topicName ?? "";
            wsChat.Cell(row, 3).Value = info?.empCd ?? "";

            var nameCell = wsChat.Cell(row, 4);
            nameCell.Value = info?.empDisplay ?? "";
            nameCell.Style.Font.FontName = "Vnitbi__";

            if (m.sentDt.HasValue) wsChat.Cell(row, 5).Value = m.sentDt.Value; else wsChat.Cell(row, 5).Value = "—";
            wsChat.Cell(row, 5).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            string senderLabel = m.senderType switch
            {
                "EMP" => "Nhân viên",
                "HR"  => "HR/CSR",
                "SYS" => "Hệ thống",
                _     => m.senderType
            };
            wsChat.Cell(row, 6).Value = senderLabel;

            var senderNameCell = wsChat.Cell(row, 7);
            senderNameCell.Value = m.senderName ?? "";
            senderNameCell.Style.Font.FontName = "Vnitbi__";

            var contentCell = wsChat.Cell(row, 8);
            contentCell.Value = m.msgType switch
            {
                "IMAGE" => "[Hình ảnh]",
                "FILE"  => "[Tệp đính kèm]",
                _       => StripHtml(m.content)
            };
            if (m.senderType == "SYS") contentCell.Style.Font.Italic = true;

            string closedByLabel = info?.closedByType switch
            {
                "HR"    => "HR",
                "CSR"   => "CSR",
                "EMP"   => "NV tự đóng",
                "ADMIN" => "Admin",
                _       => ""
            };
            var closedByCell = wsChat.Cell(row, 9);
            closedByCell.Value = string.IsNullOrEmpty(info?.closedByName) ? closedByLabel : $"{info!.closedByName} ({closedByLabel})";
            closedByCell.Style.Font.FontName = "Vnitbi__";

            if (info?.closedDt != null) wsChat.Cell(row, 10).Value = info.closedDt.Value; else wsChat.Cell(row, 10).Value = "—";
            wsChat.Cell(row, 10).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            prevInquiryId = m.inquiryId;
            row++;
        }
        if (msgResult.data.Count > 0)
            wsChat.Range(1, 1, 1, chatHeaders.Length).SetAutoFilter();
        wsChat.SheetView.FreezeRows(1);
        wsChat.Columns().AdjustToContents();
        wsChat.Column(8).Width = 70;

        // ─── Sheet 7: AI SAMHO - CSR (yêu cầu 2026-10-01: "báo cáo tính luôn AI, xuất Excel luôn
        // AI") — nguồn HR_AI_CHAT tách hẳn khỏi HR_INQUIRY nên query/sheet riêng, không gộp chung
        // số liệu với các sheet trên để tránh sai lệch báo cáo cũ.
        JObject? aiReport = null;
        try { aiReport = JObject.Parse(await _aiCsr.ReportRawAsync(from, to)); } catch { }

        if (aiReport?["success"]?.Value<bool>() == true)
        {
            var wsAi = wb.Worksheets.Add("AI SAMHO - CSR");
            var aiS  = aiReport["summary"];

            wsAi.Cell(1, 1).Value = "BÁO CÁO AI SAMHO - CSR";
            wsAi.Cell(1, 1).Style.Font.Bold = true;
            wsAi.Cell(1, 1).Style.Font.FontSize = 14;
            wsAi.Range(1, 1, 1, 2).Merge();
            wsAi.Cell(2, 1).Value = "Kỳ báo cáo"; wsAi.Cell(2, 2).Value = periodLabel;

            int ar = 4;
            void AiRow(string label, object? value) { wsAi.Cell(ar, 1).Value = label; wsAi.Cell(ar, 2).Value = value?.ToString() ?? "—"; ar++; }
            AiRow("Tổng đoạn chat AI", aiS?["total"]?.Value<int>());
            AiRow("Đang mở", aiS?["cntOpen"]?.Value<int>());
            AiRow("Đã đóng", aiS?["cntClosed"]?.Value<int>());
            AiRow("Tin nhắn TB/đoạn", aiS?["avgMsg"]?.Value<double?>());
            AiRow("Số lần AI trả lời lỗi (cần CSR hỗ trợ)", aiS?["failedCnt"]?.Value<int>());
            wsAi.Range(4, 1, ar - 1, 1).Style.Font.Bold = true;
            wsAi.Column(1).Width = 36; wsAi.Column(2).Width = 30;

            // Workload — ai đã trả lời bao nhiêu đoạn chat AI
            ar += 1;
            wsAi.Cell(ar, 1).Value = "WORKLOAD TRẢ LỜI AI CHAT"; wsAi.Cell(ar, 1).Style.Font.Bold = true;
            wsAi.Range(ar, 1, ar, 2).Merge(); ar++;
            string[] aiHrHeaders = { "Mã NS", "Họ và tên", "Số đoạn đã trả lời", "Tổng số tin đã gửi" };
            for (int i = 0; i < aiHrHeaders.Length; i++)
            {
                var c = wsAi.Cell(ar, i + 1);
                c.Value = aiHrHeaders[i]; c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed"); c.Style.Font.FontColor = XLColor.White;
            }
            int hrHeaderRow = ar; ar++;
            foreach (var h in (aiReport["byHr"] as JArray) ?? new JArray())
            {
                wsAi.Cell(ar, 1).Value = h["hrCd"]?.Value<string>() ?? "";
                var nameCell = wsAi.Cell(ar, 2); nameCell.Value = h["hrName"]?.Value<string>() ?? ""; nameCell.Style.Font.FontName = "Vnitbi__";
                wsAi.Cell(ar, 3).Value = h["repliedChats"]?.Value<int>() ?? 0;
                wsAi.Cell(ar, 4).Value = h["repliedMsgs"]?.Value<int>() ?? 0;
                ar++;
            }
            if (ar > hrHeaderRow + 1) wsAi.Range(hrHeaderRow, 1, ar - 1, aiHrHeaders.Length).SetAutoFilter();

            // Raw data — từng đoạn chat
            ar += 1;
            wsAi.Cell(ar, 1).Value = "DANH SÁCH ĐOẠN CHAT"; wsAi.Cell(ar, 1).Style.Font.Bold = true;
            wsAi.Range(ar, 1, ar, 2).Merge(); ar++;
            string[] aiRawHeaders = { "ID", "Mã NV", "Họ và tên", "Phòng ban", "Line", "Việc", "Câu hỏi đầu", "Trạng thái", "Số tin", "Ngày tạo", "Ngày đóng", "CSR trả lời cuối" };
            for (int i = 0; i < aiRawHeaders.Length; i++)
            {
                var c = wsAi.Cell(ar, i + 1);
                c.Value = aiRawHeaders[i]; c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed"); c.Style.Font.FontColor = XLColor.White;
            }
            int rawHeaderRow = ar; ar++;
            foreach (var x in (aiReport["rawData"] as JArray) ?? new JArray())
            {
                wsAi.Cell(ar, 1).Value = x["id"]?.Value<string>() ?? "";
                wsAi.Cell(ar, 2).Value = x["empcd"]?.Value<string>() ?? "";
                var nameCell = wsAi.Cell(ar, 3); nameCell.Value = x["empName"]?.Value<string>() ?? ""; nameCell.Style.Font.FontName = "Vnitbi__";
                var dlwCell = wsAi.Range(ar, 4, ar, 6); dlwCell.Style.Font.FontName = "Vnitbi__";
                wsAi.Cell(ar, 4).Value = x["deptName"]?.Value<string>() ?? "";
                wsAi.Cell(ar, 5).Value = x["lineName"]?.Value<string>() ?? "";
                wsAi.Cell(ar, 6).Value = x["workName"]?.Value<string>() ?? "";
                wsAi.Cell(ar, 7).Value = x["title"]?.Value<string>() ?? "";
                wsAi.Cell(ar, 8).Value = x["status"]?.Value<string>() == "OPEN" ? "Đang mở" : "Đã đóng";
                wsAi.Cell(ar, 9).Value = x["msgCount"]?.Value<int>() ?? 0;
                var instDt = x["instDt"]?.Value<DateTime?>();
                if (instDt.HasValue) { wsAi.Cell(ar, 10).Value = instDt.Value; wsAi.Cell(ar, 10).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; } else wsAi.Cell(ar, 10).Value = "—";
                var closedDt = x["closedDt"]?.Value<DateTime?>();
                if (closedDt.HasValue) { wsAi.Cell(ar, 11).Value = closedDt.Value; wsAi.Cell(ar, 11).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; } else wsAi.Cell(ar, 11).Value = "—";
                wsAi.Cell(ar, 12).Value = x["lastCsrCd"]?.Value<string>() ?? "—";
                ar++;
            }
            if (ar > rawHeaderRow + 1) wsAi.Range(rawHeaderRow, 1, ar - 1, aiRawHeaders.Length).SetAutoFilter();
            wsAi.SheetView.FreezeRows(rawHeaderRow);
            wsAi.Columns().AdjustToContents();
            wsAi.Column(7).Width = 45;

            // ─── Sheet 8: Nội dung chat AI (toàn bộ tin nhắn từng đoạn — yêu cầu 2026-10-01, cùng
            // tinh thần sheet "Nội dung chat" của Inquiry phía trên) ─────────────────────────────
            JObject? aiMsgReport = null;
            try { aiMsgReport = JObject.Parse(await _aiCsr.ReportMessagesRawAsync(from, to)); } catch { }

            if (aiMsgReport?["success"]?.Value<bool>() == true)
            {
                var wsAiChat = wb.Worksheets.Add("Nội dung chat AI");
                string[] aiChatHeaders = { "ID đoạn chat", "Câu hỏi đầu", "Mã NV", "Họ và tên", "Thời gian", "Người gửi", "Tên người gửi", "Nội dung" };
                for (int i = 0; i < aiChatHeaders.Length; i++)
                {
                    var c = wsAiChat.Cell(1, i + 1);
                    c.Value = aiChatHeaders[i]; c.Style.Font.Bold = true;
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed"); c.Style.Font.FontColor = XLColor.White;
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                int acRow = 2;
                decimal? prevChatId = null;
                foreach (var m in (aiMsgReport["data"] as JArray) ?? new JArray())
                {
                    decimal chatId = m["chatId"]?.Value<decimal>() ?? 0;
                    if (prevChatId.HasValue && prevChatId.Value != chatId)
                        wsAiChat.Range(acRow, 1, acRow, aiChatHeaders.Length).Style.Border.TopBorder = XLBorderStyleValues.Medium;

                    wsAiChat.Cell(acRow, 1).Value = chatId.ToString();
                    wsAiChat.Cell(acRow, 2).Value = m["title"]?.Value<string>() ?? "";
                    wsAiChat.Cell(acRow, 3).Value = m["empcd"]?.Value<string>() ?? "";
                    var nameCell = wsAiChat.Cell(acRow, 4); nameCell.Value = m["empName"]?.Value<string>() ?? ""; nameCell.Style.Font.FontName = "Vnitbi__";

                    var sentDt = m["sentDt"]?.Value<DateTime?>();
                    if (sentDt.HasValue) { wsAiChat.Cell(acRow, 5).Value = sentDt.Value; wsAiChat.Cell(acRow, 5).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; }
                    else wsAiChat.Cell(acRow, 5).Value = "—";

                    string senderType = m["senderType"]?.Value<string>() ?? "";
                    string senderLabel = senderType switch { "EMP" => "Nhân viên", "CSR" => "CSR/HR", "AI" => "AI", _ => senderType };
                    wsAiChat.Cell(acRow, 6).Value = senderLabel;

                    var senderNameCell = wsAiChat.Cell(acRow, 7);
                    senderNameCell.Value = m["senderName"]?.Value<string>() ?? "";
                    senderNameCell.Style.Font.FontName = "Vnitbi__";

                    var contentCell = wsAiChat.Cell(acRow, 8);
                    contentCell.Value = StripHtml(m["content"]?.Value<string>());
                    if (senderType == "AI" && m["aiFailed"]?.Value<bool>() == true) contentCell.Style.Font.Italic = true;

                    prevChatId = chatId;
                    acRow++;
                }
                if (acRow > 2) wsAiChat.Range(1, 1, 1, aiChatHeaders.Length).SetAutoFilter();
                wsAiChat.SheetView.FreezeRows(1);
                wsAiChat.Columns().AdjustToContents();
                wsAiChat.Column(2).Width = 40;
                wsAiChat.Column(8).Width = 70;
            }
        }

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
    // PAGE: Quản lý câu trả lời mẫu (Admin) — yêu cầu HR 2026-09-12
    // Kho dùng chung cho Admin/HR/CSR, xem chung cùng 1 view (~/Views/AdminInquiry/CannedReplies.cshtml).
    // HR/CSR cũng thêm/sửa/xoá được (yêu cầu 2026-09-14) — nhưng qua route riêng
    // /HrInquiry/CannedReplies (HrInquiryController), KHÔNG qua controller này (Admin-only ở đây).
    // GET /AdminInquiry/CannedReplies
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult CannedReplies()
    {
        if (!IsAdmin) return Forbid();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminCannedReplies()
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var raw = await _inquiry.CannedRepliesAdminRawAsync(CurrentUser?.EmpCd);
        return Content(raw, "application/json");
    }

    // Dùng bởi picker khi chat (nút "Chèn câu trả lời mẫu") — chỉ đọc mẫu đang active.
    [HttpGet]
    public async Task<IActionResult> GetCannedRepliesForPicker(string? q = null)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var raw = await _inquiry.CannedRepliesRawAsync(q, CurrentUser?.RoleName);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveCannedReply([FromBody] AdminCannedReplySaveRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
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
    public async Task<IActionResult> DeleteCannedReply([FromBody] AdminCannedReplyDeleteRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu ID" });

        var payload = new { id = req.Id, actorEmpCd = CurrentUser?.EmpCd };
        var raw = await _inquiry.CannedReplyDeleteRawAsync(payload);
        return Content(raw, "application/json");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner request models (Admin-specific)
    // ─────────────────────────────────────────────────────────────────────────

    public class AdminCannedReplySaveRequest
    {
        public long?   Id           { get; set; }
        public string  Title        { get; set; } = "";
        public string  Content      { get; set; } = "";
        public int     DisplayOrder { get; set; }
        public bool    IsActive     { get; set; } = true;
        public string? TargetRoles  { get; set; }
    }

    public class AdminCannedReplyDeleteRequest
    {
        public long Id { get; set; }
    }

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
