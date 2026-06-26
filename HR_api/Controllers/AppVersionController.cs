using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class AppVersionController : ControllerBase
{
    private readonly IConfiguration _config;

    public AppVersionController(IConfiguration config)
    {
        _config = config;
    }

    // GET /apiHR/AppVersion/check?platform=android|ios&version=7.0
    //   Trả về:
    //   {
    //      success: true,
    //      currentVersion: "7.0",
    //      latestVersion : "7.1",
    //      minVersion    : "7.1",      // < min → bắt buộc update
    //      forceUpdate   : true|false,
    //      updateUrl     : "https://...",
    //      releaseNote   : "Mô tả thay đổi"
    //   }
    [HttpGet("check")]
    public IActionResult Check(string platform = "android", string? version = null)
    {
        try
        {
            var key = (platform ?? "").Trim().ToLowerInvariant() == "ios" ? "iOS" : "Android";
            var section = _config.GetSection($"AppVersion:{key}");

            string latest = section["LatestVersion"] ?? "1.0";
            string min    = section["MinVersion"]    ?? latest;
            string url    = section["UpdateUrl"]     ?? "";
            string note   = section["ReleaseNote"]   ?? "";

            bool forceUpdate = false;
            if (!string.IsNullOrEmpty(version))
                forceUpdate = CompareVersion(version, min) < 0;

            return Ok(new
            {
                success        = true,
                currentVersion = version,
                latestVersion  = latest,
                minVersion     = min,
                forceUpdate,
                updateUrl      = url,
                releaseNote    = note
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // So sánh phiên bản dạng "7.1.2" vs "7.1.0"
    // Trả -1 nếu a < b, 0 nếu bằng, 1 nếu a > b
    private static int CompareVersion(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int va = (i < pa.Length && int.TryParse(pa[i], out var x)) ? x : 0;
            int vb = (i < pb.Length && int.TryParse(pb[i], out var y)) ? y : 0;
            if (va != vb) return va < vb ? -1 : 1;
        }
        return 0;
    }
}
