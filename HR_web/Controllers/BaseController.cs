using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HR_web.Controllers;


public class BaseController : Controller
{
   
    protected UserInfoModel? CurrentUser =>
        AuthHelper.GetCurrentUser(User);

  
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        ViewBag.CurrentUser = CurrentUser;
    }
}
