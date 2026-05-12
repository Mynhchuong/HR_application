using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;

/// <summary>
/// Tạo SQL WHERE clause và OracleParameter cho auto-filter phạm vi OT.
/// Dùng chung cho mọi page cần filter theo dept/work/line của user.
///
/// Cách dùng:
///   var f = OTScopeFilterHelper.ForScope(filter_type, filter_codes, filter_line_codes);
///   string whereSql = "WHERE ... " + f.SqlClause;
///   baseParams.AddRange(f.Params);
/// </summary>
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
    // Clerk / Thư ký
    // isOffice = true  → lọc EC.LINECD  (văn phòng, dùng line)
    // isOffice = false → lọc EC.DEPTCD  (sản xuất, dùng dept)
    // ─────────────────────────────────────────────────────────────
    public static FilterResult ForClerk(
        bool   isOffice,
        string filterVal,
        string empAlias = "EC")
    {
        string col = isOffice ? $"{empAlias}.LINECD" : $"{empAlias}.DEPTCD";
        return new FilterResult(
            $"AND {col} = :FILTER_VAL",
            new List<OracleParameter>
            {
                new OracleParameter("FILTER_VAL", OracleDbType.Varchar2) { Value = filterVal }
            });
    }
}
