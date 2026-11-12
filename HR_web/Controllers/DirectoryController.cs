using HR_web.API.Service;
using HR_web.Models.Directory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Trang xem thông tin đồng nghiệp - không yêu cầu đăng nhập.
[AllowAnonymous]
public class DirectoryController : BaseController
{
    private readonly DirectoryService _service;
    private const int HistoryPageSize = 5;

    public DirectoryController(DirectoryService service)
    {
        _service = service;
    }

    /// <summary>
    /// Chọn view theo trạng thái đăng nhập:
    ///   - đã đăng nhập  -> "IndexInApp" : chạy trong layout app nên CÓ sidenav/header
    ///     (Views/_ViewStart.cshtml tự chọn _LayoutExpat cho NV nước ngoài, _Layout cho role khác).
    ///   - chưa đăng nhập -> "Index"      : trang đứng một mình (quét QR ẩn danh / vào từ trang Login).
    /// Trước đây dùng chung 1 view Layout = null nên NV đang đăng nhập bị mất menu và tưởng app
    /// lỗi (phản ánh 2026-11-11). Controller vẫn [AllowAnonymous], chỉ khác chỗ render.
    /// </summary>
    private string ViewNameFor() => CurrentUser != null ? "IndexInApp" : "Index";

    // GET: /Directory/Index?empCd=xxx&historyPage=1
    public async Task<IActionResult> Index(string? empCd, int historyPage = 1)
    {
        if (string.IsNullOrWhiteSpace(empCd))
            return View(ViewNameFor(), new DirectoryPageModel());

        try
        {
            var employeeTask = _service.GetEmployeeAsync(empCd.Trim());
            var historyTask = _service.GetChangeHistoryAsync(empCd.Trim(), historyPage, HistoryPageSize);
            await Task.WhenAll(employeeTask, historyTask);

            var employee = employeeTask.Result;
            if (employee == null)
            {
                TempData["ErrorMessage"] = "Employee not found!";
                return View(ViewNameFor(), new DirectoryPageModel { Employee = new EmployeeDirectoryModel { EmpCd = empCd } });
            }

            return View(ViewNameFor(), new DirectoryPageModel { Employee = employee, ChangeHistory = historyTask.Result });
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred, please try again!";
            return View(ViewNameFor(), new DirectoryPageModel { Employee = new EmployeeDirectoryModel { EmpCd = empCd } });
        }
    }
}
