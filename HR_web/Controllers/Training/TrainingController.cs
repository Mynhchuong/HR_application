using HR_web.API.Service;
using HR_web.Models.Training;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Training;

[Authorize]
public class TrainingController : BaseController
{
    private readonly TrainingService _training;

    public TrainingController(TrainingService training)
    {
        _training = training;
    }

    // GET /Training/MyClasses (or general entry index redirecting to MyClasses)
    public IActionResult Index()
    {
        return RedirectToAction("MyClasses");
    }

    // GET /Training/MyClasses
    public IActionResult MyClasses()
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        return View();
    }

    // GET /Training/ClassDetail/{id}
    public IActionResult ClassDetail(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /Training/SessionDetail/{id}
    public IActionResult SessionDetail(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.SessionId = id;
        return View();
    }

    // GET /Training/QA/{id}
    public IActionResult QA(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /Training/Materials/{id}
    public IActionResult Materials(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /Training/TestDo/{id}
    public IActionResult TestDo(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;
        return View();
    }

    // GET /Training/TestResult/{id}
    public IActionResult TestResult(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.TestId = id;
        return View();
    }

    // GET /Training/Review/{id}
    public IActionResult Review(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
    }

    // GET /Training/BulletinRegister/{id}
    public IActionResult BulletinRegister(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return RedirectToAction("Login", "Account");
        ViewBag.EmpCd = empcd;
        ViewBag.ClassId = id;
        return View();
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
    public async Task<IActionResult> GetMyCertificates(string empcd)
    {
        var loginUser = CurrentUser?.EmpCd ?? "";
        if (string.IsNullOrEmpty(loginUser)) return Json(new { success = false, message = "Chưa đăng nhập" });
        var res = await _training.GetFromApiAsync<object>("TrainingAdmin/certificate/list", $"loginUser={loginUser}&empcd={loginUser}");
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
        var empcd = CurrentUser?.EmpCd ?? "";
        var res = await _training.GetFromApiAsync<object>($"Training/class/{classId}/tests", $"empcd={empcd}");
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
    [HttpGet]
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
        var response = await _training.PostToApiAsync($"Training/material/{req.MATERIAL_ID}/view", req);
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
    [HttpGet]
    public async Task<IActionResult> SubmitReview([FromBody] SubmitReviewRequest req)
    {
        req.EMPCD = CurrentUser?.EmpCd ?? "";
        
        // Fetch class detail to get the teachers of the class
        var detailRes = await _training.GetFromApiAsync<ClassDetailApiResponse>($"Training/class/{req.CLASS_ID}/detail", $"empcd={req.EMPCD}");
        var teachers = detailRes?.data?.teachers ?? new List<ClassTeacherApiItem>();

        // Map web model properties to backend API expected properties
        var apiReq = new
        {
            CLASS_ID = req.CLASS_ID,
            EMPCD = req.EMPCD,
            CONTENT_RATING = (int)req.RATING_CONTENT,
            ORGANIZATION_RATING = (int)req.RATING_TEACHER,
            FEEDBACK_TEXT = req.COMMENT_TEXT,
            TEACHER_RATINGS = teachers.Select(t => new {
                TEACHER_EMPCD = t.EMPCD,
                RATING = (int)req.RATING_TEACHER,
                FEEDBACK_TEXT = (string?)null
            }).ToList()
        };

        var response = await _training.PostToApiAsync("Training/review/submit", apiReq);
        if (response == null) return Json(new { success = false, message = "Lỗi kết nối API" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetMyReview(int id, string empcd)
    {
        var apiRes = await _training.GetFromApiAsync<GetMyReviewApiResponse>($"Training/class/{id}/my-review", $"empcd={empcd}");
        if (apiRes?.success == true && apiRes.data != null)
        {
            var webReview = new
            {
                RATING_CONTENT = apiRes.data.CONTENT_RATING,
                RATING_TEACHER = apiRes.data.ORGANIZATION_RATING,
                COMMENT_TEXT = apiRes.data.FEEDBACK_TEXT
            };
            return Json(new { success = true, data = webReview });
        }
        return Json(new { success = true, data = (object?)null });
    }
}
