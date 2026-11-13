using HR_web.Models.Account;
using HR_web.Helpers;
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
    // Sheet "Dữ liệu biểu đồ" (bảng số) + sheet "Biểu đồ" (biểu đồ Excel thật) — giống các biểu đồ trên trang Báo cáo.
    private static void BuildChartSheets(XLWorkbook wb, JObject dj, InquiryReportResponse result, List<ExcelChartBuilder.Spec> specs)
    {
        static int I(JToken? t) => t == null || t.Type == JTokenType.Null ? 0 : t.Value<int>();
        const string DS = "'Dữ liệu biểu đồ'";
        var chartWs = wb.Worksheets.Add("Biểu đồ", 3);
        var ds = wb.Worksheets.Add("Dữ liệu biểu đồ");
        chartWs.Cell(1, 1).Value = "Biểu đồ hộp thư phản ánh"; chartWs.Cell(1, 1).Style.Font.Bold = true; chartWs.Cell(1, 1).Style.Font.FontSize = 14;
        chartWs.Cell(2, 1).Value = "Kỳ: " + (dj["from"]?.Value<string>() ?? "") + " → " + (dj["to"]?.Value<string>() ?? "") + " · số liệu nằm ở sheet \"Dữ liệu biểu đồ\"";
        chartWs.ShowGridLines = false;

        string ColName(int c0) => XLHelper.GetColumnLetterFromNumber(c0);
        // Ghi 1 bảng bắt đầu ở cột c0 (1-based); trả về tham chiếu cột (theo số cột trong bảng) hoặc null nếu bảng rỗng
        Func<int, string>? Table(int c0, string[] heads, List<object[]> rows)
        {
            for (int j = 0; j < heads.Length; j++)
            {
                var h = ds.Cell(1, c0 + j); h.Value = heads[j]; h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
            }
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < heads.Length; j++)
                {
                    var v = rows[i][j];
                    if (v is int iv) ds.Cell(2 + i, c0 + j).Value = iv;
                    else if (v is double dv) ds.Cell(2 + i, c0 + j).Value = dv;
                    else ds.Cell(2 + i, c0 + j).Value = v?.ToString() ?? "";
                }
            if (rows.Count == 0) return null;
            return j => $"{DS}!${ColName(c0 + j)}$2:${ColName(c0 + j)}${rows.Count + 1}";
        }

        int slot = 0;
        void Place(ExcelChartBuilder.Spec sp)
        {
            sp.Col = (slot % 2) * 10; sp.Row = 3 + (slot / 2) * 20; sp.ColSpan = 9; sp.RowSpan = 19; slot++;
            specs.Add(sp);
        }
        static string Pct(int a, int b) => b > 0 ? Math.Round(a * 100.0 / b, 1) + "%" : "—";

        // ── Theo ngày ──
        var from = DateTime.Parse(dj["from"]!.Value<string>()!); var to = DateTime.Parse(dj["to"]!.Value<string>()!);
        var sr = dj["series"]!; var byType = (sr["byType"] as JArray) ?? new JArray();
        var msgs = ((dj["dailyMsgs"] as JArray) ?? new JArray()).ToDictionary(x => x["date"]!.Value<string>()!, x => I(x["count"]));
        var newC = ((sr["newContacts"] as JArray) ?? new JArray()).ToDictionary(x => x["bucket"]!.Value<string>()!, x => I(x["count"]));
        var dayRows = new List<object[]>(); int cum = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            string k = d.ToString("yyyy-MM-dd");
            int G(string key) => byType.Where(x => x["bucket"]?.Value<string>() == k && x["key"]?.Value<string>() == key).Sum(x => I(x["count"]));
            cum += newC.TryGetValue(k, out var nc) ? nc : 0;
            dayRows.Add(new object[] { d.ToString("dd/MM"), G("GRIEVANCE"), G("OPINION"), G("NONE"), msgs.TryGetValue(k, out var m) ? m : 0, cum });
        }
        var tDay = Table(1, new[] { "Ngày", "Kiến nghị", "Ý kiến", "Chưa phân loại", "Tin nhắn công nhân", "Số người liên hệ (cộng dồn)" }, dayRows);
        if (tDay != null)
        {
            Place(new ExcelChartBuilder.Spec { Title = "Diễn biến theo ngày", Kind = ExcelChartBuilder.Kind.ColStacked, CatRef = tDay(0), Series = {
                new() { Name = "Kiến nghị", ValRef = tDay(1), Color = "DC2626" }, new() { Name = "Ý kiến", ValRef = tDay(2), Color = "6366F1" },
                new() { Name = "Chưa phân loại (đơn cũ)", ValRef = tDay(3), Color = "CBD5E1" }, new() { Name = "Tin nhắn công nhân", ValRef = tDay(4), Color = "F59E0B", AsLine = true } } });
            Place(new ExcelChartBuilder.Spec { Title = "Số người liên hệ (cộng dồn)", Kind = ExcelChartBuilder.Kind.Line, CatRef = tDay(0), Series = { new() { Name = "Số người", ValRef = tDay(5), Color = "0F172A" } } });
        }

        // ── Chủ đề theo thời gian (top 5, theo ngày) ──
        var topT = ((sr["topTopics"] as JArray) ?? new JArray()).Select(x => (cd: x["topicCd"]!.Value<string>()!, nm: x["topicName"]?.Value<string>() ?? x["topicCd"]!.Value<string>()!)).ToList();
        var byTopic = (sr["byTopic"] as JArray) ?? new JArray();
        var topicRows = new List<object[]>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            string k = d.ToString("yyyy-MM-dd"); var row = new object[1 + topT.Count]; row[0] = d.ToString("dd/MM");
            for (int j = 0; j < topT.Count; j++)
                row[j + 1] = byTopic.Where(x => x["bucket"]?.Value<string>() == k && x["topicCd"]?.Value<string>() == topT[j].cd).Sum(x => I(x["count"]));
            topicRows.Add(row);
        }
        var tTopic = topT.Count == 0 ? null : Table(8, new[] { "Ngày" }.Concat(topT.Select(t => t.nm)).ToArray(), topicRows);
        if (tTopic != null)
        {
            var sp = new ExcelChartBuilder.Spec { Title = "Chủ đề theo thời gian", Kind = ExcelChartBuilder.Kind.Line, CatRef = tTopic(0) };
            for (int j = 0; j < topT.Count; j++) sp.Series.Add(new() { Name = topT[j].nm, ValRef = tTopic(j + 1), Color = ExcelChartBuilder.Palette[(j + 1) % 5] });
            Place(sp);
        }

        // ── Theo tháng: nhận / đã giải quyết / còn tồn ──
        var mo = dj["monthly"]!; var recv = ((mo["received"] as JArray) ?? new JArray()).ToDictionary(x => x["bucket"]!.Value<string>()!, x => I(x["count"]));
        var done = ((mo["resolved"] as JArray) ?? new JArray()).ToDictionary(x => x["bucket"]!.Value<string>()!, x => I(x["count"]));
        var monthRows = new List<object[]>(); int bal = I(mo["openAtStart"]);
        for (var d = new DateTime(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1))
        {
            string k = d.ToString("yyyy-MM"); int r = recv.TryGetValue(k, out var rv) ? rv : 0, dn = done.TryGetValue(k, out var dv2) ? dv2 : 0; bal += r - dn;
            monthRows.Add(new object[] { d.ToString("MM/yyyy"), r, dn, bal });
        }
        var tMonth = Table(15, new[] { "Tháng", "Nhận mới", "Đã giải quyết", "Còn tồn cuối tháng" }, monthRows);
        if (tMonth != null)
        {
            Place(new ExcelChartBuilder.Spec { Title = "Theo tháng: nhận mới / đã giải quyết", Kind = ExcelChartBuilder.Kind.Col, CatRef = tMonth(0), Series = {
                new() { Name = "Nhận mới", ValRef = tMonth(1), Color = "6366F1", Labels = true }, new() { Name = "Đã giải quyết", ValRef = tMonth(2), Color = "16A34A", Labels = true } } });
            Place(new ExcelChartBuilder.Spec { Title = "Còn tồn cuối tháng", Kind = ExcelChartBuilder.Kind.Line, CatRef = tMonth(0), Series = { new() { Name = "Còn tồn", ValRef = tMonth(3), Color = "DC2626", Labels = true } } });
        }

        // ── Công khai / Ẩn danh ──
        int cd = result.summary?.cntDirect ?? 0, ca = result.summary?.cntAnon ?? 0;
        var tPub = Table(20, new[] { "Loại", "Số hội thoại" }, new List<object[]> { new object[] { "Công khai", cd }, new object[] { "Ẩn danh", ca } });
        if (tPub != null) Place(new ExcelChartBuilder.Spec { Title = "Công khai / Ẩn danh", Kind = ExcelChartBuilder.Kind.Doughnut, CatRef = tPub(0), Series = { new() { Name = "Hội thoại", ValRef = tPub(1) } }, PointColors = new[] { "6366F1", "475569" } });

        // ── Giới tính ──
        var sx = ((dj["activeBySex"] as JArray) ?? new JArray()).ToDictionary(x => x["sex"]!.Value<string>()!, x => I(x["count"]));
        var sexRows = new List<object[]>(); var sexColors = new List<string>();
        foreach (var (k, nm, col) in new[] { ("F", "Nữ", "EC4899"), ("M", "Nam", "3B82F6"), ("?", "Không rõ", "94A3B8") })
            if (sx.TryGetValue(k, out var sv) && (sv > 0 || k != "?")) { sexRows.Add(new object[] { nm, sv }); sexColors.Add(col); }
        var tSex = Table(23, new[] { "Giới tính", "Số người" }, sexRows);
        if (tSex != null) Place(new ExcelChartBuilder.Spec { Title = "Giới tính người liên hệ", Kind = ExcelChartBuilder.Kind.Doughnut, CatRef = tSex(0), Series = { new() { Name = "Người", ValRef = tSex(1) } }, PointColors = sexColors.ToArray() });

        // ── Thiết bị ──
        var devMap = new Dictionary<string, string> { ["APP_ANDROID"] = "App Android", ["APP_IOS"] = "App iOS", ["WEB_MOBILE"] = "Web trên điện thoại", ["WEB_DESKTOP"] = "Web máy tính", ["UNKNOWN"] = "Không rõ" };
        var devRows = ((dj["devices"] as JArray) ?? new JArray()).Select(x => new object[] { devMap.TryGetValue(x["device"]?.Value<string>() ?? "", out var nm) ? nm : x["device"]?.Value<string>() ?? "", I(x["count"]) }).ToList();
        var tDev = Table(26, new[] { "Thiết bị", "Số người" }, devRows);
        if (tDev != null) Place(new ExcelChartBuilder.Spec { Title = "Thiết bị người dùng", Kind = ExcelChartBuilder.Kind.Bar, CatRef = tDev(0), Series = { new() { Name = "Số người", ValRef = tDev(1), Color = "6366F1", Labels = true } } });

        // ── Thời gian giải quyết ──
        var bk = (dj["resolve"]?["buckets"] as JArray) ?? new JArray(); string[] bl = { "Dưới 1 giờ", "1 – 4 giờ", "4 – 24 giờ", "1 – 3 ngày", "Trên 3 ngày" };
        var resRows = Enumerable.Range(0, Math.Min(bl.Length, bk.Count)).Select(i => new object[] { bl[i], bk[i].Value<int>() }).ToList();
        var tRes = Table(29, new[] { "Khoảng", "Số hội thoại" }, resRows);
        if (tRes != null) Place(new ExcelChartBuilder.Spec { Title = "Thời gian giải quyết", Kind = ExcelChartBuilder.Kind.Col, CatRef = tRes(0), Series = { new() { Name = "Hội thoại", ValRef = tRes(1), Color = "10B981", Labels = true } } });

        // ── Loại báo cáo / Mức độ ──
        var typeMap = new Dictionary<string, (string nm, string col)> { ["GRIEVANCE"] = ("Kiến nghị", "DC2626"), ["OPINION"] = ("Ý kiến", "6366F1"), ["NONE"] = ("Chưa phân loại (đơn cũ)", "CBD5E1") };
        var tRows = ((dj["byType"] as JArray) ?? new JArray()).Select(x => (nm: typeMap.TryGetValue(x["key"]?.Value<string>() ?? "", out var v) ? v : (x["key"]?.Value<string>() ?? "", "94A3B8"), n: I(x["count"]))).ToList();
        var tType = Table(32, new[] { "Loại báo cáo", "Số hội thoại" }, tRows.Select(x => new object[] { x.nm.Item1, x.n }).ToList());
        if (tType != null) Place(new ExcelChartBuilder.Spec { Title = "Loại báo cáo", Kind = ExcelChartBuilder.Kind.Pie, CatRef = tType(0), Series = { new() { Name = "Hội thoại", ValRef = tType(1) } }, PointColors = tRows.Select(x => x.nm.Item2).ToArray() });

        var sevCol = new Dictionary<string, string> { ["LOW"] = "6366F1", ["HIGH"] = "F59E0B", ["ZT"] = "DC2626", ["NONE"] = "94A3B8" };
        var sRows = ((dj["bySeverity"] as JArray) ?? new JArray()).Select(x => (nm: x["name"]?.Value<string>() ?? (x["key"]?.Value<string>() == "NONE" ? "Chưa phân loại (đơn cũ)" : x["key"]?.Value<string>() ?? ""), col: sevCol.TryGetValue(x["key"]?.Value<string>() ?? "", out var cc) ? cc : "94A3B8", n: I(x["count"]))).ToList();
        var tSev = Table(35, new[] { "Mức độ", "Số hội thoại" }, sRows.Select(x => new object[] { x.nm, x.n }).ToList());
        if (tSev != null) Place(new ExcelChartBuilder.Spec { Title = "Mức độ", Kind = ExcelChartBuilder.Kind.Pie, CatRef = tSev(0), Series = { new() { Name = "Hội thoại", ValRef = tSev(1) } }, PointColors = sRows.Select(x => x.col).ToArray() });

        // ── Theo chủ đề (top 10) ──
        var topicTop = result.byTopic.OrderByDescending(x => x.total).Take(10).Select(x => new object[] { x.topicName ?? x.topicCd, x.total, x.cntClosed }).ToList();
        var tTop = Table(38, new[] { "Chủ đề", "Tổng", "Đã đóng" }, topicTop);
        if (tTop != null) Place(new ExcelChartBuilder.Spec { Title = "Hội thoại theo chủ đề (top 10)", Kind = ExcelChartBuilder.Kind.Bar, CatRef = tTop(0), Series = {
            new() { Name = "Tổng", ValRef = tTop(1), Color = "6366F1", Labels = true }, new() { Name = "Đã đóng", ValRef = tTop(2), Color = "16A34A" } } });

        // ── Người tham gia xử lý (top 10, nhãn = mã nhân sự vì tên dùng font riêng không hiện được trong biểu đồ) ──
        var hrTop = result.byHr.OrderByDescending(x => x.handled).Take(10).Select(x => new object[] { x.hrCd ?? "", x.handled, x.cntClosed }).ToList();
        var tHr = Table(42, new[] { "Mã NS", "Tham gia", "Đã đóng" }, hrTop);
        if (tHr != null) Place(new ExcelChartBuilder.Spec { Title = "Người tham gia xử lý (top 10)", Kind = ExcelChartBuilder.Kind.Bar, CatRef = tHr(0), Series = {
            new() { Name = "Tham gia", ValRef = tHr(1), Color = "6366F1", Labels = true }, new() { Name = "Đã đóng", ValRef = tHr(2), Color = "16A34A" } } });

        ds.Columns().AdjustToContents();
        ds.SheetView.FreezeRows(1);
    }

    // Cắt danh sách dòng (mỗi tin 1 dòng) thành các khối ≤ max ký tự, cắt ở ranh giới tin; tin dài hơn max thì cắt cứng.
    private static List<string> SplitChatLines(IEnumerable<string> lines, int max)
    {
        var chunks = new List<string>(); var cur = new System.Text.StringBuilder();
        void Flush() { if (cur.Length > 0) { chunks.Add(cur.ToString()); cur.Clear(); } }
        foreach (var raw in lines)
        {
            var line = raw;
            while (line.Length > max) { Flush(); chunks.Add(line.Substring(0, max)); line = line.Substring(max); }
            if (cur.Length > 0 && cur.Length + 1 + line.Length > max) Flush();
            if (cur.Length > 0) cur.Append('\n');
            cur.Append(line);
        }
        Flush();
        if (chunks.Count == 0) chunks.Add("");
        return chunks;
    }

    // Đánh giá 3 mức (Phase 4): tỷ lệ hài lòng = (Hài lòng + chưa đánh giá) / tổng đã đóng — chưa đánh giá tính Hài lòng (quy tắc khách)
    private static double? SatRate(int satisfied, int unrated, int closed) => closed > 0 ? (satisfied + unrated) * 100.0 / closed : (double?)null;
    private static string SatText(int satisfied, int normal, int notSatisfied, int unrated, int closed, double? avg = null)
        => closed <= 0 ? "—" : $"{(avg.HasValue ? "★" + Math.Round(avg.Value, 1) + " · " : "")}{Math.Round((satisfied + unrated) * 100.0 / closed)}% (Hài lòng {satisfied} + chưa đánh giá {unrated} · Bình thường {normal} · Không hài lòng {notSatisfied})";

    // 6 cột đánh giá: Sao TB (/5), số lượt chấm, Hài lòng, Bình thường, Không hài lòng, Chưa đánh giá, % hài lòng
    private static void RatingCols(IXLWorksheet ws, int row, int col, double? avg, int sat, int normal, int notSat, int unrated, int closed)
    {
        if (avg.HasValue) ws.Cell(row, col).Value = Math.Round(avg.Value, 2); else ws.Cell(row, col).Value = "—";
        ws.Cell(row, col + 1).Value = sat + normal + notSat;
        ws.Cell(row, col + 2).Value = sat;
        ws.Cell(row, col + 3).Value = normal;
        ws.Cell(row, col + 4).Value = notSat;
        ws.Cell(row, col + 5).Value = unrated;
        var sr = SatRate(sat, unrated, closed);
        if (sr.HasValue) ws.Cell(row, col + 6).Value = Math.Round(sr.Value, 1) + "%"; else ws.Cell(row, col + 6).Value = "—";
    }

    private bool CanViewReport => IsAdmin || CurrentUser.HasRole("CSR");

    // Catalog loại kiến nghị (Topics): Admin + CSR (khách yêu cầu CSR tự chỉnh mức độ / PIC của từng loại, 2026-11-06)
    private bool CanManageTopics => IsAdmin || CurrentUser.HasRole("CSR");

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
    // AJAX: Công tắc bật/tắt "Hỏi AI" — Admin tự điều khiển (CSR dùng bản tương tự ở HrInquiry,
    // yêu cầu 2026-10-19).
    // GET /AdminInquiry/GetAiConfig · POST /AdminInquiry/SetAiConfig { enabled }
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAiConfig()
        => Content(await _aiCsr.GetConfigRawAsync(), "application/json");

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAiConfig([FromBody] AdminSetAiConfigRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var payload = new { enabled = req?.Enabled ?? true, actorEmpCd = CurrentUser!.EmpCd };
        return Content(await _aiCsr.SetConfigRawAsync(payload), "application/json");
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

        bool unreadEmp = (head?["UnreadEmp"]?.Value<int>() ?? 0) > 0;
        return new InquiryMessagesResponse { success = true, inquiry = inquiry, messages = MapAiMessages(msgs, id, unreadEmp) };
    }

    // unreadEmp: cờ ở MỨC CẢ THREAD (HR_AI_CHAT.UNREAD_EMP) — bảng HR_AI_CHAT_MSG không lưu cờ đọc
    // theo từng tin, nên dùng chung cho mọi tin CSR/Admin trong thread để quyết định còn thu hồi
    // được không (đồng nhất với điều kiện check ở AiCsrController.CsrRecall).
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
        bool unreadEmp = (obj["unreadEmp"]?.Value<int>() ?? 0) > 0;
        return Json(new
        {
            success = true,
            inquiry = new { status = obj["status"]?.Value<string>() ?? "OPEN" },
            messages = MapAiMessages(msgs, id, unreadEmp)
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Thu hồi tin nhắn Admin đã gửi trong chat AI SAMHO - CSR (HR_AI_CHAT, nguồn riêng,
    // không dùng chung InquiryService.RecallAsync của HR_INQUIRY).
    // POST /AdminInquiry/AiRecall
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AiRecall([FromBody] IdRequest req)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        var payload = new { msgId = req.Id, actorEmpcd = CurrentUser?.EmpCd };
        return Content(await _aiCsr.CsrRecallRawAsync(payload), "application/json");
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
    // PAGE: Đóng hàng loạt — CHỈ ADMIN (upload Excel 1 cột ID chat, gửi 1 tin nhắn rồi đóng hết).
    // Admin đóng được BẤT KỲ hội thoại nào (closerType="ADMIN" bypass lock), khác bản HR/CSR ở
    // HrInquiryController vẫn phải tôn trọng luật ai đang phụ trách.
    // GET /AdminInquiry/BulkClose
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult BulkClose()
    {
        if (!IsAdmin) return Forbid();
        return View();
    }

    // GET /AdminInquiry/BulkCloseTemplate — file mẫu Excel (1 cột "ID chat")
    [HttpGet]
    public IActionResult BulkCloseTemplate()
    {
        if (!IsAdmin) return Forbid();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ID chat");
        var h = ws.Cell(1, 1);
        h.Value = "ID chat";
        h.Style.Font.Bold = true;
        h.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
        h.Style.Font.FontColor = XLColor.White;
        ws.Cell(2, 1).Value = "INQ-20261014-0001";
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauDongHangLoat.xlsx");
    }

    // POST /AdminInquiry/BulkCloseUpload
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCloseUpload(IFormFile file, [FromForm] string message)
    {
        if (!IsAdmin) return Json(new { success = false, message = "Không có quyền" });
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });
        if (string.IsNullOrWhiteSpace(message))
            return Json(new { success = false, message = "Vui lòng nhập nội dung tin nhắn" });

        var inquiryNos = new List<string>();
        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
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
            closerType = "ADMIN",
            message
        };
        var raw = await _inquiry.BulkCloseRawAsync(payload);
        return Content(raw, "application/json");
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
    // AJAX: Danh sách hội thoại CHƯA XỬ LÝ (STATUS='OPEN') trong kỳ, có phân trang —
    // yêu cầu 2026-10-16: "hiện thêm all dọn chat chưa dc sửa lí". Tái dùng GetReportRawAsync
    // (đã có sẵn, trả toàn bộ hội thoại trong kỳ kèm STATUS) rồi lọc + phân trang ở C#, không cần
    // thêm endpoint HR_api mới.
    // GET /AdminInquiry/GetUnresolvedList?from=YYYY-MM-DD&to=YYYY-MM-DD&page=&pageSize=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetUnresolvedList(string? from = null, string? to = null, int page = 1, int pageSize = 20)
    {
        if (!CanViewReport) return Json(new { success = false, message = "Không có quyền" });

        var rawResult = await _inquiry.GetReportRawAsync(from, to);
        if (!rawResult.success)
            return Json(new { success = false, message = rawResult.message ?? "Không tải được dữ liệu" });

        // Cũ nhất lên trước — chat chưa xử lý càng lâu càng cần ưu tiên.
        var unresolved = rawResult.data.Where(d => d.status == "OPEN" && d.hrMsgCount == 0).OrderBy(d => d.instDt).ToList();   // chưa ai phản hồi (đã có tin trả lời thì không còn "chưa xử lý")

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        int total = unresolved.Count;
        int totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

        var data = unresolved
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                id           = d.id,
                inquiryNo    = d.inquiryNo,
                topicName    = d.topicName,
                chatType     = d.chatType,
                instDt       = d.instDt,
                empCd        = d.empCd,
                empDisplay   = d.empDisplay,
                deptName     = d.deptName,
                firstMsg     = StripHtml(d.firstMsg),
                msgCount     = d.msgCount,
                assignedName = d.assignedName ?? d.assignedTo,
                picName      = d.picName,
                deadlineDt   = d.deadlineDt,
                draftPending = d.draftPending
            });

        return Json(new { success = true, total, page, pageSize, totalPages, data });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AJAX: Xuất Excel riêng danh sách hội thoại CHƯA XỬ LÝ (toàn bộ, không phân trang)
    // GET /AdminInquiry/ExportUnresolved?from=YYYY-MM-DD&to=YYYY-MM-DD
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ExportUnresolved(string? from = null, string? to = null)
    {
        if (!CanViewReport) return Forbid();

        var rawResult = await _inquiry.GetReportRawAsync(from, to);
        if (!rawResult.success)
            return BadRequest(rawResult.message ?? "Không tải được dữ liệu");

        var unresolved = rawResult.data.Where(d => d.status == "OPEN" && d.hrMsgCount == 0).OrderBy(d => d.instDt).ToList();   // chưa ai phản hồi (đã có tin trả lời thì không còn "chưa xử lý")

        using var wb = new XLWorkbook();
        BuildUnresolvedSheet(wb, unresolved);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"HoiThoaiChuaXuLy_{from}_{to}.xlsx");
    }

    // Sheet "Hướng dẫn tạo biểu đồ" (yêu cầu 2026-11-13: "chỉ người ta cách tạo chart trên Excel luôn").
    // File đã có biểu đồ Excel thật ở sheet "Biểu đồ" (sửa/đổi màu/copy sang PowerPoint được) — sheet này
    // chỉ hướng dẫn CSR tự dựng thêm biểu đồ khác từ dữ liệu có trong file.
    private static void BuildChartGuideSheet(XLWorkbook wb)
    {
        var ws = wb.Worksheets.Add("Hướng dẫn tạo biểu đồ");
        int r = 1;

        void Title(string t)
        {
            var c = ws.Cell(r, 1); c.Value = t;
            c.Style.Font.Bold = true; c.Style.Font.FontSize = 14;
            ws.Range(r, 1, r, 3).Merge(); r += 2;
        }
        void Head(string t)
        {
            var c = ws.Cell(r, 1); c.Value = t;
            c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            ws.Range(r, 1, r, 3).Merge(); r++;
        }
        void Line(string a, string b = "", string c = "")
        {
            ws.Cell(r, 1).Value = a; ws.Cell(r, 2).Value = b; ws.Cell(r, 3).Value = c;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            ws.Cell(r, 2).Style.Alignment.WrapText = true;
            ws.Cell(r, 3).Style.Alignment.WrapText = true;
            ws.Range(r, 1, r, 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            r++;
        }
        void Gap() => r++;

        Title("HƯỚNG DẪN TẠO BIỂU ĐỒ TRONG EXCEL");

        Head("1. File này ĐÃ CÓ biểu đồ sẵn — xem sheet \"Biểu đồ\"");
        Line("Sheet \"Biểu đồ\" có sẵn 13 biểu đồ Excel thật (không phải ảnh): diễn biến theo ngày, theo tháng, "
           + "chủ đề theo thời gian, công khai/ẩn danh, giới tính, thiết bị, thời gian giải quyết, loại báo cáo, "
           + "mức độ, top chủ đề, top người xử lý.");
        Line("Vì là biểu đồ thật nên bấm vào là sửa được: đổi màu, đổi loại biểu đồ, thêm/bớt số liệu, "
           + "và copy dán thẳng sang PowerPoint hoặc Word mà không bị mờ.");
        Line("Số liệu dùng để vẽ các biểu đồ đó nằm ở sheet \"Dữ liệu biểu đồ\". Sửa số ở đó thì biểu đồ tự đổi theo.");
        Gap();

        Head("2. Tự tạo biểu đồ mới — 4 bước");
        Line("Bước 1", "Mở sheet có bảng số muốn vẽ (xem bảng gợi ý ở mục 3).");
        Line("Bước 2", "Bấm vào 1 ô bất kỳ trong bảng, rồi nhấn Ctrl + Shift + * để chọn trọn bảng "
                     + "(hoặc kéo chuột chọn cả dòng tiêu đề + các dòng số).");
        Line("Bước 3", "Thẻ Insert (Chèn) → Recommended Charts (Biểu đồ đề xuất) → chọn mẫu Excel gợi ý → OK.");
        Line("Bước 4", "Bấm vào biểu đồ → dấu + bên phải để thêm Tên biểu đồ (Chart Title) và "
                     + "Nhãn số (Data Labels) cho dễ đọc khi in.");
        Gap();

        Head("3. Nên dùng loại biểu đồ nào cho bảng nào");
        Line("SHEET / BẢNG", "CÂU HỎI TRẢ LỜI ĐƯỢC", "LOẠI BIỂU ĐỒ NÊN DÙNG");
        ws.Range(r - 1, 1, r - 1, 3).Style.Font.Bold = true;
        ws.Range(r - 1, 1, r - 1, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#fef2f2");
        Line("Dashboard — bảng \"Theo ngày\"", "Ngày nào công nhân phản ánh nhiều nhất?", "Cột (Column) hoặc Đường (Line)");
        Line("Dashboard — bảng \"Loại báo cáo\"", "Kiến nghị và Ý kiến chiếm bao nhiêu phần?", "Tròn (Pie)");
        Line("Dashboard — bảng \"Mức độ\"", "Mức độ nào nhiều nhất?", "Tròn (Pie)");
        Line("Dashboard — \"Thời gian xử lý\"", "Bao nhiêu ca giải quyết nhanh / chậm?", "Cột ngang (Bar)");
        Line("Theo chủ đề", "Chủ đề nào bị phản ánh nhiều nhất?", "Cột ngang (Bar), sắp xếp giảm dần");
        Line("Theo chủ đề — cột % hài lòng", "Chủ đề nào công nhân kém hài lòng nhất?", "Cột ngang (Bar)");
        Line("Người xử lý", "Ai xử lý nhiều / hài lòng cao?", "Cột ngang (Bar). ĐỪNG chọn dòng \"TẤT CẢ nhân viên\" "
                                                            + "vào biểu đồ — đó là dòng tổng, sẽ lấn hết các cột khác");
        Line("Chi tiết đánh giá", "Phân bố số sao", "Cột (Column) sau khi dùng PivotTable đếm theo số sao");
        Gap();

        Head("4. Mẹo hay dùng");
        Line("Lọc trước khi vẽ", "Bảng nào có mũi tên lọc ở dòng tiêu đề thì lọc bớt dòng rồi hãy vẽ — "
                               + "biểu đồ sẽ chỉ hiện phần đang lọc, gọn hơn nhiều.");
        Line("Bảng tổng hợp", "Muốn tự gộp số theo chủ đề / theo tháng / theo người: chọn bảng → Insert → "
                            + "PivotTable, rồi Insert → PivotChart.");
        Line("Đưa vào báo cáo", "Bấm biểu đồ → Ctrl + C → sang PowerPoint/Word → Ctrl + V. "
                              + "Muốn giữ nguyên hình không đổi thì dán kiểu Picture (Dán đặc biệt → Hình ảnh).");
        Line("Tên tiếng Việt bị lỗi font", "Một số tên nhân viên lưu theo font cũ VNI. Nếu thấy chữ lạ, "
                                         + "chọn ô đó và đổi font thành Vnitbi__ là đọc được.");
        Gap();

        Line("Lưu ý: mọi số trong file này đều tính theo đúng khoảng ngày ghi ở sheet \"Tổng quan\". "
           + "Đổi khoảng ngày thì xuất lại file trên web, đừng sửa tay vào đây.");
        ws.Cell(r - 1, 1).Style.Font.Italic = true;
        ws.Cell(r - 1, 1).Style.Font.FontColor = XLColor.FromHtml("#64748b");

        ws.Column(1).Width = 42;
        ws.Column(2).Width = 52;
        ws.Column(3).Width = 52;
    }

    // Sheet "Chưa xử lý" — dùng cho CẢ nút xuất riêng (ExportUnresolved) và sheet trong file báo cáo
    // đầy đủ (ExportReport, yêu cầu 2026-11-13 "xuất Excel đủ thông tin như trên web"). Một chỗ duy nhất
    // để 2 bản không lệch cột nhau.
    private static void BuildUnresolvedSheet(XLWorkbook wb, List<InquiryReportRawRow> unresolved)
    {
        var ws = wb.Worksheets.Add("Chưa xử lý");
        string[] headers =
        {
            "STT", "ID chat", "Chủ đề", "Loại hội thoại", "Thời gian tạo",
            "Mã NV", "Họ và tên", "Phòng ban", "Line", "Work",
            "Tin nhắn đầu tiên", "Tổng số tin nhắn", "Người đang xử lý", "PIC", "Hạn phản hồi", "Nháp chờ duyệt"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var rw in unresolved)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = rw.inquiryNo;
            ws.Cell(row, 3).Value = rw.topicName ?? "";
            ws.Cell(row, 4).Value = rw.chatType == "ANON" ? "Ẩn danh" : "Trực tiếp";
            if (rw.instDt.HasValue) ws.Cell(row, 5).Value = rw.instDt.Value; else ws.Cell(row, 5).Value = "—";
            ws.Cell(row, 5).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";

            ws.Cell(row, 6).Value = rw.empCd ?? "";
            var nameCell = ws.Cell(row, 7);
            nameCell.Value = rw.empDisplay ?? "";
            nameCell.Style.Font.FontName = "Vnitbi__";
            ws.Cell(row, 8).Value = rw.deptName ?? "";
            ws.Cell(row, 9).Value = rw.lineName ?? "";
            ws.Cell(row, 10).Value = rw.workName ?? "";
            ws.Range(row, 8, row, 10).Style.Font.FontName = "Vnitbi__";

            ws.Cell(row, 11).Value = StripHtml(rw.firstMsg);
            ws.Cell(row, 12).Value = rw.msgCount;

            var handlerCell = ws.Cell(row, 13);
            handlerCell.Value = rw.assignedName ?? rw.assignedTo ?? "Chưa ai nhận";
            handlerCell.Style.Font.FontName = "Vnitbi__";
            ws.Cell(row, 14).Value = rw.picName ?? "";
            if (rw.deadlineDt.HasValue) { ws.Cell(row, 15).Value = rw.deadlineDt.Value; ws.Cell(row, 15).Style.DateFormat.Format = "dd/MM/yyyy"; } else ws.Cell(row, 15).Value = "";
            ws.Cell(row, 16).Value = rw.draftPending > 0 ? "Có" : "";

            row++;
        }
        if (unresolved.Count > 0)
            ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        else
            ws.Cell(2, 1).Value = "Không có hội thoại nào chưa được phản hồi trong kỳ.";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        ws.Column(11).Width = 60;
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
        AddRowOpt("Đánh giá TB (sao)",   s.avgRating,    2);
        wsSum.Cell(r, 1).Value = "Hài lòng 4–5★ (gồm chưa đánh giá)"; wsSum.Cell(r, 2).Value = s.cntSatisfied + s.cntUnrated; r++;
        wsSum.Cell(r, 1).Value = "   trong đó chưa đánh giá";    wsSum.Cell(r, 2).Value = s.cntUnrated;                r++;
        wsSum.Cell(r, 1).Value = "Bình thường 3★";                 wsSum.Cell(r, 2).Value = s.cntNormal;                 r++;
        wsSum.Cell(r, 1).Value = "Không hài lòng 1–2★";                 wsSum.Cell(r, 2).Value = s.cntNotSatisfied;           r++;
        {
            var sr = SatRate(s.cntSatisfied, s.cntUnrated, s.cntClosed);
            wsSum.Cell(r, 1).Value = "Tỷ lệ hài lòng";
            if (sr.HasValue) wsSum.Cell(r, 2).Value = Math.Round(sr.Value, 1) + "%"; else wsSum.Cell(r, 2).Value = "—";
            r++;
        }
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
        var rated = result.byHr.Where(h => h.cntClosed >= 5)
                              .OrderByDescending(h => SatRate(h.cntSatisfied, h.cntUnrated, h.cntClosed)!.Value).ToList();
        var topRated    = rated.FirstOrDefault();
        var bottomRated = rated.Count > 1 ? rated.Last() : null;

        r++;
        wsSum.Cell(r, 1).Value = "── TOP HÀI LÒNG ──";
        wsSum.Cell(r, 1).Style.Font.Bold = true;
        wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        wsSum.Cell(r, 1).Style.Font.FontColor = XLColor.White;
        wsSum.Range(r, 1, r, 2).Merge();
        r++;

        if (topRated != null)
        {
            wsSum.Cell(r, 1).Value = "🏆 Cao nhất";
            wsSum.Cell(r, 2).Value = $"{topRated.hrName} ({topRated.hrCd}) — hài lòng {Math.Round(SatRate(topRated.cntSatisfied, topRated.cntUnrated, topRated.cntClosed)!.Value)}% / {topRated.handled} hội thoại";
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
            wsSum.Cell(r, 2).Value = $"{bottomRated.hrName} ({bottomRated.hrCd}) — hài lòng {Math.Round(SatRate(bottomRated.cntSatisfied, bottomRated.cntUnrated, bottomRated.cntClosed)!.Value)}% / {bottomRated.handled} hội thoại";
            wsSum.Cell(r, 2).Style.Font.FontName = "Vnitbi__";
            wsSum.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
            r++;
        }
        else
        {
            wsSum.Cell(r, 1).Value = "📉 Thấp nhất";
            wsSum.Cell(r, 2).Value = "Chưa đủ dữ liệu (cần ≥ 2 HR, mỗi người ≥ 5 hội thoại đã đóng)";
            r++;
        }

        wsSum.Column(1).Width = 30;
        wsSum.Column(2).Width = 55;

        var chartSpecs = new List<ExcelChartBuilder.Spec>();
        JObject? dashJson = null;   // giữ lại ngoài try để sheet "Theo chủ đề" bên dưới lấy số "Lặp lại"
        // ─── Sheet Dashboard (gộp chung báo cáo + dashboard): hoạt động, thời gian giải quyết, thiết bị, case lặp lại, theo ngày ───
        try
        {
            var dj = JObject.Parse(await _inquiry.GetDashboardRawAsync(from, to));
            dashJson = dj;
            if (dj["success"]?.Value<bool>() == true)
            {
                var wsDash = wb.Worksheets.Add("Dashboard");
                int dr = 1;
                wsDash.Cell(dr, 1).Value = "Dashboard hộp thư phản ánh"; wsDash.Cell(dr, 1).Style.Font.Bold = true; wsDash.Cell(dr, 1).Style.Font.FontSize = 14; dr++;
                wsDash.Cell(dr, 1).Value = "Kỳ báo cáo: " + periodLabel; dr += 2;

                void Section(string title)
                {
                    var h = wsDash.Cell(dr, 1);
                    h.Value = title; h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White;
                    h.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
                    wsDash.Range(dr, 1, dr, 3).Merge(); dr++;
                }
                void Row(string label, object? value, string? note = null)
                {
                    wsDash.Cell(dr, 1).Value = label;
                    if (value is int iv) wsDash.Cell(dr, 2).Value = iv;
                    else if (value is double dv) wsDash.Cell(dr, 2).Value = Math.Round(dv, 1);
                    else wsDash.Cell(dr, 2).Value = value?.ToString() ?? "—";
                    wsDash.Cell(dr, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    if (note != null) wsDash.Cell(dr, 3).Value = note;
                    dr++;
                }
                static int I(JToken? t) => t?.Type == JTokenType.Null || t == null ? 0 : t.Value<int>();
                static double? D(JToken? t) => t == null || t.Type == JTokenType.Null ? null : t.Value<double>();
                static string Hrs(double? h) => h == null ? "—" : h < 1 ? $"{Math.Round(h.Value * 60)} phút" : h < 48 ? $"{Math.Round(h.Value, 1)} giờ" : $"{Math.Round(h.Value / 24, 1)} ngày";

                var ov = dj["overview"]!; var rs = dj["resolve"]!; var fr = dj["firstResponse"]!;
                Section("Hoạt động");
                Row("Người dùng hoạt động", I(dj["activeUsers"]));
                Row("Lượt liên hệ (tin nhắn công nhân gửi)", I(ov["empMsgs"]));
                Row("Hội thoại mới", I(ov["newConv"]), $"{I(ov["newDirect"])} công khai ({Math.Round(I(ov["newDirect"]) * 100.0 / Math.Max(1, I(ov["newConv"])), 1)}%) · {I(ov["newAnon"])} ẩn danh ({Math.Round(I(ov["newAnon"]) * 100.0 / Math.Max(1, I(ov["newConv"])), 1)}%)");
                Row("Hội thoại đang mở (hiện tại)", I(ov["openNow"]));
                Row("Quá hạn phản hồi đang mở (hiện tại)", I(ov["overdueNow"]));
                dr++;

                var pop = dj["population"]!; var cst = dj["caseStats"]!;
                string P(int a, int b) => b > 0 ? $"{Math.Round(a * 100.0 / b, 1)}%" : "—";
                // Khớp đúng khối trên web (yêu cầu CSR 2026-11-12): Đã dùng + Nghỉ việc trong kỳ + Chưa dùng = Tổng nhân viên.
                // "Mới vào làm" để riêng, KHÔNG nằm trong phép cộng vì đã thuộc nhóm đang làm.
                Section("Mức độ sử dụng My Samho");
                int hc       = I(pop["headcount"]);
                int used     = I(pop["loggedIn"]);
                int notUsed  = Math.Max(0, hc - used);
                int resigned = I(dj["accounts"]?["resigned"]);
                int popTotal = hc + resigned;
                Row("TỔNG NHÂN VIÊN", popTotal, $"{hc} đang làm + {resigned} nghỉ việc trong kỳ");
                Row("Đã dùng My Samho (đã đăng nhập)", used, P(used, popTotal));
                Row("Nghỉ việc (trong kỳ)", resigned, $"{P(resigned, popTotal)} · {I(dj["accounts"]?["resignedLocked"])} người đã khóa My Samho");
                Row("Chưa dùng (đang làm)", notUsed, $"{P(notUsed, popTotal)} · {I(pop["noAccount"])} chưa có tài khoản, {I(pop["acctLocked"])} đang khóa, {I(pop["neverLogin"])} chưa đăng nhập lần nào");
                Row("Cộng kiểm tra", used + resigned + notUsed, used + resigned + notUsed == popTotal ? "Khớp tổng nhân viên" : "LỆCH — cần kiểm tra");
                Row("Nhân viên mới vào làm (trong kỳ)", I(dj["accounts"]?["hired"]), $"{I(dj["accounts"]?["hiredUsed"])} người đã dùng My Samho · đã nằm trong nhóm đang làm");
                Row("Đã liên hệ hộp thư trong kỳ", I(dj["activeUsers"]), P(I(dj["activeUsers"]), hc));
                Row("Dùng trên điện thoại (app hoặc web điện thoại)", I(pop["onMobile"]), P(I(pop["onMobile"]), hc));
                Row("Đã đăng ký app (nhận thông báo)", I(pop["appRegistered"]), P(I(pop["appRegistered"]), hc));
                dr++;

                Section("Hội thoại tạo trong kỳ");
                int nCases = I(cst["cases"]);
                Row("Số hội thoại", nCases);
                Row("Hội thoại / người", I(dj["activeUsers"]) > 0 ? Math.Round(I(ov["newDirect"]) / (double)I(dj["activeUsers"]), 2) : (object)"—");
                Row("Tin nhắn / hội thoại", D(cst["avgMsgPerCase"]) ?? (object)"—");
                Row("Mới (chưa ai nhận)", I(cst["open"]), P(I(cst["open"]), nCases));
                Row("Đang xử lý (đã có người phụ trách)", I(cst["inProgress"]), P(I(cst["inProgress"]), nCases));
                Row("Đã đóng", I(cst["resolved"]), P(I(cst["resolved"]), nCases));
                dr++;

                Section("Chủ đề nhiều nhất");
                foreach (var t in (dj["topBottom"]?["top"] as JArray) ?? new JArray()) Row(t["topicName"]?.Value<string>() ?? t["topicCd"]?.Value<string>(), I(t["count"]));
                Section("Chủ đề ít nhất (trong số chủ đề có phát sinh)");
                foreach (var t in (dj["topBottom"]?["bottom"] as JArray) ?? new JArray()) Row(t["topicName"]?.Value<string>() ?? t["topicCd"]?.Value<string>(), I(t["count"]));
                dr++;

                Section("Giới tính người liên hệ (chỉ hội thoại công khai)");
                {
                    var asx = ((dj["activeBySex"] as JArray) ?? new JArray()).ToDictionary(x => x["sex"]!.Value<string>()!, x => I(x["count"]));
                    var hsx = ((dj["headBySex"] as JArray) ?? new JArray()).ToDictionary(x => x["sex"]!.Value<string>()!, x => I(x["count"]));
                    int aTot = I(dj["activeUsers"]), hTot2 = hsx.Values.Sum();
                    foreach (var (k, nm) in new[] { ("F", "Nữ"), ("M", "Nam"), ("?", "Không rõ") })
                        if (asx.ContainsKey(k) || k != "?")
                            Row(nm, asx.TryGetValue(k, out var av) ? av : 0, $"{P(asx.TryGetValue(k, out var av2) ? av2 : 0, aTot)} người liên hệ · toàn bộ nhân viên {P(hsx.TryGetValue(k, out var hv) ? hv : 0, hTot2)}");
                }
                dr++;

                Section("Thời gian xử lý");
                Row("Thời gian giải quyết TB", Hrs(D(rs["avgHours"])), $"{I(rs["closed"])} hội thoại đóng trong kỳ");
                Row("Thời gian giải quyết trung vị", Hrs(D(rs["medianHours"])));
                Row("Thời gian phản hồi đầu TB", Hrs(D(fr["avgHours"])), $"{I(fr["answered"])} hội thoại đã có phản hồi");
                Row("Thời gian phản hồi đầu trung vị", Hrs(D(fr["medianHours"])));
                var bk = rs["buckets"] as JArray ?? new JArray();
                string[] bl = { "Giải quyết dưới 1 giờ", "Giải quyết 1 – 4 giờ", "Giải quyết 4 – 24 giờ", "Giải quyết 1 – 3 ngày", "Giải quyết trên 3 ngày" };
                for (int i = 0; i < bl.Length && i < bk.Count; i++) Row(bl[i], bk[i].Value<int>());
                int wd = I(rs["withDeadline"]);
                Row("Đúng hạn phản hồi", wd > 0 ? $"{Math.Round(I(rs["onTime"]) * 100.0 / wd)}%" : "—", wd > 0 ? $"{I(rs["onTime"])}/{wd} hội thoại có hạn" : "Chưa có hội thoại có hạn đã đóng");
                foreach (var o in (dj["overdueByPic"] as JArray) ?? new JArray())
                    Row("Quá hạn đang mở — " + o["pic"]?.Value<string>(), I(o["count"]));
                dr++;

                Section("Thiết bị của người dùng hoạt động (chỉ hội thoại công khai)");
                var devMap = new Dictionary<string, string> { ["APP_ANDROID"] = "App Android", ["APP_IOS"] = "App iOS", ["WEB_MOBILE"] = "Web trên điện thoại", ["WEB_DESKTOP"] = "Web máy tính", ["UNKNOWN"] = "Không rõ" };
                var devs = (dj["devices"] as JArray) ?? new JArray();
                int devTotal = devs.Sum(x => I(x["count"]));
                foreach (var dv in devs)
                {
                    var k = dv["device"]?.Value<string>() ?? "";
                    Row(devMap.TryGetValue(k, out var nm) ? nm : k, I(dv["count"]), devTotal > 0 ? $"{Math.Round(I(dv["count"]) * 100.0 / devTotal)}%" : null);
                }
                dr++;

                Section("Case lặp lại (cùng nhân viên, cùng chủ đề, từ 2 hội thoại — chỉ công khai)");
                Row("Số người", I(dj["repeat"]?["employees"])); Row("Số hội thoại", I(dj["repeat"]?["cases"]));
                foreach (var t in (dj["repeat"]?["topics"] as JArray) ?? new JArray())
                    Row("   " + (t["topicName"]?.Value<string>() ?? t["topicCd"]?.Value<string>()), I(t["cases"]), $"{I(t["employees"])} người");
                dr++;

                Section("Loại báo cáo");
                var typeMap = new Dictionary<string, string> { ["GRIEVANCE"] = "Kiến nghị", ["OPINION"] = "Ý kiến", ["NONE"] = "Chưa phân loại (đơn cũ)" };
                foreach (var t in (dj["byType"] as JArray) ?? new JArray())
                {
                    // Kèm % đã đóng riêng từng loại (yêu cầu CSR 2026-11-12)
                    int nAll = I(t["count"]), nClosed = I(t["closed"]);
                    Row(typeMap.TryGetValue(t["key"]?.Value<string>() ?? "", out var tn) ? tn : t["key"]?.Value<string>(), nAll,
                        nAll > 0 ? $"đã đóng {P(nClosed, nAll)} ({nClosed}/{nAll})" : null);
                }
                dr++;
                Section("Mức độ");
                foreach (var t in (dj["bySeverity"] as JArray) ?? new JArray())
                    Row(t["name"]?.Value<string>() ?? ((t["key"]?.Value<string>() == "NONE") ? "Chưa phân loại (đơn cũ)" : t["key"]?.Value<string>()), I(t["count"]));
                dr++;

                Section("Theo ngày");
                wsDash.Cell(dr, 1).Value = "Ngày"; wsDash.Cell(dr, 2).Value = "Hội thoại mới"; wsDash.Cell(dr, 3).Value = "Tin nhắn công nhân";
                wsDash.Range(dr, 1, dr, 3).Style.Font.Bold = true; dr++;
                var dailyA = ((dj["daily"] as JArray) ?? new JArray()).ToDictionary(x => x["date"]!.Value<string>()!, x => I(x["count"]));
                var dailyB = ((dj["dailyMsgs"] as JArray) ?? new JArray()).ToDictionary(x => x["date"]!.Value<string>()!, x => I(x["count"]));
                foreach (var day in dailyA.Keys.Union(dailyB.Keys).OrderBy(x => x))
                {
                    wsDash.Cell(dr, 1).Value = day;
                    wsDash.Cell(dr, 2).Value = dailyA.TryGetValue(day, out var a1) ? a1 : 0;
                    wsDash.Cell(dr, 3).Value = dailyB.TryGetValue(day, out var b1) ? b1 : 0;
                    dr++;
                }
                // Chuỗi theo tuần/tháng: loại báo cáo + người liên hệ mới / cộng dồn
                {
                    var sr = dj["series"]!; string gr = sr["granularity"]?.Value<string>() ?? "week";
                    dr++; Section("Theo " + (gr == "month" ? "tháng" : gr == "day" ? "ngày" : "tuần") + " — loại báo cáo và người liên hệ");
                    string[] sh = { gr == "month" ? "Tháng" : gr == "day" ? "Ngày" : "Tuần (từ thứ Hai)", "Kiến nghị", "Ý kiến", "Chưa phân loại", "Người liên hệ mới", "Cộng dồn" };
                    for (int i = 0; i < sh.Length; i++) wsDash.Cell(dr, i + 1).Value = sh[i];
                    wsDash.Range(dr, 1, dr, sh.Length).Style.Font.Bold = true; dr++;
                    var byType = (sr["byType"] as JArray) ?? new JArray();
                    var newC = (sr["newContacts"] as JArray) ?? new JArray();
                    var buckets = byType.Select(x => x["bucket"]!.Value<string>()!).Union(newC.Select(x => x["bucket"]!.Value<string>()!)).Distinct().OrderBy(x => x).ToList();
                    int cum = 0;
                    foreach (var bkt in buckets)
                    {
                        int G(string key) => byType.Where(x => x["bucket"]?.Value<string>() == bkt && x["key"]?.Value<string>() == key).Sum(x => I(x["count"]));
                        int nc = newC.Where(x => x["bucket"]?.Value<string>() == bkt).Sum(x => I(x["count"])); cum += nc;
                        wsDash.Cell(dr, 1).Value = bkt; wsDash.Cell(dr, 2).Value = G("GRIEVANCE"); wsDash.Cell(dr, 3).Value = G("OPINION");
                        wsDash.Cell(dr, 4).Value = G("NONE"); wsDash.Cell(dr, 5).Value = nc; wsDash.Cell(dr, 6).Value = cum; dr++;
                    }
                }
                wsDash.Column(4).Width = 16; wsDash.Column(5).Width = 18; wsDash.Column(6).Width = 12;
                BuildChartSheets(wb, dj, result, chartSpecs);
                wsDash.Column(1).Width = 44; wsDash.Column(2).Width = 18; wsDash.Column(3).Width = 52;
            }
        }
        catch { /* Dashboard lỗi không được làm hỏng cả file báo cáo */ }

        // ─── Sheet 2: Theo chủ đề ────────────────────────────────────────────
        var wsTopic = wb.Worksheets.Add("Theo chủ đề");
        // Cột "Lặp lại" bổ sung 2026-11-13 cho khớp bảng trên web: cùng nhân viên + cùng chủ đề, từ 2 hội thoại
        // trong kỳ (CHỈ hội thoại công khai — ẩn danh không nối được với người). Nguồn: dashboard repeat.topics.
        string[] topicHeaders = { "STT", "Chủ đề", "Tổng", "Đang mở", "Đã đóng", "Lặp lại (hội thoại)", "Lặp lại (người)", "Sao TB (/5)", "Số lượt chấm", "Hài lòng (4-5 sao)", "Bình thường (3 sao)", "Không hài lòng (1-2 sao)", "Chưa đánh giá", "% hài lòng" };
        var repeatByTopic = ((dashJson?["repeat"]?["topics"] as JArray) ?? new JArray())
            .Where(x => x["topicCd"]?.Value<string>() != null)
            .GroupBy(x => x["topicCd"]!.Value<string>()!)
            .ToDictionary(g => g.Key, g => (cases: g.Sum(x => x["cases"]?.Value<int>() ?? 0),
                                            emps:  g.Sum(x => x["employees"]?.Value<int>() ?? 0)));
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
            if (repeatByTopic.TryGetValue(t.topicCd ?? "", out var rp))
            {
                wsTopic.Cell(row, 6).Value = rp.cases;
                wsTopic.Cell(row, 7).Value = rp.emps;
            }
            else { wsTopic.Cell(row, 6).Value = 0; wsTopic.Cell(row, 7).Value = 0; }
            RatingCols(wsTopic, row, 8, t.avgRating, t.cntSatisfied, t.cntNormal, t.cntNotSatisfied, t.cntUnrated, t.cntClosed);
            row++;
        }
        if (result.byTopic.Count > 0)
            wsTopic.Range(1, 1, 1, topicHeaders.Length).SetAutoFilter();
        wsTopic.Columns().AdjustToContents();

        // ─── Sheet 3: Workload ────────────────────────────────────────────
        var wsHr = wb.Worksheets.Add("Người xử lý");
        string[] hrHeaders = { "STT", "Mã NS", "Họ và tên", "Tham gia", "Đã đóng", "Sao TB (/5)", "Số lượt chấm", "Hài lòng (4-5 sao)", "Bình thường (3 sao)", "Không hài lòng (1-2 sao)", "Chưa đánh giá", "% hài lòng", "TG xử lý TB (phút)" };
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
            RatingCols(wsHr, row, 6, h.avgRating, h.cntSatisfied, h.cntNormal, h.cntNotSatisfied, h.cntUnrated, h.cntClosed);
            if (h.avgHandleMin.HasValue) wsHr.Cell(row, 13).Value = Math.Round(h.avgHandleMin.Value, 1);
            else                          wsHr.Cell(row, 13).Value = "—";
            row++;
        }
        // Dòng tổng "TẤT CẢ nhân viên" (khớp dòng tfoot trên web, yêu cầu 2026-11-13).
        // Lấy từ summary toàn kỳ, KHÔNG cộng các dòng trên: hội thoại NV tự đóng không có ai trong HR/CSR
        // tham gia nên không nằm ở dòng nào, còn hội thoại nhiều người xử lý thì bị đếm ở nhiều dòng.
        {
            var tr = wsHr.Row(row);
            wsHr.Cell(row, 1).Value = "";
            wsHr.Cell(row, 2).Value = "";
            wsHr.Cell(row, 3).Value = "TẤT CẢ nhân viên";
            wsHr.Cell(row, 4).Value = s.total;
            wsHr.Cell(row, 5).Value = s.cntClosed;
            RatingCols(wsHr, row, 6, s.avgRating, s.cntSatisfied, s.cntNormal, s.cntNotSatisfied, s.cntUnrated, s.cntClosed);
            if (s.avgHandleMin.HasValue) wsHr.Cell(row, 13).Value = Math.Round(s.avgHandleMin.Value, 1);
            else                          wsHr.Cell(row, 13).Value = "—";
            tr.Style.Font.Bold = true;
            tr.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
            row++;
            wsHr.Cell(row, 3).Value = "Dòng TẤT CẢ tính trên toàn bộ hội thoại trong kỳ (gồm cả hội thoại NV tự đóng, không ai trong HR/CSR tham gia) nên không bằng tổng các dòng trên.";
            wsHr.Cell(row, 3).Style.Font.Italic = true;
            wsHr.Cell(row, 3).Style.Font.FontColor = XLColor.FromHtml("#64748b");
        }
        if (result.byHr.Count > 0)
            wsHr.Range(1, 1, 1, hrHeaders.Length).SetAutoFilter();
        wsHr.Columns().AdjustToContents();

        // ─── Sheet: Chưa xử lý — trên web là mục "Hội thoại chưa có phản hồi"; trước đây chỉ có ở nút
        // xuất riêng nên file báo cáo thiếu mục này (bổ sung 2026-11-13). Cùng điều kiện lọc với web.
        BuildUnresolvedSheet(wb, rawResult.data.Where(d => d.status == "OPEN" && d.hrMsgCount == 0).OrderBy(d => d.instDt).ToList());

        // Sheet "Raw Data" (chi tiết thô từng hội thoại) đã bỏ theo yêu cầu 2026-10-16 — dữ liệu
        // thô giờ xem qua danh sách "Chưa xử lý" riêng (ExportUnresolved) + sheet "Nội dung chat"
        // bên dưới, tránh file Excel phình to không cần thiết.

        // ─── Sheet 4: Chi tiết đánh giá (chỉ hội thoại đã được NV đánh giá) ──
        var wsRating = wb.Worksheets.Add("Chi tiết đánh giá");
        string[] ratingHeaders =
        {
            "STT", "ID chat", "Ngày đóng", "Chủ đề",
            "Mã NV", "Họ và tên", "Phòng ban", "Người xử lý",
            "Số sao", "Lý do / nhận xét"
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

            wsRating.Cell(row, 9).Value = $"{rw.rating} sao — {HR_web.Helpers.InquiryRatingHelper.Label(rw.rating)}";
            wsRating.Cell(row, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            wsRating.Cell(row, 10).Value = StripHtml(rw.ratingNote);

            row++;
        }
        if (rawResult.data.Any(d => d.rating.HasValue))
            wsRating.Range(1, 1, 1, ratingHeaders.Length).SetAutoFilter();
        wsRating.SheetView.FreezeRows(1);
        wsRating.Columns().AdjustToContents();
        wsRating.Column(10).Width = 60;

        // ─── Sheet 5: Nội dung chat (toàn bộ tin nhắn từng hội thoại — yêu cầu 2026-09-21,
        // HR/CSR cần xem lại NV nhắn gì, ai phản hồi lúc mấy giờ, ai bấm kết thúc) ─────
        var msgResult  = await _inquiry.GetReportMessagesAsync(from, to);
        var infoById   = rawResult.data.ToDictionary(d => d.id, d => d);
        // Mỗi ID chat = 1 DÒNG: toàn bộ nội dung trong 1 ô, tên người gửi trong 1 ô (yêu cầu CSR 2026-11-09)
        var wsChat     = wb.Worksheets.Add("Nội dung chat");
        // Yêu cầu 2026-11-13: sheet này xuất kèm PHÂN LOẠI (loại báo cáo / mức độ / phòng phụ trách / hạn)
        // và ĐÁNH GIÁ của từng hội thoại, để CSR không phải mở thêm sheet khác đối chiếu.
        // Thứ tự cột dưới đây khớp đúng thứ tự ghi ô bên dưới (dùng biến đếm cc, đừng chèn cột giữa mà không sửa cả 2 chỗ).
        string[] chatHeaders =
        {
            "ID chat", "Tiêu đề trò chuyện", "Chủ đề", "Loại báo cáo", "Mức độ", "Phòng phụ trách", "Hình thức",
            "Mã NV", "Họ và tên", "Giới tính", "Thời gian tạo", "Hạn phản hồi", "Trạng thái", "Số tin",
            "Tên người gửi (HR/CSR)", "Nội dung", "Bên nhân viên", "Bên trả lời (HR/CSR)",
            "Người kết thúc", "TG kết thúc", "Thời gian xử lý (giờ)",
            "Số sao", "Mức hài lòng", "Nhận xét đánh giá"
        };
        // Yêu cầu CSR 2026-11-13: ngoài ô "Nội dung" gộp cả 2 bên, tách thêm 2 ô chỉ tin NV và chỉ tin HR/CSR.
        const int ChatColContent = 16;   // "Nội dung" (gộp)
        const int ChatColEmp     = 17;   // "Bên nhân viên"
        const int ChatColStaff   = 18;   // "Bên trả lời (HR/CSR)"
        for (int i = 0; i < chatHeaders.Length; i++)
        {
            var c = wsChat.Cell(1, i + 1);
            c.Value = chatHeaders[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        const int CellMax = 32000;   // giới hạn 32.767 ký tự / ô của Excel
        row = 2;
        foreach (var grp in msgResult.data.GroupBy(x => x.inquiryId).OrderBy(g => infoById.TryGetValue(g.Key, out var ii) ? ii.instDt : null).ThenBy(g => g.Key))
        {
            infoById.TryGetValue(grp.Key, out var info);
            var msgsOfChat = grp.OrderBy(x => x.sentDt).ToList();

            int cc = 0;
            wsChat.Cell(row, ++cc).Value = info?.inquiryNo ?? grp.Key.ToString();

            var subjectCell = wsChat.Cell(row, ++cc);
            subjectCell.Value = info?.subject ?? "";
            subjectCell.Style.Font.FontName = "Vnitbi__";

            wsChat.Cell(row, ++cc).Value = info?.topicName ?? "";

            // ── Phân loại hội thoại
            wsChat.Cell(row, ++cc).Value = info?.reportType switch
            {
                "GRIEVANCE" => "Kiến nghị",
                "OPINION"   => "Ý kiến",
                _           => "Chưa phân loại"
            };
            wsChat.Cell(row, ++cc).Value = info?.severityName ?? "Chưa phân loại";
            wsChat.Cell(row, ++cc).Value = info?.picName ?? "Chưa gán";
            wsChat.Cell(row, ++cc).Value = info?.chatType == "ANON" ? "Ẩn danh" : "Công khai";

            wsChat.Cell(row, ++cc).Value = info?.empCd ?? "";

            var nameCell = wsChat.Cell(row, ++cc);
            nameCell.Value = info?.empDisplay ?? "";
            nameCell.Style.Font.FontName = "Vnitbi__";

            wsChat.Cell(row, ++cc).Value = info?.sex == "F" ? "Nữ" : info?.sex == "M" ? "Nam" : "";

            var instCell = wsChat.Cell(row, ++cc);
            if (info?.instDt != null) { instCell.Value = info.instDt.Value; instCell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; }

            var dlCell = wsChat.Cell(row, ++cc);
            if (info?.deadlineDt != null) { dlCell.Value = info.deadlineDt.Value; dlCell.Style.DateFormat.Format = "dd/MM/yyyy"; }
            else dlCell.Value = "—";

            wsChat.Cell(row, ++cc).Value = info?.status == "CLOSED" ? "Đã đóng" : info?.status == "OPEN" ? "Đang mở" : (info?.status ?? "");
            wsChat.Cell(row, ++cc).Value = msgsOfChat.Count;

            // Tên người gửi phía HR/CSR (font riêng Vnitbi__ nên để ô riêng, không trộn vào nội dung)
            var staff = msgsOfChat.Where(x => x.senderType == "HR" && !string.IsNullOrWhiteSpace(x.senderName)).Select(x => x.senderName!.Trim()).Distinct().ToList();
            var senderNamesCell = wsChat.Cell(row, ++cc);
            senderNamesCell.Value = string.Join("\n", staff);
            senderNamesCell.Style.Font.FontName = "Vnitbi__";

            // Nội dung: mỗi tin 1 dòng "[dd/MM HH:mm] Nhân viên|HR/CSR|Hệ thống: nội dung"; quá giới hạn ô thì sang DÒNG TIẾP THEO (cùng ID chat)
            string MsgBody(InquiryReportMsgRow m)
                => m.msgType switch { "IMAGE" => "[Hình ảnh]", "FILE" => "[Tệp đính kèm]", _ => StripHtml(m.content) };
            string MsgStamp(InquiryReportMsgRow m) => m.sentDt?.ToString("dd/MM HH:mm") ?? "—";

            var lines = msgsOfChat.Select(m =>
            {
                string who = m.senderType switch { "EMP" => "Nhân viên", "HR" => "HR/CSR", "SYS" => "Hệ thống", _ => m.senderType };
                return $"[{MsgStamp(m)}] {who}: {MsgBody(m)}";
            });
            // 2 ô tách riêng: chỉ tin NV / chỉ tin HR-CSR. KHÔNG ghi tên người gửi vào đây — tên người gửi dùng
            // font Vnitbi__ riêng nên phải để ở ô "Tên người gửi", trộn vào đây sẽ hiện sai font.
            var empLines   = msgsOfChat.Where(m => m.senderType == "EMP").Select(m => $"[{MsgStamp(m)}] {MsgBody(m)}");
            var staffLines = msgsOfChat.Where(m => m.senderType == "HR").Select(m => $"[{MsgStamp(m)}] {MsgBody(m)}");

            var chunks      = SplitChatLines(lines, CellMax);
            var empChunks   = SplitChatLines(empLines, CellMax);
            var staffChunks = SplitChatLines(staffLines, CellMax);

            var contentCell = wsChat.Cell(row, ++cc);
            contentCell.Value = chunks[0];
            contentCell.Style.Alignment.WrapText = true;
            senderNamesCell.Style.Alignment.WrapText = true;

            var empCell = wsChat.Cell(row, ++cc);
            empCell.Value = empChunks[0];
            empCell.Style.Alignment.WrapText = true;

            var staffCell = wsChat.Cell(row, ++cc);
            staffCell.Value = staffChunks[0];
            staffCell.Style.Alignment.WrapText = true;

            string closedByLabel = info?.closedByType switch
            {
                "HR"    => "HR",
                "CSR"   => "CSR",
                "EMP"   => "NV tự đóng",
                "ADMIN" => "Admin",
                _       => ""
            };
            var closedByCell = wsChat.Cell(row, ++cc);
            closedByCell.Value = string.IsNullOrEmpty(info?.closedByName) ? closedByLabel : $"{info!.closedByName} ({closedByLabel})";
            closedByCell.Style.Font.FontName = "Vnitbi__";

            var closedDtCell = wsChat.Cell(row, ++cc);
            if (info?.closedDt != null) { closedDtCell.Value = info.closedDt.Value; closedDtCell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; }
            else closedDtCell.Value = "—";

            // Thời gian xử lý = từ lúc NV tạo hội thoại tới lúc đóng (yêu cầu CSR 2026-11-13).
            // Để dạng SỐ GIỜ (không phải chữ "2 ngày 3 giờ") để CSR sắp xếp được, tìm ngay ca xử lý lâu nhất.
            var durCell = wsChat.Cell(row, ++cc);
            if (info?.instDt != null && info?.closedDt != null)
            {
                durCell.Value = Math.Round((info.closedDt.Value - info.instDt.Value).TotalHours, 1);
                durCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
            else durCell.Value = "—";   // chưa đóng thì chưa tính được

            // ── Đánh giá của nhân viên. Chưa đóng thì chưa tới lượt chấm; đã đóng mà bỏ qua thì ghi rõ "Chưa đánh giá"
            // (báo cáo tính nhóm này là Hài lòng, xem sheet Tổng quan) — đừng để trống kẻo CSR tưởng mất dữ liệu.
            var starCell = wsChat.Cell(row, ++cc);
            if (info?.rating != null) { starCell.Value = info.rating.Value; starCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; }
            else starCell.Value = "—";
            wsChat.Cell(row, ++cc).Value = info?.rating != null
                ? HR_web.Helpers.InquiryRatingHelper.Label(info.rating)
                : (info?.status == "CLOSED" ? "Chưa đánh giá" : "Chưa đóng");
            var noteCell = wsChat.Cell(row, ++cc);
            noteCell.Value = StripHtml(info?.ratingNote);
            noteCell.Style.Alignment.WrapText = true;

            wsChat.Range(row, 1, row, chatHeaders.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            row++;
            // Tràn ô 32.000 ký tự ở BẤT KỲ cột nội dung nào (gộp / NV / HR-CSR) đều xuống dòng kế cùng ID chat,
            // nên số dòng phụ lấy theo cột dài nhất, không chỉ theo cột gộp.
            int extraRows = Math.Max(chunks.Count, Math.Max(empChunks.Count, staffChunks.Count));
            for (int k = 1; k < extraRows; k++)   // phần nội dung tiếp theo: lặp ID chat + ghi chú, các cột khác để trống
            {
                wsChat.Cell(row, 1).Value = info?.inquiryNo ?? grp.Key.ToString();
                wsChat.Cell(row, 2).Value = $"(nội dung tiếp theo, phần {k + 1}/{extraRows})";
                wsChat.Cell(row, 2).Style.Font.Italic = true;
                void Cont(int col, List<string> src)
                {
                    if (k >= src.Count) return;
                    var c2 = wsChat.Cell(row, col); c2.Value = src[k]; c2.Style.Alignment.WrapText = true;
                }
                Cont(ChatColContent, chunks);
                Cont(ChatColEmp,     empChunks);
                Cont(ChatColStaff,   staffChunks);
                wsChat.Range(row, 1, row, chatHeaders.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                row++;
            }
        }
        if (msgResult.data.Count > 0)
            wsChat.Range(1, 1, 1, chatHeaders.Length).SetAutoFilter();
        wsChat.SheetView.FreezeRows(1);
        wsChat.Columns().AdjustToContents();
        wsChat.Column(2).Width  = 30;   // Tiêu đề
        wsChat.Column(15).Width = 28;   // Tên người gửi HR/CSR
        wsChat.Column(16).Width = 90;   // Nội dung (gộp)
        wsChat.Column(17).Width = 70;   // Bên nhân viên
        wsChat.Column(18).Width = 70;   // Bên trả lời (HR/CSR)
        wsChat.Column(24).Width = 45;   // Nhận xét đánh giá

        // ─── Sheet 6: AI SAMHO - CSR (yêu cầu 2026-10-01: "báo cáo tính luôn AI, xuất Excel luôn
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

            // ─── Sheet 7: Nội dung chat AI (toàn bộ tin nhắn từng đoạn — yêu cầu 2026-10-01, cùng
            // tinh thần sheet "Nội dung chat" của Inquiry phía trên) ─────────────────────────────
            JObject? aiMsgReport = null;
            try { aiMsgReport = JObject.Parse(await _aiCsr.ReportMessagesRawAsync(from, to)); } catch { }

            if (aiMsgReport?["success"]?.Value<bool>() == true)
            {
                // Mỗi đoạn chat AI = 1 DÒNG: toàn bộ nội dung 1 ô, tên người gửi (CSR/HR) 1 ô (yêu cầu CSR 2026-11-09)
                var wsAiChat = wb.Worksheets.Add("Nội dung chat AI");
                string[] aiChatHeaders = { "ID đoạn chat", "Câu hỏi đầu", "Mã NV", "Họ và tên", "Giới tính", "Thời gian tạo", "Số tin", "Tên người gửi (CSR/HR)", "Nội dung" };
                for (int i = 0; i < aiChatHeaders.Length; i++)
                {
                    var c = wsAiChat.Cell(1, i + 1);
                    c.Value = aiChatHeaders[i]; c.Style.Font.Bold = true;
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml("#7c3aed"); c.Style.Font.FontColor = XLColor.White;
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                int acRow = 2;
                const int AiCellMax = 32000;
                var aiGroups = ((aiMsgReport["data"] as JArray) ?? new JArray()).GroupBy(x => x["chatId"]?.Value<decimal>() ?? 0).OrderBy(g => g.Key);
                foreach (var grp in aiGroups)
                {
                    var list = grp.OrderBy(x => x["sentDt"]?.Value<DateTime?>() ?? DateTime.MinValue).ToList();
                    var first = list[0];

                    wsAiChat.Cell(acRow, 1).Value = grp.Key.ToString();
                    wsAiChat.Cell(acRow, 2).Value = first["title"]?.Value<string>() ?? "";
                    wsAiChat.Cell(acRow, 3).Value = first["empcd"]?.Value<string>() ?? "";
                    var nameCell = wsAiChat.Cell(acRow, 4); nameCell.Value = first["empName"]?.Value<string>() ?? ""; nameCell.Style.Font.FontName = "Vnitbi__";
                    string sx = first["sex"]?.Value<string>() ?? "";
                    wsAiChat.Cell(acRow, 5).Value = sx == "F" ? "Nữ" : sx == "M" ? "Nam" : "";

                    var created = first["sentDt"]?.Value<DateTime?>();
                    if (created.HasValue) { wsAiChat.Cell(acRow, 6).Value = created.Value; wsAiChat.Cell(acRow, 6).Style.DateFormat.Format = "dd/MM/yyyy HH:mm"; }
                    else wsAiChat.Cell(acRow, 6).Value = "—";
                    wsAiChat.Cell(acRow, 7).Value = list.Count;

                    var staff = list.Where(x => x["senderType"]?.Value<string>() == "CSR" && !string.IsNullOrWhiteSpace(x["senderName"]?.Value<string>()))
                                    .Select(x => x["senderName"]!.Value<string>()!.Trim()).Distinct().ToList();
                    var staffCell = wsAiChat.Cell(acRow, 8); staffCell.Value = string.Join("\n", staff); staffCell.Style.Font.FontName = "Vnitbi__"; staffCell.Style.Alignment.WrapText = true;

                    var aiLines = list.Select(m =>
                    {
                        string st = m["senderType"]?.Value<string>() ?? "";
                        string who = st switch { "EMP" => "Nhân viên", "CSR" => "CSR/HR", "AI" => "AI", _ => st };
                        bool failed = st == "AI" && m["aiFailed"]?.Value<bool>() == true;
                        return $"[{(m["sentDt"]?.Value<DateTime?>()?.ToString("dd/MM HH:mm") ?? "—")}] {who}{(failed ? " (lỗi)" : "")}: {StripHtml(m["content"]?.Value<string>())}";
                    });
                    var aiChunks = SplitChatLines(aiLines, AiCellMax);
                    var contentCell = wsAiChat.Cell(acRow, 9); contentCell.Value = aiChunks[0]; contentCell.Style.Alignment.WrapText = true;

                    wsAiChat.Range(acRow, 1, acRow, aiChatHeaders.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    acRow++;
                    for (int k = 1; k < aiChunks.Count; k++)   // nội dung quá giới hạn ô → dòng tiếp theo (cùng ID đoạn chat)
                    {
                        wsAiChat.Cell(acRow, 1).Value = grp.Key.ToString();
                        wsAiChat.Cell(acRow, 2).Value = $"(nội dung tiếp theo, phần {k + 1}/{aiChunks.Count})";
                        wsAiChat.Cell(acRow, 2).Style.Font.Italic = true;
                        var cc = wsAiChat.Cell(acRow, 9); cc.Value = aiChunks[k]; cc.Style.Alignment.WrapText = true;
                        wsAiChat.Range(acRow, 1, acRow, aiChatHeaders.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                        acRow++;
                    }
                }
                if (acRow > 2) wsAiChat.Range(1, 1, 1, aiChatHeaders.Length).SetAutoFilter();
                wsAiChat.SheetView.FreezeRows(1);
                wsAiChat.Columns().AdjustToContents();
                wsAiChat.Column(2).Width = 40;
                wsAiChat.Column(8).Width = 28;
                wsAiChat.Column(9).Width = 90;
            }
        }

        BuildChartGuideSheet(wb);   // phải gọi TRƯỚC SaveAs, thêm sheet sau khi lưu thì không vào file

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        byte[] xlsxBytes = ms.ToArray();
        // Biểu đồ Excel thật (ClosedXML không có) — lỗi thì vẫn trả file không biểu đồ, không làm hỏng báo cáo

        if (chartSpecs.Count > 0)
        {
            try { xlsxBytes = ExcelChartBuilder.AddCharts(xlsxBytes, "Biểu đồ", chartSpecs); }
            catch { }
        }

        string fileName = $"BaoCaoHoiThoai_{result.from ?? "tu"}_{result.to ?? "den"}.xlsx";

        return File(xlsxBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PAGE: Quản lý chủ đề (Admin)
    // GET /AdminInquiry/Topics
    // ─────────────────────────────────────────────────────────────────────────
    public IActionResult Topics()
    {
        if (!CanManageTopics) return Forbid();
        return View();
    }

    // /AdminInquiry/Dashboard: đã gộp chung vào trang Báo cáo (user 2026-11-06) — giữ đường dẫn cũ để bookmark không gãy.
    public IActionResult Dashboard() => RedirectToAction("Report");

    [HttpGet]
    public async Task<IActionResult> GetRepeatDetail(string? from = null, string? to = null)
    {
        if (!CanViewReport) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetRepeatDetailRawAsync(from, to), "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard(string? from = null, string? to = null)
    {
        if (!CanViewReport) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetDashboardRawAsync(from, to), "application/json");
    }

    // GET /AdminInquiry/RemindEmails — trang CSR/Admin cài người nhận email nhắc hạn theo chủ đề / phòng
    public IActionResult RemindEmails()
    {
        if (!CanManageTopics) return Forbid();
        return View();
    }

    // Xem trước hôm nay email nhắc hạn sẽ gửi cho ai (không gửi gì)
    [HttpGet]
    public async Task<IActionResult> GetReminderPreview()
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetReminderPreviewRawAsync(), "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminTopics()
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        var result = await _inquiry.GetAdminTopicsAsync();
        return Json(result);
    }

    // Danh sách mức độ (Low/High/ZT) và phòng ban (PIC) để đổ dropdown ở modal chủ đề
    [HttpGet]
    public async Task<IActionResult> GetSeverities()
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetSeveritiesRawAsync(), "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetPicDeptRoles()
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetPicDeptRolesRawAsync(), "application/json");
    }

    // Người nhận email nhắc hạn phản hồi (theo chủ đề / theo phòng PIC) — CSR/Admin tự sửa
    [HttpGet]
    public async Task<IActionResult> GetRemindTo()
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _inquiry.GetRemindToRawAsync(), "application/json");
    }

    public class RemindToSaveRequest { public string? ScopeType { get; set; } public string? ScopeKey { get; set; } public string? Emails { get; set; } }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRemindTo([FromBody] RemindToSaveRequest req)
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        if (req == null) return Json(new { success = false, message = "Dữ liệu rỗng" });
        return Content(await _inquiry.SaveRemindToRawAsync(new
        {
            scopeType = req.ScopeType, scopeKey = req.ScopeKey, emails = req.Emails, updtId = CurrentUser?.EmpCd
        }), "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveTopic([FromBody] AdminTopicSaveRequest req)
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
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
            severityCd    = req.SeverityCd,
            picDeptRoleId = req.PicDeptRoleId,
            updtId      = CurrentUser?.EmpCd
        };
        var raw = await _inquiry.SaveTopicRawAsync(payload, req.IsEdit ? req.TopicCd : null);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> ToggleTopic([FromBody] AdminTopicToggleRequest req)
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
        if (req == null || string.IsNullOrWhiteSpace(req.TopicCd))
            return Json(new { success = false, message = "Thiếu mã chủ đề" });

        var raw = await _inquiry.ToggleTopicRawAsync(req.TopicCd, req.IsActive, CurrentUser?.EmpCd);
        return Content(raw, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTopic([FromBody] AdminTopicDeleteRequest req)
    {
        if (!CanManageTopics) return Json(new { success = false, message = "Không có quyền" });
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
        public string? SeverityCd  { get; set; }
        public int?    PicDeptRoleId { get; set; }
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

    public class AdminSetAiConfigRequest
    {
        public bool Enabled { get; set; }
    }
}
