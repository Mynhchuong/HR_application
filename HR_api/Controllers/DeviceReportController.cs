using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

// Báo cáo "ai đã cài app My Samho, ai còn dùng web" — dựa vào HR_USERS.LAST_DEVICE_TYPE, tự ghi mỗi
// lần đăng nhập (xem AccountController.Login + HR_web DeviceDetector.cs). Mục đích: tìm NV còn dùng
// WEB_MOBILE (trình duyệt điện thoại, chưa cài app) để HR nhắc tải app (yêu cầu 2026-10-26).
[ApiController]
[Route("apiHR/[controller]")]
public class DeviceReportController : ControllerBase
{
    private readonly OracleService _oracleService;

    public DeviceReportController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // GET /DeviceReport/stats — đếm số NV active theo từng loại thiết bị lần đăng nhập gần nhất.
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        const string sql = @"
            SELECT LAST_DEVICE_TYPE, COUNT(1) AS CNT
            FROM HRMS.HR_USERS
            WHERE IS_ACTIVE = 1
            GROUP BY LAST_DEVICE_TYPE";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            DeviceType = r["LAST_DEVICE_TYPE"] == DBNull.Value ? null : r["LAST_DEVICE_TYPE"]?.ToString(),
            Count = Convert.ToInt32(r["CNT"])
        });

        int Get(string key) => rows.Where(x => x.DeviceType == key).Sum(x => x.Count);
        int total = rows.Sum(x => x.Count);
        int unknown = rows.Where(x => x.DeviceType == null).Sum(x => x.Count);

        return Ok(new
        {
            success = true,
            total,
            appIos     = Get("APP_IOS"),
            appAndroid = Get("APP_ANDROID"),
            webMobile  = Get("WEB_MOBILE"),
            webDesktop = Get("WEB_DESKTOP"),
            unknown,
        });
    }

    // GET /DeviceReport/list?deviceType=&search=&page=&page_size=
    [HttpGet("list")]
    public async Task<IActionResult> GetList(string? deviceType, string? search, int page = 1, int page_size = 50)
    {
        if (page <= 0) page = 1;
        if (page_size <= 0 || page_size > 500) page_size = 50;

        string where = " WHERE U.IS_ACTIVE = 1 ";
        var parameters = new List<OracleParameter>();

        if (!string.IsNullOrWhiteSpace(deviceType))
        {
            if (deviceType == "UNKNOWN")
            {
                where += " AND U.LAST_DEVICE_TYPE IS NULL";
            }
            else
            {
                where += " AND U.LAST_DEVICE_TYPE = :DEVICE_TYPE";
                parameters.Add(new OracleParameter("DEVICE_TYPE", deviceType));
            }
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where += " AND (UPPER(TRIM(U.FULL_NAME)) LIKE UPPER(:SEARCH) OR UPPER(TRIM(U.EMPCD)) LIKE UPPER(:SEARCH))";
            parameters.Add(new OracleParameter("SEARCH", $"%{search.Trim()}%"));
        }

        string countSql = $@"
            SELECT COUNT(1) FROM HRMS.HR_USERS U {where}";
        var countResult = await _oracleService.ExecuteQueryAsync(countSql,
            r => Convert.ToInt32(r[0]), parameters.Select(p => (OracleParameter)p.Clone()).ToArray());
        int total = countResult.FirstOrDefault();

        int minRow = (page - 1) * page_size + 1;
        int maxRow = page * page_size;

        var dataParams = parameters.Select(p => (OracleParameter)p.Clone()).ToList();
        dataParams.Add(new OracleParameter("MAX_ROW", maxRow));
        dataParams.Add(new OracleParameter("MIN_ROW", minRow));

        string sql = $@"
            SELECT * FROM (
                SELECT A1.*, ROWNUM rnum FROM (
                    SELECT U.EMPCD, U.FULL_NAME, R.ROLE_NAME, B.DEPTNM AS DEPT_NAME, B.WORKNM AS WORK_NAME,
                           U.LAST_DEVICE_TYPE, U.LAST_DEVICE_DT, U.LASTED_LOGIN
                    FROM HRMS.HR_USERS U
                    LEFT JOIN HRMS.HR_ROLES R ON U.ROLE_ID = R.ID
                    LEFT JOIN HRMS.ECM100 A ON U.EMPCD = A.EMPCD
                    LEFT JOIN HRMS.EAM410 B ON A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD
                                            AND A.WORKCD = B.WORKCD AND A.JEAJIKGB = 'Y'
                    {where}
                    ORDER BY U.LAST_DEVICE_DT DESC NULLS LAST
                ) A1 WHERE ROWNUM <= :MAX_ROW
            ) WHERE rnum >= :MIN_ROW";

        var data = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            EmpCd      = r["EMPCD"]?.ToString(),
            FullName   = r["FULL_NAME"]?.ToString(),
            RoleName   = r["ROLE_NAME"]?.ToString(),
            DeptName   = r["DEPT_NAME"]?.ToString(),
            WorkName   = r["WORK_NAME"]?.ToString(),
            DeviceType = r["LAST_DEVICE_TYPE"] == DBNull.Value ? null : r["LAST_DEVICE_TYPE"]?.ToString(),
            DeviceDt   = r["LAST_DEVICE_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LAST_DEVICE_DT"]),
            LastLogin  = r["LASTED_LOGIN"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LASTED_LOGIN"]),
        }, dataParams.ToArray());

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            page_size,
            total_pages = (int)Math.Ceiling((double)total / page_size),
        });
    }
}
