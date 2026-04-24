using HR_web.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Newtonsoft.Json;
using System.Security.Claims;

namespace HR_web.Helpers;


public static class AuthHelper
{
    private const string UserInfoClaimType = "UserInfo";

    
    public static async Task SignInAsync(HttpContext httpContext, UserInfoModel user, bool rememberMe = false)
    {
        if (user == null) return;

        var userJson = JsonConvert.SerializeObject(user);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.EmpCd),
            new Claim(ClaimTypes.GivenName, user.FullName ?? string.Empty),
            new Claim(UserInfoClaimType, userJson)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddDays(1),
            AllowRefresh = true
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

    
    public static async Task UpdateUserSessionAsync(HttpContext httpContext, UserInfoModel updatedUser, bool rememberMe = false)
    {
        await SignOutAsync(httpContext);
        await SignInAsync(httpContext, updatedUser, rememberMe);
    }
}
