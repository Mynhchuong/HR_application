using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Snapshot recipient list vào HR_SURVEY_RECIPIENT khi survey chuyển SCHEDULED → ACTIVE.
// Query pending survey cho user + kiểm tra membership.
public class SurveyRecipientService
{
    private readonly OracleService _db;
    private readonly SurveyScopeService _scope;

    public SurveyRecipientService(OracleService db, SurveyScopeService scope)
    {
        _db = db;
        _scope = scope;
    }

    // Bulk INSERT ~8000 rows dùng ArrayBind.
    public async Task<int> SnapshotAsync(int surveyId)
    {
        // Clear rows cũ (idempotent — nếu batch re-run)
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_SURVEY_RECIPIENT WHERE SURVEY_ID = :SID",
            new OracleParameter("SID", surveyId));

        var empCds = await _scope.ExpandAsync(surveyId);
        if (empCds.Count == 0) return 0;

        const string sql = @"
            INSERT INTO HRMS.HR_SURVEY_RECIPIENT (SURVEY_ID, EMPCD, SNAPSHOT_DT)
            VALUES (:SID, :EMPCD, SYSDATE)";

        var pSid = new OracleParameter("SID", OracleDbType.Int32)
        {
            Value = Enumerable.Repeat(surveyId, empCds.Count).ToArray()
        };
        var pEmp = new OracleParameter("EMPCD", OracleDbType.Varchar2)
        {
            Value = empCds.ToArray()
        };

        return await _db.ExecuteBulkInsertAsync(sql, empCds.Count, pSid, pEmp);
    }

    public async Task<bool> IsRecipientAsync(int surveyId, string empcd)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT 1 FROM HRMS.HR_SURVEY_RECIPIENT WHERE SURVEY_ID = :SID AND EMPCD = :EMPCD",
            r => 1,
            new OracleParameter("SID",   surveyId),
            new OracleParameter("EMPCD", empcd));
        return rows.Count > 0;
    }

    // Trả về ID survey ACTIVE nhỏ nhất mà user chưa làm (dùng cho filter chặn app).
    public async Task<int?> GetOldestPendingSurveyIdAsync(string empcd)
    {
        const string sql = @"
            SELECT MIN(S.ID) AS ID
              FROM HRMS.HR_SURVEY S
              JOIN HRMS.HR_SURVEY_RECIPIENT R ON R.SURVEY_ID = S.ID
             WHERE S.STATUS = 'ACTIVE'
               AND R.EMPCD = :EMPCD
               AND NOT EXISTS (
                    SELECT 1 FROM HRMS.HR_SURVEY_RESPONSE X
                     WHERE X.SURVEY_ID = S.ID
                       AND X.EMPCD = :EMPCD
                       AND X.STATUS IN ('SUBMITTED','AUTO_SUBMITTED','ILLITERATE_SKIP')
                )";
        var rows = await _db.ExecuteQueryAsync(sql,
            r => r["ID"] is DBNull ? (int?)null : Convert.ToInt32(r["ID"]),
            new OracleParameter("EMPCD", empcd));
        return rows.FirstOrDefault();
    }
}
