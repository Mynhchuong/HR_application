namespace HR_api.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeader = "X-Api-Key";

    // Các path không cần API key (Swagger UI, health check)
    private static readonly string[] SkippedPaths = ["/swagger", "/check-db"];

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        var path = context.Request.Path.Value ?? "";

        if (SkippedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || path == "/")
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var receivedKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Unauthorized: Missing API Key" });
            return;
        }

        var validKey = config["ApiSettings:ApiKey"] ?? "";
        if (!validKey.Equals(receivedKey.ToString(), StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "Unauthorized: Invalid API Key" });
            return;
        }

        await _next(context);
    }
}
