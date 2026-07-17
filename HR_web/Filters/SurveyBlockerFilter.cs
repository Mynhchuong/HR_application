using HR_web.API.Service;
using HR_web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace HR_web.Filters;

/// <summary>
/// Chặn app khi NV có survey ACTIVE chưa làm — redirect về /Survey/Do/{id}.
/// Whitelist: Account/Survey/SurveyAdmin/Notification/Image/Video (theo survey_rules §6).
/// Cache 30s per EMPCD để tránh gọi API mỗi request.
/// </summary>
public class SurveyBlockerFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> WhitelistControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "account", "profile",       // login/logout/profile
        "survey", "surveyadmin",    // chính feature — luôn cho qua
        "surveyexempt",             // trang blacklist (Phase 5)
        "notification", "adminnoti",// xem thông báo
        "image", "video",           // asset serve từ Controller
        "dropdown",                 // metadata (dept/line/work) — wizard SurveyAdmin cần
    };

    private const string CACHE_PREFIX = "survey_pending_";
    private static readonly TimeSpan CACHE_TTL = TimeSpan.FromSeconds(30);

    private readonly SurveyService _survey;
    private readonly IMemoryCache _cache;

    public SurveyBlockerFilter(SurveyService survey, IMemoryCache cache)
    {
        _survey = survey;
        _cache  = cache;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Bỏ qua AllowAnonymous
        bool anon = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();
        if (anon) { await next(); return; }

        // Whitelist controllers
        var controllerName = (context.RouteData.Values["controller"]?.ToString() ?? "").ToLower();
        if (WhitelistControllers.Contains(controllerName)) { await next(); return; }

        // Cần có user đăng nhập
        var user = AuthHelper.GetCurrentUser(context.HttpContext.User);
        if (user == null || string.IsNullOrEmpty(user.EmpCd)) { await next(); return; }

        // Chỉ Admin (6) không cần làm survey — HR (5) vẫn phải làm.
        if (user.RoleId == 6) { await next(); return; }

        // Cache 30s để tránh spam API
        var cacheKey = CACHE_PREFIX + user.EmpCd;
        if (!_cache.TryGetValue<int?>(cacheKey, out var pendingId))
        {
            pendingId = await _survey.GetOldestPendingSurveyIdAsync(user.EmpCd, user.RoleId);
            _cache.Set(cacheKey, pendingId, CACHE_TTL);
        }

        if (pendingId.HasValue && pendingId.Value > 0)
        {
            context.Result = new RedirectToActionResult("Do", "Survey", new { id = pendingId.Value });
            return;
        }

        await next();
    }

    /// <summary>Invalidate cache sau khi user submit / skip survey.</summary>
    public static void InvalidateUser(IMemoryCache cache, string empcd)
    {
        if (!string.IsNullOrEmpty(empcd)) cache.Remove(CACHE_PREFIX + empcd);
    }
}
