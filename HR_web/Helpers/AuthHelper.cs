using HR_web.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Newtonsoft.Json;
using System.Security.Claims;

namespace HR_web.Helpers;

/// <summary>
/// Quản lý authentication session - thay thế FormsAuthentication trong .NET Framework cũ.
/// User info được serialize thành Claim và lưu trong Cookie.
/// </summary>
public static class AuthHelper
{
    private const string UserInfoClaimType = "UserInfo";

    /// <summary>
    /// Đăng nhập và tạo Cookie Authentication (thay FormsAuthentication.SetAuthCookie)
    /// </summary>
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

    /// <summary>
    /// Đăng xuất - thay FormsAuthentication.SignOut() + Session.Clear()
    /// </summary>
    public static async Task SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Lấy UserInfoModel từ Claims trong Cookie - thay HttpContext.Current.Items["UserInfo"]
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

    /// <summary>
    /// Cập nhật user info trong Cookie (thay UpdateUserSession cũ)
    /// Dùng khi user đổi mật khẩu hoặc cập nhật chữ ký
    /// </summary>
    public static async Task UpdateUserSessionAsync(HttpContext httpContext, UserInfoModel updatedUser, bool rememberMe = false)
    {
        await SignOutAsync(httpContext);
        await SignInAsync(httpContext, updatedUser, rememberMe);
    }
}
