using HR_web.Models.Account;
using HR_web.Models;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Encodings.Web;

namespace HR_web.Helpers;

/// <summary>
/// AlertHelper - thay thế phiên bản cũ dùng MvcHtmlString + HtmlHelper (System.Web.Mvc).
/// .NET 8: dùng IHtmlContent + IHtmlHelper thay thế.
/// VirtualPathUtility.ToAbsolute() → Url.Content() (được gọi từ context của View).
/// </summary>
public static class AlertHelper
{
    public enum AlertType { Success, Error, Info, Warning }

    /// <summary>
    /// Render các toast notification từ TempData.
    /// Gọi trong Layout bằng: @Html.RenderAlerts()
    /// </summary>
    public static IHtmlContent RenderAlerts(this IHtmlHelper html)
    {
        var tempData = html.ViewContext.TempData;

        var alerts = new[]
        {
            new { Key = "SuccessMessage", Type = AlertType.Success, Icon = "check_circle", ImgPath = "~/assets/img/illustrations/success.png" },
            new { Key = "ErrorMessage",   Type = AlertType.Error,   Icon = "error",         ImgPath = "~/assets/img/illustrations/error.png" },
            new { Key = "InfoMessage",    Type = AlertType.Info,    Icon = "info",           ImgPath = "~/assets/img/illustrations/inform.png" },
            new { Key = "WarningMessage", Type = AlertType.Warning, Icon = "warning",        ImgPath = "~/assets/img/illustrations/warning.png" },
        };

        var builder = new HtmlContentBuilder();

        foreach (var alert in alerts)
        {
            if (tempData[alert.Key] is not string message) continue;

            string colorClass = alert.Type switch
            {
                AlertType.Success => "text-bg-success",
                AlertType.Error   => "text-bg-danger",
                AlertType.Info    => "text-bg-info",
                AlertType.Warning => "text-bg-warning",
                _                 => "text-bg-secondary"
            };

            // Dùng urlHelper từ context để resolve ~/... (thay VirtualPathUtility.ToAbsolute)
            var urlHelper = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(html.ViewContext);
            string imgSrc = urlHelper.Content(alert.ImgPath);

            var toastHtml = $@"
<div class='toast align-items-center {colorClass} border-0 alert-toast show' role='alert' aria-live='assertive' aria-atomic='true'>
    <div class='d-flex align-items-center'>
        <img src='{imgSrc}' style='width:50px;height:50px;margin-right:10px;' alt='alert' />
        <div class='toast-body flex-fill'>
            <span class='material-symbols-rounded me-2'>{alert.Icon}</span>
            {HtmlEncoder.Default.Encode(message)}
        </div>
        <button type='button' class='btn-close btn-close-white me-2 m-auto' data-bs-dismiss='toast' aria-label='Close'></button>
    </div>
</div>";

            builder.AppendHtml(toastHtml);
        }

        return builder;
    }
}
