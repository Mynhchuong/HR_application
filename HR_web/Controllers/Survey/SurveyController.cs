using HR_web.API.Service;
using HR_web.Filters;
using HR_web.Models.Survey;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace HR_web.Controllers.Survey;

// User-facing MVC. Route "survey" đã whitelist trong SurveyBlockerFilter
// nên user có survey pending không bị vòng lặp redirect.
[Authorize]
public class SurveyController : BaseController
{
    private readonly SurveyService _svc;
    private readonly IMemoryCache _cache;

    public SurveyController(SurveyService svc, IMemoryCache cache)
    {
        _svc = svc;
        _cache = cache;
    }

    // ─────────────────────────────────────────────
    // GET /Survey/Do/{id}
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Do(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd))
            return RedirectToAction("Login", "Account");

        var (survey, response, error) = await _svc.GetDetailAsync(empcd, id);

        var vm = new SurveyDoViewModel
        {
            Survey   = survey,
            Response = response,
            Error    = error,
        };

        return View(vm);
    }

    // ─────────────────────────────────────────────
    // GET /Survey/CheckPending  (AJAX) — dùng để re-check ngay khi WebView phục hồi 1 trang
    // từ back-forward cache (nút back cứng Android) thay vì gửi request thật lên server, việc
    // đó khiến SurveyBlockerFilter không chạy lại được → user "thoát" được survey đang chặn.
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> CheckPending()
    {
        var user = CurrentUser;
        if (user == null || string.IsNullOrEmpty(user.EmpCd))
            return Json(new { pendingId = (int?)null });

        // Chỉ Admin (6) không cần làm survey — HR (5) vẫn phải làm.
        if (user.RoleId == 6)
            return Json(new { pendingId = (int?)null });

        var pendingId = await _svc.GetOldestPendingSurveyIdAsync(user.EmpCd, user.RoleId);
        return Json(new { pendingId });
    }

    // ─────────────────────────────────────────────
    // POST /Survey/SaveAnswer  (AJAX)
    // Body: { SURVEY_ID, QUESTION_ID, ANSWER_OPTION_IDS?, ANSWER_TEXT?, ANSWER_NUMBER? }
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerVm req)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });

        var payload = new
        {
            EMPCD             = empcd,
            SURVEY_ID         = req.SurveyId,
            QUESTION_ID       = req.QuestionId,
            ANSWER_OPTION_IDS = req.AnswerOptionIds,
            ANSWER_TEXT       = req.AnswerText,
            ANSWER_NUMBER     = req.AnswerNumber,
            IP_ADDRESS        = HttpContext.Connection.RemoteIpAddress?.ToString(),
            USER_AGENT        = Request.Headers["User-Agent"].ToString(),
        };

        var (ok, err) = await _svc.SaveAnswerAsync(payload);
        return Json(new { success = ok, message = err });
    }

    // ─────────────────────────────────────────────
    // POST /Survey/Submit  (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] SubmitVm req)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });

        var (result, err) = await _svc.SubmitAsync(empcd, req.SurveyId);
        if (result?.SUCCESS == true)
            SurveyBlockerFilter.InvalidateUser(_cache, empcd);
        return Json(new { success = result?.SUCCESS ?? false, message = err, data = result });
    }

    // ─────────────────────────────────────────────
    // POST /Survey/Skip  (AJAX)  — "Tôi không biết chữ"
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Skip([FromBody] SubmitVm req)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new { success = false, message = "Chưa đăng nhập" });

        var (ok, err) = await _svc.SkipIlliterateAsync(empcd, req.SurveyId);
        if (ok)
            SurveyBlockerFilter.InvalidateUser(_cache, empcd);
        return Json(new { success = ok, message = err });
    }

    // ─── Request models nhỏ ─────────────────────────

    public class SaveAnswerVm
    {
        public int      SurveyId        { get; set; }
        public int      QuestionId      { get; set; }
        public string?  AnswerOptionIds { get; set; }
        public string?  AnswerText      { get; set; }
        public decimal? AnswerNumber    { get; set; }
    }

    public class SubmitVm
    {
        public int SurveyId { get; set; }
    }
}
