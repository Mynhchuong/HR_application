using HR_web.API.Service;
using HR_web.ViewModels.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

[Authorize]
public class HomeController : BaseController
{
    private readonly HomeApiService _homeApi;

    public HomeController(HomeApiService homeApi)
    {
        _homeApi = homeApi;
    }

    // ============================================================
    // GET / hoặc /Home/Index — dispatcher theo role
    // ============================================================
    public async Task<IActionResult> Index()
    {
        var user = CurrentUser;
        if (user == null || string.IsNullOrEmpty(user.EmpCd))
            return RedirectToAction("Login", "Account");

        var role = user.RoleName ?? "Employee";
        var vm   = await BuildViewModelAsync(user.EmpCd, role, user.FullName);

        // Dispatcher: chọn view theo role
        if (role is "HR" or "Admin")     return View("Admin",    vm);
        if (role == "Expat")             return View("Expat",    vm);
        if (role is "Supervisor" or "DeputyManager" or "Manager" or "Assistant")
                                          return View("Manager",  vm);

        return View("Employee", vm);  // Employee, Clerk, role lạ
    }

    // ============================================================
    // GET /Home/Summary — AJAX polling từ Summary Card partial
    // ============================================================
    [HttpGet]
    public async Task<IActionResult> Summary(int force = 0)
    {
        if (CurrentUser?.EmpCd == null) return Json(new { success = false });

        var data = await _homeApi.GetSummaryAsync(CurrentUser.EmpCd, CurrentUser.RoleName, force == 1);
        return Json(new { success = data != null, data });
    }

    // ============================================================
    // GET /Home/TeamBirthday — modal list khi click chip
    // ============================================================
    [HttpGet]
    public async Task<IActionResult> TeamBirthday()
    {
        if (CurrentUser?.EmpCd == null) return Json(new { success = false });

        var data = await _homeApi.GetTeamBirthdayAsync(CurrentUser.EmpCd, CurrentUser.RoleName);
        return Json(new { success = data != null, data });
    }

    // ============================================================
    // Build VM từ API response — map DTO → VM
    // ============================================================
    private async Task<HomeIndexViewModel> BuildViewModelAsync(string empCd, string roleName, string? fullName)
    {
        var vm = new HomeIndexViewModel
        {
            EmpCd    = empCd,
            RoleName = roleName,
            FullName = fullName ?? ""
        };

        var data = await _homeApi.GetInitAsync(empCd, roleName, fullName);
        if (data == null) return vm;   // API fail → view render với data trống

        if (data.Greeting != null)
        {
            vm.Greeting = new HomeGreetingVm { Message = data.Greeting.MESSAGE, Emoji = data.Greeting.EMOJI };
        }

        if (data.Birthday != null)
        {
            vm.Birthday = new HomeBirthdayVm
            {
                Type    = data.Birthday.TYPE,
                EmpName = data.Birthday.EMP_NAME,
                Message = data.Birthday.MESSAGE,
                Emoji   = data.Birthday.EMOJI,
                Years   = data.Birthday.YEARS
            };
        }

        if (data.Payday != null && data.Payday.IS_PAYDAY)
        {
            vm.Payday = new HomePaydayVm
            {
                ActualDate = data.Payday.ACTUAL_DATE,
                Message    = data.Payday.MESSAGE,
                Emoji      = data.Payday.EMOJI,
                CtaUrl     = data.Payday.CTA_URL
            };
        }

        if (data.Shift != null && !string.IsNullOrEmpty(data.Shift.SHIFTCD))
        {
            // Ca đêm = STIME > ETIME (wrap qua nửa đêm, vd K3 "2230"→"0630")
            bool isNight = false;
            if (int.TryParse(data.Shift.STIME, out var s) &&
                int.TryParse(data.Shift.ETIME, out var e))
                isNight = s > e;

            vm.Shift = new HomeShiftVm
            {
                ShiftCd      = data.Shift.SHIFTCD,
                StartTime    = FormatShiftTime(data.Shift.STIME),
                EndTime      = FormatShiftTime(data.Shift.ETIME),
                WorkDate     = data.Shift.WORK_DATE,
                IsNightShift = isNight
            };
        }

        if (data.Banner != null)
        {
            vm.Banner = new HomeBannerVm
            {
                Id             = data.Banner.ID,
                // Route: ImageController.GetHomeBanner(fileName)
                ImageUrl       = Url.Action("GetHomeBanner", "Image", new { fileName = data.Banner.IMAGE_FILE }) ?? "",
                OverlayText    = data.Banner.OVERLAY_TEXT,
                OverlayPos     = data.Banner.OVERLAY_POS,
                LinkUrl        = SanitizeLinkUrl(data.Banner.LINK_URL),
                LinkTarget     = data.Banner.LINK_TARGET == "_blank" ? "_blank" : "_self",
                IsDismissible  = data.Banner.IS_DISMISSIBLE
            };
        }

        vm.Pinned = (data.Pinned ?? new()).Select(p => new HomePinnedItemVm
        {
            Id       = p.ID,
            Title    = p.TITLE,
            CoverImg = p.COVER_IMG
        }).ToList();

        return vm;
    }

    // "0600" → "06:00", "1430" → "14:30"
    private static string? FormatShiftTime(string? hhmm)
    {
        if (string.IsNullOrEmpty(hhmm) || hhmm.Length != 4) return hhmm;
        return $"{hhmm[..2]}:{hhmm[2..]}";
    }

    // Sanitize LinkUrl để chống XSS qua javascript:/data:/vbscript:
    // (rule §8 Security). Chỉ chấp nhận http(s):// hoặc bắt đầu bằng /
    private static string? SanitizeLinkUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("/")) return trimmed;
        if (trimmed.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return null;   // reject javascript:, data:, vbscript:, mailto:, tel:, v.v.
    }

    // ============================================================
    // Error page (giữ nguyên)
    // ============================================================
    [AllowAnonymous]
    public IActionResult Error() => View();
}
