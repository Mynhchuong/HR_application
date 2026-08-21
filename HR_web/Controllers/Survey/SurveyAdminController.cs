using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Survey;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Survey;

// HR/Admin: quản lý survey (list, create, edit, preview, test, publish, pause…) + report
[Authorize(Roles = "Admin,HR,CSR")]
public class SurveyAdminController : BaseController
{
    private readonly SurveyAdminService _admin;
    private readonly SurveyReportService _report;
    private readonly DropdownService _dropdown;
    private readonly HR_web.API.ApiService _api;

    public SurveyAdminController(SurveyAdminService admin, SurveyReportService report, DropdownService dropdown, HR_web.API.ApiService api)
    {
        _admin = admin;
        _report = report;
        _dropdown = dropdown;
        _api = api;
    }

    // ─────────────────────────────────────────────
    // GET /SurveyAdmin  (default = Index — list)
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Index(string? status, string? type, string? search)
    {
        ViewBag.FilterStatus = status;
        ViewBag.FilterType   = type;
        ViewBag.FilterSearch = search;
        var list = await _admin.ListAsync(status, type, search);
        return View(list);
    }

    // ─────────────────────────────────────────────
    // GET /SurveyAdmin/Edit?id=  hoặc /SurveyAdmin/Edit
    // Tạo mới (id null) hoặc sửa (chỉ DRAFT/SCHEDULED)
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Edit(int? id)
    {
        ViewBag.Depts = await _dropdown.GetDeptAsync();

        if (id == null || id == 0)
        {
            var newVm = new SurveyModel
            {
                START_DATE     = DateTime.Today.AddDays(1),
                END_DATE       = DateTime.Today.AddDays(7),
                RECIPIENT_MODE = "ALL",
                LANG           = "VI",
                SURVEY_TYPE    = "POLL",
                STATUS         = "DRAFT",
            };
            ViewBag.IsNew = true;
            return View(newVm);
        }

        var item = await _admin.PreviewAsync(id.Value);
        if (item == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy survey";
            return RedirectToAction("Index");
        }

        var editableStatuses = new[] { "DRAFT", "SCHEDULED", "ACTIVE", "PAUSED" };
        if (!editableStatuses.Contains(item.STATUS))
        {
            TempData["ErrorMessage"] = $"Không sửa được survey ở trạng thái {item.STATUS}";
            return RedirectToAction("Index");
        }

        ViewBag.IsNew = false;
        // ACTIVE/PAUSED: chỉ sửa được tiêu đề/mô tả/ngày kết thúc/đối tượng nhận — xem SaveAsync (API).
        ViewBag.IsLimitedEdit = item.STATUS == "ACTIVE" || item.STATUS == "PAUSED";
        return View(item);
    }

    // ─────────────────────────────────────────────
    // POST /SurveyAdmin/Save  (AJAX từ wizard)
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveSurveyVm req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd;
        var (ok, msg, newId) = await _admin.SaveAsync(req);
        return Json(new { success = ok, message = msg, id = newId });
    }

    // ─────────────────────────────────────────────
    // POST /SurveyAdmin/ChangeStatus  (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus([FromBody] ChangeStatusVm req)
    {
        var (ok, msg) = await _admin.ChangeStatusAsync(req.Id, req.NewStatus, CurrentUser?.EmpCd);
        return Json(new { success = ok, message = msg });
    }

    // POST /SurveyAdmin/PublishStream — proxy stream NDJSON từ API về browser
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task PublishStream([FromBody] ChangeStatusVm req)
    {
        Response.ContentType = "application/x-ndjson";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var payload = new { ID = req.Id, NEW_STATUS = "SCHEDULED", LOGIN_USER = CurrentUser?.EmpCd };
        var upstream = await _api.PostStreamAsync("SurveyAdmin/publish-stream", payload);
        if (upstream == null || !upstream.IsSuccessStatusCode)
        {
            await Response.WriteAsync("{\"phase\":\"error\",\"message\":\"Không kết nối được server\"}\n");
            return;
        }

        using var stream = await upstream.Content.ReadAsStreamAsync();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await Response.Body.WriteAsync(buffer.AsMemory(0, read));
            await Response.Body.FlushAsync();
        }
    }

    // ─────────────────────────────────────────────
    // GET /SurveyAdmin/Preview/{id}  — HR xem trước UI làm survey
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Preview(int id)
    {
        var s = await _admin.PreviewAsync(id);
        if (s == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy survey";
            return RedirectToAction("Index");
        }
        return View(s);
    }

    // ─────────────────────────────────────────────
    // GET /SurveyAdmin/TestMode/{id} — HR làm thử (không lưu response)
    // ─────────────────────────────────────────────
    public async Task<IActionResult> TestMode(int id)
    {
        var s = await _admin.PreviewAsync(id);
        if (s == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy survey";
            return RedirectToAction("Index");
        }
        return View(s);
    }

    // ─────────────────────────────────────────────
    // POST /SurveyAdmin/TestSubmit  (AJAX từ TestMode)
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSubmit([FromBody] object payload)
    {
        var result = await _admin.TestSubmitAsync(payload);
        return Json(new { success = result != null, data = result });
    }

    // ─────────────────────────────────────────────
    // GET /SurveyAdmin/Report/{id}
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Report(int id)
    {
        var overview = await _report.GetOverviewAsync(id);
        if (overview == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy survey";
            return RedirectToAction("Index");
        }
        var vm = new SurveyReportViewModel { Overview = overview };
        if (overview.SURVEY_TYPE == "QUIZ")
            vm.Quiz = await _report.GetQuizAsync(id);
        return View(vm);
    }

    // GET /SurveyAdmin/ReportTextAnswers?qid=&page=  (AJAX)
    [HttpGet]
    public async Task<IActionResult> ReportTextAnswers(int qid, int page = 1)
    {
        var data = await _report.GetTextAnswersAsync(qid, page, 9999);
        return Json(new { success = true, data });
    }

    // GET /SurveyAdmin/ReportOptionRespondents?optId=  (AJAX)
    [HttpGet]
    public async Task<IActionResult> ReportOptionRespondents(int optId)
    {
        var data = await _report.GetOptionRespondentsAsync(optId);
        return Json(new { success = true, data });
    }

    // GET /SurveyAdmin/ReportOptionExport?optId=&optionText=
    [HttpGet]
    public async Task<IActionResult> ReportOptionExport(int optId, string? optionText)
    {
        var data = await _report.GetOptionRespondentsAsync(optId);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh sách");

        string[] headers = { "STT", "Mã NV", "Họ tên", "Phòng ban", "Dây chuyền", "Công đoạn", "Nộp lúc" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
        }

        int row = 2, stt = 1;
        foreach (var p in data)
        {
            ws.Cell(row, 1).Value = stt++;
            ws.Cell(row, 2).Value = p.EMPCD;
            ws.Cell(row, 3).Value = p.FULL_NAME ?? "";
            ws.Cell(row, 4).Value = p.DEPTCD ?? "";
            ws.Cell(row, 5).Value = p.LINECD ?? "";
            ws.Cell(row, 6).Value = p.WORKCD ?? "";
            ws.Cell(row, 7).Value = p.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            row++;
        }

        int[] widths = { 6, 14, 26, 14, 14, 14, 18 };
        for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var safeName = string.IsNullOrWhiteSpace(optionText) ? $"Option_{optId}" : optionText.Length > 30 ? optionText[..30] : optionText;
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"DapAn_{safeName}_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // GET /SurveyAdmin/Participants/5?deptcd=&linecd=&workcd=&empcd=&status=&page=
    public async Task<IActionResult> Participants(int id, string? deptcd, string? linecd,
        string? workcd, string? empcd, string? status, int page = 1)
    {
        var survey = await _admin.GetDetailAsync(id);
        if (survey == null) return NotFound();

        var pageData = await _report.GetParticipantsAsync(id, deptcd, linecd, workcd, empcd, status, page, 30);
        var depts = await _dropdown.GetDeptAsync();

        var vm = new SurveyParticipantPageViewModel
        {
            SURVEY_ID    = id,
            TITLE        = survey.TITLE,
            SURVEY_TYPE  = survey.SURVEY_TYPE,
            Page         = pageData,
            FilterDept   = deptcd,
            FilterLine   = linecd,
            FilterWork   = workcd,
            FilterEmpcd  = empcd,
            FilterStatus = status,
            Depts        = depts,
        };
        return View(vm);
    }

    // POST /SurveyAdmin/DeleteResponse  (AJAX) — xoá response bị ghi nhầm EMPCD
    // (vd quên logout tài khoản dùng chung, người sau làm survey bị gán nhầm mã NV)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteResponse([FromBody] DeleteResponseVm req)
    {
        var (ok, msg) = await _admin.DeleteResponseAsync(req.SurveyId, req.Empcd);
        return Json(new { success = ok, message = msg });
    }

    // POST /SurveyAdmin/BulkDeleteResponse  (AJAX) — HR dán danh sách EMPCD để reset làm lại hàng loạt
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteResponse([FromBody] BulkDeleteResponseVm req)
    {
        var empcds = (req.Empcds ?? "")
            .Split(new[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .Distinct()
            .ToList();

        if (empcds.Count == 0)
            return Json(new { success = false, message = "Chưa nhập mã NV nào" });

        var (ok, deletedCount, notFound, msg) = await _admin.BulkDeleteResponseAsync(req.SurveyId, empcds);
        return Json(new { success = ok, deletedCount, notFoundCount = notFound.Count, notFound, message = msg });
    }

    // GET /SurveyAdmin/ScopeCount?deptcd=X&linecd=Y&workcd=Z  (AJAX preview)
    [HttpGet]
    public async Task<IActionResult> ScopeCount(string deptcd, string? linecd, string? workcd)
    {
        var qs = $"deptcd={Uri.EscapeDataString(deptcd ?? "")}";
        if (!string.IsNullOrEmpty(linecd)) qs += $"&linecd={Uri.EscapeDataString(linecd)}";
        if (!string.IsNullOrEmpty(workcd)) qs += $"&workcd={Uri.EscapeDataString(workcd)}";
        var result = await _api.GetAsync<object>("SurveyAdmin/scope-count", qs);
        return Json(result ?? new { count = 0 });
    }

    // GET /SurveyAdmin/ParticipantAnswers?surveyId=&empcd=   (AJAX)
    [HttpGet]
    public async Task<IActionResult> ParticipantAnswers(int surveyId, string empcd)
    {
        var data = await _report.GetParticipantAnswersAsync(surveyId, empcd);
        if (data == null) return Json(new { success = false, message = "Không tìm thấy" });
        return Json(new { success = true, data });
    }

    // GET /SurveyAdmin/ParticipantExport?id=&deptcd=&linecd=&workcd=&empcd=&status=
    [HttpGet]
    public async Task<IActionResult> ParticipantExport(int id, string? deptcd, string? linecd,
        string? workcd, string? empcd, string? status)
    {
        var survey = await _admin.GetDetailAsync(id);
        if (survey == null) return NotFound();

        // Get all (pageSize=9999)
        var pageData = await _report.GetParticipantsAsync(id, deptcd, linecd, workcd, empcd, status, 1, 9999);
        bool isQuiz = survey.SURVEY_TYPE == "QUIZ";

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Danh sách");

        // Header
        int col = 1;
        ws.Cell(1, col++).Value = "STT";
        ws.Cell(1, col++).Value = "Mã NV";
        ws.Cell(1, col++).Value = "Họ tên";
        ws.Cell(1, col++).Value = "Phòng ban";
        ws.Cell(1, col++).Value = "Dây chuyền";
        ws.Cell(1, col++).Value = "Công đoạn";
        ws.Cell(1, col++).Value = "Trạng thái";
        if (isQuiz)
        {
            ws.Cell(1, col++).Value = "Điểm";
            ws.Cell(1, col++).Value = "Tối đa";
            ws.Cell(1, col++).Value = "Kết quả";
        }
        ws.Cell(1, col++).Value = "Bắt đầu";
        ws.Cell(1, col++).Value = "Thời gian nộp";

        var headerRow = ws.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        headerRow.Style.Font.FontColor = XLColor.White;

        var statusMap = new Dictionary<string, string>
        {
            ["SUBMITTED"]       = "Đã nộp",
            ["AUTO_SUBMITTED"]  = "Tự nộp (hết hạn)",
            ["ILLITERATE_SKIP"] = "Mù chữ",
            ["IN_PROGRESS"]     = "Đang làm",
            ["NOT_STARTED"]     = "Chưa làm",
        };

        int row = 2;
        int stt = 1;
        foreach (var p in pageData.ITEMS)
        {
            col = 1;
            ws.Cell(row, col++).Value = stt++;
            ws.Cell(row, col++).Value = p.EMPCD;
            ws.Cell(row, col++).Value = p.FULL_NAME ?? "";
            ws.Cell(row, col++).Value = p.DEPTCD ?? "";
            ws.Cell(row, col++).Value = p.LINECD ?? "";
            ws.Cell(row, col++).Value = p.WORKCD ?? "";
            ws.Cell(row, col++).Value = statusMap.GetValueOrDefault(p.STATUS, p.STATUS);
            if (isQuiz)
            {
                if (p.SCORE.HasValue) ws.Cell(row, col).Value = p.SCORE.Value;
                else ws.Cell(row, col).Value = "";
                col++;
                if (p.MAX_SCORE.HasValue) ws.Cell(row, col).Value = p.MAX_SCORE.Value;
                else ws.Cell(row, col).Value = "";
                col++;
                ws.Cell(row, col++).Value = p.IS_PASS == 1 ? "PASS" : p.IS_PASS == 0 ? "FAIL" : "";
            }
            ws.Cell(row, col++).Value = p.START_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            ws.Cell(row, col++).Value = p.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Survey_{id}_DanhSach_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // GET /SurveyAdmin/ReportExport?id=  → 1 file Excel gồm Overview + Quiz sheet (nếu quiz)
    // GET /SurveyAdmin/ReportExport?id=  → 1 file Excel gồm Overview + All Answers + Per-question sheets + Quiz sheet (nếu quiz)
    [HttpGet]
    public async Task<IActionResult> ReportExport(int id)
    {
        var overview = await _report.GetOverviewAsync(id);
        if (overview == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy survey";
            return RedirectToAction("Index");
        }

        var allAnswers = await _report.GetAllAnswersAsync(id);

        using var wb = new XLWorkbook();
        BuildOverviewSheet(wb, overview, allAnswers);
        BuildAllAnswersSheet(wb, overview, allAnswers);

        if (overview.SURVEY_TYPE == "QUIZ")
        {
            var quiz = await _report.GetQuizAsync(id);
            if (quiz != null) BuildQuizSheet(wb, overview, quiz);
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Survey_{id}_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private static void BuildOverviewSheet(XLWorkbook wb, SurveyReportOverviewModel o,
        List<SurveyFullAnswerRecordModel> allAnswers)
    {
        var ws = wb.Worksheets.Add("Overview");
        ws.Cell(1, 1).Value = "Survey #" + o.SURVEY_ID + " — " + o.TITLE;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        int r = 3;
        (string, object)[] rows =
        {
            ("Loại",            o.SURVEY_TYPE),
            ("Ngôn ngữ",        o.LANG),
            ("Trạng thái",      o.STATUS),
            ("Bắt đầu",         o.START_DATE?.ToString("dd/MM/yyyy") ?? ""),
            ("Kết thúc",        o.END_DATE?.ToString("dd/MM/yyyy") ?? ""),
            ("Tổng người nhận", o.TOTAL_RECIPIENTS),
            ("Đã nộp",          o.SUBMITTED_COUNT),
            ("Tự nộp (expired)",o.AUTO_SUBMIT_COUNT),
            ("Mù chữ (skip)",   o.ILLITERATE_COUNT),
            ("Đang làm",        o.IN_PROGRESS_COUNT),
            ("Chưa bắt đầu",    o.NOT_STARTED_COUNT),
        };
        foreach (var (k, v) in rows)
        {
            ws.Cell(r, 1).Value = k;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = v?.ToString() ?? "";
            r++;
        }
        ws.Column(1).Width = 22;
        ws.Column(2).Width = 40;

        // Group answers by question ID
        var qAnswersDict = allAnswers
            .GroupBy(a => a.QUESTION_ID)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Per-question aggregation sheet — dùng index để tránh duplicate sheet name
        for (int qi = 0; qi < o.QUESTIONS.Count; qi++)
        {
            var q = o.QUESTIONS[qi];
            var name = $"Q{qi + 1}";
            var qs = wb.Worksheets.Add(name);
            qs.Cell(1, 1).Value = $"Câu {qi + 1}: {q.QUESTION_TEXT}";
            qs.Cell(1, 1).Style.Font.Bold = true;
            qs.Cell(1, 1).Style.Font.FontSize = 12;

            int detailStartRow = 4;

            if (q.OPTIONS.Any())
            {
                qs.Cell(3, 1).Value = "THỐNG KÊ PHƯƠNG ÁN CHỌN";
                qs.Cell(3, 1).Style.Font.Bold = true;

                qs.Cell(4, 1).Value = "Đáp án";
                qs.Cell(4, 2).Value = "Số người chọn";
                qs.Cell(4, 3).Value = "Tỷ lệ (%)";
                qs.Cell(4, 1).Style.Font.Bold = true;
                qs.Cell(4, 2).Style.Font.Bold = true;
                qs.Cell(4, 3).Style.Font.Bold = true;
                qs.Cell(4, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                qs.Cell(4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                qs.Cell(4, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");

                int totalOptHits = Math.Max(1, q.OPTIONS.Sum(x => x.COUNT));
                int r2 = 5;
                foreach (var opt in q.OPTIONS)
                {
                    double optPct = Math.Round(opt.COUNT * 100.0 / totalOptHits, 1);
                    qs.Cell(r2, 1).Value = opt.OPTION_TEXT;
                    qs.Cell(r2, 2).Value = opt.COUNT;
                    qs.Cell(r2, 3).Value = optPct + "%";
                    r2++;
                }
                detailStartRow = r2 + 2;
            }
            else if (q.RATING_DIST != null)
            {
                qs.Cell(3, 1).Value = "THỐNG KÊ ĐÁNH GIÁ SAO";
                qs.Cell(3, 1).Style.Font.Bold = true;

                qs.Cell(4, 1).Value = "Sao";
                qs.Cell(4, 2).Value = "Số lượt đánh giá";
                qs.Cell(4, 3).Value = "Tỷ lệ (%)";
                qs.Cell(4, 1).Style.Font.Bold = true;
                qs.Cell(4, 2).Style.Font.Bold = true;
                qs.Cell(4, 3).Style.Font.Bold = true;
                qs.Cell(4, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                qs.Cell(4, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                qs.Cell(4, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");

                int totalRatingHits = Math.Max(1, q.RATING_DIST.Sum());
                for (int i = 0; i < 5; i++)
                {
                    double starPct = Math.Round(q.RATING_DIST[i] * 100.0 / totalRatingHits, 1);
                    qs.Cell(5 + i, 1).Value = (i + 1) + " sao";
                    qs.Cell(5 + i, 2).Value = q.RATING_DIST[i];
                    qs.Cell(5 + i, 3).Value = starPct + "%";
                }
                detailStartRow = 12;
            }

            // Danh sách chi tiết ai chọn đáp án nào
            qs.Cell(detailStartRow, 1).Value = "DANH SÁCH CHI TIẾT CÂU TRẢ LỜI CỦA TỪNG NHÂN VIÊN";
            qs.Cell(detailStartRow, 1).Style.Font.Bold = true;

            string[] hdr = { "STT", "Số thẻ (Mã NV)", "Họ tên", "Phòng ban", "Dây chuyền", "Công đoạn", "Đáp án / Nội dung trả lời", "Thời gian nộp" };
            int hRow = detailStartRow + 1;
            for (int i = 0; i < hdr.Length; i++)
            {
                var c = qs.Cell(hRow, i + 1);
                c.Value = hdr[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#334155");
                c.Style.Font.FontColor = XLColor.White;
            }

            int dataRow = hRow + 1;
            int stt = 1;

            if (qAnswersDict.TryGetValue(q.QUESTION_ID, out var ansList) && ansList.Count > 0)
            {
                foreach (var a in ansList)
                {
                    qs.Cell(dataRow, 1).Value = stt++;
                    qs.Cell(dataRow, 2).Value = a.EMPCD;
                    qs.Cell(dataRow, 3).Value = a.FULL_NAME ?? "";
                    qs.Cell(dataRow, 4).Value = a.DEPT_NAME ?? a.DEPTCD ?? "";
                    qs.Cell(dataRow, 5).Value = a.LINE_NAME ?? a.LINECD ?? "";
                    qs.Cell(dataRow, 6).Value = a.WORK_NAME ?? a.WORKCD ?? "";
                    qs.Cell(dataRow, 7).Value = a.ANSWER_TEXT ?? "";
                    qs.Cell(dataRow, 8).Value = a.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
                    dataRow++;
                }
            }
            else
            {
                qs.Cell(dataRow, 1).Value = "Chưa có dữ liệu trả lời";
            }

            qs.Column(1).Width = 8;
            qs.Column(2).Width = 14;
            qs.Column(3).Width = 26;
            qs.Column(4).Width = 22;
            qs.Column(5).Width = 18;
            qs.Column(6).Width = 18;
            qs.Column(7).Width = 45;
            qs.Column(8).Width = 18;
        }
    }

    private static void BuildAllAnswersSheet(XLWorkbook wb, SurveyReportOverviewModel o,
        List<SurveyFullAnswerRecordModel> allAnswers)
    {
        var ws = wb.Worksheets.Add("Tat_Ca_Cau_Tra_Loi");
        ws.Cell(1, 1).Value = "BẢNG TỔNG HỢP CÂU TRẢ LỜI TẤT CẢ CÂU HỎI — SURVEY #" + o.SURVEY_ID;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;

        var questions = o.QUESTIONS.OrderBy(q => q.DISPLAY_ORDER).ThenBy(q => q.QUESTION_ID).ToList();

        // Build header row
        int headerRow = 3;
        string[] baseHdrs = { "STT", "Số thẻ (Mã NV)", "Họ tên", "Phòng ban", "Dây chuyền", "Công đoạn" };
        for (int i = 0; i < baseHdrs.Length; i++)
        {
            var c = ws.Cell(headerRow, i + 1);
            c.Value = baseHdrs[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
        }

        for (int i = 0; i < questions.Count; i++)
        {
            var c = ws.Cell(headerRow, baseHdrs.Length + i + 1);
            c.Value = $"Q{i + 1}: {questions[i].QUESTION_TEXT}";
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
        }

        var timeCell = ws.Cell(headerRow, baseHdrs.Length + questions.Count + 1);
        timeCell.Value = "Thời gian nộp";
        timeCell.Style.Font.Bold = true;
        timeCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        timeCell.Style.Font.FontColor = XLColor.White;

        // Group by user
        var userGrouped = allAnswers
            .GroupBy(a => a.EMPCD)
            .OrderByDescending(g => g.First().SUBMIT_DT)
            .ThenBy(g => g.Key);

        int row = headerRow + 1;
        int stt = 1;
        foreach (var grp in userGrouped)
        {
            var first = grp.First();
            var qAns = grp.ToDictionary(a => a.QUESTION_ID, a => a.ANSWER_TEXT);

            ws.Cell(row, 1).Value = stt++;
            ws.Cell(row, 2).Value = first.EMPCD;
            ws.Cell(row, 3).Value = first.FULL_NAME ?? "";
            ws.Cell(row, 4).Value = first.DEPT_NAME ?? first.DEPTCD ?? "";
            ws.Cell(row, 5).Value = first.LINE_NAME ?? first.LINECD ?? "";
            ws.Cell(row, 6).Value = first.WORK_NAME ?? first.WORKCD ?? "";

            for (int i = 0; i < questions.Count; i++)
            {
                var qid = questions[i].QUESTION_ID;
                ws.Cell(row, baseHdrs.Length + i + 1).Value = qAns.GetValueOrDefault(qid) ?? "";
            }

            ws.Cell(row, baseHdrs.Length + questions.Count + 1).Value = first.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            row++;
        }

        ws.Columns().AdjustToContents(1, 100);
    }

    private static void BuildQuizSheet(XLWorkbook wb, SurveyReportOverviewModel o, SurveyReportQuizModel q)
    {
        var ws = wb.Worksheets.Add("Quiz");
        ws.Cell(1, 1).Value = "Quiz #" + o.SURVEY_ID + " — " + o.TITLE;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(3, 1).Value = "Điểm TB";  ws.Cell(3, 2).Value = q.AVG_SCORE;
        ws.Cell(4, 1).Value = "Pass";     ws.Cell(4, 2).Value = q.PASS_COUNT;
        ws.Cell(5, 1).Value = "Fail";     ws.Cell(5, 2).Value = q.FAIL_COUNT;
        ws.Range(3, 1, 5, 1).Style.Font.Bold = true;

        string[] headers = { "STT", "EmpCd", "Họ tên", "Dept", "Line", "Work", "Điểm", "Max", "Pass/Fail", "Nộp lúc" };
        int startRow = 7;
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(startRow, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
            c.Style.Font.FontColor = XLColor.White;
        }

        int row = startRow + 1;
        int stt = 1;
        foreach (var u in q.USERS)
        {
            ws.Cell(row, 1).Value = stt++;
            ws.Cell(row, 2).Value = u.EMPCD;
            ws.Cell(row, 3).Value = u.FULL_NAME ?? "";
            ws.Cell(row, 4).Value = u.DEPTCD ?? "";
            ws.Cell(row, 5).Value = u.LINECD ?? "";
            ws.Cell(row, 6).Value = u.WORKCD ?? "";
            ws.Cell(row, 7).Value = u.SCORE ?? 0;
            ws.Cell(row, 8).Value = u.MAX_SCORE ?? 0;
            ws.Cell(row, 9).Value = u.IS_PASS == 1 ? "PASS" : (u.IS_PASS == 0 ? "FAIL" : "");
            ws.Cell(row, 10).Value = u.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
            row++;
        }
        ws.Range(startRow, 1, startRow, headers.Length).SetAutoFilter();
        // Fixed widths thay vì AdjustToContents (rất chậm với 8k+ rows)
        int[] widths = { 6, 14, 26, 12, 12, 12, 8, 8, 12, 18 };
        for (int i = 0; i < widths.Length; i++)
            ws.Column(i + 1).Width = widths[i];
    }

    // ─── Request models ────────────────────────

    public class SaveSurveyVm
    {
        public int?      ID             { get; set; }
        public string    TITLE          { get; set; } = "";
        public string?   DESCRIPTION    { get; set; }
        public string    SURVEY_TYPE    { get; set; } = "POLL";
        public string    LANG           { get; set; } = "VI";
        public DateTime? START_DATE     { get; set; }
        public DateTime? END_DATE       { get; set; }
        public string    RECIPIENT_MODE { get; set; } = "ALL";
        public decimal?  PASS_SCORE     { get; set; }
        public string?   LOGIN_USER     { get; set; }
        public List<QuestionVm> QUESTIONS { get; set; } = new();
        public List<ScopeVm>    SCOPES    { get; set; } = new();
    }
    public class QuestionVm
    {
        public int?     ID            { get; set; }
        public string   QUESTION_TEXT { get; set; } = "";
        public string   QUESTION_TYPE { get; set; } = "SINGLE";
        public int      IS_REQUIRED   { get; set; } = 1;
        public int      DISPLAY_ORDER { get; set; }
        public decimal  POINTS        { get; set; }
        public List<OptionVm> OPTIONS { get; set; } = new();
    }
    public class OptionVm
    {
        public int?    ID            { get; set; }
        public string  OPTION_TEXT   { get; set; } = "";
        public int     DISPLAY_ORDER { get; set; }
        public int     IS_CORRECT    { get; set; }
    }
    public class ScopeVm
    {
        public string  SCOPE_TYPE { get; set; } = "";
        public string? DEPTCD     { get; set; }
        public string? LINECD     { get; set; }
        public string? WORKCD     { get; set; }
        public string? EMPCD      { get; set; }
    }
    public class ChangeStatusVm
    {
        public int    Id        { get; set; }
        public string NewStatus { get; set; } = "";
    }
    public class DeleteResponseVm
    {
        public int    SurveyId { get; set; }
        public string Empcd    { get; set; } = "";
    }
    public class BulkDeleteResponseVm
    {
        public int     SurveyId { get; set; }
        public string? Empcds   { get; set; }
    }
}
