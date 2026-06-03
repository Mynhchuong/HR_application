using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Calendar;

[Authorize]
public class CalendarController : BaseController
{
    private readonly CalendarService _calendarService;

    public CalendarController(CalendarService calendarService)
    {
        _calendarService = calendarService;
    }

    // GET /Calendar/MyCalendar
    public IActionResult MyCalendar()
    {
        return View();
    }

    // GET /Calendar/GetMonthly?year=2026&month=6  (AJAX)
    [HttpGet]
    public async Task<IActionResult> GetMonthly(int? year = null, int? month = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            int y = year  ?? DateTime.Today.Year;
            int m = month ?? DateTime.Today.Month;

            var result = await _calendarService.GetMonthlyAsync(CurrentUser.EmpCd, y, m);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}
