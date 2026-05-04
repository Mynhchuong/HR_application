using HR_web.API.Service;
using HR_web.Models.Payslip;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

[Authorize]
public class PayslipController : BaseController
{
    private readonly PayslipService _payslipService;

    public PayslipController(PayslipService payslipService)
    {
        _payslipService = payslipService;
    }

    
    public async Task<IActionResult> Index()
    {
        var empCd = CurrentUser?.EmpCd;
        var periods = await _payslipService.GetPeriodsAsync(empCd);

        var filteredPeriods = periods
            .Where(x => x.IS_PUBLISHED == 1 ||
                        (x.IS_AUTO_PUBLISH == 1 && x.PUBLISH_DATE <= DateTime.Now))
            .OrderByDescending(x => x.INST_DT) 
            .ToList();

        var today = DateTime.Now.Date;
        var activePeriod = filteredPeriods
            .FirstOrDefault(x => x.START_DATE.HasValue && x.END_DATE.HasValue &&
                                 x.START_DATE.Value.Date <= today &&
                                 x.END_DATE.Value.Date >= today);

        ViewBag.Periods = filteredPeriods;
        ViewBag.DefaultPeriodId = activePeriod?.ID;

        return View();
    }

    
    [HttpGet]
    public async Task<IActionResult> GetMyPayslip(decimal periodId)
    {
        try
        {
            var data = await _payslipService.GetMyPayslipAsync(CurrentUser!.EmpCd, periodId);
            return Json(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /Payslip/Admin (HR quản lý kỳ lương)
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Admin()
    {
        // Kiểm tra quyền HR hoặc Admin
        if (CurrentUser == null ||
            (CurrentUser.RoleName != "HR" && CurrentUser.RoleName != "Admin"))
            return RedirectToAction("Index", "Home");

        var periods = await _payslipService.GetPeriodsAsync();
        ViewBag.Periods = periods.OrderByDescending(x => x.INST_DT).ToList(); // Mới nhất lên đầu
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Payslip/GetItemsVisibility?periodId=xxx (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetItemsVisibility(decimal periodId)
    {
        var items = await _payslipService.GetItemsVisibilityAsync(periodId);
        return Json(new { success = true, data = items });
    }

    
    [HttpPost]
    public async Task<IActionResult> UpdateItemsVisibility([FromBody] VisibilityUpdateRequest model)
    {
        if (CurrentUser?.RoleName != "HR" && CurrentUser?.RoleName != "Admin")
            return Json(new { success = false, message = "Từ chối truy cập" });

        if (model == null) return Json(new { success = false, message = "Invalid request" });

        var result = await _payslipService.UpdateItemsVisibilityAsync(model.PeriodId, model.Items);
        return Json(result);
    }


    [HttpPost]
    public async Task<IActionResult> UploadData([FromBody] UploadDataRequest model)
    {
        try
        {
            if (CurrentUser == null ||
                (CurrentUser.RoleName != "HR" && CurrentUser.RoleName != "Admin"))
                return Json(new { success = false, message = "Từ chối truy cập" });

            if (model == null || model.Data == null || model.Data.Count == 0)
                return Json(new { success = false, message = "Dữ liệu trống" });

            var result = await _payslipService.UploadPayslipAsync(model.PeriodId, model.Data, model.IsFirstBatch);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi xử lý tại Controller: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /Payslip/CreatePeriod (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreatePeriod(string periodName, DateTime? start, DateTime? end,
        DateTime? publishDate, int isAutoPublish, string remark)
    {
        if (CurrentUser?.RoleName != "HR" && CurrentUser?.RoleName != "Admin")
            return Json(new { success = false, message = "Từ chối truy cập" });

        var result = await _payslipService.CreatePeriodAsync(
            periodName, start, end, publishDate, isAutoPublish, remark, CurrentUser!.EmpCd);
        return Json(result);
    }

    // ─────────────────────────────────────────────
    // POST: /Payslip/ReleasePeriod (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> ReleasePeriod(decimal id)
    {
        if (CurrentUser?.RoleName != "HR" && CurrentUser?.RoleName != "Admin")
            return Json(new { success = false, message = "Từ chối truy cập" });

        var result = await _payslipService.ReleasePeriodAsync(id, CurrentUser!.EmpCd);
        return Json(result);
    }

    // ─────────────────────────────────────────────
    // GET: /Payslip/GetAdminList?periodId=xxx (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAdminList(decimal periodId, string? search = null, int page = 1, int pageSize = 100)
    {
        var result = await _payslipService.GetAdminListAsync(periodId, search, page, pageSize);
        return Json(result);  // Không cần MaxJsonLength trong .NET 8
    }

    // ─────────────────────────────────────────────
    // GET: /Payslip/GetExportData?periodId=xxx (AJAX)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetExportData(decimal periodId)
    {
        var result = await _payslipService.GetExportDataAsync(periodId);
        return Json(result);
    }
}
