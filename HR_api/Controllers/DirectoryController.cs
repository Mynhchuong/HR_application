using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Directory;

namespace HR_api.Controllers;

// Trang "xem thông tin đồng nghiệp" - không yêu cầu đăng nhập (public trong mạng công ty).
[ApiController]
[Route("apiHR/[controller]")]
public class DirectoryController : ControllerBase
{
    private readonly OracleService _oracleService;

    public DirectoryController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // GET apiHR/Directory/employee?empCd=xxx
    [HttpGet("employee")]
    public async Task<IActionResult> GetEmployee(string empCd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empCd)) return BadRequest(new { error = "empCd is required" });

            // Ghi chú: dựa theo query gốc, đã sửa 2 lỗi thực tế phát hiện khi chạy thử:
            //  - TB_MASCHANGEWORK không có cột NEW_WORKCD_CODE, ECM100 không có BONADDR0 -> bỏ.
            //  - Join EBM200 (ca hôm nay) là INNER JOIN -> nhân viên không có ca hôm nay sẽ bị loại
            //    khỏi kết quả hoàn toàn. Chuyển sang LEFT JOIN vì đây là trang xem hồ sơ chung,
            //    không phải danh sách ca làm việc.
            string sql = @"
                SELECT
                    A.SEQ,
                    A.EMPCD,
                    A.CNAME,
                    A.JIKWICD,
                    E.ENGFNM,
                    A.BIRTHDAT,
                    A.JUMINNO,
                    A.SEXGB,
                    A.IGENTDAT,
                    A.DIRECTYN,
                    A.BONADDR1,
                    A.BONADDR2,
                    A.BONADDR3,
                    A.DEPTCD,
                    A.LINECD,
                    A.WORKCD,
                    A.WORKCD_CODE,
                    A.DEPTNM,
                    A.LINENM,
                    A.WORKNM,
                    C.ENGFNM AS WORKCD_NAME,
                    A.SHIFT_TYPE,
                    A.NEW_DEPTCD,
                    A.NEW_LINECD,
                    A.NEW_WORKCD,
                    B.DEPTNM AS NEW_DEPTNM,
                    B.TEAMNM AS NEW_LINENM,
                    B.WORKNM AS NEW_WORKNM
                FROM (
                    SELECT
                        B.SEQ,
                        A.EMPCD,
                        A.CNAME,
                        A.JIKWICD,
                        A.BIRTHDAT,
                        A.JUMINNO,
                        A.SEXGB,
                        A.IGENTDAT,
                        A.DIRECTYN,
                        A.BONADDR1,
                        A.BONADDR2,
                        A.BONADDR3,
                        A.DEPTCD,
                        A.LINECD,
                        A.WORKCD,
                        A.WORKCD_CODE,
                        E.SHIFT_TYPE,
                        B.NEW_DEPTCD,
                        B.NEW_LINECD,
                        B.NEW_WORKCD,
                        C.DEPTNM,
                        C.TEAMNM AS LINENM,
                        C.WORKNM
                    FROM HRMS.ECM100           A
                        ,HRMS.TB_MASCHANGEWORK B
                        ,HRMS.EAM410           C
                        ,HRMS.EBM200           D
                        ,HRMS.EBM100           E
                    WHERE A.EMPCD  = B.EMPCD(+)
                    AND A.DEPTCD = C.DEPTCD
                    AND A.LINECD = C.LINECD
                    AND A.WORKCD = C.WORKCD
                    AND A.EMPCD  = D.EMPCD(+)
                    AND D.DAT(+)    = TRUNC(SYSDATE)
                    AND D.SHIFTCD = E.SHIFTCD(+)
                    AND B.DAT(+)   = TO_CHAR(SYSDATE, 'YYYYMMDD')
                    AND B.USEYN(+) = 'Y'
                    AND A.JEAJIKGB = 'Y'
                    AND A.EMPCD  = :EMPCD
                ) A
                LEFT JOIN HRMS.EAM410 B
                    ON  A.NEW_DEPTCD = B.DEPTCD
                    AND A.NEW_LINECD = B.LINECD
                    AND A.NEW_WORKCD = B.WORKCD
                LEFT JOIN HRMS.SAD100 C
                    ON  C.CODETP = 'E152'
                    AND C.CODEID = A.WORKCD_CODE
                LEFT JOIN HRMS.SAD100 E
                    ON E.CODETP = 'E103'
                    AND E.CODEID = A.JIKWICD";

            var results = await _oracleService.ExecuteQueryAsync(sql, reader => new EmployeeDirectoryModel
            {
                EmpCd          = reader["EMPCD"]?.ToString(),
                CName          = reader["CNAME"]?.ToString(),
                JikwiCd        = reader["JIKWICD"]?.ToString(),
                PositionNameEn = reader["ENGFNM"]?.ToString(),
                BirthDate      = SafeToDate(reader["BIRTHDAT"]),
                Juminno        = reader["JUMINNO"]?.ToString(),
                SexGb          = reader["SEXGB"]?.ToString(),
                HireDate       = SafeToDate(reader["IGENTDAT"]),
                DirectYn       = reader["DIRECTYN"]?.ToString(),
                Addr1          = reader["BONADDR1"]?.ToString(),
                Addr2          = reader["BONADDR2"]?.ToString(),
                Addr3          = reader["BONADDR3"]?.ToString(),
                DeptCd         = reader["DEPTCD"]?.ToString(),
                LineCd         = reader["LINECD"]?.ToString(),
                WorkCd         = reader["WORKCD"]?.ToString(),
                WorkCdCode     = reader["WORKCD_CODE"]?.ToString(),
                DeptName       = reader["DEPTNM"]?.ToString(),
                LineName       = reader["LINENM"]?.ToString(),
                WorkName       = reader["WORKNM"]?.ToString(),
                WorkCdNameEn   = reader["WORKCD_NAME"]?.ToString(),
                ShiftType      = reader["SHIFT_TYPE"]?.ToString(),
                NewDeptCd      = reader["NEW_DEPTCD"]?.ToString(),
                NewLineCd      = reader["NEW_LINECD"]?.ToString(),
                NewWorkCd      = reader["NEW_WORKCD"]?.ToString(),
                NewDeptName    = reader["NEW_DEPTNM"]?.ToString(),
                NewLineName    = reader["NEW_LINENM"]?.ToString(),
                NewWorkName    = reader["NEW_WORKNM"]?.ToString(),
            }, new OracleParameter("EMPCD", empCd.Trim()));

            return Ok(results.FirstOrDefault());
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET apiHR/Directory/change-history?empCd=xxx
    // Lịch sử chuyển dept/line/work của nhân viên (toàn bộ, không giới hạn theo ngày hôm nay).
    [HttpGet("change-history")]
    public async Task<IActionResult> GetChangeHistory(string empCd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empCd)) return BadRequest(new { error = "empCd is required" });

            string sql = @"
                SELECT
                    T.SEQ,
                    T.DAT,
                    OLDW.DEPTNM AS OLD_DEPTNM,
                    OLDW.TEAMNM AS OLD_LINENM,
                    OLDW.WORKNM AS OLD_WORKNM,
                    NEWW.DEPTNM AS NEW_DEPTNM,
                    NEWW.TEAMNM AS NEW_LINENM,
                    NEWW.WORKNM AS NEW_WORKNM
                FROM HRMS.TB_MASCHANGEWORK T
                LEFT JOIN HRMS.EAM410 OLDW
                    ON  T.DEPTCD = OLDW.DEPTCD
                    AND T.LINECD = OLDW.LINECD
                    AND T.WORKCD = OLDW.WORKCD
                LEFT JOIN HRMS.EAM410 NEWW
                    ON  T.NEW_DEPTCD = NEWW.DEPTCD
                    AND T.NEW_LINECD = NEWW.LINECD
                    AND T.NEW_WORKCD = NEWW.WORKCD
                WHERE T.EMPCD = :EMPCD
                AND T.USEYN = 'Y'
                ORDER BY T.DAT DESC, T.SEQ DESC";

            var results = await _oracleService.ExecuteQueryAsync(sql, reader => new EmployeeChangeHistoryModel
            {
                Seq         = Convert.ToInt32(reader["SEQ"]),
                Dat         = SafeToDate(reader["DAT"]),
                OldDeptName = reader["OLD_DEPTNM"]?.ToString(),
                OldLineName = reader["OLD_LINENM"]?.ToString(),
                OldWorkName = reader["OLD_WORKNM"]?.ToString(),
                NewDeptName = reader["NEW_DEPTNM"]?.ToString(),
                NewLineName = reader["NEW_LINENM"]?.ToString(),
                NewWorkName = reader["NEW_WORKNM"]?.ToString(),
            }, new OracleParameter("EMPCD", empCd.Trim()));

            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET apiHR/Directory/workcd-list?deptCd=&lineCd=&page=1&pageSize=50
    // Danh sách toàn bộ tổ hợp Dept/Line/Work (dùng để quản lý hình minh hoạ theo work cd).
    [HttpGet("workcd-list")]
    public async Task<IActionResult> GetWorkCdList(string? deptCd, string? lineCd, int page = 1, int pageSize = 50)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 200) pageSize = 50;

            var where = new List<string> { "USEYN = 'Y'" };

            // OracleParameter không được dùng chung giữa 2 command khác nhau (ORA-50030),
            // nên phải tạo instance MỚI cho mỗi lần gọi ExecuteQueryAsync -> dùng factory.
            List<OracleParameter> BuildParams()
            {
                var p = new List<OracleParameter>();
                if (!string.IsNullOrWhiteSpace(deptCd))
                    p.Add(new OracleParameter("DEPTCD", deptCd.Trim()));
                if (!string.IsNullOrWhiteSpace(lineCd))
                    p.Add(new OracleParameter("LINECD", lineCd.Trim()));
                return p;
            }

            if (!string.IsNullOrWhiteSpace(deptCd))
                where.Add("DEPTCD = :DEPTCD");
            if (!string.IsNullOrWhiteSpace(lineCd))
                where.Add("LINECD = :LINECD");

            string whereClause = string.Join(" AND ", where);

            var countSql = $@"
                SELECT COUNT(*) C FROM (
                    SELECT DISTINCT DEPTCD, LINECD, WORKCD, DEPTNM, TEAMNM, WORKNM
                    FROM HRMS.EAM410 WHERE {whereClause}
                )";
            var totalResult = await _oracleService.ExecuteQueryAsync(countSql, reader => Convert.ToInt32(reader["C"]), BuildParams().ToArray());
            int total = totalResult.FirstOrDefault();

            var sql = $@"
                SELECT DEPTCD, LINECD, WORKCD, DEPTNM, TEAMNM, WORKNM FROM (
                    SELECT DEPTCD, LINECD, WORKCD, DEPTNM, TEAMNM, WORKNM,
                           ROW_NUMBER() OVER (ORDER BY DEPTNM, TEAMNM, WORKNM) RN
                    FROM (SELECT DISTINCT DEPTCD, LINECD, WORKCD, DEPTNM, TEAMNM, WORKNM
                          FROM HRMS.EAM410 WHERE {whereClause})
                )
                WHERE RN BETWEEN :FROM_ROW AND :TO_ROW
                ORDER BY DEPTNM, TEAMNM, WORKNM";

            var pageParams = BuildParams();
            pageParams.Add(new OracleParameter("FROM_ROW", (page - 1) * pageSize + 1));
            pageParams.Add(new OracleParameter("TO_ROW", page * pageSize));

            var items = await _oracleService.ExecuteQueryAsync(sql, reader => new WorkCdItemModel
            {
                DeptCd   = reader["DEPTCD"]?.ToString() ?? "",
                LineCd   = reader["LINECD"]?.ToString() ?? "",
                WorkCd   = reader["WORKCD"]?.ToString() ?? "",
                DeptName = reader["DEPTNM"]?.ToString(),
                LineName = reader["TEAMNM"]?.ToString(),
                WorkName = reader["WORKNM"]?.ToString(),
            }, pageParams.ToArray());

            return Ok(new { total, page, pageSize, items });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // GET apiHR/Directory/dept-list - danh sách dept cho dropdown filter
    [HttpGet("dept-list")]
    public async Task<IActionResult> GetDeptList()
    {
        var items = await _oracleService.ExecuteQueryAsync(
            "SELECT DISTINCT DEPTCD, DEPTNM FROM HRMS.EAM410 WHERE USEYN = 'Y' ORDER BY DEPTNM",
            reader => new { DeptCd = reader["DEPTCD"]?.ToString(), DeptName = reader["DEPTNM"]?.ToString() });
        return Ok(items);
    }

    // GET apiHR/Directory/line-list?deptCd= - danh sách line cho dropdown filter (lọc theo dept nếu có)
    [HttpGet("line-list")]
    public async Task<IActionResult> GetLineList(string? deptCd)
    {
        var where = "USEYN = 'Y'";
        var parameters = new List<OracleParameter>();
        if (!string.IsNullOrWhiteSpace(deptCd))
        {
            where += " AND DEPTCD = :DEPTCD";
            parameters.Add(new OracleParameter("DEPTCD", deptCd.Trim()));
        }

        var items = await _oracleService.ExecuteQueryAsync(
            $"SELECT DISTINCT LINECD, TEAMNM FROM HRMS.EAM410 WHERE {where} ORDER BY TEAMNM",
            reader => new { LineCd = reader["LINECD"]?.ToString(), LineName = reader["TEAMNM"]?.ToString() },
            parameters.ToArray());
        return Ok(items);
    }

    private static DateTime? SafeToDate(object value)
    {
        if (value == null || value == DBNull.Value) return null;
        var str = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(str)) return null;

        if (DateTime.TryParseExact(str, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
            return dt;
        if (DateTime.TryParse(str, out dt))
            return dt;
        return null;
    }
}
