using HR_web.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Newtonsoft.Json;
using System.Security.Claims;

namespace HR_web.Helpers;


public static class AuthHelper
{
    private const string UserInfoClaimType = "UserInfo";
    public const string LastActiveCheckClaimType = "LastActiveCheck";


    public static async Task SignInAsync(HttpContext httpContext, UserInfoModel user)
    {
        if (user == null) return;

        var userJson = JsonConvert.SerializeObject(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.EmpCd),
            new Claim(ClaimTypes.GivenName, user.FullName ?? string.Empty),
            new Claim(ClaimTypes.Role, user.RoleName ?? string.Empty),
            new Claim(UserInfoClaimType, userJson),
            new Claim(LastActiveCheckClaimType, DateTimeOffset.UtcNow.ToString("o"))
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true, // Cookie sống qua khi đóng trình duyệt/app, nhưng vẫn hết hạn tuyệt đối sau 24h (không sliding)
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24),
            AllowRefresh = false
        };

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);
    }

    
    public static async Task SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Lấy UserInfoModel từ Claims trong Cookie 
    /// </summary>
    public static UserInfoModel? GetCurrentUser(ClaimsPrincipal principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var claim = principal.FindFirst(UserInfoClaimType);
        if (claim == null) return null;

        try
        {
            return JsonConvert.DeserializeObject<UserInfoModel>(claim.Value);
        }
        catch
        {
            return null;
        }
    }

    
    public static async Task UpdateUserSessionAsync(HttpContext httpContext, UserInfoModel updatedUser)
    {
        await SignOutAsync(httpContext);
        await SignInAsync(httpContext, updatedUser);
    }
}
