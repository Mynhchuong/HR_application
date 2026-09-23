using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace HR_web.Filters;

/// <summary>
/// Đăng ký DÙNG CHUNG cho MỌI "app gate" (Survey/Gift/Banner sau này...) — 1 nơi duy nhất để mỗi
/// gate tự động biết controller của các gate KHÁC mà miễn trừ, không phải tự khai whitelist riêng.
///
/// Lý do cần cái này (bug thật đã xảy ra trước khi refactor): SurveyBlockerFilter và GiftGateFilter
/// mỗi cái tự giữ 1 whitelist riêng, không biết controller của gate kia. NV vừa có survey vừa có quà
/// PENDING cùng lúc: Survey chặn NV về /Survey/Do → nhưng "survey" không nằm trong whitelist của
/// Gift nên GiftGateFilter lại chặn tiếp, đá qua /Gift/MyGifts → "gift" lại không nằm trong whitelist
/// của Survey nên bị đá ngược lại /Survey/Do → lặp vô hạn. Union danh sách ở đây giải quyết tận gốc:
/// mọi gate tự exempt controller của MỌI gate khác đã đăng ký, thêm gate mới chỉ cần gọi Register 1
/// lần, không phải sửa lại các gate cũ.
/// </summary>
public static class AppGateRegistry
{
    // Common: mọi gate luôn cho qua — đăng nhập, hồ sơ cá nhân, ảnh/video asset, thông báo,
    // dropdown metadata (report/filter cần gọi được dù đang bị gate nào đó chặn).
    public static readonly HashSet<string> CommonWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "account", "profile", "notification", "adminnoti", "image", "video", "dropdown",
    };

    // Trang ĐÍCH của mỗi gate (nơi BuildRedirect() đưa NV tới, vd "survey"/"gift") — PHẢI miễn trừ
    // CHÉO khỏi MỌI gate khác, đây mới là nguyên nhân gốc của bug loop đã sửa (NV vừa có survey vừa
    // có quà: gate A đá qua trang đích gate A, gate B chưa biết nên chặn tiếp, đá ngược lại — lặp
    // vô hạn). Dùng chung 1 set vì tính chất "là đích của 1 gate thì phải né được MỌI gate" áp dụng
    // như nhau cho tất cả.
    private static readonly HashSet<string> _destinations = new(StringComparer.OrdinalIgnoreCase);

    // Trang QUẢN TRỊ riêng của từng gate (vd "giftadmin", "surveyadmin") — chỉ cần miễn trừ khỏi
    // ĐÚNG gate của nó (để HR/Admin thao tác quản trị không bị chính gate đó chặn giữa chừng vì họ
    // cũng có 1 phần việc cá nhân đang PENDING), KHÔNG miễn trừ khỏi các gate KHÁC. Trước đây gộp
    // chung với _destinations vào 1 whitelist union duy nhất → lỗ hổng thật: NV có Survey chưa làm
    // vẫn né được bằng cách vào /GiftAdmin/... (không phải đích của gate nào cả) vì "giftadmin" tự
    // động được mọi gate khác miễn trừ theo (bug phát hiện 2026-09-23, sửa tại đây).
    private static readonly Dictionary<string, HashSet<string>> _ownAdminByGate =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _lock = new();

    /// <summary>Gọi 1 lần (static ctor của gate con) — controller ĐÍCH mà BuildRedirect() trỏ tới,
    /// cần miễn trừ khỏi MỌI gate (kể cả gate khác) để tránh loop.</summary>
    public static void RegisterDestination(params string[] controllers)
    {
        lock (_lock)
        {
            foreach (var c in controllers) _destinations.Add(c);
        }
    }

    /// <summary>Gọi 1 lần (static ctor của gate con) — controller quản trị riêng của gate này, chỉ
    /// miễn trừ khỏi ĐÚNG gate này (gateName), không ảnh hưởng tới các gate khác.</summary>
    public static void RegisterOwnAdmin(string gateName, params string[] controllers)
    {
        lock (_lock)
        {
            if (!_ownAdminByGate.TryGetValue(gateName, out var set))
                _ownAdminByGate[gateName] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in controllers) set.Add(c);
        }
    }

    public static bool IsExempt(string gateName, string controllerName) =>
        CommonWhitelist.Contains(controllerName)
        || _destinations.Contains(controllerName)
        || (_ownAdminByGate.TryGetValue(gateName, out var set) && set.Contains(controllerName));

    public static void InvalidateCache(IMemoryCache cache, string cachePrefix, string empcd)
    {
        if (!string.IsNullOrEmpty(empcd)) cache.Remove(cachePrefix + empcd);
    }
}

/// <summary>
/// Base cho các gate kiểu "bắt buộc NV xử lý xong 1 việc trước khi dùng tiếp app" (làm survey, xác
/// nhận nhận quà, ...) — redirect trang thật hoặc trả 409 JSON nếu là AJAX/fetch. Rút từ
/// SurveyBlockerFilter + GiftGateFilter (2 filter gần như y hệt nhau: whitelist + cache 30s per
/// EMPCD + phân biệt AJAX/fetch vs điều hướng trang thật). Gate mới chỉ cần kế thừa và khai:
///  - GateName, CachePrefix, danh sách controller riêng (Register trong static ctor)
///  - LoadPendingAsync: còn gì pending không
///  - BuildRedirect: điều hướng trang thật đi đâu (dùng lại NGUYÊN để tự suy ra URL cho case AJAX,
///    khỏi phải khai URL 2 lần ở 2 chỗ dễ lệch nhau)
/// KHÔNG cần copy lại phần cache/AJAX-detect/whitelist — sửa 1 chỗ, mọi gate ăn theo.
///
/// Response AJAX/fetch chuẩn hoá {success:false, gate, redirect_url} cho MỌI gate — JS phía client
/// (app-gate.js) chỉ cần đọc field chung này, thêm gate mới KHÔNG phải sửa JS.
///
/// Thứ tự ưu tiên khi nhiều gate cùng pending: theo thứ tự đăng ký trong Program.cs
/// (options.Filters.Add) — gate đăng ký trước chặn trước, gate sau không chạy tới vì filter trước
/// không gọi next(). Đổi mật khẩu bắt buộc (RequireUpdateProfileFilter) vẫn đứng riêng, KHÔNG kế
/// thừa base này — đó là gate ưu tiên cao nhất, cố ý chặn tất cả kể cả trang các gate khác.
/// </summary>
public abstract class AppGateFilterBase<TPending> : IAsyncActionFilter where TPending : class
{
    protected static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;

    protected AppGateFilterBase(IMemoryCache cache) => _cache = cache;

    /// <summary>Tên ngắn gọn nhận diện gate (vd "survey", "gift") — trả về nguyên trong JSON AJAX.</summary>
    protected abstract string GateName { get; }

    protected abstract string CachePrefix { get; }

    /// <summary>NV này còn gì đang PENDING cần xử lý không (null = không có, cho qua).</summary>
    protected abstract Task<TPending?> LoadPendingAsync(string empcd, UserInfoModel user);

    /// <summary>Điều hướng trang thật (F5, click link) khi có pending — cũng dùng để tự suy URL cho JSON AJAX.</summary>
    protected abstract RedirectToActionResult BuildRedirect(TPending pending);

    /// <summary>Gate này có áp dụng cho user hiện tại không (vd Survey: Admin miễn). Mặc định: có.</summary>
    protected virtual bool AppliesTo(UserInfoModel user) => true;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        bool anon = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();
        if (anon) { await next(); return; }

        var controllerName = (context.RouteData.Values["controller"]?.ToString() ?? "").ToLower();
        if (AppGateRegistry.IsExempt(GateName, controllerName)) { await next(); return; }

        var user = AuthHelper.GetCurrentUser(context.HttpContext.User);
        if (user == null || string.IsNullOrEmpty(user.EmpCd) || !AppliesTo(user)) { await next(); return; }

        var cacheKey = CachePrefix + user.EmpCd;
        if (!_cache.TryGetValue(cacheKey, out TPending? pending))
        {
            pending = await LoadPendingAsync(user.EmpCd, user);
            _cache.Set(cacheKey, pending, CacheTtl);
        }

        if (pending != null)
        {
            var redirect = BuildRedirect(pending);

            // X-Requested-With chỉ jQuery $.ajax tự gắn — fetch() (home.js, TrainingTeach, chat...)
            // không có, sẽ bị coi nhầm là điều hướng trang thật rồi redirect thẳng, khiến code gọi
            // fetch().then(r => r.json()) parse nhầm HTML của trang đích. Thêm check Accept: điều
            // hướng trang thật của trình duyệt luôn có "text/html" trong Accept; mọi lời gọi lấy dữ
            // liệu (cả $.ajax lẫn fetch mặc định "*/*") thì không.
            var acceptHeader = context.HttpContext.Request.Headers["Accept"].ToString();
            bool isAjax = string.Equals(
                context.HttpContext.Request.Headers["X-Requested-With"].ToString(),
                "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || !acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase);

            if (isAjax)
            {
                // Suy URL từ CHÍNH redirect trang thật (Url.Action tôn trọng PathBase khi deploy
                // dưới virtual directory) — chỉ khai đích đến 1 lần duy nhất ở BuildRedirect.
                var urlHelper = context.HttpContext.RequestServices
                    .GetRequiredService<IUrlHelperFactory>().GetUrlHelper(context);
                var url = urlHelper.Action(redirect.ActionName, redirect.ControllerName, redirect.RouteValues);

                context.Result = new ObjectResult(new { success = false, gate = GateName, redirect_url = url })
                { StatusCode = 409 };
            }
            else
            {
                context.Result = redirect;
            }
            return;
        }

        await next();
    }
}
