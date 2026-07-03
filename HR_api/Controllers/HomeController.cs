using HR_api.Models.Home;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class HomeController : ControllerBase
{
    private readonly HomeService           _homeSvc;
    private readonly HomeSummaryService    _summarySvc;
    private readonly HomeBirthdayService   _birthdaySvc;
    private readonly HomeMyCalendarService _myCalSvc;

    public HomeController(
        HomeService           homeSvc,
        HomeSummaryService    summarySvc,
        HomeBirthdayService   birthdaySvc,
        HomeMyCalendarService myCalSvc)
    {
        _homeSvc     = homeSvc;
        _summarySvc  = summarySvc;
        _birthdaySvc = birthdaySvc;
        _myCalSvc    = myCalSvc;
    }

    // ============================================================
    // GET /apiHR/Home/init?empcd=X&role_name=Y&full_name=Z
    // Trả gộp: greeting + birthday + payday + shift + banner + pinned
    // ============================================================
    [HttpGet("init")]
    public async Task<IActionResult> Init(string empcd, string? role_name = null, string? full_name = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            var user = new HomeUserContext
            {
                EMPCD    = empcd,
                ROLENAME = string.IsNullOrEmpty(role_name) ? "Employee" : role_name,
                FULLNAME = full_name
            };

            var data = await _homeSvc.BuildIndexAsync(user);
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // GET /apiHR/Home/summary?empcd=X&role_name=Y&force=0
    // Realtime card — Leave/GP/OT count + team birthday count
    // Cache 30s/EMPCD; force=1 bypass cache
    // ============================================================
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(string empcd, string? role_name = null, int force = 0)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            var user = new HomeUserContext
            {
                EMPCD    = empcd,
                ROLENAME = string.IsNullOrEmpty(role_name) ? "Employee" : role_name
            };

            var data = await _summarySvc.GetAsync(user, force: force == 1);
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // GET /apiHR/Home/team-birthday?empcd=X&role_name=Y
    // Danh sách NV có SN hôm nay (theo scope hoặc toàn cty)
    // Frontend gọi khi click chip trong Summary Card
    // ============================================================
    [HttpGet("team-birthday")]
    public async Task<IActionResult> TeamBirthday(string empcd, string? role_name = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            var user = new HomeUserContext
            {
                EMPCD    = empcd,
                ROLENAME = string.IsNullOrEmpty(role_name) ? "Employee" : role_name
            };

            var data = await _birthdaySvc.GetTeamBirthdayAsync(user);
            return Ok(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // GET /apiHR/Home/my-calendar?empcd=X&year=&month=
    // Lịch cá nhân — chỉ trạng thái ĐÃ HOÀN TẤT (approved/confirmed).
    // Nguồn: My Samho — không phải ERP.
    // ============================================================
    [HttpGet("my-calendar")]
    public async Task<IActionResult> MyCalendar(string empcd, int year = 0, int month = 0)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            if (year <= 0 || month <= 0 || month > 12)
            {
                var t = DateTime.Today;
                year  = t.Year;
                month = t.Month;
            }

            var data = await _myCalSvc.GetAsync(empcd, year, month);
            return Ok(new { success = true, year, month, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
