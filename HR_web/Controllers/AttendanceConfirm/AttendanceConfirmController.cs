using HR_web.API.Service;
using HR_web.Models.AttendanceConfirm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.AttendanceConfirm;

[Authorize]
public class AttendanceConfirmController : BaseController
{
    private readonly AttendanceConfirmService _svc;

    public AttendanceConfirmController(AttendanceConfirmService svc)
    {
        _svc = svc;
    }

    // GET: /AttendanceConfirm/Index — quản lý (scope dept/line/work) + Admin/HR/Clerk (toàn bộ)
    [Authorize(Roles = "Admin,HR,Clerk,Supervisor,DeputyManager,Manager,Expat")]
    public IActionResult Index()
    {
        // Mặc định đầu tháng hiện tại -> hôm nay (yêu cầu HR/Clerk 2026-09-18: xem theo tháng, tháng
        // nào cũng phát sinh nhiều — rolling 30 ngày trước đây dễ lệch qua tháng trước gây khó theo dõi).
        ViewBag.DateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.ToString("yyyy-MM-dd");
        return View();
    }

    // GET: /AttendanceConfirm/WorkerForm?date=yyyy-MM-dd — công nhân khai giờ vào/ra 1 ngày
    public IActionResult WorkerForm(string? date)
    {
        ViewBag.WorkDate = string.IsNullOrEmpty(date) ? DateTime.Today.ToString("yyyy-MM-dd") : date;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetList(
        string? dept_id, string? line_id, string? work_id, string? search, string? status, string? missing_type,
        string? date_from, string? date_to, int page = 1, int page_size = 50)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        var result = await _svc.GetMissingListAsync(
            CurrentUser.EmpCd, dept_id, line_id, work_id, search, status, missing_type, date_from, date_to, page, page_size);
        return Json(result);
    }

    // GET: /AttendanceConfirm/GetMyPending — badge menu sidebar "Xác nhận chấm công" + gợi ý ngày ở WorkerForm
    [HttpGet]
    public async Task<IActionResult> GetMyPending()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, count = 0, data = new List<object>() });
        var result = await _svc.GetMyPendingAsync(CurrentUser.EmpCd);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetMyDay(string date)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        var result = await _svc.GetMyDayAsync(CurrentUser.EmpCd, date);
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitWorker([FromBody] WorkerSubmitRequest req)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        req.EMPCD = CurrentUser.EmpCd; // NV chỉ khai được cho chính mình
        var result = await _svc.SubmitWorkerAsync(req);
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ManagerConfirm([FromBody] ManagerConfirmRequest req)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        req.ACTOR_EMPCD = CurrentUser.EmpCd;
        var result = await _svc.ManagerConfirmAsync(req);
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,HR,Clerk,Supervisor,DeputyManager,Manager,Expat")]
    public async Task<IActionResult> RequestConfirm([FromBody] List<RequestConfirmTarget> targets)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        if (targets == null || targets.Count == 0) return Json(new { success = false, message = "Chưa chọn nhân viên/ngày nào" });

        var req = new RequestConfirmBulkRequest
        {
            ITEMS = targets.Select(t => new RequestConfirmItem
            {
                EMPCD = t.EMPCD, WORK_DATE = t.WORK_DATE, ACTOR_EMPCD = CurrentUser.EmpCd
            }).ToList()
        };
        var result = await _svc.RequestConfirmAsync(req);
        return Json(result);
    }

    public class RequestConfirmTarget { public string EMPCD { get; set; } = ""; public string WORK_DATE { get; set; } = ""; }
}
