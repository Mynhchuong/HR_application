using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Training;

// Training views cho Manager/Supervisor/Clerk — team schedule §14.5.
// Reports §14 cho HR/Admin — Phase 6.
[Authorize]
public class TrainingController : BaseController
{
    private readonly TrainingService _training;

    public TrainingController(TrainingService training)
    {
        _training = training;
    }

    // GET /Training/TeamSchedule?today=1
    // today=1: force chỉ hôm nay (từ Home dashboard drill-down). Default = 7 ngày sắp tới.
    public async Task<IActionResult> TeamSchedule(int today = 0)
    {
        var empcd = CurrentUser?.EmpCd ?? "";
        if (string.IsNullOrEmpty(empcd))
            return RedirectToAction("Login", "Account");

        // Guard: check scope. Nếu không có scope → thông báo thay vì render grid rỗng.
        var hasScope = await _training.HasScopeAsync(empcd);
        ViewBag.HasScope = hasScope;

        var from = DateTime.Today;
        var to   = today == 1 ? DateTime.Today : DateTime.Today.AddDays(7);
        ViewBag.From = from.ToString("yyyy-MM-dd");
        ViewBag.To   = to.ToString("yyyy-MM-dd");
        ViewBag.Today = today == 1;

        return View();
    }

    // GET /Training/GetTeamSchedule?from=&to=&status=  (AJAX)
    [HttpGet]
    public async Task<IActionResult> GetTeamSchedule(
        DateTime? from = null,
        DateTime? to = null,
        string? status = null)
    {
        var empcd = CurrentUser?.EmpCd ?? "";
        if (string.IsNullOrEmpty(empcd))
            return Json(new { success = false, message = "Chưa đăng nhập" });

        var data = await _training.GetTeamScheduleAsync(empcd, from, to, status);
        return Json(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════
    //  §14 REPORTS — HR/Admin
    // ═══════════════════════════════════════════════════════════════

    // GET /Training/Reports — landing (chọn Class, chọn loại report)
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> Reports(int? classId = null, string? tab = null)
    {
        var classes = await _training.GetClassListAsync();
        ViewBag.Classes = classes;
        ViewBag.SelectedClassId = classId;
        ViewBag.Tab = tab ?? "class";
        return View();
    }

    // AJAX: GET /Training/GetReportClass?classId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetReportClass(int classId)
    {
        var data = await _training.GetClassReportAsync(classId);
        return Json(new { success = data != null, data });
    }

    // AJAX: GET /Training/GetReportAttendance?classId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetReportAttendance(int classId)
    {
        var data = await _training.GetAttendanceMatrixAsync(classId);
        return Json(new { success = data != null, data });
    }

    // AJAX: GET /Training/GetReportTest?testId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetReportTest(int testId)
    {
        var data = await _training.GetTestReportAsync(testId);
        return Json(new { success = data != null, data });
    }

    // AJAX: GET /Training/GetReportSatisfaction?classId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetReportSatisfaction(int classId)
    {
        var data = await _training.GetSatisfactionReportAsync(classId);
        return Json(new { success = data != null, data });
    }

    // ─── EXCEL EXPORTS ─────────────────────────────────────────────

    // GET /Training/ExportClass?classId= — §14.1 Excel
    [HttpGet, Authorize(Roles = "Admin,HR")]
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
        ws.Cell(r++, 1).Value = "Avg attendance %";              ws.Cell(r-1, 2).Value = report.AVG_ATTENDANCE_PERCENT ?? 0;
        ws.Cell(r++, 1).Value = "Avg final score";               ws.Cell(r-1, 2).Value = report.AVG_FINAL_SCORE ?? 0;

        // Histogram sheet
        var wsH = wb.Worksheets.Add("Score histogram");
        WriteHeader(wsH, 1, new[] { "Bucket", "Count" });
        for (int i = 0; i < report.SCORE_HISTOGRAM.Count; i++)
        {
            wsH.Cell(i + 2, 1).Value = report.SCORE_HISTOGRAM[i].LABEL;
            wsH.Cell(i + 2, 2).Value = report.SCORE_HISTOGRAM[i].COUNT;
        }
        wsH.Columns().AdjustToContents();

        // Group breakdown sheet
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

    // GET /Training/ExportAttendance?classId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ExportAttendance(int classId)
    {
        var m = await _training.GetAttendanceMatrixAsync(classId);
        if (m == null) return NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Attendance matrix");

        // Header row: EMPCD | Name | Group | Attendance% | Session 1 | Session 2 | ...
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
                ws.Cell(i + 2, col++).Value = st.STATUS_PER_SESSION.TryGetValue(key, out var v) ? v : "";
            }
        }

        ws.Columns().AdjustToContents();
        return BuildXlsx(wb, $"report_attendance_{classId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    // GET /Training/ExportTest?testId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ExportTest(int testId)
    {
        var r = await _training.GetTestReportAsync(testId);
        if (r == null) return NotFound();

        using var wb = new XLWorkbook();
        var wsS = wb.Worksheets.Add("Scores");
        WriteHeader(wsS, 1, new[] { "EMPCD", "Name", "Score", "Max score", "Pass", "Status", "Submit" });
        for (int i = 0; i < r.SCORES.Count; i++)
        {
            var s = r.SCORES[i];
            wsS.Cell(i + 2, 1).Value = s.EMPCD;
            wsS.Cell(i + 2, 2).Value = s.EMP_NAME ?? "";
            wsS.Cell(i + 2, 3).Value = s.SCORE ?? 0;
            wsS.Cell(i + 2, 4).Value = s.MAX_SCORE ?? 0;
            wsS.Cell(i + 2, 5).Value = s.IS_PASS == 1 ? "PASS" : (s.IS_PASS == 0 ? "FAIL" : "-");
            wsS.Cell(i + 2, 6).Value = s.STATUS;
            wsS.Cell(i + 2, 7).Value = s.SUBMIT_DT?.ToString("dd/MM/yyyy HH:mm") ?? "";
        }
        wsS.Columns().AdjustToContents();

        // Summary
        var wsSum = wb.Worksheets.Add("Summary");
        WriteHeader(wsSum, 1, new[] { "Metric", "Value" });
        var rows = new (string, object)[]
        {
            ("Test title",     r.TEST_TITLE),
            ("Pass score",     r.PASS_SCORE ?? 0),
            ("Attempt count",  r.ATTEMPT_COUNT),
            ("Pass count",     r.PASS_COUNT),
            ("Fail count",     r.FAIL_COUNT),
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

        // Top wrong
        var wsW = wb.Worksheets.Add("Top wrong questions");
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

        return BuildXlsx(wb, $"report_test_{testId}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    // GET /Training/ExportSatisfaction?classId=
    [HttpGet, Authorize(Roles = "Admin,HR")]
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
    //  STUDENT VIEWS
    // ═══════════════════════════════════════════════════════════════

    public IActionResult Index()
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        return View("Student/Index");
    }

    public IActionResult ClassDetail(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View("Student/ClassDetail");
    }

    public IActionResult TestAttempt(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;
        return View("Student/TestAttempt");
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER VIEWS
    // ═══════════════════════════════════════════════════════════════

    public IActionResult TeacherDashboard()
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        return View("Teacher/Dashboard");
    }

    public IActionResult TeacherAttendance(int sessionId)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.SessionId = sessionId;
        return View("Teacher/Attendance");
    }

    public IActionResult TestGrading(int testId)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.TestId = testId;
        return View("Teacher/TestGrading");
    }

    // ═══════════════════════════════════════════════════════════════
    //  STUDENT AJAX PROXIES
    // ═══════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetMyClasses(string empcd)
    {
        var res = await _training.GetFromApiAsync<object>("Training/my-classes", $"empcd={empcd}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetClassDetail(int classId, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"Training/class/{classId}/detail", $"empcd={empcd}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetClassTests(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/test/list", $"classId={classId}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> RegisterClass(int classId, [FromBody] SelfRegisterRequest req)
    {
        var response = await _training.PostToApiAsync($"Training/class/{classId}/register", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetSessionDetail(int sessionId, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"Training/session/{sessionId}/detail", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> SessionCheckIn([FromBody] CheckInRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/session/checkin", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> TrackMaterialView([FromBody] MaterialViewRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/material/view", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetQAList(int classId, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"Training/class/{classId}/qa", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> AskQuestion([FromBody] AskQuestionRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/qa/ask", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetTestForStudent(int id, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"Training/test/{id}", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> StartTest([FromBody] StartAttemptRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/test/start", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveTestAnswer([FromBody] SaveAnswerRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/test/save-answer", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SubmitTest([FromBody] SubmitAttemptRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/test/submit", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetTestResult(int id, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"Training/test/{id}/my-result", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("Training/review/submit", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetMyReview(int id, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"Training/class/{id}/my-review", $"empcd={empcd}");
        return Json(res);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER AJAX PROXIES
    // ═══════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetTeacherClasses(string empcd)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingTeach/my-classes", $"empcd={empcd}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetTeacherAttendanceView(int sessionId, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/session/{sessionId}/attendance", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmAttendance([FromBody] ConfirmAttendanceRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/session/confirm", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmAttendanceBatch([FromBody] ConfirmAttendanceBatchRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/session/confirm-batch", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetAbsentStats(int classId, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/class/{classId}/absent-stats", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> DropStudent([FromBody] DropStudentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/enrollment/drop", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterial([FromBody] SaveMaterialRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/material/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMaterial([FromBody] DeleteMaterialRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/material/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> QAAnswer([FromBody] AnswerQuestionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/qa/answer", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> QADelete([FromBody] DeleteQuestionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/qa/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> TestSave([FromBody] SaveTestRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> TestQuestionsSave([FromBody] SaveTestQuestionsRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/questions/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> TestPublish([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/publish", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingGrade(int id, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/test/{id}/pending-grade", $"empcd={empcd}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> Grade([FromBody] GradeAnswerRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/grade", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // ═══════════════════════════════════════════════════════════════
    //  HR/ADMIN VIEWS & AJAX PROXIES
    // ═══════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetClassList(string? status = null, string? search = null, int? courseId = null)
    {
        var res = await _training.GetClassListAsync(status, search, courseId);
        return Json(new { success = true, data = res });
    }

    [Authorize(Roles = "Admin,HR")]
    public IActionResult Manage()
    {
        return View("Admin/Manage");
    }

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetCourses(string? mode = null, int? active = null, string? search = null)
    {
        var qs = "";
        if (!string.IsNullOrEmpty(mode)) qs += $"mode={mode}&";
        if (active.HasValue)             qs += $"active={active.Value}&";
        if (!string.IsNullOrEmpty(search)) qs += $"search={Uri.EscapeDataString(search)}&";

        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/course/list", qs);
        return Json(res);
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveCourse([FromBody] SaveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveClass([FromBody] SaveClassRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [Authorize(Roles = "Admin,HR")]
    public IActionResult ClassDetailAdmin(int id)
    {
        ViewBag.ClassId = id;
        return View("Admin/ClassDetailAdmin");
    }

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetClassDetailAdmin(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/class/detail", $"id={classId}");
        return Json(res);
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveSession([FromBody] SaveSessionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/session/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AssignTeacher([FromBody] AssignTeacherRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/assign-teacher", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> RemoveTeacher([FromBody] RemoveTeacherRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/remove-teacher", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
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

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ApproveEnrollment([FromBody] ApproveEnrollmentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/approve", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> RejectEnrollment([FromBody] RejectEnrollmentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/reject", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveGroup([FromBody] SaveGroupRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/group/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AutoSplitGroup([FromBody] AutoSplitRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/group/auto-split", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AssignGroup([FromBody] AssignGroupRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/assign-group", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveAdminTest([FromBody] SaveTestRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveAdminTestQuestions([FromBody] SaveTestQuestionsRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/questions/save", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> PublishAdminTest([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/publish", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // ═══════════════════════════════════════════════════════════════
    //  LIFECYCLE PROXIES (Class, Session, Enrollment, Certificate)
    // ═══════════════════════════════════════════════════════════════

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ArchiveCourse([FromBody] ArchiveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/archive", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> UnarchiveCourse([FromBody] ArchiveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/unarchive", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> DeleteCourse([FromBody] ArchiveCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/course/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> PublishRegistration([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/publish-registration", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> FinalizeEnrollment([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/finalize-enrollment", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CancelClass([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/cancel", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CloseClass([FromBody] ChangeClassStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/close", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> ExpressCreate([FromBody] ExpressCreateRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/express-create", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CloneFromCourse([FromBody] CloneFromCourseRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/class/clone-from-course", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> RescheduleSession([FromBody] RescheduleSessionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/session/reschedule", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CancelSession([FromBody] CancelSessionRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/session/cancel", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> PreAssignEnrollment([FromBody] BulkPreAssignRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/pre-assign", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> RecoverEnrollment([FromBody] RecoverEnrollmentRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/enrollment/recover", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CloseAdminTest([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/test/close", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetReviewReport(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/review/report", $"classId={classId}");
        return Json(res);
    }

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetCertificateList(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/certificate/list", $"classId={classId}");
        return Json(res);
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> RevokeCertificate([FromBody] RevokeCertificateRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingAdmin/certificate/revoke", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetEnrollments(int classId, string? status = null)
    {
        var qs = $"status={status}";
        var res = await _training.GetFromApiAsync<object>($"TrainingAdmin/class/{classId}/enrollments", qs);
        return Json(res);
    }

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
