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

    public DirectoryController(DirectoryService service)
    {
        _service = service;
    }

    // GET: /Directory/Index?empCd=xxx
    public async Task<IActionResult> Index(string? empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd))
            return View(new DirectoryPageModel());

        try
        {
            var employeeTask = _service.GetEmployeeAsync(empCd.Trim());
            var historyTask = _service.GetChangeHistoryAsync(empCd.Trim());
            await Task.WhenAll(employeeTask, historyTask);

            var employee = employeeTask.Result;
            if (employee == null)
            {
                TempData["ErrorMessage"] = "Employee not found!";
                return View(new DirectoryPageModel { Employee = new EmployeeDirectoryModel { EmpCd = empCd } });
            }

            return View(new DirectoryPageModel { Employee = employee, ChangeHistory = historyTask.Result });
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred, please try again!";
            return View(new DirectoryPageModel { Employee = new EmployeeDirectoryModel { EmpCd = empCd } });
        }
    }
}
