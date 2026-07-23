using HR_web.API.Service;
using HR_web.Models.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Training;

[Authorize]
public class TrainingTeachController : BaseController
{
    private readonly TrainingService _training;

    public TrainingTeachController(TrainingService training)
    {
        _training = training;
    }

    // GET /TrainingTeach/MyClasses
    public async Task<IActionResult> MyClasses()
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        bool isTeacher = await _training.IsActiveTeacherAsync(empcd);
        if (!isTeacher && !isHrOrAdmin) return Forbid();

        ViewBag.EmpCd = empcd;
        return View();
    }

    // GET /TrainingTeach/ClassManage/{id}
    public async Task<IActionResult> ClassManage(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckClassAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingTeach/SessionAttendance/{id}
    public async Task<IActionResult> SessionAttendance(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckSessionAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.SessionId = id;
        return View();
    }

    // GET /TrainingTeach/TestCreate/{id}?testId= — testId có giá trị → mở form ở chế độ Sửa
    // (chỉ cho sửa khi bài thi còn DRAFT — khớp ràng buộc TrainingTestService.SaveAsync).
    public async Task<IActionResult> TestCreate(int id, int? testId = null)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckClassAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;

        if (testId.HasValue)
        {
            var detail = await _training.GetTestDetailAsync(testId.Value);
            if (detail?.test == null || detail.test.CLASS_ID != id || detail.test.STATUS != "DRAFT")
                return RedirectToAction("ClassManage", new { id });

            ViewBag.EditTestId = testId.Value;
            ViewBag.EditData = System.Text.Json.JsonSerializer.Serialize(new
            {
                title = detail.test.TITLE,
                description = detail.test.DESCRIPTION,
                durationMinutes = detail.test.DURATION_MINUTES,
                passScore = detail.test.PASS_SCORE,
                maxAttempts = detail.test.MAX_ATTEMPTS,
                questions = detail.questions ?? new List<TestQuestionInputModel>(),
            });
        }

        return View();
    }

    // GET /TrainingTeach/TestGrade/{id}
    public async Task<IActionResult> TestGrade(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckTestAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;

        // Lấy CLASS_ID của test để nút "Quay về quản lý" trỏ đúng lớp (không phải cổng giảng dạy chung).
        // API test/detail trả data = { test, questions } → CLASS_ID nằm trong data.test.
        var testRes = await _training.GetFromApiAsync<object>("TrainingAdmin/test/detail", $"id={id}");
        var classIdStr = ((testRes as Newtonsoft.Json.Linq.JObject)?["data"]?["test"]?["CLASS_ID"])?.ToString();
        ViewBag.ClassId = int.TryParse(classIdStr, out var cid) ? cid : (int?)null;

        return View();
    }

    // GET /TrainingTeach/TestReport/{id} — điểm học viên + chi tiết câu sai cho giáo viên đứng lớp
    public async Task<IActionResult> TestReport(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckTestAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;

        var testRes = await _training.GetFromApiAsync<object>("TrainingAdmin/test/detail", $"id={id}");
        var classIdStr = ((testRes as Newtonsoft.Json.Linq.JObject)?["data"]?["test"]?["CLASS_ID"])?.ToString();
        ViewBag.ClassId = int.TryParse(classIdStr, out var cid) ? cid : (int?)null;

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetTestReport(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });

        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/test/{id}/report", $"empcd={empcd}");
        return Json(res);
    }

    // GET /TrainingTeach/UploadMaterial/{id}
    public async Task<IActionResult> UploadMaterial(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckClassAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingTeach/QA/{id}
    public async Task<IActionResult> QA(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckClassAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER AJAX PROXIES
    // ═══════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetTeacherClasses(string empcd)
    {
        var loginUser = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(loginUser)) return Forbid();

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && empcd != loginUser) return Forbid();

        var res = await _training.GetFromApiAsync<object>("TrainingTeach/my-classes", $"empcd={empcd}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetClassDetail(int classId)
    {
        var loginUser = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(loginUser)) return Forbid();

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckClassAccessAsync(classId, loginUser)) return Forbid();

        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/class/detail", $"id={classId}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetTeacherAttendanceView(int sessionId, string empcd)
    {
        var loginUser = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(loginUser)) return Forbid();

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckSessionAccessAsync(sessionId, loginUser)) return Forbid();

        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/session/{sessionId}/attendance", $"empcd={loginUser}");
        if (res == null) return Json(new { success = false, status = 403, message = "Bạn không có quyền truy cập thông tin buổi học này" });
        return Json(res);
    }

    [HttpPost]
    [HttpGet]
    public async Task<IActionResult> ConfirmAttendance([FromBody] ConfirmAttendanceRequest req)
    {
        var apiReq = new
        {
            SESSION_ID = req.SESSION_ID,
            EMPCD = req.ATTENDEE_EMPCD,
            STATUS = req.ATTENDANCE_STATUS,
            NOTE = req.REASON,
            LOGIN_USER = CurrentUser?.EmpCd ?? ""
        };
        var response = await _training.PostToApiAsync($"TrainingTeach/session/confirm", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    [HttpGet]
    public async Task<IActionResult> ConfirmAttendanceBatch([FromBody] ConfirmAttendanceBatchRequest req)
    {
        var loginUser = CurrentUser?.EmpCd ?? "";

        // 1. Fetch the list of students in the session to get their EMPCD and GROUP_NAME
        var attRes = await _training.GetFromApiAsync<SessionAttendanceViewApiResponse>($"TrainingTeach/session/{req.SESSION_ID}/attendance", $"empcd={loginUser}");
        var allItems = attRes?.data?.ATTENDANCE ?? new List<AttendanceModelApiItem>();

        // 2. Filter students if group filter is active
        if (!string.IsNullOrWhiteSpace(req.GROUP_NAME))
        {
            allItems = allItems.Where(i => i.GROUP_NAME == req.GROUP_NAME).ToList();
        }

        // 3. Map to ConfirmAttendanceBatchRequest structure expected by API
        var apiReq = new
        {
            SESSION_ID = req.SESSION_ID,
            LOGIN_USER = loginUser,
            ITEMS = allItems.Select(i => new
            {
                EMPCD = i.EMPCD,
                STATUS = req.ATTENDANCE_STATUS,
                NOTE = (string?)null
            }).ToList()
        };

        var response = await _training.PostToApiAsync($"TrainingTeach/session/confirm-batch", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // "Lưu tất cả" — nhận đúng danh sách trạng thái/lý do đã chọn từng học viên từ client
    // (không re-fetch/áp đồng loạt như ConfirmAttendanceBatch ở trên), forward thẳng qua API
    // session/confirm-batch (API đã hỗ trợ ITEMS với STATUS/NOTE riêng từng dòng sẵn từ trước).
    [HttpPost]
    public async Task<IActionResult> ConfirmAttendanceBulk([FromBody] ConfirmAttendanceBulkRequest req)
    {
        var loginUser = CurrentUser?.EmpCd ?? "";
        var apiReq = new
        {
            SESSION_ID = req.SESSION_ID,
            LOGIN_USER = loginUser,
            ITEMS = req.ITEMS.Select(i => new { EMPCD = i.EMPCD, STATUS = i.STATUS, NOTE = i.NOTE }).ToList()
        };

        var response = await _training.PostToApiAsync($"TrainingTeach/session/confirm-batch", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetAbsentStats(int classId, string empcd)
    {
        var loginUser = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(loginUser)) return Forbid();

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckClassAccessAsync(classId, loginUser)) return Forbid();

        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/class/{classId}/absent-stats", $"empcd={loginUser}");
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
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[API Error Details]: {json}");
        }
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
    public async Task<IActionResult> GetCourseTests(int classId)
    {
        var data = await _training.GetCourseTestsAsync(classId);
        return Json(new { success = data != null, data });
    }

    [HttpPost]
    public async Task<IActionResult> CopyTest([FromBody] CopyTestRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/copy", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // Sửa lại ngày mở/đóng cho bài đã publish (không đổi từ nháp — dùng TestPublish cho việc đó)
    [HttpPost]
    public async Task<IActionResult> TestUpdateSchedule([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/update-schedule", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetPendingGrade(int id, string empcd)
    {
        var loginUser = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(loginUser)) return Forbid();

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckTestAccessAsync(id, loginUser)) return Forbid();

        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/test/{id}/pending-grade", $"empcd={loginUser}");
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

    [HttpPost]
    public async Task<IActionResult> TestDelete([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> TestClose([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/close", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // GET /TrainingTeach/TestPreview/{id}
    public async Task<IActionResult> TestPreview(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckTestAccessAsync(id, empcd)) return Forbid();

        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetTestDetailForPreview(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Forbid();

        bool isHrOrAdmin = CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";
        if (!isHrOrAdmin && !await _training.CheckTestAccessAsync(id, empcd)) return Forbid();

        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/test/detail", $"id={id}");
        return Json(res);
    }

    [HttpPost]
    public async Task<IActionResult> SetFinalTest([FromBody] SetFinalTestWebRequest req)
    {
        var loginUser = CurrentUser?.EmpCd ?? "";
        var apiReq = new {
            CLASS_ID = req.CLASS_ID,
            TEST_ID = req.TEST_ID,
            LOGIN_USER = loginUser
        };
        var response = await _training.PostToApiAsync("TrainingTeach/class/set-final-test", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }
}

public class SetFinalTestWebRequest
{
    public int CLASS_ID { get; set; }
    public int? TEST_ID { get; set; }
}
