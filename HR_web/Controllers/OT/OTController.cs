using HR_web.API.Service;
using HR_web.Models.OT;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.OT;

[Authorize]
public class OTController : BaseController
{
    private readonly OtService _otService;

    public OTController(OtService otService)
    {
        _otService = otService;
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtConfirmForm
    // ─────────────────────────────────────────────
    public async Task<IActionResult> OtConfirmForm(string? work_date = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return RedirectToAction("Login", "Account");

            string selectedDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
            var data = await _otService.GetOTTodayAsync(CurrentUser.EmpCd, selectedDate);
            if (data == null)
                ViewBag.Error = "Không có dữ liệu OT hoặc không thể kết nối máy chủ.";

            ViewBag.WorkDate = selectedDate;
            ViewBag.HasSignature = CurrentUser?.SIGNATUREBLOB == "Y";
            return View(data);
        }
        catch (Exception ex)
        {
            ViewBag.Error = ex.Message;
            ViewBag.HasSignature = CurrentUser?.SIGNATUREBLOB == "Y";
            return View((OTTodayModel?)null);
        }
    }

    // ─────────────────────────────────────────────
    // POST: /OT/OtConfirmForm
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> OtConfirmForm(string empcd, string confirmStatus, string work_date, decimal ot_hours)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(work_date) || !DateTime.TryParse(work_date, out _))
                return Json(new { success = false, message = "Ngày tăng ca không hợp lệ" });

            if (ot_hours <= 0)
                return Json(new { success = false, message = "Số giờ tăng ca không hợp lệ" });

            var result = await _otService.ConfirmOTAsync(empcd, confirmStatus, work_date, ot_hours);
            return Json(new { success = result.success, message = result.message ?? (result.success ? "Thành công" : "Có lỗi xảy ra") });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForHR
    // ─────────────────────────────────────────────
    public IActionResult OtListForHR(string? work_date = null, string? dept_id = null)
    {
        ViewBag.Summary  = new List<OTHRSummaryModel>();
        ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        ViewBag.DeptId   = dept_id;
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTHRDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTHRDetailPage(
        string? work_date = null, string? dept_id = null,
        string? search = null, string? status = null,
        string? dept_name = null, string? line_name = null,
        string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 50, int admin = 0)
    {
        try
        {
            var result = await _otService.GetOTHRDetailAsync(
                work_date, dept_id, search, status, dept_name, line_name, line_id, work_id, page, page_size, admin);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForClerk
    // ─────────────────────────────────────────────
    public IActionResult OtListForClerk(string? work_date = null    )
    {
        ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        return View();
    }
    // ─────────────────────────────────────────────
    // GET: /OT/OtListForSupervisor
    // ─────────────────────────────────────────────
    public IActionResult OtListForSupervisor(string? work_date = null)
    {
        ViewBag.WorkDate        = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        ViewBag.FilterType      = CurrentUser?.FilterType      ?? "";
        ViewBag.FilterCodes     = CurrentUser?.FilterCodes     ?? new List<string>();
        ViewBag.FilterLineCodes = CurrentUser?.FilterLineCodes ?? new List<string>();
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForExpat
    // ─────────────────────────────────────────────
    public IActionResult OtListForExpat(string? work_date = null)
    {
        ViewBag.WorkDate        = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        ViewBag.FilterType      = CurrentUser?.FilterType      ?? "";
        ViewBag.FilterCodes     = CurrentUser?.FilterCodes     ?? new List<string>();
        ViewBag.FilterLineCodes = CurrentUser?.FilterLineCodes ?? new List<string>();
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTSupervisorDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTSupervisorDetailPage(
        string? work_date = null, string? status = null,
        string? search    = null,
        string? dept_id   = null,
        string? line_id   = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (CurrentUser == null || string.IsNullOrEmpty(CurrentUser.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            // filterType có thể empty với session cũ — API tự check HR_USERS_DEPT bằng empCd
            var filterType = CurrentUser.FilterType ?? "dept";

            var result = await _otService.GetOTSupervisorDetailAsync(
                CurrentUser.EmpCd, filterType, work_date, status, search, dept_id, line_id, work_id, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /OT/RemindPendingOTSign (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> RemindPendingOTSign(
        string? work_date, string? dept_id, string? line_id, string? work_id)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var (ok, msg, sent) = await _otService.RemindPendingOTSignAsync(
                CurrentUser.EmpCd, work_date ?? DateTime.Today.ToString("yyyy-MM-dd"),
                dept_id, line_id, work_id);
            return Json(new { success = ok, message = msg, sent });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTClerkDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTClerkDetailPage(
        string? work_date = null, string? status = null,
        string? search = null, string? dept_id = null, string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _otService.GetOTClerkDetailAsync(CurrentUser.EmpCd, work_date, status, search, dept_id, line_id, work_id, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /OT/NotifyPending — HR broadcast nhắc kí OT
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> NotifyPending([FromBody] NotifyPendingBody body)
    {
        if (body == null || string.IsNullOrEmpty(body.work_date))
            return Json(new { success = false, message = "Thiếu ngày làm việc" });

        var raw = await _otService.HrNotifyPendingRawAsync(body.work_date, body.dept_id, CurrentUser?.EmpCd);
        return Content(raw, "application/json");
    }

    public class NotifyPendingBody
    {
        public string? work_date { get; set; }
        public string? dept_id   { get; set; }
    }

    // ═════════════════════════════════════════════════════════════
    // ADMIN — trang quản lý OT (chỉ Admin/HR)
    // ═════════════════════════════════════════════════════════════

    // GET: /OT/OtListForAdmin
    [Authorize(Roles = "Admin,HR")]
    public IActionResult OtListForAdmin(string? work_date = null)
    {
        ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        return View();
    }

    // POST: /OT/AdminBulkSignFor
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AdminBulkSignFor([FromBody] OTAdminBulkSignForRequest body)
    {
        if (body == null) return Json(new { success = false, message = "Body rỗng" });
        body.ACTOR_EMPCD = CurrentUser?.EmpCd;
        var result = await _otService.AdminBulkSignForAsync(body);
        return Json(result);
    }

    // POST: /OT/AdminBulkUpdate
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AdminBulkUpdate([FromBody] OTAdminBulkUpdateRequest body)
    {
        if (body == null) return Json(new { success = false, message = "Body rỗng" });
        body.ACTOR_EMPCD = CurrentUser?.EmpCd;
        var result = await _otService.AdminBulkUpdateAsync(body);
        return Json(result);
    }

    // POST: /OT/AdminBulkDelete
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AdminBulkDelete([FromBody] OTAdminBulkDeleteRequest body)
    {
        if (body == null) return Json(new { success = false, message = "Body rỗng" });
        body.ACTOR_EMPCD = CurrentUser?.EmpCd;
        var result = await _otService.AdminBulkDeleteAsync(body);
        return Json(result);
    }
}
