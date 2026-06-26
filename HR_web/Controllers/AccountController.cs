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
        // 1. Xoá cookie auth
        await AuthHelper.SignOutAsync(HttpContext);

        // 2. Xoá session (LoginFails, các flag tạm)
        HttpContext.Session.Clear();

        // 3. Xoá cookie EMP_CD (RememberMe) để lần sau vào /Login không bị pre-fill mã NV cũ
        Response.Cookies.Delete("EMP_CD");

        // 4. Xoá cả cookie ASP.NET phụ trợ + cookie auth tên cấu hình (defensive)
        Response.Cookies.Delete(".HR.Auth");
        Response.Cookies.Delete(".AspNetCore.Session");
        Response.Cookies.Delete(".AspNetCore.Antiforgery");
        Response.Cookies.Delete(".AspNetCore.Cookies");

        return RedirectToAction("Login");
    }

    // ─────────────────────────────────────────────
    // GET: /Account/ForgotPassword — form quên mật khẩu
    // POST: /Account/ForgotPassword — đặt lại mật khẩu (1 lần / tuần)
    // ─────────────────────────────────────────────
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View(new HR_web.Models.Account.ForgotPasswordModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(HR_web.Models.Account.ForgotPasswordModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (model.NewPassword != model.ConfirmPassword)
        {
            ModelState.AddModelError(string.Empty, "Mật khẩu mới và xác nhận không khớp");
            return View(model);
        }

        var (ok, msg) = await _service.ForgotPasswordAsync(
            model.EMPCD?.Trim() ?? "",
            model.Juminno?.Trim() ?? "",
            model.JuminnoDate?.Trim() ?? "",
            model.NewPassword?.Trim() ?? "");

        if (!ok)
        {
            ModelState.AddModelError(string.Empty, msg);
            return View(model);
        }

        TempData["InfoMessage"] = msg + " Bạn đã có thể đăng nhập với mật khẩu mới.";
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
