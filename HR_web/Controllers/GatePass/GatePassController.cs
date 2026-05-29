using HR_web.API.Service;
using HR_web.Models.GatePass;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.GatePass;

[Authorize]
public class GatePassController : BaseController
{
    private readonly GatePassService _gpService;

    public GatePassController(GatePassService gpService)
    {
        _gpService = gpService;
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GpRequestForm
    // ─────────────────────────────────────────────
    public IActionResult GpRequestForm()
    {
        ViewBag.Today    = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.Tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // POST: /GatePass/GpRequestForm
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> GpRequestForm(string gp_type, string reg_date, string? out_time, string? in_time, string? reason)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return RedirectToAction("Login", "Account");

            var request = new GpSubmitRequest
            {
                EMPCD    = CurrentUser.EmpCd,
                REG_DATE = reg_date,
                GP_TYPE  = gp_type,
                OUT_TIME = out_time,
                IN_TIME  = in_time,
                REASON   = reason
            };

            var result = await _gpService.SubmitAsync(request);

            if (result.success)
                TempData["SuccessMessage"] = result.message ?? "Đăng ký Gate Pass thành công";
            else
                TempData["ErrorMessage"] = result.message ?? "Có lỗi xảy ra";

            return RedirectToAction("GpRequestForm");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("GpRequestForm");
        }
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GetShiftInfo (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetShiftInfo(string? reg_date)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false });

            var result = await _gpService.GetShiftInfoAsync(CurrentUser.EmpCd, reg_date);
            return Json(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GpMyRequests
    // ─────────────────────────────────────────────
    public IActionResult GpMyRequests()
    {
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GetMyRequestsPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMyRequestsPage(int page = 1, int page_size = 20, string? date_from = null, string? date_to = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _gpService.GetMyRequestsAsync(CurrentUser.EmpCd, page, page_size, date_from, date_to);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /GatePass/UpdateRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UpdateRequest([FromBody] GpUpdateRequest model)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            model.EMPCD = CurrentUser.EmpCd;
            var result  = await _gpService.UpdateAsync(model);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /GatePass/DeleteRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> DeleteRequest(string request_id)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _gpService.DeleteAsync(request_id, CurrentUser.EmpCd);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GpListForSupervisor
    // ─────────────────────────────────────────────
    public IActionResult GpListForSupervisor()
    {
        ViewBag.DateFrom     = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.DateTo       = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GetSupervisorPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetSupervisorPage(
        string? status = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            // Admin bypass: dùng HR endpoint (toàn công ty, không cần scope)
            if (CurrentUser.RoleName == "Admin")
            {
                var adminResult = await _gpService.GetHRListAsync(
                    status, search, dept_id, line_id, work_id,
                    date_from, date_to, page, page_size);
                return Json(adminResult);
            }

            if (string.IsNullOrEmpty(CurrentUser.FilterType))
                return Json(new { success = false, message = "Chưa được phân quyền bộ phận" });

            var result = await _gpService.GetSupervisorListAsync(
                CurrentUser.EmpCd, status, search, dept_id, line_id, work_id,
                date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /GatePass/ApproveRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ApproveRequest(string request_id, string? comment = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _gpService.ApproveAsync(request_id, CurrentUser.EmpCd, comment);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /GatePass/RejectRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> RejectRequest(string request_id, string? comment = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _gpService.RejectAsync(request_id, CurrentUser.EmpCd, comment);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GpListForClerk
    // ─────────────────────────────────────────────
    public IActionResult GpListForClerk()
    {
        ViewBag.DateFrom = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GetClerkPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetClerkPage(
        string? status = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            // Admin bypass
            if (CurrentUser.RoleName == "Admin")
            {
                var adminResult = await _gpService.GetHRListAsync(
                    status, search, dept_id, line_id, work_id,
                    date_from, date_to, page, page_size);
                return Json(adminResult);
            }

            var result = await _gpService.GetClerkListAsync(
                CurrentUser.EmpCd, status, search, dept_id, line_id, work_id,
                date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GpListForExpat
    // ─────────────────────────────────────────────
    public IActionResult GpListForExpat()
    {
        ViewBag.DateFrom     = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.DateTo       = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GpListForHR
    // ─────────────────────────────────────────────
    public IActionResult GpListForHR()
    {
        ViewBag.DateFrom = DateTime.Today.ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /GatePass/GetHRPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetHRPage(
        string? status = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _gpService.GetHRListAsync(
                status, search, dept_id, line_id, work_id,
                date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}
