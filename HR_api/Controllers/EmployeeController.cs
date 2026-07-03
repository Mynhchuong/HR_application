using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class EmployeeController : ControllerBase
{
    private readonly OracleService _oracleService;
    public EmployeeController(OracleService oracleService) { _oracleService = oracleService; }

    // GET /apiHR/Employee/my-team?approver_empcd=&search=&deptcd=&linecd=&workcd=
    [HttpGet("my-team")]
    public async Task<IActionResult> GetMyTeam(
        string  approver_empcd,
        string? search  = null,
        string? deptcd  = null,
        string? linecd  = null,
        string? workcd  = null)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", approver_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "MT");

            var searchFilter  = string.IsNullOrWhiteSpace(search) ? "" :
                "AND (UPPER(EC.EMPCD) LIKE :SEARCH OR UPPER(EC.CNAME) LIKE :SEARCH)";
            var deptFilter    = string.IsNullOrWhiteSpace(deptcd) ? "" : "AND EC.DEPTCD = :DEPTCD";
            var lineFilter    = string.IsNullOrWhiteSpace(linecd) ? "" : "AND EC.LINECD = :LINECD";
            var workFilter    = string.IsNullOrWhiteSpace(workcd) ? "" : "AND EC.WORKCD = :WORKCD";

            string sql = $@"
                SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                       EC.DEPTCD, EC.LINECD, EC.WORKCD,
                       B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                       U.LASTED_LOGIN LAST_LOGIN,
                       NVL((SELECT SUM(NVL(T_ROT,0) + NVL(T_OT,0)) FROM HRMS.EBM200
                            WHERE EMPCD = EC.EMPCD
                              AND DAT BETWEEN TO_DATE(TO_CHAR(SYSDATE,'YYYY')||'0101','YYYYMMDD') AND SYSDATE),0) SUM_YEAR,
                       NVL((SELECT SUM(NVL(T_ROT,0) + NVL(T_OT,0)) FROM HRMS.EBM200
                            WHERE EMPCD = EC.EMPCD
                              AND TO_CHAR(DAT,'YYYYMM') = TO_CHAR(SYSDATE,'YYYYMM')),0) SUM_MONTH
                FROM HRMS.ECM100 EC
                LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = EC.EMPCD
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  {scopeFilter.SqlClause}
                  {searchFilter}
                  {deptFilter}
                  {lineFilter}
                  {workFilter}
                ORDER BY EC.DEPTCD, EC.LINECD, EC.WORKCD, EC.EMPCD";

            var p = new List<OracleParameter>(scopeFilter.Params);
            if (!string.IsNullOrWhiteSpace(search))
                p.Add(new OracleParameter("SEARCH", OracleDbType.Varchar2) { Value = "%" + search.ToUpper() + "%" });
            if (!string.IsNullOrWhiteSpace(deptcd))
                p.Add(new OracleParameter("DEPTCD", OracleDbType.Varchar2) { Value = deptcd });
            if (!string.IsNullOrWhiteSpace(linecd))
                p.Add(new OracleParameter("LINECD", OracleDbType.Varchar2) { Value = linecd });
            if (!string.IsNullOrWhiteSpace(workcd))
                p.Add(new OracleParameter("WORKCD", OracleDbType.Varchar2) { Value = workcd });

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                EMPCD     = r["EMPCD"]?.ToString()     ?? "",
                EMP_NAME  = r["EMP_NAME"]?.ToString()  ?? "",
                DEPTCD    = r["DEPTCD"]?.ToString()    ?? "",
                LINECD    = r["LINECD"]?.ToString()    ?? "",
                WORKCD    = r["WORKCD"]?.ToString()    ?? "",
                DEPT_NAME = r["DEPT_NAME"]?.ToString() ?? "",
                LINE_NAME = r["LINE_NAME"]?.ToString() ?? "",
                WORK_NAME = r["WORK_NAME"]?.ToString() ?? "",
                SUM_YEAR  = r["SUM_YEAR"] == DBNull.Value ? 0 : Convert.ToDecimal(r["SUM_YEAR"]),
                SUM_MONTH = r["SUM_MONTH"] == DBNull.Value ? 0 : Convert.ToDecimal(r["SUM_MONTH"]),
                LAST_LOGIN = r["LAST_LOGIN"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LAST_LOGIN"])
            }, p.ToArray());

            return Ok(new { success = true, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
