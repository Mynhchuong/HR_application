namespace HR_web.Helpers;

// Phân loại thiết bị NV đang đăng nhập dựa vào User-Agent thật của request (yêu cầu 2026-10-26: biết
// ai đã cài app My Samho, ai còn dùng web desktop/trình duyệt mobile để nhắc tải app). Chỉ gọi lúc
// đăng nhập (AccountController.Login) — không ghi mỗi request, tránh tải thêm DB không cần thiết.
//
// App My Samho TỰ GHI ĐÈ User-Agent thành chuỗi riêng khi chạy trong WebView (không phải UA gốc của
// Safari/Chrome bên trong nữa) — xem ios_app/.../ViewController.swift (webView.customUserAgent) và
// android/.../MainActivity.java (webSettings.setUserAgentString) — nên phân biệt "mở bằng app đã cài"
// vs "mở bằng trình duyệt thường trên điện thoại" (dù cùng 1 máy) là chắc chắn, không đoán mò.
public static class DeviceDetector
{
    public const string AppIos     = "APP_IOS";
    public const string AppAndroid = "APP_ANDROID";
    public const string WebMobile  = "WEB_MOBILE";
    public const string WebDesktop = "WEB_DESKTOP";

    public static string Classify(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return WebDesktop;

        if (userAgent.Contains("MySamhoMobile/iOS", StringComparison.OrdinalIgnoreCase))
            return AppIos;
        if (userAgent.Contains("MysamhoMobile/Android", StringComparison.OrdinalIgnoreCase))
            return AppAndroid;

        bool looksMobile = userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase);

        return looksMobile ? WebMobile : WebDesktop;
    }

    public static string DisplayName(string? deviceType) => deviceType switch
    {
        AppIos     => "App iOS",
        AppAndroid => "App Android",
        WebMobile  => "Web mobile (chưa cài app)",
        WebDesktop => "Web máy tính",
        _          => "Chưa rõ",
    };
}
