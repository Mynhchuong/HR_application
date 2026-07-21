using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Training;

[Authorize(Roles = "Admin,HR")]
public class TrainingAdminController : BaseController
{
    private readonly TrainingService _training;

    public TrainingAdminController(TrainingService training)
    {
        _training = training;
    }

    // GET /TrainingAdmin/Index
    public IActionResult Index()
    {
        return View();
    }

    // GET /TrainingAdmin/Reports
    public async Task<IActionResult> Reports(int? classId = null, string? tab = null)
    {
        var classes = await _training.GetClassListAsync();
        ViewBag.Classes = classes;
        ViewBag.SelectedClassId = classId;
        ViewBag.Tab = tab ?? "class";
        return View();
    }

    // GET /TrainingAdmin/CourseCreate
    public IActionResult CourseCreate()
    {
        return View();
    }

    // GET /TrainingAdmin/CourseEdit/{id}
    public IActionResult CourseEdit(int id)
    {
        ViewBag.CourseId = id;
        return View();
    }

    // GET /TrainingAdmin/ClassCreate
    public IActionResult ClassCreate()
    {
        return View();
    }

    // GET /TrainingAdmin/ClassEdit/{id}
    public IActionResult ClassEdit(int id)
    {
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingAdmin/Certificates
    public IActionResult Certificates()
    {
        return View();
    }

    // GET /TrainingAdmin/ClassAssign/{id}
    public IActionResult ClassAssign(int id)
    {
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingAdmin/ClassReport/{id}
    public IActionResult ClassReport(int id)
    {
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingAdmin/CourseReport/{id}
    public IActionResult CourseReport(int id)
    {
        ViewBag.CourseId = id;
        return View();
    }

    // GET /TrainingAdmin/AttendanceReport/{id}
    public IActionResult AttendanceReport(int id)
    {
        ViewBag.ClassId = id;
        return View();
    }

    // ═══════════════════════════════════════════════════════════════
    //  REPORTS & EXCEL EXPORTS
    // ═══════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetReportClass(int classId)
    {
        var data = await _training.GetClassReportAsync(classId);
        return Json(new { success = data != null, data });
    }

    [HttpGet]
    public async Task<IActionResult> GetFailedStudents(int classId)
    {
        var data = await _training.GetFailedStudentsAsync(classId);
        return Json(new { success = data != null, data });
    }

    [HttpGet]
    public async Task<IActionResult> GetReportCourse(int courseId)
    {
        var data = await _training.GetCourseReportAsync(courseId);
        return Json(new { success = data != null, data });
    }

    [HttpGet]
    public async Task<IActionResult> GetReportAttendance(int classId)
    {
        var data = await _training.GetAttendanceMatrixAsync(classId);
        return Json(new { success = data != null, data });
    }

    [HttpGet]
    public async Task<IActionResult> GetReportTest(int testId)
    {
        var data = await _training.GetTestReportAsync(testId);
        return Json(new { success = data != null, data });
    }

    [HttpGet]
    public async Task<IActionResult> GetReportSatisfaction(int classId)
    {
        var data = await _training.GetSatisfactionReportAsync(classId);
        return Json(new { success = data != null, data });
    }

    [HttpGet]
    public async Task<IActionResult> ExportClass(int classId)
    {
        var report = await _training.GetClassReportAsync(classId);
        if (report == null) return NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Overview");
        WriteHeader(ws, 1, new[] { "Metric", "Value" });
        int r = 2;
        ws.Cell(r++, 1).Value = "Class name";                    ws.Cell(r-1, 2).Value = report.CLASS_NAME;
        ws.Cell(r++, 1).Value = "Course";                        ws.Cell(r-1, 2).Value = report.COURSE_TITLE;
        ws.Cell(r++, 1).Value = "Status";                        ws.Cell(r-1, 2).Value = report.CLASS_STATUS ?? "";
        ws.Cell(r++, 1).Value = "Enrolled";                      ws.Cell(r-1, 2).Value = report.ENROLLED_COUNT;
        ws.Cell(r++, 1).Value = "Assigned (mandatory)";          ws.Cell(r-1, 2).Value = report.ASSIGNED_COUNT;
        ws.Cell(r++, 1).Value = "Self-registered";               ws.Cell(r-1, 2).Value = report.SELF_REGISTER_COUNT;
        ws.Cell(r++, 1).Value = "Dropped";                       ws.Cell(r-1, 2).Value = report.DROPPED_COUNT;
        ws.Cell(r++, 1).Value = "Completed";                     ws.Cell(r-1, 2).Value = report.COMPLETED_COUNT;
        ws.Cell(r++, 1).Value = "Failed";                        ws.Cell(r-1, 2).Value = report.FAILED_COUNT;
        ws.Cell(r++, 1).Value = "Certified";                     ws.Cell(r-1, 2).Value = report.CERTIFIED_COUNT;
        ws.Cell(r++, 1).Value = "Thi lại lần 1";                  ws.Cell(r-1, 2).Value = report.RETAKE_1_COUNT;
        ws.Cell(r++, 1).Value = "Thi lại lần 2";                  ws.Cell(r-1, 2).Value = report.RETAKE_2_COUNT;
        ws.Cell(r++, 1).Value = "Thi lại lần 3+";                 ws.Cell(r-1, 2).Value = report.RETAKE_3_COUNT;
        ws.Cell(r++, 1).Value = "Avg attendance %";              ws.Cell(r-1, 2).Value = report.AVG_ATTENDANCE_PERCENT ?? 0;
        ws.Cell(r++, 1).Value = "Avg final score";               ws.Cell(r-1, 2).Value = report.AVG_FINAL_SCORE ?? 0;

        var wsH = wb.Worksheets.Add("Score histogram");
        WriteHeader(wsH, 1, new[] { "Bucket", "Count" });
        for (int i = 0; i < report.SCORE_HISTOGRAM.Count; i++)
        {
            wsH.Cell(i + 2, 1).Value = report.SCORE_HISTOGRAM[i].LABEL;
            wsH.Cell(i + 2, 2).Value = report.SCORE_HISTOGRAM[i].COUNT;
        }
        wsH.Columns().AdjustToContents();

        if (report.GROUP_BREAKDOWN.Count > 0)
        {
            var wsG = wb.Worksheets.Add("Groups");
            WriteHeader(wsG, 1, new[] { "Group", "Enrolled", "Completed", "Certified", "Avg attendance" });
            for (int i = 0; i < report.GROUP_BREAKDOWN.Count; i++)
            {
                var g = report.GROUP_BREAKDOWN[i];
                wsG.Cell(i + 2, 1).Value = g.GROUP_NAME;
                wsG.Cell(i + 2, 2).Value = g.ENROLLED;
                wsG.Cell(i + 2, 3).Value = g.COMPLETED;
                wsG.Cell(i + 2, 4).Value = g.CERTIFIED;
                wsG.Cell(i + 2, 5).Value = g.AVG_ATTENDANCE ?? 0;
            }
            wsG.Columns().AdjustToContents();
        }

        ws.Columns().AdjustToContents();
        return BuildXlsx(wb, $"report_class_{classId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCourse(int courseId)
    {
        var report = await _training.GetCourseReportAsync(courseId);
        if (report == null) return NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Course report");
        ws.Cell(1, 1).Value = "Course";
        ws.Cell(1, 2).Value = report.COURSE_TITLE;
        WriteHeader(ws, 3, new[] { "Class", "Status", "Enrolled", "Assigned", "Self-registered", "Dropped", "Completed", "Failed", "Certified", "Avg attendance %", "Avg final score" });

        int r = 4;
        void WriteRow(ReportCourseClassRow row)
        {
            int c = 1;
            ws.Cell(r, c++).Value = row.CLASS_NAME;
            ws.Cell(r, c++).Value = row.CLASS_STATUS ?? "";
            ws.Cell(r, c++).Value = row.ENROLLED_COUNT;
            ws.Cell(r, c++).Value = row.ASSIGNED_COUNT;
            ws.Cell(r, c++).Value = row.SELF_REGISTER_COUNT;
            ws.Cell(r, c++).Value = row.DROPPED_COUNT;
            ws.Cell(r, c++).Value = row.COMPLETED_COUNT;
            ws.Cell(r, c++).Value = row.FAILED_COUNT;
            ws.Cell(r, c++).Value = row.CERTIFIED_COUNT;
            ws.Cell(r, c++).Value = row.AVG_ATTENDANCE_PERCENT ?? 0;
            ws.Cell(r, c++).Value = row.AVG_FINAL_SCORE ?? 0;
            r++;
        }

        foreach (var row in report.CLASSES) WriteRow(row);
        var totalRow = ws.Row(r);
        WriteRow(report.TOTAL);
        totalRow.Style.Font.SetBold();

        ws.Columns().AdjustToContents();
        return BuildXlsx(wb, $"report_course_{courseId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportAttendance(int classId)
    {
        var m = await _training.GetAttendanceMatrixAsync(classId);
        if (m == null) return NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Attendance matrix");

        var head = new List<string> { "EMPCD", "Name", "Group", "Attendance %" };
        foreach (var s in m.SESSIONS)
        {
            var lbl = $"#{s.SESSION_NO} {s.SESSION_DATE:dd/MM}";
            if (s.GROUP_NAME != null) lbl += $" [{s.GROUP_NAME}]";
            head.Add(lbl);
        }
        WriteHeader(ws, 1, head.ToArray());

        for (int i = 0; i < m.STUDENTS.Count; i++)
        {
            var st = m.STUDENTS[i];
            int col = 1;
            ws.Cell(i + 2, col++).Value = st.EMPCD;
            ws.Cell(i + 2, col++).Value = st.EMP_NAME ?? "";
            ws.Cell(i + 2, col++).Value = st.GROUP_NAME ?? "";
            ws.Cell(i + 2, col++).Value = st.ATTENDANCE_PERCENT;
            foreach (var s in m.SESSIONS)
            {
                var key = s.SESSION_ID.ToString();
                var v = st.STATUS_PER_SESSION.TryGetValue(key, out var vv) ? vv : "";
                // Đánh dấu (tự ĐD) khi học viên tự bấm điểm danh qua app — giữ lại trong file Excel
                // để có hồ sơ tra soát/minh bạch, không chỉ xem được lúc mở báo cáo trên web.
                var selfCi = st.SELF_CHECKIN_PER_SESSION.TryGetValue(key, out var ci) && ci;
                ws.Cell(i + 2, col++).Value = selfCi && v != "" ? $"{v} (tự ĐD)" : v;
            }
        }

        ws.Columns().AdjustToContents();
        return BuildXlsx(wb, $"report_attendance_{classId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportTest(int testId)
    {
        var r = await _training.GetTestReportAsync(testId);
        if (r == null) return NotFound();

        using var wb = new XLWorkbook();
        var wsS = wb.Worksheets.Add("Scores");
        WriteHeader(wsS, 1, new[] { "EMPCD", "Name", "Attempt No", "Lượt thi / Thi lại", "Score", "Max score", "Pass", "Status", "Submit" });
        // Đếm số lượt/học viên — HR cần biết ai thi lại từ 3 lần trở lên (dấu hiệu cần lưu ý).
        var attemptCountByEmp = r.SCORES.GroupBy(s => s.EMPCD).ToDictionary(g => g.Key, g => g.Count());
        for (int i = 0; i < r.SCORES.Count; i++)
        {
            var s = r.SCORES[i];
            var attNo = s.ATTEMPT_NO;
            var attLabel = attNo == 1 ? "Lần 1 (Chính thức)" : (attNo == 2 ? "Thi lại lần 1" : (attNo == 3 ? "Thi lại lần 2" : $"Thi lại lần {attNo - 1}"));

            wsS.Cell(i + 2, 1).Value = s.EMPCD;
            wsS.Cell(i + 2, 2).Value = s.EMP_NAME ?? "";
            wsS.Cell(i + 2, 2).Style.Font.FontName = "Vnitbi__";
            wsS.Cell(i + 2, 3).Value = s.ATTEMPT_NO;
            wsS.Cell(i + 2, 4).Value = attLabel;
            if (attemptCountByEmp.TryGetValue(s.EMPCD, out var cnt) && cnt >= 3)
                wsS.Cell(i + 2, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff3cd");
            wsS.Cell(i + 2, 5).Value = s.SCORE ?? 0;
            wsS.Cell(i + 2, 6).Value = s.MAX_SCORE ?? 0;
            wsS.Cell(i + 2, 7).Value = s.IS_PASS == 1 ? "PASS" : (s.IS_PASS == 0 ? "FAIL" : "-");
            wsS.Cell(i + 2, 8).Value = s.STATUS;
            wsS.Cell(i + 2, 9).Value = s.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
        }
        wsS.Columns().AdjustToContents();

        var wsSum = wb.Worksheets.Add("Summary");
        WriteHeader(wsSum, 1, new[] { "Metric", "Value" });
        var rows = new (string, object)[]
        {
            ("Test title",     r.TEST_TITLE),
            ("Pass score",     r.PASS_SCORE ?? 0),
            ("Attempt count",  r.ATTEMPT_COUNT),
            ("Pass count",     r.PASS_COUNT),
            ("Fail count",     r.FAIL_COUNT),
            ("Thi lại lần 1",  r.RETAKE_1_COUNT),
            ("Thi lại lần 2",  r.RETAKE_2_COUNT),
            ("Thi lại lần 3+", r.RETAKE_3_COUNT),
            ("Avg score",      r.AVG_SCORE ?? 0),
            ("Max score",      r.MAX_SCORE ?? 0),
            ("Min score",      r.MIN_SCORE ?? 0),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            wsSum.Cell(i + 2, 1).Value = rows[i].Item1;
            wsSum.Cell(i + 2, 2).Value = XLCellValue.FromObject(rows[i].Item2);
        }
        wsSum.Columns().AdjustToContents();

        var wsW = wb.Worksheets.Add("Wrong questions");
        WriteHeader(wsW, 1, new[] { "Question", "Type", "Attempts", "Wrong", "Wrong %" });
        for (int i = 0; i < r.TOP_WRONG_QUESTIONS.Count; i++)
        {
            var q = r.TOP_WRONG_QUESTIONS[i];
            wsW.Cell(i + 2, 1).Value = q.QUESTION_TEXT;
            wsW.Cell(i + 2, 2).Value = q.QUESTION_TYPE;
            wsW.Cell(i + 2, 3).Value = q.ATTEMPT_COUNT;
            wsW.Cell(i + 2, 4).Value = q.WRONG_COUNT;
            wsW.Cell(i + 2, 5).Value = q.WRONG_PERCENT;
        }
        wsW.Columns().AdjustToContents();

        // Chi tiết ai sai câu nào — 1 dòng / (câu hỏi, học viên sai) để HR lọc/sort thoải mái trong Excel.
        var wsWD = wb.Worksheets.Add("Wrong answer detail");
        WriteHeader(wsWD, 1, new[] { "Question", "Type", "EMPCD", "Name", "Attempt No" });
        int rowWD = 2;
        foreach (var q in r.TOP_WRONG_QUESTIONS)
        {
            foreach (var s in q.WRONG_STUDENTS)
            {
                wsWD.Cell(rowWD, 1).Value = q.QUESTION_TEXT;
                wsWD.Cell(rowWD, 2).Value = q.QUESTION_TYPE;
                wsWD.Cell(rowWD, 3).Value = s.EMPCD;
                wsWD.Cell(rowWD, 4).Value = s.EMP_NAME ?? "";
                wsWD.Cell(rowWD, 4).Style.Font.FontName = "Vnitbi__";
                wsWD.Cell(rowWD, 5).Value = s.ATTEMPT_NO;
                rowWD++;
            }
        }
        wsWD.Columns().AdjustToContents();

        return BuildXlsx(wb, $"report_test_{testId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportSatisfaction(int classId)
    {
        var s = await _training.GetSatisfactionReportAsync(classId);
        if (s == null) return NotFound();

        using var wb = new XLWorkbook();
        var wsSum = wb.Worksheets.Add("Summary");
        WriteHeader(wsSum, 1, new[] { "Metric", "Value" });
        wsSum.Cell(2, 1).Value = "Responses";       wsSum.Cell(2, 2).Value = s.RESPONSE_COUNT;
        wsSum.Cell(3, 1).Value = "Avg content";     wsSum.Cell(3, 2).Value = s.AVG_CONTENT ?? 0;
        wsSum.Cell(4, 1).Value = "Avg organization";wsSum.Cell(4, 2).Value = s.AVG_ORGANIZATION ?? 0;
        wsSum.Columns().AdjustToContents();

        var wsT = wb.Worksheets.Add("Teacher aggregate");
        WriteHeader(wsT, 1, new[] { "Teacher EMPCD", "Name", "Avg rating", "Count" });
        for (int i = 0; i < s.TEACHER_AGGREGATES.Count; i++)
        {
            var t = s.TEACHER_AGGREGATES[i];
            wsT.Cell(i + 2, 1).Value = t.TEACHER_EMPCD;
            wsT.Cell(i + 2, 2).Value = t.TEACHER_NAME ?? "";
            wsT.Cell(i + 2, 3).Value = t.AVG_RATING;
            wsT.Cell(i + 2, 4).Value = t.COUNT;
        }
        wsT.Columns().AdjustToContents();

        var wsFB = wb.Worksheets.Add("Feedback");
        WriteHeader(wsFB, 1, new[] { "#", "Feedback text" });
        for (int i = 0; i < s.FEEDBACK_LIST.Count; i++)
        {
            wsFB.Cell(i + 2, 1).Value = i + 1;
            wsFB.Cell(i + 2, 2).Value = s.FEEDBACK_LIST[i];
        }
        wsFB.Columns().AdjustToContents();

        return BuildXlsx(wb, $"report_satisfaction_{classId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    // ═══════════════════════════════════════════════════════════════
    //  HR/ADMIN AJAX PROXIES
    // ═══════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetClassList(string? status = null, string? search = null, int? courseId = null)
    {
        var qs = $"status={status}&search={Uri.EscapeDataString(search ?? "")}&courseId={courseId}";
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/class/list", qs);
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetCourses(string? mode = null, int? active = null, string? search = null)
    {
        var qs = "";
        if (!string.IsNullOrEmpty(mode)) qs += $"mode={mode}&";
        if (active.HasValue)             qs += $"active={active.Value}&";
        if (!string.IsNullOrEmpty(search)) qs += $"search={Uri.EscapeDataString(search)}&";

        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/course/list", qs);
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> SaveCourse([FromBody] SaveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveClass([FromBody] SaveClassRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetClassDetailAdmin(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/class/detail", $"id={classId}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> SaveSession([FromBody] SaveSessionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/session/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // GET /TrainingAdmin/SessionImportTemplate — tải file Excel mẫu để nhập nhiều buổi học 1 lần
    [HttpGet]
    public IActionResult SessionImportTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sessions");
        string[] headers = { "SESSION_NO", "SESSION_DATE (yyyy-MM-dd)", "NHOM (tên nhóm, để trống = buổi chung)", "START_TIME (HHMM)", "END_TIME (HHMM)", "TOPIC", "LOCATION" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
            c.Style.Font.FontColor = XLColor.White;
        }

        ws.Cell(2, 1).Value = 1;
        ws.Cell(2, 2).Value = DateTime.Today.ToString("yyyy-MM-dd");
        ws.Cell(2, 3).Value = "";
        ws.Cell(2, 4).Value = "0800";
        ws.Cell(2, 5).Value = "1130";
        ws.Cell(2, 6).Value = "Giới thiệu tổng quan";
        ws.Cell(2, 7).Value = "Hội trường A";
        ws.Cell(3, 1).Value = 2;
        ws.Cell(3, 2).Value = DateTime.Today.ToString("yyyy-MM-dd");
        ws.Cell(3, 3).Value = "Nhóm 1";
        ws.Cell(3, 4).Value = "1300";
        ws.Cell(3, 5).Value = "1430";
        ws.Cell(3, 6).Value = "Thực hành";
        ws.Cell(3, 7).Value = "Xưởng A";
        ws.Range(2, 4, 3, 5).Style.NumberFormat.Format = "@";   // giữ dạng text, tránh Excel tự bỏ số 0 đầu

        ws.Cell(5, 1).Value = "* SESSION_NO: số buổi, không trùng trong cùng 1 lớp.";
        ws.Cell(6, 1).Value = "* SESSION_DATE: định dạng yyyy-MM-dd (VD 2026-08-01).";
        ws.Cell(7, 1).Value = "* NHOM: để trống = buổi chung cả lớp. Nếu lớp có chia nhóm (VD Nhóm 1, Nhóm 2...), nhập đúng tên nhóm đã tạo trong lớp — buổi đó chỉ áp dụng cho nhóm này (nhiều nhóm có thể học cùng ngày khác giờ).";
        ws.Cell(8, 1).Value = "* START_TIME/END_TIME: 4 chữ số HHMM (VD 0800, 1130) — nên định dạng ô là Text để không bị Excel tự bỏ số 0 đầu.";
        ws.Cell(9, 1).Value = "* TOPIC, LOCATION: có thể để trống.";
        ws.Range(5, 1, 9, 1).Style.Font.Italic = true;
        ws.Range(5, 1, 9, 1).Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauImportBuoiHoc.xlsx");
    }

    // POST /TrainingAdmin/SessionImport — nhập nhiều buổi học từ file Excel
    [HttpPost]
    public async Task<IActionResult> SessionImport(IFormFile file, int classId)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });

        // Resolve tên nhóm (cột NHOM) sang GROUP_ID — cần DS nhóm hiện có của Class.
        var groupsRes = await _training.GetFromApiAsync<SimpleGroupListResponse>($"TrainingAdmin/class/{classId}/groups");
        var groupMap = (groupsRes?.data ?? new())
            .GroupBy(g => g.GROUP_NAME.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().ID);

        var rows = new List<BulkImportSessionRow>();
        var groupErrors = new List<string>();
        int skipped = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Template cũ (trước khi thêm cột NHOM) có START_TIME ở cột 3 — nếu import sẽ đọc lệch
            // hết các cột sau. Chặn sớm với message rõ ràng thay vì báo "không tìm thấy nhóm '0800'".
            var header3 = ws.Cell(1, 3).GetString()?.Trim() ?? "";
            if (header3.StartsWith("START_TIME", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "File này dùng template cũ (chưa có cột NHOM). Vui lòng bấm 'Tải template' để lấy file mẫu mới rồi nhập lại." });

            for (int r = 2; r <= lastRow; r++)
            {
                var noS      = ws.Cell(r, 1).GetString()?.Trim() ?? "";
                var dateS    = ws.Cell(r, 2).GetString()?.Trim() ?? "";
                var groupS   = ws.Cell(r, 3).GetString()?.Trim() ?? "";
                var start    = NormalizeHHMM(ws.Cell(r, 4).GetString()?.Trim() ?? "");
                var end      = NormalizeHHMM(ws.Cell(r, 5).GetString()?.Trim() ?? "");
                var topic    = ws.Cell(r, 6).GetString()?.Trim();
                var loc      = ws.Cell(r, 7).GetString()?.Trim();

                if (string.IsNullOrEmpty(noS) || string.IsNullOrEmpty(dateS) || start.Length != 4 || end.Length != 4)
                { skipped++; continue; }
                if (!int.TryParse(noS, out var sessionNo)) { skipped++; continue; }
                if (!DateTime.TryParse(dateS, out var date)) { skipped++; continue; }

                int? groupId = null;
                if (!string.IsNullOrEmpty(groupS))
                {
                    if (groupMap.TryGetValue(groupS.ToUpperInvariant(), out var gid)) groupId = gid;
                    else { groupErrors.Add($"Dòng {r}: không tìm thấy nhóm '{groupS}' trong lớp — bỏ qua dòng này."); continue; }
                }

                rows.Add(new BulkImportSessionRow
                {
                    SESSION_NO   = sessionNo,
                    SESSION_DATE = date,
                    START_TIME   = start,
                    END_TIME     = end,
                    TOPIC        = string.IsNullOrEmpty(topic) ? null : topic,
                    LOCATION     = string.IsNullOrEmpty(loc) ? null : loc,
                    GROUP_ID     = groupId,
                });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi đọc file: " + ex.Message });
        }

        if (rows.Count == 0)
        {
            if (groupErrors.Count > 0)
                return Json(new { success = false, message = "Không có dòng hợp lệ trong file:\n" + string.Join("\n", groupErrors) });
            return Json(new { success = false, message = "Không có dòng hợp lệ trong file" });
        }

        var apiReq = new BulkImportSessionsRequest
        {
            CLASS_ID   = classId,
            ROWS       = rows,
            LOGIN_USER = CurrentUser?.EmpCd ?? "",
        };
        var response = await _training.PostToApiAsync("TrainingAdmin/session/bulk-import", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();

        // Gộp lỗi "không tìm thấy nhóm" (phát hiện lúc parse ở HR_web) vào ERRORS trả về từ API.
        // API fail (không có data) → gộp vào message để lỗi nhóm không bị nuốt mất.
        if (groupErrors.Count > 0)
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (obj["data"] is Newtonsoft.Json.Linq.JObject dataObj)
            {
                var errArr = dataObj["ERRORS"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();
                foreach (var e in groupErrors) errArr.Add(e);
                dataObj["ERRORS"] = errArr;
            }
            else
            {
                var msg = obj["message"]?.ToString() ?? "";
                obj["message"] = (msg + "\n" + string.Join("\n", groupErrors)).Trim();
            }
            return Content(obj.ToString(Newtonsoft.Json.Formatting.None), "application/json");
        }

        return Content(json, "application/json");
    }

    // Chuẩn hoá "8:00", "800", "0800" đều về "0800" — Excel hay tự bỏ số 0 đầu khi ô là dạng số.
    private static string NormalizeHHMM(string s)
    {
        s = s.Replace(":", "").Trim();
        if (int.TryParse(s, out var n) && n >= 0) return n.ToString("D4");
        return s;
    }

    [HttpPost]
    public async Task<IActionResult> AssignTeacher([FromBody] AssignTeacherRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/assign-teacher", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> RemoveTeacher([FromBody] RemoveTeacherRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/remove-teacher", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> AssignStudent([FromBody] AssignStudentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var empcds = req.EMPCDS ?? new List<string>();
        if (empcds.Count == 0 && !string.IsNullOrEmpty(req.EMPCD))
        {
            empcds.Add(req.EMPCD);
        }

        var apiReq = new {
            CLASS_ID = req.CLASS_ID,
            EMPCDS = empcds,
            LOGIN_USER = req.LOGIN_USER
        };

        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/assign", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // GET /TrainingAdmin/StudentImportTemplate — file Excel mẫu nhập DS học viên (+ nhóm)
    [HttpGet]
    public IActionResult StudentImportTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Students");
        WriteHeader(ws, 1, new[] { "EMPCD (mã nhân viên)", "NHOM (tên nhóm, để trống = chưa chia)" });

        ws.Cell(2, 1).Value = "18042301";
        ws.Cell(2, 2).Value = "Nhóm 1";
        ws.Cell(3, 1).Value = "25081801";
        ws.Cell(3, 2).Value = "";
        ws.Column(1).Style.NumberFormat.Format = "@";   // EMPCD giữ dạng text, không mất số 0 đầu

        ws.Cell(5, 1).Value = "* EMPCD: mã nhân viên, mỗi dòng 1 người. Trùng mã → lấy dòng cuối.";
        ws.Cell(6, 1).Value = "* NHOM: tên nhóm học. Nhóm chưa có trong lớp → hệ thống TỰ TẠO nhóm mới rồi gán vào. Để trống = chưa chia nhóm.";
        ws.Cell(7, 1).Value = "* Học viên đã có trong lớp sẽ không bị thêm trùng (chỉ cập nhật nhóm nếu có).";
        ws.Range(5, 1, 7, 1).Style.Font.Italic = true;
        ws.Range(5, 1, 7, 1).Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();
        return BuildXlsx(wb, "Mau_DanhSachHocVien.xlsx");
    }

    // POST /TrainingAdmin/StudentImport — nhập DS học viên từ Excel: enroll + tự tạo nhóm thiếu + gán nhóm.
    // Orchestrate 3 API sẵn có (enrollment/assign → class/group/save → enrollment/assign-group) —
    // không atomic toàn cục nhưng từng bước idempotent (MERGE/PK) nên chạy lại file là bù được.
    [HttpPost]
    public async Task<IActionResult> StudentImport(IFormFile file, int classId)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });
        var loginUser = CurrentUser?.EmpCd ?? "";

        // Parse file: EMPCD | NHOM. Trùng EMPCD → dòng cuối thắng.
        var byEmp = new Dictionary<string, string>();
        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var emp = ws.Cell(r, 1).GetString()?.Trim() ?? "";
                var grp = ws.Cell(r, 2).GetString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(emp) || emp.StartsWith("*")) continue;   // bỏ dòng trống / ghi chú
                byEmp[emp] = grp;
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi đọc file: " + ex.Message });
        }
        if (byEmp.Count == 0)
            return Json(new { success = false, message = "Không có dòng hợp lệ trong file" });

        // 1. Enroll theo lô 50 người/lần — file lớn gọi 1 phát dễ treo/timeout API.
        //    Lô đầu lỗi → fail nguyên file như cũ; lô sau lỗi (vd vượt cap giữa chừng) → dừng và báo
        //    trong ERRORS, sửa xong nhập lại file là bù được phần thiếu (các bước idempotent).
        const int BATCH_SIZE = 50;
        var allEmpcds = byEmp.Keys.ToList();
        var errors = new List<string>();
        var failedEmpcds = new List<string>();
        int newInserted = 0, upgraded = 0, skippedExisted = 0, skippedDropped = 0;
        for (int i = 0; i < allEmpcds.Count; i += BATCH_SIZE)
        {
            var batch = allEmpcds.Skip(i).Take(BATCH_SIZE).ToList();
            var assignRes = await _training.PostToApiAsync("TrainingAdmin/enrollment/assign",
                new { CLASS_ID = classId, EMPCDS = batch, LOGIN_USER = loginUser });
            var assignObj = assignRes == null ? null
                : Newtonsoft.Json.Linq.JObject.Parse(await assignRes.Content.ReadAsStringAsync());
            if ((bool?)assignObj?["success"] != true)
            {
                var msg = assignObj?["message"]?.ToString() ?? "Lỗi kết nối API";
                if (i == 0)
                    return Json(new { success = false, message = msg });
                errors.Add($"Dừng ở lô {i + 1}-{Math.Min(i + BATCH_SIZE, allEmpcds.Count)}/{allEmpcds.Count}: {msg}. Nhập lại file sau khi xử lý để bổ sung phần còn thiếu.");
                break;
            }
            var d = assignObj["data"];
            newInserted    += (int?)d?["NEW_INSERTED"]    ?? 0;
            upgraded       += (int?)d?["UPGRADED"]        ?? 0;
            skippedExisted += (int?)d?["SKIPPED_EXISTED"] ?? 0;
            skippedDropped += (int?)d?["SKIPPED_DROPPED"] ?? 0;
            if (d?["FAILED_EMPCDS"] is Newtonsoft.Json.Linq.JArray fails)
                failedEmpcds.AddRange(fails.Select(t => t.ToString()));
        }
        if (failedEmpcds.Count > 0)
            errors.Add($"{failedEmpcds.Count} mã không tồn tại/không thêm được: {string.Join(", ", failedEmpcds)}");

        // 2. Map nhóm hiện có của lớp
        var groupsRes = await _training.GetFromApiAsync<SimpleGroupListResponse>($"TrainingAdmin/class/{classId}/groups");
        var groupMap = (groupsRes?.data ?? new())
            .GroupBy(g => g.GROUP_NAME.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().ID);

        // 3. Từng nhóm trong file: chưa có → tạo mới; rồi bulk gán các EMPCD của nhóm đó (theo lô 50)
        int groupsCreated = 0, groupAssigned = 0;
        var buckets = byEmp.Where(kv => !string.IsNullOrEmpty(kv.Value))
                           .GroupBy(kv => kv.Value.ToUpperInvariant());
        foreach (var bucket in buckets)
        {
            var displayName = bucket.First().Value.Trim();
            if (!groupMap.TryGetValue(bucket.Key, out var gid))
            {
                var saveRes = await _training.PostToApiAsync("TrainingAdmin/class/group/save",
                    new { CLASS_ID = classId, GROUP_NAME = displayName, LOGIN_USER = loginUser });
                var saveObj = saveRes == null ? null
                    : Newtonsoft.Json.Linq.JObject.Parse(await saveRes.Content.ReadAsStringAsync());
                if ((bool?)saveObj?["success"] == true && saveObj["data"]?["id"] != null)
                {
                    gid = (int)saveObj["data"]!["id"]!;
                    groupMap[bucket.Key] = gid;
                    groupsCreated++;
                }
                else
                {
                    errors.Add($"Không tạo được nhóm '{displayName}': {saveObj?["message"]?.ToString() ?? "lỗi kết nối"}");
                    continue;
                }
            }

            var bucketEmpcds = bucket.Select(kv => kv.Key).ToList();
            for (int i = 0; i < bucketEmpcds.Count; i += BATCH_SIZE)
            {
                var batch = bucketEmpcds.Skip(i).Take(BATCH_SIZE).ToList();
                var gaRes = await _training.PostToApiAsync("TrainingAdmin/enrollment/assign-group",
                    new { CLASS_ID = classId, GROUP_ID = gid, EMPCDS = batch, LOGIN_USER = loginUser });
                var gaObj = gaRes == null ? null
                    : Newtonsoft.Json.Linq.JObject.Parse(await gaRes.Content.ReadAsStringAsync());
                if ((bool?)gaObj?["success"] == true)
                    groupAssigned += (int?)gaObj["data"]?["updated"] ?? 0;
                else
                    errors.Add($"Gán nhóm '{displayName}' (lô {i + 1}-{Math.Min(i + BATCH_SIZE, bucketEmpcds.Count)}) lỗi: {gaObj?["message"]?.ToString() ?? "lỗi kết nối"}");
            }
        }

        return Json(new
        {
            success = true,
            data = new
            {
                NEW_INSERTED    = newInserted,
                UPGRADED        = upgraded,
                SKIPPED_EXISTED = skippedExisted,
                SKIPPED_DROPPED = skippedDropped,
                GROUPS_CREATED  = groupsCreated,
                GROUP_ASSIGNED  = groupAssigned,
                ERRORS          = errors,
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> ApproveEnrollment([FromBody] ApproveEnrollmentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/approve", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> RejectEnrollment([FromBody] RejectEnrollmentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/reject", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveGroup([FromBody] SaveGroupRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/group/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteGroup([FromBody] DeleteGroupRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/group/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> AutoSplitGroup([FromBody] AutoSplitRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/group/auto-split", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> AssignGroup([FromBody] AssignGroupRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/assign-group", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveAdminTest([FromBody] SaveTestRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveAdminTestQuestions([FromBody] SaveTestQuestionsRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/questions/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> PublishAdminTest([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/publish", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> ArchiveCourse([FromBody] ArchiveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/archive", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> UnarchiveCourse([FromBody] ArchiveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/unarchive", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCourse([FromBody] ArchiveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> PublishRegistration([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        // Build link "Đăng ký ngay" bằng Url.Action — tự động đúng PathBase/scheme/host của request
        // hiện tại (VD /HR_Web nếu deploy dưới virtual path), không cần đoán/hardcode ở phía API.
        req.REGISTER_URL = Url.Action("ClassRegister", "Training", new { id = req.ID }, Request.Scheme);
        var response = await _training.PostToApiAsync("TrainingAdmin/class/publish-registration", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> FinalizeEnrollment([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/finalize-enrollment", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> RemindReview(int id)
    {
        var response = await _training.PostToApiAsync($"TrainingAdmin/class/{id}/remind-review", new { });
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> CancelClass([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/cancel", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> CloseClass([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/close", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteClass([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // POST /TrainingAdmin/FinalizeClass — chốt kết quả lớp: compute attendance/điểm + cấp chứng chỉ (§7)
    [HttpPost]
    public async Task<IActionResult> FinalizeClass([FromBody] ChangeClassStatusRequest req)
    {
        var apiReq = new { CLASS_ID = req.ID, LOGIN_USER = CurrentUser?.EmpCd ?? "" };
        var response = await _training.PostToApiAsync("TrainingAdmin/class/finalize", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> ExpressCreate([FromBody] ExpressCreateRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/express-create", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> CloneFromCourse([FromBody] CloneFromCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/clone-from-course", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // §15b Cách 2 — "Sao chép sang đợt mới" từ 1 Class đã có (Class Detail)
    [HttpPost]
    public async Task<IActionResult> CloneFromClass([FromBody] CloneFromClassRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/clone-from-class", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> RescheduleSession([FromBody] RescheduleSessionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/session/reschedule", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> CancelSession([FromBody] CancelSessionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/session/cancel", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> PreAssignEnrollment([FromBody] BulkPreAssignRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/pre-assign", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> RecoverEnrollment([FromBody] RecoverEnrollmentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/recover", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> RemoveStudent([FromBody] RemoveEnrollmentWebRequest req)
    {
        var apiReq = new { CLASS_ID = req.CLASS_ID, EMPCD = req.EMPCD, LOGIN_USER = CurrentUser?.EmpCd ?? "" };
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/remove", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> CloseAdminTest([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/close", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetReviewReport(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/review/report", $"classId={classId}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetCertificateList(
        int? classId = null, int? courseId = null, string? empcd = null, string? deptcd = null,
        string? linecd = null, string? workcd = null, DateTime? from = null, DateTime? to = null)
    {
        var loginUser = CurrentUser?.EmpCd ?? "";
        var qs = $"loginUser={loginUser}";
        if (classId.HasValue)             qs += $"&classId={classId.Value}";
        if (courseId.HasValue)            qs += $"&courseId={courseId.Value}";
        if (!string.IsNullOrEmpty(empcd))  qs += $"&empcd={empcd}";
        if (!string.IsNullOrEmpty(deptcd)) qs += $"&deptcd={deptcd}";
        if (!string.IsNullOrEmpty(linecd)) qs += $"&linecd={linecd}";
        if (!string.IsNullOrEmpty(workcd)) qs += $"&workcd={workcd}";
        if (from.HasValue)                qs += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue)                  qs += $"&to={to.Value:yyyy-MM-dd}";

        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/certificate/list", qs);
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> RevokeCertificate([FromBody] RevokeCertificateRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/certificate/revoke", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> GrantRetake([FromBody] GrantRetakeRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/retake-grant", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> GrantRetakeAllFailed([FromBody] GrantRetakeAllRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/retake-grant-all-failed", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetCourseSessions(int courseId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/course/session-templates", $"courseId={courseId}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetEnrollments(int classId, string? status = null)
    {
        var qs = $"status={status}";
        var res = await _training.GetFromApiAsync<object>($"TrainingAdmin/class/{classId}/enrollments", qs);
        return Json(res);
    }

    // GET /TrainingAdmin/ExportEnrollments?classId=4&groupId=&search= — xuất DS học viên trong lớp
    [HttpGet]
    public async Task<IActionResult> ExportEnrollments(int classId, string? groupId = null, string? search = null)
    {
        var res = await _training.GetFromApiAsync<object>($"TrainingAdmin/class/{classId}/enrollments", "");
        var list = (res as Newtonsoft.Json.Linq.JObject)?["data"] as Newtonsoft.Json.Linq.JArray
                   ?? new Newtonsoft.Json.Linq.JArray();

        var searchUp = (search ?? "").Trim().ToUpperInvariant();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Học viên");
        WriteHeader(ws, 1, new[] { "STT", "Mã NV", "Họ và tên", "Dept", "Line", "Work", "Tổ/Nhóm", "Nguồn", "Trạng thái" });

        int row = 2;
        foreach (var item in list)
        {
            var empcd = item["EMPCD"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(searchUp) && !empcd.ToUpperInvariant().Contains(searchUp)) continue;
            if (!string.IsNullOrEmpty(groupId) && (item["GROUP_ID"]?.ToString() ?? "") != groupId) continue;

            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = empcd;
            ws.Cell(row, 3).Value = item["EMP_NAME"]?.ToString() ?? "";
            ws.Cell(row, 4).Value = item["DEPTCD"]?.ToString() ?? "";
            ws.Cell(row, 5).Value = item["LINECD"]?.ToString() ?? "";
            ws.Cell(row, 6).Value = item["WORKCD"]?.ToString() ?? "";
            ws.Cell(row, 7).Value = item["GROUP_NAME"]?.ToString() ?? "";
            ws.Cell(row, 8).Value = item["SOURCE"]?.ToString() == "ASSIGNED" ? "Chỉ định" : "Tự đăng ký";
            ws.Cell(row, 9).Value = StatusLabelVi(item["STATUS"]?.ToString());
            row++;
        }
        ws.Range(1, 1, 1, 9).SetAutoFilter();
        ws.Columns().AdjustToContents();

        return BuildXlsx(wb, $"HocVien_Lop{classId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    private static string StatusLabelVi(string? status) => status switch
    {
        "PENDING_APPROVAL" => "Chờ duyệt",
        "ENROLLED"         => "Đã tham gia",
        "COMPLETED"        => "Hoàn thành",
        "FAILED"           => "Không đạt",
        "REJECTED"         => "Từ chối",
        "DROPPED"          => "Đã loại",
        _                  => status ?? ""
    };

    // ─── Helpers ──────────────────────────────────────────────────

    private static void WriteHeader(IXLWorksheet ws, int row, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(row, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0d6efd");
            c.Style.Font.FontColor = XLColor.White;
        }
    }

    private FileContentResult BuildXlsx(XLWorkbook wb, string fileName)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
