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

    // ─────────────────────────────────────────────
    // GET: /Payslip/Index (Nhân viên xem phiếu lương)
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        var periods = await _payslipService.GetPeriodsAsync();

        // Chỉ hiện kỳ đã publish hoặc auto-publish đã đến hạn
        ViewBag.Periods = periods
            .Where(x => x.IS_PUBLISHED == 1 ||
                        (x.IS_AUTO_PUBLISH == 1 && x.PUBLISH_DATE <= DateTime.Now))
            .ToList();

        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /Payslip/GetMyPayslip?periodId=xxx (AJAX)
    // Bỏ JsonRequestBehavior.AllowGet - không cần trong .NET 8
    // ─────────────────────────────────────────────
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
        ViewBag.Periods = periods;
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

    // ─────────────────────────────────────────────
    // POST: /Payslip/UpdateItemsVisibility (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UpdateItemsVisibility(decimal periodId, [FromBody] List<PayrollItemModel> items)
    {
        if (CurrentUser?.RoleName != "HR" && CurrentUser?.RoleName != "Admin")
            return Json(new { success = false, message = "Từ chối truy cập" });

        var result = await _payslipService.UpdateItemsVisibilityAsync(periodId, items);
        return Json(result);
    }

    // ─────────────────────────────────────────────
    // POST: /Payslip/UploadData (AJAX - upload hàng loạt)
    // Bỏ jsonResult.MaxJsonLength - không cần trong .NET 8
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> UploadData(decimal periodId, [FromBody] List<PayslipUploadRow> data, bool isFirstBatch = false)
    {
        try
        {
            if (CurrentUser == null ||
                (CurrentUser.RoleName != "HR" && CurrentUser.RoleName != "Admin"))
                return Json(new { success = false, message = "Từ chối truy cập" });

            if (data == null || data.Count == 0)
                return Json(new { success = false, message = "Dữ liệu trống" });

            var result = await _payslipService.UploadPayslipAsync(periodId, data, isFirstBatch);
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
