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
            return View(data);
        }
        catch (Exception ex)
        {
            ViewBag.Error = ex.Message;
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
            var result = await _otService.ConfirmOTAsync(empcd, confirmStatus, work_date, ot_hours);

            if (result.success)
                TempData["Success"] = result.message;
            else
                TempData["Error"] = result.message ?? "Có lỗi xảy ra";

            return RedirectToAction("OtConfirmForm", new { work_date });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("OtConfirmForm", new { work_date });
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
        int page = 1, int page_size = 50)
    {
        try
        {
            var result = await _otService.GetOTHRDetailAsync(
                work_date, dept_id, search, status, dept_name, line_name, line_id, work_id, page, page_size);
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
    // GET: /OT/OtListForClerk
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

            var filterType = CurrentUser.FilterType;
            if (string.IsNullOrEmpty(filterType))
                return Json(new { success = false, message = "Chưa được phân quyền bộ phận" });

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
    // GET: /OT/GetOTClerkDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTClerkDetailPage(
        string? work_date = null, string? status = null,
        string? search = null, string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _otService.GetOTClerkDetailAsync(CurrentUser.EmpCd, work_date, status, search, line_id, work_id, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}
