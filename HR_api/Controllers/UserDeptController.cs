using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.UserDept;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class UserDeptController : ControllerBase
{
    private readonly OracleService _oracleService;

    public UserDeptController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // GET /apiHR/UserDept?empcd=&deptcd=&page=&page_size=
    [HttpGet]
    public async Task<IActionResult> GetList(
        string? empcd     = null,
        string? deptcd    = null,
        int     page      = 1,
        int     page_size = 50)
    {
        try
        {
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            string fromSql = @"
                FROM HRMS.HR_USERS_DEPT D
                JOIN HRMS.ECM100 E ON E.EMPCD = D.EMPCD
                LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = D.DEPTCD AND B.LINECD = D.LINECD AND B.WORKCD = D.WORKCD";

            string whereSql = @"
                WHERE (:EMPCD_FLAG IS NULL OR UPPER(D.EMPCD) LIKE :EMPCD_VAL)
                  AND (:DEPT_FLAG  IS NULL OR D.DEPTCD = :DEPT_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("EMPCD_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(empcd)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("EMPCD_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(empcd)  ? null : "%" + empcd.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DEPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(deptcd) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DEPT_VAL",   OracleDbType.Varchar2) { Value = (object?)deptcd ?? DBNull.Value }
            };

            string sqlCount = $"SELECT COUNT(*) CNT {fromSql} {whereSql}";
            var countRows = await _oracleService.ExecuteQueryAsync(sqlCount,
                r => Convert.ToInt32(r["CNT"]),
                baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());
            int total = countRows.FirstOrDefault();

            if (total == 0)
                return Ok(new { success = true, total = 0, page, page_size, total_pages = 0, data = new List<object>() });

            string sqlData = $@"
                SELECT * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.EMPCD, T.DEPTCD, T.LINECD) RN
                    FROM (
                        SELECT D.EMPCD, D.DEPTCD, D.LINECD, D.WORKCD,
                               E.CNAME       EMP_NAME,
                               B.DEPTNM      DEPT_NAME,
                               B.TEAMNM      LINE_NAME,
                               B.WORKNM      WORK_NAME,
                               D.CREATEDATE, D.CREATEBY
                        {fromSql} {whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var rows = await _oracleService.ExecuteQueryAsync(sqlData, r => new
            {
                EMPCD       = r["EMPCD"]?.ToString(),
                EMP_NAME    = r["EMP_NAME"]?.ToString(),
                DEPTCD      = r["DEPTCD"]?.ToString(),
                DEPT_NAME   = r["DEPT_NAME"]?.ToString(),
                LINECD      = r["LINECD"]?.ToString(),
                LINE_NAME   = r["LINE_NAME"]?.ToString(),
                WORKCD      = r["WORKCD"]?.ToString(),
                WORK_NAME   = r["WORK_NAME"]?.ToString(),
                CREATEDATE  = r["CREATEDATE"] == DBNull.Value ? null : ((DateTime)r["CREATEDATE"]).ToString("yyyy-MM-dd"),
                CREATEBY    = r["CREATEBY"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                total,
                page,
                page_size,
                total_pages = (int)Math.Ceiling((double)total / page_size),
                data        = rows
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/UserDept/import
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] UserDeptImportRequest req)
    {
        try
        {
            if (req?.Rows == null || req.Rows.Count == 0)
                return Ok(new { success = false, message = "Không có dữ liệu" });

            int inserted = 0, skipped = 0;
            string createdBy = req.CreatedBy?.Trim() ?? "SYSTEM";

            string mergeSql = @"
                MERGE INTO HRMS.HR_USERS_DEPT T
                USING (SELECT :EMPCD EMPCD, :DEPTCD DEPTCD, :LINECD LINECD, :WORKCD WORKCD FROM DUAL) S
                ON (    T.EMPCD  = S.EMPCD
                    AND T.DEPTCD = S.DEPTCD
                    AND (T.LINECD = S.LINECD OR (T.LINECD IS NULL AND S.LINECD IS NULL))
                    AND (T.WORKCD = S.WORKCD OR (T.WORKCD IS NULL AND S.WORKCD IS NULL)))
                WHEN NOT MATCHED THEN
                    INSERT (EMPCD, DEPTCD, LINECD, WORKCD, CREATEDATE, CREATEBY)
                    VALUES (S.EMPCD, S.DEPTCD, S.LINECD, S.WORKCD, SYSDATE, :CREATED_BY)";

            foreach (var row in req.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.EMPCD) || string.IsNullOrWhiteSpace(row.DEPTCD))
                { skipped++; continue; }

                int n = await _oracleService.ExecuteNonQueryAsync(mergeSql,
                    new OracleParameter("EMPCD",       OracleDbType.Varchar2) { Value = row.EMPCD.Trim() },
                    new OracleParameter("DEPTCD",      OracleDbType.Varchar2) { Value = row.DEPTCD.Trim() },
                    new OracleParameter("LINECD",      OracleDbType.Varchar2) { Value = (object?)(row.LINECD?.Trim()) ?? DBNull.Value },
                    new OracleParameter("WORKCD",      OracleDbType.Varchar2) { Value = (object?)(row.WORKCD?.Trim()) ?? DBNull.Value },
                    new OracleParameter("CREATED_BY",  OracleDbType.Varchar2) { Value = createdBy });

                if (n > 0) inserted++; else skipped++;
            }

            return Ok(new { success = true, inserted, skipped,
                            message = $"Đã thêm {inserted} dòng, bỏ qua {skipped} dòng (trùng hoặc thiếu dữ liệu)" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // DELETE /apiHR/UserDept?empcd=&deptcd=&linecd=&workcd=
    [HttpDelete]
    public async Task<IActionResult> Delete(
        string  empcd,
        string? deptcd = null,
        string? linecd = null,
        string? workcd = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu EMPCD" });

            string sql;
            OracleParameter[] p;

            if (!string.IsNullOrEmpty(deptcd) && !string.IsNullOrEmpty(linecd) && !string.IsNullOrEmpty(workcd))
            {
                sql = @"DELETE FROM HRMS.HR_USERS_DEPT
                        WHERE EMPCD = :EMPCD AND DEPTCD = :DEPTCD
                          AND (LINECD = :LINECD OR (LINECD IS NULL AND :LINECD IS NULL))
                          AND (WORKCD = :WORKCD OR (WORKCD IS NULL AND :WORKCD IS NULL))";
                p = new[]
                {
                    new OracleParameter("EMPCD",  empcd),
                    new OracleParameter("DEPTCD", deptcd),
                    new OracleParameter("LINECD",  (object?)linecd ?? DBNull.Value),
                    new OracleParameter("WORKCD",  (object?)workcd ?? DBNull.Value)
                };
            }
            else
            {
                sql = "DELETE FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD";
                p   = new[] { new OracleParameter("EMPCD", empcd) };
            }

            int deleted = await _oracleService.ExecuteNonQueryAsync(sql, p);
            return Ok(new { success = true, deleted });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
