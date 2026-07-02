using HR_api.Data;
using HR_api.Models.GatePass;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Extract từ GatePassController.GetShiftInfo — Home + GatePass cùng dùng.
// Auto-detect ca đêm: nếu SYSDATE < STIME và ca đêm (STIME > ETIME) → DAT hôm qua.
public class ShiftLookupService
{
    private readonly OracleService _oracleService;

    public ShiftLookupService(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // Auto-detect ca hôm nay (dùng cho Home).
    public async Task<GpShiftInfoModel?> GetTodayShiftAsync(string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return null;

        const string sql = @"
            SELECT TO_CHAR(A.DAT, 'YYYY-MM-DD')     WORK_DATE,
                   TO_CHAR(A.DAT + 1, 'YYYY-MM-DD') WORK_DATE_TOMORROW,
                   A.SHIFTCD, B.STIME, B.ETIME
            FROM HRMS.EBM300 A, HRMS.EBM100 B
            WHERE A.SHIFTCD = B.SHIFTCD
              AND A.EMPCD = :EMPCD
              AND A.DAT = CASE
                            WHEN TO_NUMBER(TO_CHAR(SYSDATE,'HH24MI')) < TO_NUMBER(B.STIME)
                                 AND TO_NUMBER(B.STIME) > TO_NUMBER(B.ETIME)
                            THEN TRUNC(SYSDATE) - 1
                            ELSE TRUNC(SYSDATE)
                          END
              AND ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new GpShiftInfoModel
        {
            SHIFTCD            = r["SHIFTCD"]?.ToString(),
            STIME              = r["STIME"]?.ToString(),
            ETIME              = r["ETIME"]?.ToString(),
            WORK_DATE          = r["WORK_DATE"]?.ToString(),
            WORK_DATE_TOMORROW = r["WORK_DATE_TOMORROW"]?.ToString()
        }, new OracleParameter("EMPCD", empcd));

        return rows.FirstOrDefault();
    }

    // Query ca cho 1 ngày cụ thể (dùng cho GatePass khi user chọn today/tomorrow).
    // Union EBM300 + EBM300_WAIT vì ngày mai có thể chưa finalize.
    public async Task<GpShiftInfoModel?> GetShiftForDateAsync(string empcd, DateTime regDate)
    {
        if (string.IsNullOrEmpty(empcd)) return null;

        const string sql = @"
            SELECT T.SHIFTCD, S.STIME, S.ETIME FROM (
                SELECT SHIFTCD FROM HRMS.EBM300      WHERE EMPCD = :EMPCD  AND DAT = :REG_DATE  AND ROWNUM = 1
                UNION ALL
                SELECT SHIFTCD FROM HRMS.EBM300_WAIT WHERE EMPCD = :EMPCD1 AND DAT = :REG_DATE1 AND ROWNUM = 1
            ) T
            JOIN HRMS.EBM100 S ON S.SHIFTCD = T.SHIFTCD
            WHERE ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new GpShiftInfoModel
        {
            SHIFTCD            = r["SHIFTCD"]?.ToString(),
            STIME              = r["STIME"]?.ToString(),
            ETIME              = r["ETIME"]?.ToString(),
            WORK_DATE          = regDate.ToString("yyyy-MM-dd"),
            WORK_DATE_TOMORROW = regDate.AddDays(1).ToString("yyyy-MM-dd")
        },
        new OracleParameter("EMPCD",     empcd),
        new OracleParameter("REG_DATE",  regDate),
        new OracleParameter("EMPCD1",    empcd),
        new OracleParameter("REG_DATE1", regDate));

        return rows.FirstOrDefault();
    }
}
