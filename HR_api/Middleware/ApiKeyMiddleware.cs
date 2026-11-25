using System.Security.Cryptography;
using System.Text;

namespace HR_api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeader = "X-Api-Key";

    /// <summary>HttpContext.Items: true khi request mang đúng API key (kể cả ở path được miễn key).</summary>
    public const string ValidKeyItem = "ApiKeyValid";

    // Các path không cần API key (Swagger UI — chỉ bật ở Development).
    //
    // AppVersion/check (bảo mật 2026-11-21): trước đây app Android/iOS gắn cứng API key vào bản cài để gọi
    // thẳng endpoint này — ai giải nén APK/IPA là có key gọi được TOÀN BỘ HR_api từ internet. Key đã đổi; các
    // bản app cũ ngoài kia vẫn gửi key cũ nên endpoint này phải mở không cần key để cơ chế ép cập nhật
    // vẫn chạy. Gọi không có key hợp lệ thì AppVersionController bỏ qua empcd (không trả link riêng của NV).
    //
    // /apiHR/Tpm/notify/: webhook TPM -> HR_api (có phiếu báo hư mới). TPM không giữ API key của HR_api; endpoint tự kiểm
    // khóa nội bộ X-MySamho-Key (Tpm:InternalKey, cùng khóa HR_api dùng gọi TPM) — xem TpmController.NotifyBroken.
    private static readonly string[] SkippedPaths = ["/swagger", "/apiHR/AppVersion/check", "/apiHR/Tpm/notify/"];

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        var path = context.Request.Path.Value ?? "";

        var validKey = config["ApiSettings:ApiKey"] ?? "";
        bool keyOk = context.Request.Headers.TryGetValue(ApiKeyHeader, out var receivedKey)
                     && validKey.Length > 0
                     && CryptographicOperations.FixedTimeEquals(
                            Encoding.UTF8.GetBytes(receivedKey.ToString()), Encoding.UTF8.GetBytes(validKey));
        context.Items[ValidKeyItem] = keyOk;

        if (keyOk || path == "/" || SkippedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { success = false, message = "Unauthorized" });
    }
}
