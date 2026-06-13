using HR_web.API.Service;
using HR_web.Models.Leave;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Leave;

[Authorize]
public class LeaveController : BaseController
{
    private readonly LeaveService    _leaveService;
    private readonly GatePassService _gatePassService;

    public LeaveController(LeaveService leaveService, GatePassService gatePassService)
    {
        _leaveService    = leaveService;
        _gatePassService = gatePassService;
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/LeaveMyRequests
    // ─────────────────────────────────────────────
    public IActionResult LeaveMyRequests()
    {
        ViewBag.DateFrom = DateTime.Today.AddMonths(-3).ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.AddMonths(3).ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetMyRequestsPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMyRequestsPage(
        string? source = null, int page = 1, int page_size = 20,
        string? date_from = null, string? date_to = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _leaveService.GetMyRequestsAsync(
                CurrentUser.EmpCd, source, page, page_size, date_from, date_to);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/CreateRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] LeaveCreateRequest model)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            model.EMPCD = CurrentUser.EmpCd;
            var result  = await _leaveService.CreateAsync(model);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/UpdateRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UpdateRequest([FromBody] LeaveUpdateRequest model)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            model.EMPCD = CurrentUser.EmpCd;
            var result  = await _leaveService.UpdateAsync(model);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/DeleteRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> DeleteRequest(string request_id)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.DeleteAsync(request_id, CurrentUser.EmpCd);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/ConfirmLeave (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ConfirmLeave(string request_id)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.ConfirmAsync(request_id, CurrentUser.EmpCd);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/LeaveApprovalForExpat
    // ─────────────────────────────────────────────
    public IActionResult LeaveApprovalForExpat()
    {
        ViewBag.DateFrom     = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        ViewBag.DateTo       = DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd");
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd  ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/LeaveApprovalList
    // ─────────────────────────────────────────────
    public IActionResult LeaveApprovalList()
    {
        ViewBag.DateFrom     = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        ViewBag.DateTo       = DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd");
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd  ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetApprovalListPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetApprovalListPage(
        string? status = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            if (CurrentUser.RoleName == "Admin")
            {
                var adminRes = await _leaveService.GetHRListAsync(
                    status, null, search, dept_id, line_id, work_id,
                    date_from, date_to, page, page_size);
                return Json(adminRes);
            }

            var result = await _leaveService.GetApprovalListAsync(
                CurrentUser.EmpCd, status, search, dept_id, line_id, work_id,
                date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/ApproveRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ApproveRequest(string request_id, string? comment = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.ApproveAsync(request_id, CurrentUser.EmpCd, comment);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/RejectRequest (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> RejectRequest(string request_id, string? comment = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.RejectAsync(request_id, CurrentUser.EmpCd, comment);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/TeamCalendar
    // ─────────────────────────────────────────────
    public IActionResult TeamCalendar()
    {
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd   ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        ViewBag.CurrentMonth = DateTime.Today.Month;
        ViewBag.CurrentYear  = DateTime.Today.Year;
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/TeamCalendarForExpat
    // ─────────────────────────────────────────────
    public IActionResult TeamCalendarForExpat()
    {
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd   ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        ViewBag.CurrentMonth = DateTime.Today.Month;
        ViewBag.CurrentYear  = DateTime.Today.Year;
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/TeamSchedule
    // ─────────────────────────────────────────────
    public IActionResult TeamSchedule()
    {
        ViewBag.CurrentEmpCd = CurrentUser?.EmpCd   ?? "";
        ViewBag.CurrentRole  = CurrentUser?.RoleName ?? "";
        ViewBag.CurrentMonth = DateTime.Today.Month;
        ViewBag.CurrentYear  = DateTime.Today.Year;
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetTeamSchedulePage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetTeamSchedulePage(int? month = null, int? year = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetTeamScheduleAsync(CurrentUser.EmpCd, month, year);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/AssignLeave (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> AssignLeave([FromBody] LeaveAssignRequest model)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            model.ASSIGNER_EMPCD = CurrentUser.EmpCd;
            var result = await _leaveService.AssignAsync(model);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetMyAssignmentsPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMyAssignmentsPage(
        string? status = null, string? search = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 20)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetMyAssignmentsAsync(
                CurrentUser.EmpCd, status, search, date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/LeaveAssignmentLog
    // ─────────────────────────────────────────────
    public IActionResult LeaveAssignmentLog()
    {
        ViewBag.DateFrom = DateTime.Today.AddMonths(-3).ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.AddMonths(3).ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetAssignmentLogPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAssignmentLogPage(
        string? assigner_cd = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? status = null, string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetAssignmentLogAsync(
                assigner_cd, search, dept_id, line_id, work_id,
                status, date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetAnnualLeaveBalance (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAnnualLeaveBalance()
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetAnnualLeaveBalanceAsync(CurrentUser.EmpCd);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetMyBalance (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetMyBalance()
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetMyBalanceAsync(CurrentUser.EmpCd);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/LeaveListForHR
    // ─────────────────────────────────────────────
    public IActionResult LeaveListForHR()
    {
        ViewBag.DateFrom = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/AdminAssignLeave
    // ─────────────────────────────────────────────
    public IActionResult AdminAssignLeave()
    {
        if (CurrentUser?.RoleName != "Admin")
            return Forbid();
        ViewBag.CurrentEmpCd = CurrentUser.EmpCd;
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetAdminEmpList (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAdminEmpList(
        string? search = null, string? dept_id = null,
        string? line_id = null, string? work_id = null)
    {
        try
        {
            if (CurrentUser?.RoleName != "Admin")
                return Json(new { success = false, message = "Không có quyền truy cập" });
            var result = await _leaveService.GetAdminEmpListAsync(search, dept_id, line_id, work_id);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // POST: /Leave/AdminAssign (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> AdminAssign([FromBody] LeaveAssignRequest model)
    {
        try
        {
            if (CurrentUser?.RoleName != "Admin")
                return Json(new { success = false, message = "Không có quyền truy cập" });
            model.ASSIGNER_EMPCD = CurrentUser.EmpCd;
            var result = await _leaveService.AdminAssignAsync(model);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/LeaveListForClerk
    // ─────────────────────────────────────────────
    public IActionResult LeaveListForClerk()
    {
        if (CurrentUser?.RoleName != "Clerk" && CurrentUser?.RoleName != "Admin")
            return Forbid();
        ViewBag.DateFrom = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd");
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetClerkListPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetClerkListPage(
        string? status = null, string? source = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetClerkListAsync(
                CurrentUser.EmpCd, status, source, search, dept_id, line_id, work_id,
                date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/GetHRListPage (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetHRListPage(
        string? status = null, string? source = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _leaveService.GetHRListAsync(
                status, source, search, dept_id, line_id, work_id,
                date_from, date_to, page, page_size);
            return Json(result);
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /Leave/AdminManageRequests
    // ─────────────────────────────────────────────
    public IActionResult AdminManageRequests()
    {
        if (CurrentUser?.RoleName != "Admin") return Forbid();
        ViewBag.CurrentEmpCd = CurrentUser.EmpCd;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminConfirmedLeaves(
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null, string? status = null,
        int page = 1, int page_size = 100)
    {
        if (CurrentUser?.RoleName != "Admin") return Forbid();
        var result = await _leaveService.GetAdminConfirmedLeavesAsync(dept_id, line_id, work_id, date_from, date_to, status, page, page_size);
        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> AdminDeleteLeaves([FromBody] AdminBulkDeleteRequest model)
    {
        if (CurrentUser?.RoleName != "Admin") return Forbid();
        model.ACTOR_EMPCD = CurrentUser.EmpCd ?? "";
        var result = await _leaveService.AdminDeleteLeavesAsync(model);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAdminConfirmedGp(
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null, int page = 1, int page_size = 100)
    {
        if (CurrentUser?.RoleName != "Admin") return Forbid();
        var result = await _gatePassService.GetAdminConfirmedGpAsync(dept_id, line_id, work_id, date_from, date_to, page, page_size);
        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> AdminDeleteGp([FromBody] AdminBulkDeleteRequest model)
    {
        if (CurrentUser?.RoleName != "Admin") return Forbid();
        model.ACTOR_EMPCD = CurrentUser.EmpCd ?? "";
        var result = await _gatePassService.AdminDeleteGpAsync(model);
        return Json(result);
    }

}
