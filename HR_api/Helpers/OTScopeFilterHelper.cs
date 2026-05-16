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
    // Filter bằng exact tuple (DEPTCD, LINECD, WORKCD) từ HR_USERS_DEPT.
    // Tránh false-positive khi cùng code (LINECD/WORKCD) bị reuse
    // ở các DEPT khác nhau trong EAM410.
    // prefix: tránh trùng tên param khi ghép nhiều filter trong 1 query.
    // ─────────────────────────────────────────────────────────────
    public static FilterResult ForScopeByTuple(
        string empCd,
        string empAlias = "EC",
        string prefix   = "SC")
    {
        string paramName = $"{prefix}_EMPCD";
        string sql = $@"AND ({empAlias}.DEPTCD, {empAlias}.LINECD, {empAlias}.WORKCD) IN (
        SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :{paramName}
    )";
        var p = new List<OracleParameter>
        {
            new OracleParameter(paramName, OracleDbType.Varchar2) { Value = empCd }
        };
        return new FilterResult(sql, p);
    }
}
