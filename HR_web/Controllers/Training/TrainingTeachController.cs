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
    public IActionResult MyClasses()
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        return View();
    }

    // GET /TrainingTeach/ClassManage/{id}
    public IActionResult ClassManage(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingTeach/SessionAttendance/{id}
    public IActionResult SessionAttendance(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.SessionId = id;
        return View();
    }

    // GET /TrainingTeach/TestCreate/{id}
    public IActionResult TestCreate(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingTeach/TestGrade/{id}
    public IActionResult TestGrade(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;
        return View();
    }

    // GET /TrainingTeach/UploadMaterial/{id}
    public IActionResult UploadMaterial(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /TrainingTeach/QA/{id}
    public IActionResult QA(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
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
        var res = await _training.GetFromApiAsync<object>("TrainingTeach/my-classes", $"empcd={empcd}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetClassDetail(int classId)
    {
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/class/detail", $"id={classId}");
        return Json(res);
    }

    [HttpGet]
    public async Task<IActionResult> GetTeacherAttendanceView(int sessionId, string empcd)
    {
        var res = await _training.GetFromApiAsync<object>($"TrainingTeach/session/{sessionId}/attendance", $"empcd={empcd}");
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
        var attRes = await _training.GetFromApiAsync<SessionAttendanceViewApiResponse>($"TrainingTeach/session/{req.SESSION_ID}/attendance", "");
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
        var response = await _training.PostToApiAsync("TrainingTeach/material/upload", req);
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

    [HttpPost]
    public async Task<IActionResult> TestDelete([FromBody] ChangeTestStatusRequest req)
    {
        req.LOGIN_USER = CurrentUser?.EmpCd ?? "";
        var response = await _training.PostToApiAsync("TrainingTeach/test/delete", req);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // GET /TrainingTeach/TestPreview/{id}
    public IActionResult TestPreview(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetTestDetailForPreview(int id)
    {
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
