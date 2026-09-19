using HR_web.API.Service;
using HR_web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace HR_web.Filters;

/// <summary>
/// Chặn app khi NV có quà ở trạng thái PENDING_CONFIRM (HR đã phát, đang chờ NV xác nhận đã
/// nhận) — mirror SurveyBlockerFilter. Whitelist: Account/Gift/GiftAdmin/Notification/Image/Video.
/// Khác Survey: phân biệt AJAX (trả 409 JSON để JS toàn cục tự điều hướng) vs điều hướng trang
/// thật (redirect thẳng, giống Survey) — vì hầu hết trang trong app load dữ liệu qua $.ajax.
/// Cache 30s per EMPCD để tránh gọi API mỗi request.
/// </summary>
public class GiftGateFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> WhitelistControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "account",                  // login/logout
        "gift",                     // trang xác nhận nhận quà của công nhân — bắt buộc
        "giftadmin",                // trang quản trị/báo cáo — tránh HR/Clerk tự khóa mình khi xử lý quà cho người khác
        "notification", "adminnoti",// xem thông báo
        "image", "video",           // asset serve từ Controller
    };

    private const string CACHE_PREFIX = "gift_pending_";
    private static readonly TimeSpan CACHE_TTL = TimeSpan.FromSeconds(30);

    private readonly GiftService _gift;
    private readonly IMemoryCache _cache;

    public GiftGateFilter(GiftService gift, IMemoryCache cache)
    {
        _gift  = gift;
        _cache = cache;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        bool anon = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();
        if (anon) { await next(); return; }

        var controllerName = (context.RouteData.Values["controller"]?.ToString() ?? "").ToLower();
        if (WhitelistControllers.Contains(controllerName)) { await next(); return; }

        var user = AuthHelper.GetCurrentUser(context.HttpContext.User);
        if (user == null || string.IsNullOrEmpty(user.EmpCd)) { await next(); return; }

        var cacheKey = CACHE_PREFIX + user.EmpCd;
        if (!_cache.TryGetValue<int?>(cacheKey, out var pendingId))
        {
            pendingId = await _gift.GetOldestPendingIdAsync(user.EmpCd);
            _cache.Set(cacheKey, pendingId, CACHE_TTL);
        }

        if (pendingId.HasValue && pendingId.Value > 0)
        {
            bool isAjax = string.Equals(
                context.HttpContext.Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

            if (isAjax)
            {
                context.Result = new ObjectResult(new { success = false, gift_pending = true, recipient_id = pendingId.Value }) { StatusCode = 409 };
            }
            else
            {
                context.Result = new RedirectToActionResult("MyGifts", "Gift", new { id = pendingId.Value });
            }
            return;
        }

        await next();
    }

    /// <summary>Invalidate cache sau khi NV xác nhận nhận quà.</summary>
    public static void InvalidateUser(IMemoryCache cache, string empcd)
    {
        if (!string.IsNullOrEmpty(empcd)) cache.Remove(CACHE_PREFIX + empcd);
    }
}
