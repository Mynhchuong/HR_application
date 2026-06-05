using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

public class AccountController : Controller
{
    private readonly AccountService _service;

    public AccountController(AccountService service)
    {
        _service = service;
    }

    // ─────────────────────────────────────────────
    // GET: /Account/Login
    // ─────────────────────────────────────────────
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        var model = new LoginModel();

        if (Request.Cookies.TryGetValue("EMP_CD", out var savedEmpCd))
        {
            model.EMPCD = savedEmpCd;
            model.RememberMe = true;
        }

        return View(model);
    }

    // ─────────────────────────────────────────────
    // POST: /Account/Login
    // ─────────────────────────────────────────────
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _service.LoginAsync(model.EMPCD, model.Password);

        if (user == null)
        {
            var fails = HttpContext.Session.GetInt32("LoginFails") ?? 0;
            fails++;
            HttpContext.Session.SetInt32("LoginFails", fails);
            ModelState.AddModelError(string.Empty, fails >= 3
                ? $"Sai tài khoản hoặc mật khẩu (lần {fails}). Nếu quên mật khẩu, vui lòng liên hệ phòng HR để được hỗ trợ."
                : "Sai tài khoản hoặc mật khẩu.");
            return View(model);
        }

        HttpContext.Session.Remove("LoginFails");

        user.RequirePasswordChange = (model.Password == "123456");

        if (model.RememberMe)
        {
            Response.Cookies.Append("EMP_CD", model.EMPCD,
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(30), HttpOnly = true });
        }
        else
        {
            Response.Cookies.Delete("EMP_CD");
        }

        await AuthHelper.SignInAsync(HttpContext, user, model.RememberMe);

        if (user.RequirePasswordChange)
        {
            TempData["InfoMessage"] = "Bảo mật hệ thống: Vui lòng đổi mật khẩu để bảo vệ tài khoản của bạn!";
            return RedirectToAction("ProfileUser", "Profile");
        }
        if (user.SIGNATUREBLOB == "N")
        {
            TempData["InfoMessage"] = "Bảo mật hệ thống: Vui lòng cập nhật hình ảnh chữ ký để có thể tiếp tục thao tác!";
            return RedirectToAction("ProfileUser", "Profile");
        }

        return RedirectToAction("Index", "Home");
    }

    // ─────────────────────────────────────────────
    // GET/POST: /Account/Logout
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Logout()
    {
        await AuthHelper.SignOutAsync(HttpContext);
        return RedirectToAction("Login");
    }

    // ─────────────────────────────────────────────
    // GET: /Account/AccessDenied
    // ─────────────────────────────────────────────
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
