using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Expand HR_SURVEY_SCOPE + RECIPIENT_MODE thành danh sách EMPCD.
// Loại NV nghỉ việc (ECM100.RETDAT) và blacklist active (HR_SURVEY_EXEMPT).
// DEPT_LINE_WORK join qua HR_USERS_DEPT (mapping do project quản lý).
public class SurveyScopeService
{
    private readonly OracleService _db;
    public SurveyScopeService(OracleService db) { _db = db; }

    public async Task<List<string>> ExpandAsync(int surveyId)
    {
        // Lấy mode trước
        var modeRows = await _db.ExecuteQueryAsync(
            "SELECT RECIPIENT_MODE FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => r["RECIPIENT_MODE"]?.ToString() ?? "ALL",
            new OracleParameter("ID", surveyId));

        var mode = modeRows.FirstOrDefault() ?? "ALL";

        // Union scope + explicit EMPCD list, trừ blacklist active
        // Dùng EC.DEPTCD/LINECD/WORKCD trực tiếp thay vì HR_USERS_DEPT
        const string sql = @"
            SELECT DISTINCT EC.EMPCD
              FROM HRMS.ECM100 EC
             WHERE EC.JEAJIKGB = 'Y'
               AND (
                    :RMODE = 'ALL'
                 OR EXISTS (
                        SELECT 1 FROM HRMS.HR_SURVEY_SCOPE SC
                         WHERE SC.SURVEY_ID  = :SID
                           AND SC.SCOPE_TYPE = 'DEPT_LINE_WORK'
                           AND SC.DEPTCD     = EC.DEPTCD
                           AND (SC.LINECD IS NULL OR SC.LINECD = EC.LINECD)
                           AND (SC.WORKCD IS NULL OR SC.WORKCD = EC.WORKCD)
                    )
                 OR EC.EMPCD IN (
                        SELECT SC.EMPCD FROM HRMS.HR_SURVEY_SCOPE SC
                         WHERE SC.SURVEY_ID = :SID
                           AND SC.SCOPE_TYPE = 'EMPCD'
                           AND SC.EMPCD IS NOT NULL
                    )
                )
               AND NOT EXISTS (
                    SELECT 1 FROM HRMS.HR_SURVEY_EXEMPT EX
                     WHERE EX.EMPCD = EC.EMPCD
                       AND EX.IS_ACTIVE = 1
                       AND EX.EFFECTIVE_DATE <= TRUNC(SYSDATE)
                )";

        return await _db.ExecuteQueryAsync(sql,
            r => r["EMPCD"]?.ToString() ?? "",
            new OracleParameter("RMODE", mode),
            new OracleParameter("SID",   surveyId));
    }

    // Đếm NV active match với 1 scope row (Dept + tuỳ chọn Line/Work).
    // Dùng cho preview trước khi Publish.
    public async Task<int> CountScopeAsync(string deptcd, string? linecd, string? workcd)
    {
        const string sql = @"
            SELECT COUNT(*) AS CNT FROM HRMS.ECM100 EC
             WHERE EC.JEAJIKGB = 'Y'
               AND EC.DEPTCD = :DEPTCD
               AND (:LINECD IS NULL OR EC.LINECD = :LINECD)
               AND (:WORKCD IS NULL OR EC.WORKCD = :WORKCD)
               AND NOT EXISTS (
                    SELECT 1 FROM HRMS.HR_SURVEY_EXEMPT EX
                     WHERE EX.EMPCD = EC.EMPCD
                       AND EX.IS_ACTIVE = 1
                       AND EX.EFFECTIVE_DATE <= TRUNC(SYSDATE)
               )";
        var rows = await _db.ExecuteQueryAsync(sql, r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("DEPTCD", deptcd),
            new OracleParameter("LINECD", (object?)linecd ?? DBNull.Value),
            new OracleParameter("WORKCD", (object?)workcd ?? DBNull.Value));
        return rows.FirstOrDefault();
    }
}
