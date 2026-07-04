using HR_api.Data;
using HR_api.Models.Survey;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// CRUD blacklist HR_SURVEY_EXEMPT. Import/export Excel + template để Phase 5.
public class SurveyExemptService
{
    private readonly OracleService _db;
    public SurveyExemptService(OracleService db) { _db = db; }

    public async Task<List<SurveyExemptModel>> ListAsync(
        string? empcdSearch, string? exemptType, int? isActive,
        string? nameSearch = null, string? deptcd = null, string? linecd = null, string? workcd = null)
    {
        // Join ECM100 để lấy tên + dept/line/work hiện tại (không phải snapshot).
        // Filter Dept/Line/Work theo ECM100 (mapping chính) — cascade optional.
        const string sql = @"
            SELECT EX.EMPCD, EX.EXEMPT_TYPE, EX.NOTE, EX.EFFECTIVE_DATE, EX.IS_ACTIVE,
                   EX.INST_ID, EX.INST_DT, EX.UPDT_ID, EX.UPDT_DT,
                   EC.CNAME AS FULL_NAME,
                   EC.DEPTCD, EC.LINECD, EC.WORKCD
              FROM HRMS.HR_SURVEY_EXEMPT EX
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = EX.EMPCD
             WHERE (:P_EMP    IS NULL OR EX.EMPCD LIKE '%' || :P_EMP || '%')
               AND (:P_TYPE   IS NULL OR EX.EXEMPT_TYPE = :P_TYPE)
               AND (:P_ACTIVE IS NULL OR EX.IS_ACTIVE   = :P_ACTIVE)
               AND (:P_NAME   IS NULL OR UPPER(EC.CNAME) LIKE '%' || UPPER(:P_NAME) || '%')
               AND (:P_DEPT   IS NULL OR EC.DEPTCD = :P_DEPT)
               AND (:P_LINE   IS NULL OR EC.LINECD = :P_LINE)
               AND (:P_WORK   IS NULL OR EC.WORKCD = :P_WORK)
             ORDER BY EX.UPDT_DT DESC NULLS LAST, EX.INST_DT DESC";

        return await _db.ExecuteQueryAsync(sql, r => new SurveyExemptModel
        {
            EMPCD          = r["EMPCD"]?.ToString() ?? "",
            EXEMPT_TYPE    = r["EXEMPT_TYPE"]?.ToString() ?? "",
            NOTE           = r["NOTE"] as string,
            EFFECTIVE_DATE = Convert.ToDateTime(r["EFFECTIVE_DATE"]),
            IS_ACTIVE      = Convert.ToInt32(r["IS_ACTIVE"]),
            INST_ID        = r["INST_ID"] as string,
            INST_DT        = r["INST_DT"] as DateTime?,
            UPDT_ID        = r["UPDT_ID"] as string,
            UPDT_DT        = r["UPDT_DT"] as DateTime?,
            FULL_NAME      = r["FULL_NAME"] as string,
            DEPTCD         = r["DEPTCD"] as string,
            LINECD         = r["LINECD"] as string,
            WORKCD         = r["WORKCD"] as string,
        },
            new OracleParameter("P_EMP",    (object?)empcdSearch ?? DBNull.Value),
            new OracleParameter("P_TYPE",   (object?)exemptType  ?? DBNull.Value),
            new OracleParameter("P_ACTIVE", (object?)isActive    ?? DBNull.Value),
            new OracleParameter("P_NAME",   (object?)nameSearch  ?? DBNull.Value),
            new OracleParameter("P_DEPT",   (object?)deptcd      ?? DBNull.Value),
            new OracleParameter("P_LINE",   (object?)linecd      ?? DBNull.Value),
            new OracleParameter("P_WORK",   (object?)workcd      ?? DBNull.Value));
    }

    public async Task SaveAsync(SaveExemptRequest req)
    {
        const string sql = @"
            MERGE INTO HRMS.HR_SURVEY_EXEMPT EX
            USING (SELECT :EMP AS EMPCD, :TYPE AS EXEMPT_TYPE FROM DUAL) SRC
               ON (EX.EMPCD = SRC.EMPCD AND EX.EXEMPT_TYPE = SRC.EXEMPT_TYPE)
             WHEN MATCHED THEN UPDATE
                SET NOTE           = :NOTE,
                    EFFECTIVE_DATE = COALESCE(:EFF, EX.EFFECTIVE_DATE),
                    IS_ACTIVE      = :ACT,
                    UPDT_ID        = :ACTOR
             WHEN NOT MATCHED THEN INSERT (EMPCD, EXEMPT_TYPE, NOTE, EFFECTIVE_DATE, IS_ACTIVE, INST_ID)
                VALUES (:EMP, :TYPE, :NOTE, COALESCE(:EFF, TRUNC(SYSDATE)), :ACT, :ACTOR)";

        await _db.ExecuteNonQueryAsync(sql,
            new OracleParameter("EMP",   req.EMPCD),
            new OracleParameter("TYPE",  req.EXEMPT_TYPE),
            new OracleParameter("NOTE",  (object?)req.NOTE ?? DBNull.Value),
            new OracleParameter("EFF",   (object?)req.EFFECTIVE_DATE ?? DBNull.Value),
            new OracleParameter("ACT",   req.IS_ACTIVE),
            new OracleParameter("ACTOR", (object?)req.LOGIN_USER ?? DBNull.Value));
    }

    // Bulk import (loop MERGE) — trả về số dòng insert/update
    public async Task<int> ImportAsync(List<SaveExemptRequest> items, string? actor)
    {
        int count = 0;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.EMPCD) || string.IsNullOrWhiteSpace(it.EXEMPT_TYPE)) continue;
            it.LOGIN_USER ??= actor;
            it.IS_ACTIVE = it.IS_ACTIVE == 0 ? 0 : 1;
            await SaveAsync(it);
            count++;
        }
        return count;
    }

    // Soft delete = IS_ACTIVE = 0
    public async Task DeleteAsync(DeleteExemptRequest req)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_SURVEY_EXEMPT
               SET IS_ACTIVE = 0, UPDT_ID = :ACTOR
             WHERE EMPCD = :EMP AND EXEMPT_TYPE = :TYPE",
            new OracleParameter("EMP",   req.EMPCD),
            new OracleParameter("TYPE",  req.EXEMPT_TYPE),
            new OracleParameter("ACTOR", (object?)req.LOGIN_USER ?? DBNull.Value));
    }
}
