using HR_api.Data;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class AppVersionController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly OracleService  _db;

    public AppVersionController(IConfiguration config, OracleService db)
    {
        _config = config;
        _db     = db;
    }

    // GET /apiHR/AppVersion/check?platform=android|ios&version=7.0&empcd=12345678
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
    //
    // iOS không có App Store public (phân phối qua Apple Business Manager, mỗi NV 1 link redeem
    // riêng trong HR_APP_LINK — xem AppLinkService.cs) nên UpdateUrl cấu hình tĩnh trong
    // appsettings.json chỉ là fallback. Có empcd -> tra đúng link ĐÃ GÁN cho NV đó (không phải link
    // mới — 1 lần redeem xong thì app đã nằm trong Apple ID của họ, mở lại đúng link cũ sẽ ra thẳng
    // trang app đó trên App Store để bấm Cập nhật, không redeem lại — cùng cơ chế "ALREADY_ASSIGNED"
    // AppLinkService đã dùng ở Guide/Index, yêu cầu 2026-10-26: "xài lại link cũ để update iOS").
    [HttpGet("check")]
    public async Task<IActionResult> Check(string platform = "android", string? version = null, string? empcd = null)
    {
        try
        {
            var isIos = (platform ?? "").Trim().ToLowerInvariant() == "ios";
            var key = isIos ? "iOS" : "Android";
            var section = _config.GetSection($"AppVersion:{key}");

            string latest = section["LatestVersion"] ?? "1.0";
            string min    = section["MinVersion"]    ?? latest;
            string url    = section["UpdateUrl"]     ?? "";
            string note   = section["ReleaseNote"]   ?? "";

            if (isIos && !string.IsNullOrWhiteSpace(empcd))
            {
                var assigned = await _db.ExecuteQueryAsync(@"
                    SELECT LINK_URL FROM (
                        SELECT LINK_URL FROM HRMS.HR_APP_LINK
                        WHERE ASSIGNED_EMPCD = :E AND STATUS = 'ASSIGNED'
                        ORDER BY ASSIGNED_DT DESC
                    ) WHERE ROWNUM = 1",
                    r => r["LINK_URL"]?.ToString() ?? "",
                    new OracleParameter("E", empcd));
                if (assigned.Count > 0 && !string.IsNullOrEmpty(assigned[0]))
                    url = assigned[0];
            }

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
