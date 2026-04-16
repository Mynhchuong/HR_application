using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

[Authorize]
public class HomeController : BaseController
{
    public IActionResult Index()
    {
        return RedirectToAction("UserManager", "User");
    }

    [AllowAnonymous]
    public IActionResult Error()
    {
        return View();
    }
}
