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
        const string sql = @"
            SELECT DISTINCT EC.EMPCD
              FROM HRMS.ECM100 EC
             WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
               AND (
                    :MODE = 'ALL'
                 OR EXISTS (
                        SELECT 1
                          FROM HRMS.HR_SURVEY_SCOPE SC
                          JOIN HRMS.HR_USERS_DEPT UD ON UD.EMPCD = EC.EMPCD
                         WHERE SC.SURVEY_ID = :SID
                           AND SC.SCOPE_TYPE = 'DEPT_LINE_WORK'
                           AND SC.DEPTCD = UD.DEPTCD
                           AND (SC.LINECD IS NULL OR SC.LINECD = UD.LINECD)
                           AND (SC.WORKCD IS NULL OR SC.WORKCD = UD.WORKCD)
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
            new OracleParameter("MODE", mode),
            new OracleParameter("SID",  surveyId));
    }
}
