using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Recipient logic — lazy check, không snapshot toàn công ty.
//   • RECIPIENT_MODE = 'ALL'        → không lưu row, check on-the-fly qua HR_SURVEY_EXEMPT
//   • DEPT_LINE_WORK / EMPCD_LIST / MIXED → snapshot vào HR_SURVEY_RECIPIENT khi ACTIVE
public class SurveyRecipientService
{
    private readonly OracleService _db;
    private readonly SurveyScopeService _scope;

    public SurveyRecipientService(OracleService db, SurveyScopeService scope)
    {
        _db = db;
        _scope = scope;
    }

    private const int BatchSize = 50;

    // Snapshot khi survey chuyển SCHEDULED → ACTIVE.
    // Với mode ALL: skip insert (tránh 8k+ rows), trả về số nhân viên hợp lệ để log.
    public async Task<int> SnapshotAsync(int surveyId)
    {
        return await SnapshotWithProgressAsync(surveyId, null);
    }

    // Batch INSERT 50 rows/lần. Nếu có onProgress → callback sau mỗi batch để stream tiến độ.
    public async Task<int> SnapshotWithProgressAsync(int surveyId, Func<int, int, Task>? onProgress)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_SURVEY_RECIPIENT WHERE SURVEY_ID = :SID",
            new OracleParameter("SID", surveyId));

        var mode = await GetRecipientModeAsync(surveyId);
        if (mode == "ALL")
        {
            var cnt = await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) C FROM HRMS.ECM100 EC
                 WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                   AND NOT EXISTS (
                        SELECT 1 FROM HRMS.HR_SURVEY_EXEMPT EX
                         WHERE EX.EMPCD = EC.EMPCD
                           AND EX.IS_ACTIVE = 1
                           AND EX.EFFECTIVE_DATE <= TRUNC(SYSDATE)
                   )",
                r => Convert.ToInt32(r["C"]));
            var total = cnt.FirstOrDefault();
            if (onProgress != null) await onProgress(total, total);
            return total;
        }

        var empCds = await _scope.ExpandAsync(surveyId);
        if (empCds.Count == 0)
        {
            if (onProgress != null) await onProgress(0, 0);
            return 0;
        }

        const string sql = @"
            INSERT INTO HRMS.HR_SURVEY_RECIPIENT (SURVEY_ID, EMPCD, SNAPSHOT_DT)
            VALUES (:SID, :EMPCD, SYSDATE)";

        int inserted = 0;
        for (int i = 0; i < empCds.Count; i += BatchSize)
        {
            var chunk = empCds.Skip(i).Take(BatchSize).ToArray();
            var pSid = new OracleParameter("SID", OracleDbType.Int32)
            {
                Value = Enumerable.Repeat(surveyId, chunk.Length).ToArray()
            };
            var pEmp = new OracleParameter("EMPCD", OracleDbType.Varchar2)
            {
                Value = chunk
            };

            inserted += await _db.ExecuteBulkInsertAsync(sql, chunk.Length, pSid, pEmp);
            if (onProgress != null) await onProgress(inserted, empCds.Count);
        }

        return inserted;
    }

    // Check user có thuộc phạm vi survey không.
    //   • Mode ALL → không nằm trong exempt là được
    //   • Mode khác → có row trong HR_SURVEY_RECIPIENT
    public async Task<bool> IsRecipientAsync(int surveyId, string empcd)
    {
        var mode = await GetRecipientModeAsync(surveyId);

        if (mode == "ALL")
        {
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT 1 FROM HRMS.ECM100 EC
                 WHERE EC.EMPCD = :EMPCD
                   AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                   AND NOT EXISTS (
                        SELECT 1 FROM HRMS.HR_SURVEY_EXEMPT EX
                         WHERE EX.EMPCD = EC.EMPCD
                           AND EX.IS_ACTIVE = 1
                           AND EX.EFFECTIVE_DATE <= TRUNC(SYSDATE)
                   )",
                r => 1,
                new OracleParameter("EMPCD", empcd));
            return rows.Count > 0;
        }

        var recRows = await _db.ExecuteQueryAsync(
            "SELECT 1 FROM HRMS.HR_SURVEY_RECIPIENT WHERE SURVEY_ID = :SID AND EMPCD = :EMPCD",
            r => 1,
            new OracleParameter("SID",   surveyId),
            new OracleParameter("EMPCD", empcd));
        return recRows.Count > 0;
    }

    // Trả về ID survey ACTIVE nhỏ nhất mà user chưa làm (dùng cho filter chặn app).
    // Combine: ALL-mode (check exempt) OR có row trong HR_SURVEY_RECIPIENT.
    // Filter LANG theo role: Expat (7) chỉ thấy EN, NV VN thường chỉ thấy VI.
    public async Task<int?> GetOldestPendingSurveyIdAsync(string empcd, int? roleId = null)
    {
        var langFilter = roleId == 7 ? "EN" : "VI";
        const string sql = @"
            SELECT MIN(S.ID) AS ID
              FROM HRMS.HR_SURVEY S
             WHERE S.STATUS = 'ACTIVE'
               AND S.LANG = :LANG
               AND (
                    (S.RECIPIENT_MODE = 'ALL'
                        AND NOT EXISTS (
                            SELECT 1 FROM HRMS.HR_SURVEY_EXEMPT EX
                             WHERE EX.EMPCD = :EMPCD
                               AND EX.IS_ACTIVE = 1
                               AND EX.EFFECTIVE_DATE <= TRUNC(SYSDATE)
                        )
                    )
                 OR EXISTS (
                        SELECT 1 FROM HRMS.HR_SURVEY_RECIPIENT R
                         WHERE R.SURVEY_ID = S.ID
                           AND R.EMPCD = :EMPCD
                    )
                )
               AND NOT EXISTS (
                    SELECT 1 FROM HRMS.HR_SURVEY_RESPONSE X
                     WHERE X.SURVEY_ID = S.ID
                       AND X.EMPCD = :EMPCD
                       AND X.STATUS IN ('SUBMITTED','AUTO_SUBMITTED','ILLITERATE_SKIP')
                )";
        var rows = await _db.ExecuteQueryAsync(sql,
            r => r["ID"] is DBNull ? (int?)null : Convert.ToInt32(r["ID"]),
            new OracleParameter("LANG",  langFilter),
            new OracleParameter("EMPCD", empcd));
        return rows.FirstOrDefault();
    }

    private async Task<string> GetRecipientModeAsync(int surveyId)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT RECIPIENT_MODE FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => r["RECIPIENT_MODE"]?.ToString() ?? "ALL",
            new OracleParameter("ID", surveyId));
        return rows.FirstOrDefault() ?? "ALL";
    }
}
