using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HR_web.Filters;

/// <summary>
/// Global filter - thay RequireUpdateProfileAttribute cũ (System.Web.Mvc.ActionFilterAttribute).
/// Chuyển hướng user về trang Profile nếu chưa đổi mật khẩu hoặc chưa cập nhật chữ ký.
/// Gate ưu tiên cao nhất, cố ý KHÔNG kế thừa AppGateFilterBase (chặn tất cả kể cả trang các gate
/// khác) — nhưng vẫn trả cùng hợp đồng JSON {success:false, gate, redirect_url} cho request
/// AJAX/fetch như AppGateFilterBase, để app-gate.js xử lý đồng nhất thay vì để badge sidebar
/// (loadBadge trong _Layout.cshtml) âm thầm fail khi bị chặn giữa lúc bắt buộc đổi mật khẩu.
/// </summary>
public class RequireUpdateProfileFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Bỏ qua nếu action/controller có [AllowAnonymous]
        bool isAllowAnonymous =
            context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();

        if (isAllowAnonymous)
        {
            await next();
            return;
        }

        // Bỏ qua các controller không cần kiểm tra
        var controllerName = (context.RouteData.Values["controller"]?.ToString() ?? "").ToLower();
        if (controllerName is "account" or "profile" or "image")
        {
            await next();
            return;
        }

        // Lấy user từ Claims
        var userInfo = AuthHelper.GetCurrentUser(context.HttpContext.User);

        if (userInfo != null)
        {
            if (userInfo.RequirePasswordChange || userInfo.SIGNATUREBLOB == "N")
            {
                var redirect = new RedirectToActionResult("ProfileUser", "Profile", null);

                // Cùng logic phân biệt AJAX/fetch vs điều hướng trang thật như AppGateFilterBase.
                var acceptHeader = context.HttpContext.Request.Headers["Accept"].ToString();
                bool isAjax = string.Equals(
                    context.HttpContext.Request.Headers["X-Requested-With"].ToString(),
                    "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                    || !acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase);

                if (isAjax)
                {
                    var urlHelper = context.HttpContext.RequestServices
                        .GetRequiredService<IUrlHelperFactory>().GetUrlHelper(context);
                    var url = urlHelper.Action(redirect.ActionName, redirect.ControllerName, redirect.RouteValues);

                    context.Result = new ObjectResult(new { success = false, gate = "update_profile", redirect_url = url })
                    { StatusCode = 409 };
                }
                else
                {
                    var controller = context.Controller as Microsoft.AspNetCore.Mvc.Controller;

                    if (userInfo.RequirePasswordChange)
                        controller!.TempData["InfoMessage"] = "Bảo mật: Từ chối truy cập! Bắt buộc phải đổi mật khẩu bảo mật (Mật khẩu mặc định 123456 không an toàn).";
                    else if (userInfo.SIGNATUREBLOB == "N")
                        controller!.TempData["InfoMessage"] = "Bảo mật: Từ chối truy cập! Yêu cầu phải cập nhật chữ ký cá nhân.";

                    context.Result = redirect;
                }
                return;
            }
        }

        await next();
    }
}
