using System.Security.Claims;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HR_web.Helpers;

// Kiểm tra định kỳ giữa phiên xem tài khoản còn active không (HR_USERS.IS_ACTIVE + ECM100.JEAJIKGB),
// để tự động logout ngay khi HR cho nghỉ việc thay vì phải chờ cookie hết hạn 24h.
public static class SessionValidator
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(15);

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var identity = context.Principal?.Identity as ClaimsIdentity;
        if (identity == null || !identity.IsAuthenticated)
            return;

        var lastCheckClaim = identity.FindFirst(AuthHelper.LastActiveCheckClaimType)?.Value;
        var lastCheck = DateTimeOffset.MinValue;
        if (lastCheckClaim != null)
            DateTimeOffset.TryParse(lastCheckClaim, out lastCheck);

        if (DateTimeOffset.UtcNow - lastCheck < RecheckInterval)
            return;

        var empCd = identity.Name;
        if (string.IsNullOrEmpty(empCd))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var accountService = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
        bool active = await accountService.CheckActiveAsync(empCd);

        if (!active)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        // Cập nhật mốc check gần nhất, KHÔNG đổi ExpiresUtc (giữ nguyên hạn tuyệt đối 24h)
        var oldClaim = identity.FindFirst(AuthHelper.LastActiveCheckClaimType);
        if (oldClaim != null) identity.RemoveClaim(oldClaim);
        identity.AddClaim(new Claim(AuthHelper.LastActiveCheckClaimType, DateTimeOffset.UtcNow.ToString("o")));

        context.ShouldRenew = true;
    }
}
