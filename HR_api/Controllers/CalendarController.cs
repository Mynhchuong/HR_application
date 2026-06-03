using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class CalendarController : ControllerBase
{
    private readonly OracleService _oracleService;

    public CalendarController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // GET /apiHR/Calendar/my-monthly?empcd=XXX&year=2026&month=6
    [HttpGet("my-monthly")]
    public async Task<IActionResult> GetMyMonthly(string empcd, int? year = null, int? month = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            int y = year  ?? DateTime.Today.Year;
            int m = month ?? DateTime.Today.Month;

            var dFrom = new DateTime(y, m, 1);
            var dTo   = dFrom.AddMonths(1).AddDays(-1);

            // ── OT ─────────────────────────────────────────────────────────────
            string sqlOT = @"
                SELECT TRUNC(E.DAT) OT_DATE,
                       MAX(E.OVER_TIME) OT_HOURS,
                       MAX(NVL(R.CONFIRM_STATUS, 'PENDING')) CONFIRM_STATUS
                FROM (
                    SELECT EMPCD, DAT, OVER_TIME
                    FROM HRMS.EBM300
                    WHERE EMPCD = :E1 AND TRUNC(DAT) BETWEEN :F1 AND :T1
                      AND (OT_BEFORE = 'Y' OR OT_AFTER = 'Y' OR OVER_TIME IS NOT NULL)
                    UNION ALL
                    SELECT EMPCD, DAT, OVER_TIME
                    FROM HRMS.EBM300_WAIT
                    WHERE EMPCD = :E2 AND TRUNC(DAT) BETWEEN :F2 AND :T2
                      AND (OT_BEFORE = 'Y' OR OT_AFTER = 'Y' OR OVER_TIME IS NOT NULL)
                ) E
                LEFT JOIN HRMS.HR_OT_REQUEST R
                    ON R.EMPCD = E.EMPCD AND TRUNC(R.WORK_DATE) = TRUNC(E.DAT)
                GROUP BY TRUNC(E.DAT)
                ORDER BY TRUNC(E.DAT)";

            var otList = await _oracleService.ExecuteQueryAsync(sqlOT, r => new
            {
                date   = ((DateTime)r["OT_DATE"]).ToString("yyyy-MM-dd"),
                hours  = r["OT_HOURS"] == DBNull.Value ? 0m : Convert.ToDecimal(r["OT_HOURS"]),
                status = r["CONFIRM_STATUS"]?.ToString() ?? "PENDING"
            },
            new OracleParameter("E1", empcd), new OracleParameter("F1", dFrom), new OracleParameter("T1", dTo),
            new OracleParameter("E2", empcd), new OracleParameter("F2", dFrom), new OracleParameter("T2", dTo));

            // ── Gate Pass ───────────────────────────────────────────────────────
            string sqlGP = @"
                SELECT TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) GP_DATE,
                       GP.GP_TYPE, R.STATUS
                FROM HRMS.HR_GATEPASS_REQUEST GP
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = GP.REQUEST_ID
                WHERE GP.EMPCD = :EMPCD
                  AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN :D_FROM AND :D_TO
                ORDER BY GP_DATE";

            var gpList = await _oracleService.ExecuteQueryAsync(sqlGP, r => new
            {
                date   = ((DateTime)r["GP_DATE"]).ToString("yyyy-MM-dd"),
                type   = r["GP_TYPE"]?.ToString(),
                status = r["STATUS"]?.ToString()
            },
            new OracleParameter("EMPCD",  empcd),
            new OracleParameter("D_FROM", dFrom),
            new OracleParameter("D_TO",   dTo));

            // ── Leave ───────────────────────────────────────────────────────────
            string sqlLeave = @"
                SELECT TO_CHAR(L.FROM_DATE,'YYYY-MM-DD') FROM_DATE,
                       TO_CHAR(L.TO_DATE,'YYYY-MM-DD')   TO_DATE,
                       L.LEAVE_TYPE, R.STATUS,
                       NVL(L.TOTAL_DAYS,0) TOTAL_DAYS
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.EMPCD = :EMPCD
                  AND L.FROM_DATE <= :D_TO
                  AND L.TO_DATE   >= :D_FROM
                ORDER BY L.FROM_DATE";

            var leaveList = await _oracleService.ExecuteQueryAsync(sqlLeave, r => new
            {
                from      = r["FROM_DATE"]?.ToString(),
                to        = r["TO_DATE"]?.ToString(),
                leaveType = r["LEAVE_TYPE"]?.ToString(),
                status    = r["STATUS"]?.ToString(),
                days      = Convert.ToDecimal(r["TOTAL_DAYS"])
            },
            new OracleParameter("EMPCD",  empcd),
            new OracleParameter("D_FROM", dFrom),
            new OracleParameter("D_TO",   dTo));

            return Ok(new
            {
                success = true,
                data    = new { ot = otList, gatePass = gpList, leave = leaveList }
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
