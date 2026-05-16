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
    // Build IN-clause scope filter từ danh sách code đã pre-fetch.
    // mainCol  : "DEPTCD" (Clerk/Manager) hoặc "WORKCD" (Supervisor)
    // mainCodes: list giá trị IN cho cột chính
    // lineCodes: list giá trị IN cho LINECD (bỏ trống = không filter line)
    // prefix   : tránh trùng tên param khi ghép nhiều filter
    // ─────────────────────────────────────────────────────────────
    public static FilterResult ForScopeByInList(
        string       mainCol,
        List<string> mainCodes,
        List<string> lineCodes,
        string       empAlias = "EC",
        string       prefix   = "SC")
    {
        var mainIn = string.Join(", ", mainCodes.Select((_, i) => $":{prefix}_M{i}"));
        string sql = $"AND {empAlias}.{mainCol} IN ({mainIn})";
        var p = mainCodes
            .Select((v, i) => new OracleParameter($"{prefix}_M{i}", OracleDbType.Varchar2) { Value = v })
            .ToList<OracleParameter>();

        if (lineCodes.Count > 0)
        {
            var lineIn = string.Join(", ", lineCodes.Select((_, i) => $":{prefix}_L{i}"));
            sql += $"\n  AND {empAlias}.LINECD IN ({lineIn})";
            p.AddRange(lineCodes.Select((v, i) =>
                new OracleParameter($"{prefix}_L{i}", OracleDbType.Varchar2) { Value = v }));
        }

        return new FilterResult(sql, p);
    }
}
