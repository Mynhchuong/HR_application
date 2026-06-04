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

            // ── Chấm công (bao gồm OT) ─────────────────────────────────────────
            string sqlAtt = @"
                SELECT TO_CHAR(A.DAT,'YYYY-MM-DD')                        ATT_DATE,
                       B.SHIFTCD                                           SHIFT_CODE,
                       TRIM(C.STIME) || '-' || TRIM(C.ETIME)              SHIFT_TIMES,
                       A.TIME_IN,
                       A.TIME_OUT,
                       NVL(A.T_FORMAL, 0)                                 T_FORMAL,
                       NVL(A.T_OT,    0)                                  T_OT,
                       NVL(A.T_ROT,   0)                                  T_ROT,
                       NVL(B.OVER_TIME, 0)                                OT_PLAN,
                       NVL(A.OT_BEFORE_TIME, 0)                           OT_BEFORE_TIME,
                       NVL(A.OT_AFTER_TIME,  0)                           OT_AFTER_TIME
                FROM HRMS.EBM200 A
                INNER JOIN HRMS.EBM300 B ON A.EMPCD = B.EMPCD AND A.DAT = B.DAT
                INNER JOIN HRMS.EBM100 C ON B.SHIFTCD = C.SHIFTCD
                WHERE A.EMPCD = :EMPCD
                  AND A.DAT BETWEEN :D_FROM AND :D_TO
                ORDER BY A.DAT";

            static string? fmtTime(object val)
            {
                if (val == DBNull.Value || val == null) return null;
                var dt = Convert.ToDateTime(val);
                return dt.ToString("HH:mm");
            }

            // EBM100 STIME/ETIME are stored as "0730" — insert colon
            static string fmtShiftTime(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
                raw = raw.Trim();
                return raw.Length == 4 ? raw[..2] + ":" + raw[2..] : raw;
            }

            var attList = await _oracleService.ExecuteQueryAsync(sqlAtt, r =>
            {
                var shiftTimes = r["SHIFT_TIMES"]?.ToString() ?? "";
                var parts      = shiftTimes.Split('-');
                var start      = parts.Length > 0 ? fmtShiftTime(parts[0]) : "";
                var end        = parts.Length > 1 ? fmtShiftTime(parts[1]) : "";

                return new
                {
                    date          = r["ATT_DATE"]?.ToString() ?? "",
                    shiftCode     = r["SHIFT_CODE"]?.ToString() ?? "",
                    shiftLabel    = (r["SHIFT_CODE"]?.ToString() ?? "") + " " + start + "-" + end,
                    timeIn        = fmtTime(r["TIME_IN"]),
                    timeOut       = fmtTime(r["TIME_OUT"]),
                    tFormal       = Convert.ToDecimal(r["T_FORMAL"]),
                    tOt           = Convert.ToDecimal(r["T_OT"]),
                    tRot          = Convert.ToDecimal(r["T_ROT"]),
                    otPlan        = Convert.ToDecimal(r["OT_PLAN"]),
                    otBeforeTime  = Convert.ToDecimal(r["OT_BEFORE_TIME"]),
                    otAfterTime   = Convert.ToDecimal(r["OT_AFTER_TIME"])
                };
            },
            new OracleParameter("EMPCD",  empcd),
            new OracleParameter("D_FROM", dFrom),
            new OracleParameter("D_TO",   dTo));

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

            // ── Nghỉ phép (chỉ APPROVED) ────────────────────────────────────────
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
                  AND R.STATUS = 'APPROVED'
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
                data    = new { attendance = attList, gatePass = gpList, leave = leaveList }
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
