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
        // Nếu đã đăng nhập rồi → về trang chủ
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("UserManager", "User");

        var model = new LoginModel();

        // Nhớ tài khoản từ cookie (thay Request.Cookies["EMP_CD"].Value cũ)
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
            ModelState.AddModelError(string.Empty, "Sai tài khoản hoặc mật khẩu");
            return View(model);
        }

        // Kiểm tra đổi mật khẩu mặc định
        user.RequirePasswordChange = (model.Password == "123456");

        // Lưu/xóa cookie "Nhớ tài khoản" (thay Response.Cookies[...].Value cũ)
        if (model.RememberMe)
        {
            Response.Cookies.Append("EMP_CD", model.EMPCD,
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(30), HttpOnly = true });
        }
        else
        {
            Response.Cookies.Delete("EMP_CD");
        }

        // Tạo Cookie Authentication (thay FormsAuthentication.SetAuthCookie)
        await AuthHelper.SignInAsync(HttpContext, user, model.RememberMe);

        // Ưu tiên đổi mật khẩu / cập nhật chữ ký
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

        return RedirectToAction("UserManager", "User");
    }

    // ─────────────────────────────────────────────
    // GET/POST: /Account/Logout
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Logout()
    {
        // Xóa Cookie Authentication (thay FormsAuthentication.SignOut() + Session.Clear())
        await AuthHelper.SignOutAsync(HttpContext);
        return RedirectToAction("Login");
    }
}
