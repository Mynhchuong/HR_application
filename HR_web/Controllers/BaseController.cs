using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HR_web.Controllers;

/// <summary>
/// BaseController - thay thế BaseController cũ dùng System.Web.Mvc.
/// CurrentUser được đọc từ Claims qua AuthHelper thay vì HttpContext.Items.
/// </summary>
public class BaseController : Controller
{
    /// <summary>
    /// Trả về thông tin user đang đăng nhập từ Claims Cookie.
    /// </summary>
    protected UserInfoModel? CurrentUser =>
        AuthHelper.GetCurrentUser(User);

    /// <summary>
    /// Chạy trước mỗi action - đẩy CurrentUser vào ViewBag để Layout có thể dùng.
    /// </summary>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        ViewBag.CurrentUser = CurrentUser;
    }
}
