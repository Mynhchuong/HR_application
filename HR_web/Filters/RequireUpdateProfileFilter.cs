using HR_web.Helpers;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HR_web.Filters;

/// <summary>
/// Global filter - thay RequireUpdateProfileAttribute cũ (System.Web.Mvc.ActionFilterAttribute).
/// Chuyển hướng user về trang Profile nếu chưa đổi mật khẩu hoặc chưa cập nhật chữ ký.
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
                var controller = context.Controller as Microsoft.AspNetCore.Mvc.Controller;

                if (userInfo.RequirePasswordChange)
                    controller!.TempData["InfoMessage"] = "Bảo mật: Từ chối truy cập! Bắt buộc phải đổi mật khẩu bảo mật (Mật khẩu mặc định 123456 không an toàn).";
                else if (userInfo.SIGNATUREBLOB == "N")
                    controller!.TempData["InfoMessage"] = "Bảo mật: Từ chối truy cập! Yêu cầu phải cập nhật chữ ký cá nhân.";

                context.Result = new RedirectToActionResult("ProfileUser", "Profile", null);
                return;
            }
        }

        await next();
    }
}
