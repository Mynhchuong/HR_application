using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace HR_web.Helpers;

// Ký/verify token cho link video/tài liệu training (GetTrainingMaterial) — dùng THAY/BỔ SUNG cookie
// session cho riêng endpoint này. Lý do: video <video> tag trong WKWebView (app My Samho iOS) và một
// số WebView Android phát qua trình phát media nội bộ (AVPlayer/MediaPlayer) không chắc mang theo
// cookie session của WebView khi gọi network — khiến request bị [Authorize] redirect về trang Login
// (HTML) thay vì trả file video, player fail câm lặng (màn hình đen). Báo cáo thật 2026-10-24: xem
// được ở trình duyệt máy tính VÀ trình duyệt mobile (cùng dùng cookie), KHÔNG xem được CHỈ trong app
// My Samho (cả iOS lẫn Android) — khớp đúng dấu hiệu cookie không tới được trình phát video native.
// Token HMAC-SHA256, hết hạn theo thời gian — không phụ thuộc cookie nên chạy đúng mọi nền tảng.
public static class MaterialUrlTokenHelper
{
    public static string Sign(string subFolder, string fileName, long expUnix, string secret)
    {
        var payload = $"{subFolder}|{fileName}|{expUnix}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return WebEncoders.Base64UrlEncode(hash);
    }

    public static bool Verify(string subFolder, string fileName, long expUnix, string? sig, string secret)
    {
        if (string.IsNullOrEmpty(sig)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnix) return false;

        var expected = Sign(subFolder, fileName, expUnix, secret);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes   = Encoding.UTF8.GetBytes(sig);
        if (expectedBytes.Length != actualBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
