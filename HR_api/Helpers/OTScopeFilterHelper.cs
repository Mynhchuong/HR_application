using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;


public static class OTScopeFilterHelper
{
    public record FilterResult(string SqlClause, List<OracleParameter> Params);

    // ─────────────────────────────────────────────────────────────
    // Supervisor / Manager / Assistant
    // filter_type = "work"  → lọc EC.WORKCD  (Supervisor)
    // filter_type = "dept"  → lọc EC.DEPTCD  (Manager/Assistant)
    // filter_line_codes     → lọc thêm EC.LINECD nếu có
    // ─────────────────────────────────────────────────────────────
    public static FilterResult ForScope(
        string  filterType,
        string  filterCodes,
        string? filterLineCodes = null,
        string  empAlias        = "EC")
    {
        bool   isSupervisor = string.Equals(filterType, "work", StringComparison.OrdinalIgnoreCase);
        string col          = isSupervisor ? $"{empAlias}.WORKCD" : $"{empAlias}.DEPTCD";

        string sql = $"AND INSTR(',' || :FILTER_CODES || ',', ',' || {col} || ',') > 0";
        var    p   = new List<OracleParameter>
        {
            new OracleParameter("FILTER_CODES", OracleDbType.Varchar2) { Value = filterCodes }
        };

        if (!string.IsNullOrEmpty(filterLineCodes))
        {
            sql += $"\n  AND INSTR(',' || :FILTER_LINE_CODES || ',', ',' || {empAlias}.LINECD || ',') > 0";
            p.Add(new OracleParameter("FILTER_LINE_CODES", OracleDbType.Varchar2) { Value = filterLineCodes });
        }

        return new FilterResult(sql, p);
    }

    // ─────────────────────────────────────────────────────────────
    // Supervisor / Manager / Assistant — truy vấn trực tiếp HR_USERS_DEPT
    // Thay cho ForScope (cookie-based). EXISTS match chính xác từng cặp
    // (WORKCD+LINECD cho Supervisor, DEPTCD+LINECD cho Manager/Assistant).
    //
    // Cách dùng:
    //   var f = OTScopeFilterHelper.ForScopeByEmpcd(filter_type, supervisor_empcd);
    //   whereSql += f.SqlClause;
    //   baseParams.AddRange(f.Params);
    // ─────────────────────────────────────────────────────────────
    public static FilterResult ForScopeByEmpcd(
        string filterType,
        string supervisorEmpcd,
        string empAlias = "EC")
    {
        bool   isSupervisor = string.Equals(filterType, "work", StringComparison.OrdinalIgnoreCase);
        string mainCol      = isSupervisor ? "WORKCD" : "DEPTCD";

        string sql = $@"AND EXISTS (
    SELECT 1 FROM HRMS.HR_USERS_DEPT UD
    WHERE UD.EMPCD   = :SUP_EMPCD
      AND UD.{mainCol} = {empAlias}.{mainCol}
      AND UD.LINECD  = {empAlias}.LINECD
)";
        return new FilterResult(sql, new List<OracleParameter>
        {
            new OracleParameter("SUP_EMPCD", OracleDbType.Varchar2) { Value = supervisorEmpcd }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Authorization check + standard "not authorized" response
    // ─────────────────────────────────────────────────────────────
    public static bool IsAuthorized(string? value) => !string.IsNullOrEmpty(value);

    public static object NotAuthorizedResponse(int page, int pageSize) => new
    {
        success     = true,
        summary     = new { TOTAL = 0, PENDING = 0, CONFIRMED = 0, REJECTED = 0 },
        total       = 0,
        page,
        page_size   = pageSize,
        total_pages = 0,
        data        = new System.Collections.Generic.List<object>(),
        message     = "Chưa được phân quyền bộ phận"
    };

    // ─────────────────────────────────────────────────────────────
    // Clerk / Thư ký — luôn filter theo DEPTCD
    // linecd != null  → thêm AND LINECD (dùng khi dept quá lớn, vd A01001)
    // ─────────────────────────────────────────────────────────────
    public static FilterResult ForClerkByDept(
        string  deptcd,
        string? linecd    = null,
        string  empAlias  = "EC")
    {
        string sql = $"AND {empAlias}.DEPTCD = :CLERK_DEPTCD";
        var p = new List<OracleParameter>
        {
            new OracleParameter("CLERK_DEPTCD", OracleDbType.Varchar2) { Value = deptcd }
        };
        if (!string.IsNullOrEmpty(linecd))
        {
            sql += $"\n  AND {empAlias}.LINECD = :CLERK_LINECD";
            p.Add(new OracleParameter("CLERK_LINECD", OracleDbType.Varchar2) { Value = linecd });
        }
        return new FilterResult(sql, p);
    }
}
