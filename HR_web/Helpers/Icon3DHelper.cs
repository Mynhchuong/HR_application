using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR_web.Helpers;

// Icon 3D (Microsoft Fluent Emoji, MIT) ở wwwroot/assets/icons/3d/{key}.webp — danh sách key: ICON_PROMPTS.txt.
// Cỡ ảnh theo font-size của thẻ cha (1.25em) để thay thẳng chỗ emoji cũ mà không phải chỉnh lại CSS.
public static class Icon3DHelper
{
    // Icon được cache 1 năm (Program.cs) — thay/sửa ảnh icon thì tăng v để máy người dùng tải bản mới.
    public const string Ext = ".webp?v=1";

    public static IHtmlContent Icon3D(this IHtmlHelper html, string key, string? cls = null)
    {
        var src = html.ViewContext.HttpContext.Request.PathBase + "/assets/icons/3d/" + key + Ext;
        return new HtmlString($"<img class=\"ic3d{(string.IsNullOrEmpty(cls) ? "" : " " + cls)}\" src=\"{src}\" alt=\"\" loading=\"lazy\" decoding=\"async\">");
    }

    // Tên icon Material trong SideMenuBuilder -> icon 3D. Icon menu mới chưa có trong bảng thì tự rơi về
    // icon Material như cũ (không vỡ). hr-nav.js dùng chung bảng này (layout nhúng window.MENU_IC3D) cho mục đã ghim.
    public static readonly IReadOnlyDictionary<string, string> MenuMap = new Dictionary<string, string>
    {
        ["home"] = "m-home", ["dashboard"] = "hr-report", ["restaurant"] = "menu", ["campaign"] = "bulletin-comment",
        ["policy"] = "m-policy", ["menu_book"] = "policy", ["qr_code_scanner"] = "qr-scan", ["person"] = "profile",
        ["calendar_month"] = "hr-sunday", ["fact_check"] = "overtime", ["schedule"] = "attendance", ["card_giftcard"] = "gift",
        ["door_front"] = "gatepass", ["event_busy"] = "leave", ["school"] = "training", ["tips_and_updates"] = "kaizen",
        ["payments"] = "payslip", ["forum"] = "message", ["build"] = "tpm-report", ["history"] = "m-history",
        ["manage_accounts"] = "hr-users", ["view_list"] = "ot-list", ["event_available"] = "assigned-leave", ["groups"] = "team",
        ["assignment"] = "document-request", ["bar_chart"] = "salary-chart", ["engineering"] = "safety",
        ["supervisor_account"] = "m-supervisor", ["assignment_late"] = "doc-missing", ["edit_note"] = "survey",
        ["wb_sunny"] = "shift-noon", ["manage_history"] = "loading", ["edit_calendar"] = "team-calendar", ["redeem"] = "m-redeem",
        ["restaurant_menu"] = "meal-default", ["set_meal"] = "meal-main", ["lock_clock"] = "meal-locked",
        ["bakery_dining"] = "meal-bread", ["card_membership"] = "certificate", ["quickreply"] = "direct-chat",
        ["playlist_add_check"] = "success", ["image"] = "m-image", ["account_balance_wallet"] = "m-wallet", ["shield"] = "insurance",
        ["home_app_logo"] = "hr-home-config", ["poll"] = "survey", ["person_off"] = "no-permission", ["phone_iphone"] = "app-update",
        ["notifications"] = "bell", ["devices"] = "m-devices", ["support_agent"] = "csr-agent", ["bolt"] = "m-bolt",
        ["topic"] = "m-topic", ["forward_to_inbox"] = "hr-send", ["lightbulb"] = "kaizen", ["business_center"] = "hr-inquiry",
        ["today"] = "menu", ["date_range"] = "attendance-history", ["admin_panel_settings"] = "settings",
        ["edit_notifications"] = "inquiry-grievance", ["contact_page"] = "cccd-scan",
    };

    public static bool HasMenu3D(string? material) => material != null && MenuMap.ContainsKey(material);

    public static IHtmlContent MenuIcon(this IHtmlHelper html, string? material, string? color = null)
    {
        if (material != null && MenuMap.TryGetValue(material, out var key)) return html.Icon3D(key);
        var style = string.IsNullOrEmpty(color) ? "" : $" style=\"color:{HtmlEncoder.Default.Encode(color)};\"";
        return new HtmlString($"<i class=\"material-symbols-rounded\"{style}>{HtmlEncoder.Default.Encode(material ?? "")}</i>");
    }
}
