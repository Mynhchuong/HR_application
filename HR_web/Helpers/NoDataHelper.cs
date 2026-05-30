using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR_web.Helpers;

public static class NoDataHelper
{
    private static readonly string[] NoDataImages = ["Chi_nodata.png", "A_nodata.png"];

    public static IHtmlContent RenderNoData(this IHtmlHelper html, string? subtitle = null)
    {
        var urlHelper = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(html.ViewContext);
        var img = NoDataImages[Random.Shared.Next(NoDataImages.Length)];
        var imgSrc = urlHelper.Content($"~/assets/img/illustrations/{img}");

        var subtitleHtml = subtitle is not null
            ? $"<p class='text-muted mt-1 mb-0' style='font-size:0.95rem;'>{subtitle}</p>"
            : string.Empty;

        var html_content = $@"
<div class='text-center py-5'>
    <img src='{imgSrc}' alt='Không có dữ liệu' style='max-width:220px;margin-bottom:1rem;opacity:0.9;' />
    <div class='d-flex align-items-center justify-content-center gap-2 mt-1'>
        <span class='material-symbols-rounded text-secondary' style='font-size:1.5rem;'>event_busy</span>
        <h5 class='fw-bold mb-0 text-secondary'>Không có dữ liệu</h5>
    </div>
    {subtitleHtml}
</div>";

        return new HtmlString(html_content);
    }
}
