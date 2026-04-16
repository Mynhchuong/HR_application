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
    public async Task<IActionResult> OtConfirmForm()
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return RedirectToAction("Login", "Account");

            var data = await _otService.GetOTTodayAsync(CurrentUser.EmpCd);
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
    public async Task<IActionResult> OtConfirmForm(string empcd, string confirmStatus, string? rejectReason = null)
    {
        try
        {
            var result = await _otService.ConfirmOTAsync(empcd, confirmStatus, rejectReason);

            if (result.success)
                TempData["Success"] = result.message;
            else
                TempData["Error"] = result.message ?? "Có lỗi xảy ra";

            return RedirectToAction("OtConfirmForm");
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("OtConfirmForm");
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForHR
    // ─────────────────────────────────────────────
    public async Task<IActionResult> OtListForHR(string? work_date = null, string? dept_id = null)
    {
        try
        {
            var summary = await _otService.GetOTHRSummaryAsync(work_date, dept_id);

            ViewBag.Summary = summary;
            ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
            ViewBag.DeptId = dept_id;

            return View();
        }
        catch (Exception ex)
        {
            ViewBag.Error = ex.Message;
            return View();
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTHRDetailPage (AJAX / JSON)
    // Bỏ JsonRequestBehavior.AllowGet - không cần trong .NET 8
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTHRDetailPage(
        string? work_date = null, string? dept_id = null,
        string? search = null, string? status = null,
        string? dept_name = null, string? line_name = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            var result = await _otService.GetOTHRDetailAsync(
                work_date, dept_id, search, status, dept_name, line_name, page, page_size);
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
    public async Task<IActionResult> OtListForClerk(string? work_date = null)
    {
        try
        {
            var clerk_empcd = CurrentUser?.EmpCd;

            if (string.IsNullOrEmpty(clerk_empcd))
                return RedirectToAction("Login", "Account");

            var data = await _otService.GetOTClerkAsync(clerk_empcd, work_date);

            ViewBag.Summary = data?.summary;
            ViewBag.DeptId = data?.dept_id;
            ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;

            return View(data?.data);
        }
        catch (Exception ex)
        {
            ViewBag.Error = ex.Message;
            return View((List<OTClerkModel>?)null);
        }
    }
}
