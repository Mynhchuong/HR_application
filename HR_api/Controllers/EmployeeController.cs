using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class EmployeeController : ControllerBase
{
    private readonly OracleService _oracleService;
    public EmployeeController(OracleService oracleService) { _oracleService = oracleService; }

    // GET /apiHR/Employee/my-team?approver_empcd=&search=&deptcd=&linecd=&workcd=&shiftcd=&no_login=&page=&page_size=&all=
    //
    // Trước đây tải NGUYÊN toàn bộ danh sách khớp scope (không phân trang SQL) + chạy 2 subquery
    // tương quan lên HRMS.EBM200 (tổng giờ TC năm/tháng) CHO MỖI DÒNG — với NV có scope rộng (VD
    // thư ký phụ trách nhiều bộ phận, có thể lên tới hàng ngàn NV trong HRMS.HR_USERS_DEPT) thì
    // vừa tải quá nhiều dữ liệu vừa chạy hàng ngàn subquery, trang load cực chậm (bug thật báo
    // 2026-10-15, EMPCD 04080905: scope 69 tổ hợp phòng/chuyền/tổ = 1836 NV). Viết lại theo 3 bước:
    //   (1) COUNT tổng.
    //   (2) Lấy ĐÚNG 1 trang (ROW_NUMBER) — gồm luôn ca làm việc hôm nay (JOIN EBM200/EBM100, cần
    //       lọc theo SHIFTCD nên phải JOIN ngay trong query chính, không thể batch sau).
    //   (3) Các query GROUP BY/batch riêng (OT, phép năm) CHỈ cho ĐÚNG các EMPCD của trang đó —
    //       chi phí giờ chỉ phụ thuộc page_size, không phụ thuộc tổng số NV trong scope.
    // all=true (dùng cho Export Excel): bỏ qua ROW_NUMBER, lấy hết nhưng vẫn dùng cách gộp theo
    // nhóm ở bước (3) (chia batch IN-list) thay vì N+1 subquery như code cũ.
    //
    // Logic lõi (COUNT + phân trang + OT/phép năm theo batch) dùng CHUNG với GetAllEmployees (xem
    // BuildTeamResultAsync) — chỉ khác nhau ở mệnh đề scope truyền vào.
    [HttpGet("my-team")]
    public async Task<IActionResult> GetMyTeam(
        string  approver_empcd,
        string? search    = null,
        string? deptcd    = null,
        string? linecd    = null,
        string? workcd    = null,
        string? shiftcd   = null,
        bool    no_login  = false,
        int     page      = 1,
        int     page_size = 50,
        bool    all       = false)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", approver_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "MT");

            var result = await BuildTeamResultAsync(
                scopeFilter.SqlClause, scopeFilter.Params,
                search, deptcd, linecd, workcd, shiftcd, no_login, page, page_size, all);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Employee/all-team?requester_empcd=&search=&deptcd=&linecd=&workcd=&shiftcd=&no_login=&page=&page_size=&all=
    // Danh sách TOÀN CÔNG TY (không giới hạn theo HR_USERS_DEPT) — dành cho HR/Admin cần xem hết
    // công nhân, không riêng phạm vi phụ trách (yêu cầu 2026-10-16). Role check qua HR_USERS/HR_ROLES
    // thay cho HR_USERS_DEPT (vốn chỉ dùng để giới hạn Supervisor/Manager/thư ký, không áp dụng cho
    // HR/Admin — HR/Admin không cần có dòng nào trong HR_USERS_DEPT mới xem được hết công ty).
    [HttpGet("all-team")]
    public async Task<IActionResult> GetAllEmployees(
        string  requester_empcd,
        string? search    = null,
        string? deptcd    = null,
        string? linecd    = null,
        string? workcd    = null,
        string? shiftcd   = null,
        bool    no_login  = false,
        int     page      = 1,
        int     page_size = 50,
        bool    all       = false)
    {
        try
        {
            if (!await IsHrOrAdminAsync(requester_empcd))
                return Ok(new { success = false, message = "Bạn không có quyền xem danh sách này" });

            var result = await BuildTeamResultAsync(
                "", new List<OracleParameter>(),
                search, deptcd, linecd, workcd, shiftcd, no_login, page, page_size, all);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // Logic lõi dùng chung cho GetMyTeam (có scope) và GetAllEmployees (scopeSql="", không giới hạn).
    private async Task<object> BuildTeamResultAsync(
        string scopeSql, List<OracleParameter> scopeParams,
        string? search, string? deptcd, string? linecd, string? workcd, string? shiftcd, bool no_login,
        int page, int page_size, bool all)
    {
        var searchFilter  = string.IsNullOrWhiteSpace(search) ? "" :
            "AND (UPPER(EC.EMPCD) LIKE :SEARCH OR UPPER(EC.CNAME) LIKE :SEARCH)";
        var deptFilter    = string.IsNullOrWhiteSpace(deptcd)  ? "" : "AND EC.DEPTCD = :DEPTCD";
        var lineFilter    = string.IsNullOrWhiteSpace(linecd)  ? "" : "AND EC.LINECD = :LINECD";
        var workFilter    = string.IsNullOrWhiteSpace(workcd)  ? "" : "AND EC.WORKCD = :WORKCD";
        var shiftFilter   = string.IsNullOrWhiteSpace(shiftcd) ? "" : "AND SH.SHIFTCD = :SHIFTCD";
        var noLoginFilter = no_login ? "AND U.LASTED_LOGIN IS NULL" : "";

        // Ca làm việc hôm nay JOIN ngay trong query chính (không batch sau) vì cần lọc theo SHIFTCD
        // trước khi phân trang/COUNT — khác OT/phép năm (chỉ hiển thị, không filter, vẫn batch sau).
        string baseFrom = @"
            FROM HRMS.ECM100 EC
            LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
            LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = EC.EMPCD
            LEFT JOIN HRMS.EBM200 SH ON SH.EMPCD = EC.EMPCD AND SH.DAT = TRUNC(SYSDATE)
            LEFT JOIN HRMS.EBM100 SC ON SC.SHIFTCD = SH.SHIFTCD";
        string whereSql = $@"
            WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              {scopeSql}
              {searchFilter}
              {deptFilter}
              {lineFilter}
              {workFilter}
              {shiftFilter}
              {noLoginFilter}";

        OracleParameter[] MakeFilterParams()
        {
            var p = new List<OracleParameter>(scopeParams.Select(x => (OracleParameter)x.Clone()));
            if (!string.IsNullOrWhiteSpace(search))
                p.Add(new OracleParameter("SEARCH", OracleDbType.Varchar2) { Value = "%" + search.ToUpper() + "%" });
            if (!string.IsNullOrWhiteSpace(deptcd))
                p.Add(new OracleParameter("DEPTCD", OracleDbType.Varchar2) { Value = deptcd });
            if (!string.IsNullOrWhiteSpace(linecd))
                p.Add(new OracleParameter("LINECD", OracleDbType.Varchar2) { Value = linecd });
            if (!string.IsNullOrWhiteSpace(workcd))
                p.Add(new OracleParameter("WORKCD", OracleDbType.Varchar2) { Value = workcd });
            if (!string.IsNullOrWhiteSpace(shiftcd))
                p.Add(new OracleParameter("SHIFTCD", OracleDbType.Varchar2) { Value = shiftcd });
            return p.ToArray();
        }

        // (1) COUNT tổng.
        var totalRows = await _oracleService.ExecuteQueryAsync(
            $"SELECT COUNT(*) CNT {baseFrom} {whereSql}",
            r => Convert.ToInt32(r["CNT"]),
            MakeFilterParams());
        int total = totalRows.FirstOrDefault();

        if (total == 0)
            return new { success = true, total = 0, page, page_size, total_pages = 0, data = new List<object>() };

        // (2) Lấy danh sách NV của trang hiện tại (hoặc hết, nếu all=true) — KHÔNG có SUM_YEAR/SUM_MONTH.
        string baseSelect = @"
            SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                   EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                   EC.HOMETEL PHONE, EC.EMAIL,
                   U.LASTED_LOGIN LAST_LOGIN,
                   SC.SHIFTCD SHIFTCD, SC.STIME STIME, SC.ETIME ETIME, SC.SHIFT_TYPE SHIFT_TYPE";

        string pageSql;
        var pageParams = new List<OracleParameter>(MakeFilterParams());
        if (all)
        {
            pageSql = $"{baseSelect} {baseFrom} {whereSql} ORDER BY EC.DEPTCD, EC.LINECD, EC.WORKCD, EC.EMPCD";
        }
        else
        {
            if (page < 1) page = 1;
            if (page_size < 1) page_size = 50;
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;
            pageSql = $@"
                SELECT * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.DEPTCD, T.LINECD, T.WORKCD, T.EMPCD) RN
                    FROM ({baseSelect} {baseFrom} {whereSql}) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";
            pageParams.Add(new OracleParameter("R_MIN", offset));
            pageParams.Add(new OracleParameter("R_MAX", maxRn));
        }

        // "HHmm" → "HH:mm" để hiển thị, VD "0730" → "07:30".
        static string FmtHm(string? hhmm) =>
            !string.IsNullOrEmpty(hhmm) && hhmm.Length == 4 ? $"{hhmm.Substring(0, 2)}:{hhmm.Substring(2, 2)}" : "";

        var pageList = await _oracleService.ExecuteQueryAsync(pageSql, r => new
        {
            EMPCD      = r["EMPCD"]?.ToString()     ?? "",
            EMP_NAME   = r["EMP_NAME"]?.ToString()  ?? "",
            DEPTCD     = r["DEPTCD"]?.ToString()    ?? "",
            LINECD     = r["LINECD"]?.ToString()    ?? "",
            WORKCD     = r["WORKCD"]?.ToString()    ?? "",
            DEPT_NAME  = r["DEPT_NAME"]?.ToString() ?? "",
            LINE_NAME  = r["LINE_NAME"]?.ToString() ?? "",
            WORK_NAME  = r["WORK_NAME"]?.ToString() ?? "",
            PHONE      = r["PHONE"]?.ToString()     ?? "",
            EMAIL      = r["EMAIL"]?.ToString()     ?? "",
            LAST_LOGIN = r["LAST_LOGIN"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LAST_LOGIN"]),
            SHIFT_CD   = r["SHIFTCD"]?.ToString()   ?? "",
            SHIFT_TIME_RAW_S = r["STIME"]?.ToString() ?? "",
            SHIFT_TIME_RAW_E = r["ETIME"]?.ToString() ?? "",
            SHIFT_TYPE = r["SHIFT_TYPE"]?.ToString() ?? "",
        }, pageParams.ToArray());

        // (3) 1 query GROUP BY duy nhất lấy tổng giờ TC năm/tháng cho ĐÚNG các EMPCD vừa lấy —
        // chia batch 1000 EMPCD/lần (giới hạn IN-list của Oracle).
        var otByEmpcd = new Dictionary<string, (decimal Year, decimal Month)>();
        var empcds = pageList.Select(x => x.EMPCD).Distinct().ToList();
        for (int i = 0; i < empcds.Count; i += 1000)
        {
            var batch = empcds.Skip(i).Take(1000).ToList();
            var inParams = batch.Select((id, j) => new OracleParameter($"E{j}", id)).ToArray();
            var inList = string.Join(",", inParams.Select(p => $":{p.ParameterName}"));
            string otSql = $@"
                SELECT EMPCD,
                       SUM(CASE WHEN DAT >= TRUNC(SYSDATE,'YYYY') THEN NVL(T_ROT,0)+NVL(T_OT,0) ELSE 0 END) SUM_YEAR,
                       SUM(CASE WHEN TO_CHAR(DAT,'YYYYMM') = TO_CHAR(SYSDATE,'YYYYMM') THEN NVL(T_ROT,0)+NVL(T_OT,0) ELSE 0 END) SUM_MONTH
                FROM HRMS.EBM200
                WHERE EMPCD IN ({inList})
                  AND DAT BETWEEN TRUNC(SYSDATE,'YYYY') AND SYSDATE
                GROUP BY EMPCD";
            var otRows = await _oracleService.ExecuteQueryAsync(otSql, r => new
            {
                EMPCD     = r["EMPCD"]?.ToString() ?? "",
                SUM_YEAR  = r["SUM_YEAR"]  == DBNull.Value ? 0m : Convert.ToDecimal(r["SUM_YEAR"]),
                SUM_MONTH = r["SUM_MONTH"] == DBNull.Value ? 0m : Convert.ToDecimal(r["SUM_MONTH"]),
            }, inParams);
            foreach (var o in otRows) otByEmpcd[o.EMPCD] = (o.SUM_YEAR, o.SUM_MONTH);
        }

        // Phép năm còn lại = RECEIVE_NUM (cấp trong năm, bảng EFM100) - USED_NUM (đã nghỉ, bảng
        // EFM410) — công thức giống LeaveController.CheckAlBalanceAsync/GetAnnualBalance, tính
        // theo batch cho ĐÚNG EMPCD của trang hiện tại (không phải N+1 theo từng dòng).
        var leaveLeftByEmpcd = new Dictionary<string, int>();
        for (int i = 0; i < empcds.Count; i += 1000)
        {
            var batch = empcds.Skip(i).Take(1000).ToList();
            var inParams = batch.Select((id, j) => new OracleParameter($"L{j}", id)).ToArray();
            var inList = string.Join(",", inParams.Select(p => $":{p.ParameterName}"));
            string leaveSql = $@"
                WITH ALLOC AS (
                    SELECT EMPCD, MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY') AND EMPCD IN ({inList})
                    GROUP BY EMPCD
                ),
                USED AS (
                    SELECT EMPCD, COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY') AND EMPCD IN ({inList})
                    GROUP BY EMPCD
                )
                SELECT NVL(A.EMPCD, U.EMPCD) AS EMPCD,
                       NVL(A.RECEIVE_NUM,0) - NVL(U.USED_NUM,0) AS LEFT_NUM
                FROM ALLOC A
                FULL OUTER JOIN USED U ON U.EMPCD = A.EMPCD";
            var leaveRows = await _oracleService.ExecuteQueryAsync(leaveSql, r => new
            {
                EMPCD     = r["EMPCD"]?.ToString() ?? "",
                LEFT_NUM  = r["LEFT_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"]),
            }, inParams);
            foreach (var l in leaveRows) leaveLeftByEmpcd[l.EMPCD] = l.LEFT_NUM;
        }

        var data = pageList.Select(d =>
        {
            otByEmpcd.TryGetValue(d.EMPCD, out var ot);
            leaveLeftByEmpcd.TryGetValue(d.EMPCD, out var leaveLeft);
            return new
            {
                d.EMPCD, d.EMP_NAME, d.DEPTCD, d.LINECD, d.WORKCD,
                d.DEPT_NAME, d.LINE_NAME, d.WORK_NAME, d.PHONE, d.EMAIL, d.LAST_LOGIN,
                SUM_YEAR = ot.Year, SUM_MONTH = ot.Month,
                LEAVE_LEFT = leaveLeft,
                SHIFT_CD   = d.SHIFT_CD,
                SHIFT_TIME = string.IsNullOrEmpty(d.SHIFT_CD) ? "" : $"{FmtHm(d.SHIFT_TIME_RAW_S)}-{FmtHm(d.SHIFT_TIME_RAW_E)}",
                SHIFT_TYPE = d.SHIFT_TYPE
            };
        }).ToList();

        int totalPages = all ? 1 : (int)Math.Ceiling((double)total / page_size);
        return new { success = true, total, page, page_size = all ? total : page_size, total_pages = totalPages, data };
    }

    // GET /apiHR/Employee/my-team-filters?approver_empcd=
    // Danh sách PHÂN BIỆT (DEPTCD,LINECD,WORKCD) trong đúng scope của NV đó + danh sách CA LÀM VIỆC
    // (master, không phụ thuộc scope) — dùng dựng dropdown Bộ phận/Line/Nhóm việc/Ca phía FE,
    // KHÔNG cần tải hết danh sách NV.
    [HttpGet("my-team-filters")]
    public async Task<IActionResult> GetMyTeamFilters(string approver_empcd)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "MTF");
            var result = await BuildFiltersResultAsync(scopeFilter.SqlClause, scopeFilter.Params);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Employee/all-team-filters?requester_empcd=
    // Bản không giới hạn scope của my-team-filters, dùng cho trang "toàn công ty" của HR/Admin.
    [HttpGet("all-team-filters")]
    public async Task<IActionResult> GetAllEmployeesFilters(string requester_empcd)
    {
        try
        {
            if (!await IsHrOrAdminAsync(requester_empcd))
                return Ok(new { success = false, message = "Bạn không có quyền xem danh sách này" });

            var result = await BuildFiltersResultAsync("", new List<OracleParameter>());
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    private async Task<object> BuildFiltersResultAsync(string scopeSql, List<OracleParameter> scopeParams)
    {
        string sql = $@"
            SELECT DISTINCT EC.DEPTCD, B.DEPTNM DEPT_NAME, EC.LINECD, B.TEAMNM LINE_NAME, EC.WORKCD, B.WORKNM WORK_NAME
            FROM HRMS.ECM100 EC
            LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
            WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              {scopeSql}
            ORDER BY EC.DEPTCD, EC.LINECD, EC.WORKCD";

        var data = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            DEPTCD    = r["DEPTCD"]?.ToString()    ?? "",
            DEPT_NAME = r["DEPT_NAME"]?.ToString() ?? "",
            LINECD    = r["LINECD"]?.ToString()    ?? "",
            LINE_NAME = r["LINE_NAME"]?.ToString() ?? "",
            WORKCD    = r["WORKCD"]?.ToString()    ?? "",
            WORK_NAME = r["WORK_NAME"]?.ToString() ?? "",
        }, scopeParams.ToArray());

        // Danh sách ca làm việc (master EBM100) — KHÔNG phụ thuộc scope, chung cho toàn công ty,
        // dùng cho dropdown lọc "Ca làm việc".
        static string FmtHm(string? hhmm) =>
            !string.IsNullOrEmpty(hhmm) && hhmm.Length == 4 ? $"{hhmm.Substring(0, 2)}:{hhmm.Substring(2, 2)}" : "";

        var shiftRows = await _oracleService.ExecuteQueryAsync(
            "SELECT SHIFTCD, STIME, ETIME, SHIFT_TYPE FROM HRMS.EBM100 ORDER BY SHIFTCD",
            r => new
            {
                SHIFTCD    = r["SHIFTCD"]?.ToString()    ?? "",
                STIME      = r["STIME"]?.ToString()      ?? "",
                ETIME      = r["ETIME"]?.ToString()      ?? "",
                SHIFT_TYPE = r["SHIFT_TYPE"]?.ToString() ?? "",
            });
        var shifts = shiftRows.Select(s => new
        {
            SHIFTCD    = s.SHIFTCD,
            SHIFT_TIME = $"{FmtHm(s.STIME)}-{FmtHm(s.ETIME)}",
            SHIFT_TYPE = s.SHIFT_TYPE
        }).ToList();

        return new { success = true, data, shifts };
    }

    // Role check cho all-team/all-team-filters — HR/Admin không cần có dòng trong HR_USERS_DEPT
    // (khác Supervisor/Manager/thư ký) nên không dùng IsAuthorized+HR_USERS_DEPT như my-team.
    private async Task<bool> IsHrOrAdminAsync(string? empCd)
    {
        if (string.IsNullOrEmpty(empCd)) return false;
        var rows = await _oracleService.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_USERS U
            JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID
            WHERE U.EMPCD = :EMPCD AND R.ROLE_NAME IN ('Admin','HR') AND ROWNUM = 1",
            r => 1,
            new OracleParameter("EMPCD", empCd));
        return rows.Count > 0;
    }
}
