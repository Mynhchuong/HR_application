using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace HR_web.Filters;

/// <summary>
/// Biến lỗi "token chống giả mạo không hợp lệ" (HTTP 400 trắng trơn) thành hành vi dễ hiểu.
///
/// VÌ SAO CẦN: token chống giả mạo của ASP.NET Core có NHÚNG danh tính người đăng nhập. Trang được
/// render lúc còn phiên, nhưng cookie đăng nhập hết hạn tuyệt đối sau 24h (Auth:ExpireTimeSpan,
/// SlidingExpiration = false). Nếu NV để yên tab/app rồi quay lại bấm nút, POST đi kèm token của
/// "người đã đăng nhập" trong khi request lúc này là ẩn danh -> Antiforgery từ chối -> trình duyệt
/// hiện trang 400 trắng, không chữ nào. NV báo "bấm Đăng xuất không được" chính là ca này
/// (tái hiện được 2026-11-12: cùng token, chỉ bỏ cookie .HR.Auth là 302 thành 400).
///
/// XỬ LÝ:
///   - Đăng xuất: cứ cho đăng xuất. Phiên đã hết thì chẳng còn gì để CSRF bảo vệ, mà đó cũng
///     đúng ý người dùng. Không bao giờ để nút Đăng xuất "không ăn".
///   - Request AJAX: trả JSON 401 để JS xử lý, KHÔNG trả HTML redirect (JS sẽ nuốt mất và
///     tưởng là thành công).
///   - Còn lại: về trang Login kèm thông báo hết phiên, thay vì màn hình 400 trống.
///
/// Lỗi antiforgery vì lý do KHÁC (token bịa, thiếu hẳn) cũng rơi vào đây — vẫn an toàn vì
/// không có đường nào thực thi được hành động, chỉ là bị đẩy về trang đăng nhập.
///
/// PHẢI là IAlwaysRunResultFilter, KHÔNG dùng được IExceptionFilter: [ValidateAntiForgeryToken]
/// là authorization filter, nó TỰ bắt AntiforgeryValidationException rồi short-circuit bằng
/// AntiforgeryValidationFailedResult (400) — exception không bao giờ bay ra ngoài nên
/// exception filter không chạy (đã thử và xác nhận 2026-11-12). "AlwaysRun" là để filter vẫn
/// chạy kể cả khi pipeline bị short-circuit từ tầng authorization.
/// </summary>
public class AntiforgeryExpiredFilter : IAsyncAlwaysRunResultFilter
{
    private readonly ILogger<AntiforgeryExpiredFilter> _logger;
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public AntiforgeryExpiredFilter(ILogger<AntiforgeryExpiredFilter> logger,
                                    ITempDataDictionaryFactory tempDataFactory)
    {
        _logger          = logger;
        _tempDataFactory = tempDataFactory;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is not AntiforgeryValidationFailedResult)
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var path = http.Request.Path.Value ?? "";

        bool isLogout      = path.EndsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase);
        bool stillSignedIn = http.User?.Identity?.IsAuthenticated == true;

        _logger.LogWarning("Token chống giả mạo không hợp lệ tại {Path} (còn đăng nhập: {SignedIn})",
                           path, stillSignedIn);

        // CHỈ đăng xuất khi phiên đã chết thật, hoặc khi chính NV bấm Đăng xuất.
        // Nếu phiên CÒN hạn mà token hỏng (tab mở quá lâu, mở 2 tab, bấm 2 lần...) thì đá người
        // đang làm việc ra ngoài là quá tay — giữ phiên, chỉ bảo họ tải lại trang và thử lại.
        if (!stillSignedIn || isLogout)
        {
            try { await Helpers.AuthHelper.SignOutAsync(http); } catch { }
            try { http.Session?.Clear(); } catch { }
        }

        if (IsAjax(http.Request))
        {
            context.Result = new JsonResult(new
            {
                success = false,
                sessionExpired = !stillSignedIn,
                message = stillSignedIn
                    ? "Thao tác đã hết hiệu lực, vui lòng tải lại trang và thử lại / This action expired, please reload the page and try again"
                    : "Phiên làm việc đã hết hạn, vui lòng đăng nhập lại / Your session has expired, please sign in again"
            })
            { StatusCode = stillSignedIn ? StatusCodes.Status400BadRequest : StatusCodes.Status401Unauthorized };
            await next();
            return;
        }

        // Phiên còn hạn: giữ nguyên đăng nhập, đưa về trang chủ kèm lời nhắc tải lại trang.
        if (stillSignedIn && !isLogout)
        {
            var td = _tempDataFactory.GetTempData(http);
            td["ErrorMessage"] = "Thao tác đã hết hiệu lực, vui lòng tải lại trang và thử lại";
            td.Save();
            context.Result = new RedirectToActionResult("Index", "Home", null);
            await next();
            return;
        }

        // Bấm Đăng xuất: coi như đã đăng xuất xong (ở trên đã SignOut) — về thẳng Login, không báo lỗi.
        if (!isLogout)
        {
            // Filter này chạy trong result pipeline nên SaveTempDataFilter của MVC có thể đã
            // chạy xong -> phải tự gọi Save(), nếu không thông báo rơi mất và NV bị đá về
            // trang Login mà không hiểu vì sao (đã dính đúng lỗi này khi test 2026-11-12).
            var tempData = _tempDataFactory.GetTempData(http);
            tempData["ErrorMessage"] = "Phiên làm việc đã hết hạn, vui lòng đăng nhập lại / Your session has expired, please sign in again";
            tempData.Save();
        }

        context.Result = new RedirectToActionResult("Login", "Account", null);
        await next();
    }

    private static bool IsAjax(HttpRequest r)
        => r.Headers["X-Requested-With"] == "XMLHttpRequest"
        || !string.IsNullOrEmpty(r.Headers["RequestVerificationToken"])   // header JS dùng (xem Program.cs)
        || (r.Headers["Accept"].ToString()?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);
}
