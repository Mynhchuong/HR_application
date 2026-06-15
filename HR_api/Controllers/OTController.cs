using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.OT;
using HR_api.Services;
using System.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class OTController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly NotificationService _notiSvc;

    public OTController(OracleService oracleService, NotificationService notiSvc)
    {
        _oracleService = oracleService;
        _notiSvc = notiSvc;
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetOTToday(string empcd, string? work_date = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            DateTime workDate = (!string.IsNullOrEmpty(work_date) && DateTime.TryParse(work_date, out var _wd)) ? _wd : DateTime.Today;

            string sql = @"
                SELECT E.EMPCD, E.DAT WORK_DATE, E.OVER_TIME OT_HOURS, E.OT_BEFORE, E.OT_BEFORE_TIME, E.OT_AFTER, E.OT_AFTER_TIME, E.OT_REST,
                       CASE WHEN E.OT_BEFORE = 'Y' OR E.OT_AFTER = 'Y' THEN 'Y' ELSE 'N' END HAS_OT,
                       CASE WHEN E.OT_BEFORE = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.STIME,'YYYYMMDDHH24MI') - E.OT_BEFORE_TIME / 24
                            WHEN E.OT_AFTER = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.ETIME,'YYYYMMDDHH24MI')
                       END START_OT,
                       CASE WHEN E.OT_AFTER = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.ETIME,'YYYYMMDDHH24MI') + E.OT_AFTER_TIME / 24
                            WHEN E.OT_BEFORE = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.STIME,'YYYYMMDDHH24MI')
                       END END_OT,
                       NVL(R.CONFIRM_STATUS, 'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE, R.OT_HOURS CONFIRMED_OT_HOURS,
                       NVL((SELECT SUM(NVL(T_ROT,0)+NVL(T_OT,0)) FROM HRMS.EBM200 WHERE EMPCD = :EMPCD AND TO_CHAR(DAT,'YYYYIW') = TO_CHAR(SYSDATE,'YYYYIW') AND DAT <= SYSDATE), 0) SUM_WEEK,
                       NVL((SELECT SUM(NVL(T_ROT,0)+NVL(T_OT,0)) FROM HRMS.EBM200 WHERE EMPCD = :EMPCD AND DAT BETWEEN TRUNC(SYSDATE,'MM') AND SYSDATE), 0) SUM_MONTH,
                       NVL((SELECT SUM(NVL(T_ROT,0)+NVL(T_OT,0)) FROM HRMS.EBM200 WHERE EMPCD = :EMPCD AND DAT BETWEEN TO_DATE(TO_CHAR(SYSDATE,'YYYY')||'0101','YYYYMMDD') AND SYSDATE), 0) SUM_YEAR
                FROM (SELECT EMPCD,DAT,SHIFTCD,MAX(OVER_TIME)OVER_TIME,MAX(OT_BEFORE)OT_BEFORE,
                MAX(OT_BEFORE_TIME)OT_BEFORE_TIME, MAX(OT_AFTER)OT_AFTER, MAX(OT_AFTER_TIME)OT_AFTER_TIME, MAX(OT_REST)OT_REST
                      FROM (
                      SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME, OT_REST
                      FROM HRMS.EBM300 WHERE DAT = :WORK_DATE AND EMPCD = :EMPCD1
                      UNION ALL
                      SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME, OT_REST
                      FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2 AND EMPCD = :EMPCD2)
                      WHERE OVER_TIME IS NOT NULL
                      GROUP BY EMPCD,DAT,SHIFTCD) E
                JOIN HRMS.EBM100 S ON S.SHIFTCD = E.SHIFTCD
                LEFT JOIN (SELECT EMPCD, CONFIRM_STATUS, CONFIRM_DATE, OT_HOURS FROM HRMS.HR_OT_REQUEST WHERE WORK_DATE = :WORK_DATE3) R ON R.EMPCD = E.EMPCD
                WHERE ROWNUM = 1
                ";

            var result = await _oracleService.ExecuteQueryAsync(sql, r =>
            {
                var erpHours       = r["OT_HOURS"]           == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["OT_HOURS"]);
                var confirmedHours = r["CONFIRMED_OT_HOURS"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["CONFIRMED_OT_HOURS"]);
                bool hoursUpdated  = confirmedHours.HasValue && erpHours.HasValue && confirmedHours != erpHours;
                string confirmStatus = hoursUpdated ? "PENDING" : (r["CONFIRM_STATUS"]?.ToString() ?? "PENDING");

                return new OTTodayModel
                {
                    EMPCD          = r["EMPCD"]?.ToString() ?? string.Empty,
                    WORK_DATE      = r["WORK_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["WORK_DATE"]),
                    OT_HOURS       = erpHours,
                    OT_BEFORE      = r["OT_BEFORE"]?.ToString(),
                    OT_BEFORE_TIME = r["OT_BEFORE_TIME"]?.ToString(),
                    OT_AFTER       = r["OT_AFTER"]?.ToString(),
                    OT_AFTER_TIME  = r["OT_AFTER_TIME"]?.ToString(),
                    OT_REST        = r["OT_REST"]?.ToString(),
                    HAS_OT         = r["HAS_OT"]?.ToString(),
                    START_OT       = r["START_OT"]    == DBNull.Value ? null : Convert.ToDateTime(r["START_OT"]),
                    END_OT         = r["END_OT"]      == DBNull.Value ? null : Convert.ToDateTime(r["END_OT"]),
                    CONFIRM_STATUS = confirmStatus,
                    CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                    SUM_WEEK       = Convert.ToDecimal(r["SUM_WEEK"]),
                    SUM_MONTH      = Convert.ToDecimal(r["SUM_MONTH"]),
                    SUM_YEAR       = Convert.ToDecimal(r["SUM_YEAR"]),
                    IS_EDITABLE    = workDate.Date >= DateTime.Today.Date,
                    HOURS_UPDATED  = hoursUpdated,
                    PREV_OT_HOURS  = hoursUpdated ? confirmedHours : null
                };
            },
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("WORK_DATE", workDate),
            new OracleParameter("EMPCD1", empcd),
            new OracleParameter("WORK_DATE2", workDate),
            new OracleParameter("EMPCD2", empcd),
            new OracleParameter("WORK_DATE3", workDate));

            if (result.Count == 0)
                return Ok(new { success = true, data = (object?)null, message = "Không có kế hoạch tăng ca trong ngày này" });

            return Ok(new { success = true, data = result[0] });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmOT([FromBody] OTConfirmRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            DateTime workDate = (!string.IsNullOrEmpty(model.WORK_DATE) && DateTime.TryParse(model.WORK_DATE, out var _wd2)) ? _wd2 : DateTime.Today;

            if (model.CONFIRM_STATUS != "CONFIRMED" && model.CONFIRM_STATUS != "REJECTED")
                return Ok(new { success = false, message = "Trạng thái không hợp lệ" });

            string sqlCheckERP = @"
                SELECT COUNT(*) CNT FROM (SELECT EMPCD FROM HRMS.EBM300      WHERE DAT = :WORK_DATE  AND EMPCD = :EMPCD  AND OVER_TIME IS NOT NULL AND OVER_TIME > 0
                                          UNION ALL
                                          SELECT EMPCD FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2 AND EMPCD = :EMPCD1 AND OVER_TIME IS NOT NULL AND OVER_TIME > 0)";

            var hasOT = await _oracleService.ExecuteQueryAsync(sqlCheckERP, r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("WORK_DATE", workDate),
                new OracleParameter("WORK_DATE2", workDate),
                new OracleParameter("EMPCD", model.EMPCD),
                new OracleParameter("EMPCD1", model.EMPCD));

            if (hasOT.Count == 0 || hasOT[0] == 0)
                return Ok(new { success = false, message = "Không có kế hoạch tăng ca trong ngày này" });

            // Bước 1: Kiểm tra xem HR_OT_REQUEST đã có dòng cho EMPCD + WORK_DATE chưa
            string sqlGetExisting = "SELECT REQUEST_ID, CONFIRM_STATUS, OT_HOURS FROM HRMS.HR_OT_REQUEST WHERE EMPCD = :EMPCD AND WORK_DATE = :WORK_DATE AND ROWNUM = 1";
            var existingRows = await _oracleService.ExecuteQueryAsync(sqlGetExisting, r => new {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                OT_HOURS       = r["OT_HOURS"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["OT_HOURS"])
            },
                new OracleParameter("EMPCD", model.EMPCD),
                new OracleParameter("WORK_DATE", workDate));

            if (existingRows.Count > 0 && existingRows[0] != null)
            {
                // Đã có trong HR_OT_REQUEST → UPDATE cả hai bảng
                var existing = existingRows[0];
                bool hoursChanged = existing.OT_HOURS.HasValue && model.OT_HOURS.HasValue && existing.OT_HOURS != model.OT_HOURS;

                if (existing.CONFIRM_STATUS != model.CONFIRM_STATUS || hoursChanged)
                {
                    string sqlUpdateOT = "UPDATE HRMS.HR_OT_REQUEST SET CONFIRM_STATUS = :CONFIRM_STATUS, OT_HOURS = :OT_HOURS, CONFIRM_DATE = SYSDATE WHERE REQUEST_ID = :REQUEST_ID";
                    await _oracleService.ExecuteNonQueryAsync(sqlUpdateOT,
                        new OracleParameter("CONFIRM_STATUS", model.CONFIRM_STATUS),
                        new OracleParameter("OT_HOURS", (object?)model.OT_HOURS ?? DBNull.Value),
                        new OracleParameter("REQUEST_ID", existing.REQUEST_ID));

                    string sqlUpdateReq = "UPDATE HRMS.HR_REQUEST SET STATUS = :STATUS, UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE WHERE REQUEST_ID = :REQUEST_ID";
                    await _oracleService.ExecuteNonQueryAsync(sqlUpdateReq,
                        new OracleParameter("STATUS", model.CONFIRM_STATUS),
                        new OracleParameter("EMPCD", model.EMPCD),
                        new OracleParameter("REQUEST_ID", existing.REQUEST_ID));

                    string msgUpdate = model.CONFIRM_STATUS == "CONFIRMED" ? "Xác nhận tăng ca thành công" : "Từ chối tăng ca thành công";
                    return Ok(new { success = true, message = msgUpdate, request_id = existing.REQUEST_ID });
                }
                else
                {
                    string msgSame = model.CONFIRM_STATUS == "CONFIRMED" ? "Bạn đã xác nhận tăng ca ngày này rồi" : "Bạn đã từ chối tăng ca ngày này rồi";
                    return Ok(new { success = true, message = msgSame, request_id = existing.REQUEST_ID });
                }
            }

            // Bước 2: HR_OT_REQUEST chưa có → INSERT qua procedure (transaction an toàn)
            string requestId = DateTime.Now.ToString("yyyyMMddHHmmss") + model.EMPCD;

            var pResult  = new OracleParameter("P_RESULT",  OracleDbType.Int32)          { Direction = System.Data.ParameterDirection.Output };
            var pMessage = new OracleParameter("P_MESSAGE", OracleDbType.Varchar2, 500)  { Direction = System.Data.ParameterDirection.Output };

            await _oracleService.ExecuteProcedureAsync("HRMS.SP_OT_CONFIRM_INSERT",
                new OracleParameter("P_REQUEST_ID",     requestId),
                new OracleParameter("P_EMPCD",          model.EMPCD),
                new OracleParameter("P_WORK_DATE",      workDate),
                new OracleParameter("P_OT_HOURS",       (object?)model.OT_HOURS ?? DBNull.Value),
                new OracleParameter("P_CONFIRM_STATUS", model.CONFIRM_STATUS),
                pResult,
                pMessage);

            if (int.Parse(pResult.Value?.ToString() ?? "0") != 0)
                return Ok(new { success = false, message = $"Lỗi hệ thống, vui lòng thử lại. ({pMessage.Value})" });

            string msg = model.CONFIRM_STATUS == "CONFIRMED" ? "Xác nhận tăng ca thành công" : "Từ chối tăng ca thành công";
            return Ok(new { success = true, message = msg, request_id = requestId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = $"Lỗi hệ thống, vui lòng thử lại. ({ex.Message})" });
        }
    }

    [HttpGet("clerk")]
    public async Task<IActionResult> GetOTClerk(string clerk_empcd, string? work_date = null,
        string? status = null, string? search = null,
        string? dept_id = null, string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(clerk_empcd)) return Ok(new { success = false, message = "Thiếu mã clerk" });

            DateTime workDate = (!string.IsNullOrEmpty(work_date) && DateTime.TryParse(work_date, out var _wd)) ? _wd : DateTime.Today;
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            var hasClerkScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :CE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CE", clerk_empcd));

            if (hasClerkScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Không tìm thấy thông tin clerk" });

            // Filter exact (DEPTCD, LINECD, WORKCD) tuple — tránh false-positive khi code bị reuse
            var clerkFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(clerk_empcd, prefix: "CK");

            string withSql = @"
                WITH OT_BASE AS (
                    SELECT /*+ MATERIALIZE */ EMPCD, DAT, SHIFTCD,
                           MAX(OVER_TIME)      OT_HOURS,
                           MAX(OT_BEFORE)      OT_BEFORE,
                           MAX(OT_BEFORE_TIME) OT_BEFORE_TIME,
                           MAX(OT_AFTER)       OT_AFTER,
                           MAX(OT_AFTER_TIME)  OT_AFTER_TIME
                    FROM (
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                        FROM HRMS.EBM300      WHERE DAT = :WORK_DATE
                        UNION ALL
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                        FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2
                    )
                    GROUP BY EMPCD, DAT, SHIFTCD
                )";

            string fromSql = @"
                FROM OT_BASE OT
                JOIN HRMS.ECM100 EC ON EC.EMPCD  = OT.EMPCD
                JOIN HRMS.EBM100 S  ON S.SHIFTCD = OT.SHIFTCD
                LEFT JOIN HRMS.EAM410        B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_OT_REQUEST R ON R.EMPCD  = OT.EMPCD  AND R.WORK_DATE = :WORK_DATE3
                                               AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)
                LEFT JOIN HRMS.HR_USERS      UR ON UR.EMPCD = OT.EMPCD
                LEFT JOIN HRMS.HR_ROLES      RR ON RR.ID    = UR.ROLE_ID";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  " + clerkFilter.SqlClause + @"
                  AND (:ST_FLAG IS NULL OR NVL(R.CONFIRM_STATUS,'PENDING') = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(EC.EMPCD) LIKE :SRCH_VAL1)
                  AND (:DPT_FLAG IS NULL OR EC.DEPTCD = :DPT_VAL)
                  AND (:LN_FLAG IS NULL OR EC.LINECD = :LN_VAL)
                  AND (:WK_FLAG IS NULL OR EC.WORKCD = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("WORK_DATE",  OracleDbType.Date) { Value = workDate },
                new OracleParameter("WORK_DATE2", OracleDbType.Date) { Value = workDate },
                new OracleParameter("WORK_DATE3", OracleDbType.Date) { Value = workDate },
                new OracleParameter("ST_FLAG",    OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",     OracleDbType.Varchar2) { Value = (object?)status ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL1",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",    OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",    OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",     OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",    OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",     OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value }
            };
            baseParams.AddRange(clerkFilter.Params);

            // 1. Summary COUNT
            string sqlSummary = withSql + @"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN NVL(R.CONFIRM_STATUS,'PENDING') = 'PENDING'   THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) CONFIRMED,
                       SUM(CASE WHEN R.CONFIRM_STATUS = 'REJECTED'  THEN 1 ELSE 0 END) REJECTED
                " + fromSql + whereSql;

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new OTClerkSummary
            {
                TOTAL     = r["TOTAL"]     == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING   = r["PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                CONFIRMED = r["CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["CONFIRMED"]),
                REJECTED  = r["REJECTED"]  == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new OTClerkSummary();
            summary.IS_DONE = summary.PENDING == 0;

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<OTClerkModel>() });

            // 2. Paged data
            string sqlData = withSql + @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.CONFIRM_STATUS,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.LINE_ID, T.EMPCD) RN
                    FROM (
                        SELECT OT.EMPCD, EC.CNAME EMP_NAME, EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME, EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               OT.OT_HOURS, OT.OT_BEFORE, OT.OT_BEFORE_TIME, OT.OT_AFTER, OT.OT_AFTER_TIME,
                               S.STIME, S.ETIME, NVL(R.CONFIRM_STATUS,'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE,
                               RR.ROLE_NAME REQUESTER_ROLE
                        " + fromSql + whereSql + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r =>
            {
                var model = new OTClerkModel
                {
                    EMPCD          = r["EMPCD"]?.ToString() ?? string.Empty,
                    EMP_NAME       = r["EMP_NAME"]?.ToString(),
                    DEPT_ID        = r["DEPT_ID"]?.ToString(),
                    DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                    LINE_ID        = r["LINE_ID"]?.ToString(),
                    LINE_NAME      = r["LINE_NAME"]?.ToString(),
                    WORK_ID        = r["WORK_ID"]?.ToString(),
                    WORK_NAME      = r["WORK_NAME"]?.ToString(),
                    OT_HOURS       = r["OT_HOURS"]       == DBNull.Value ? null : Convert.ToDecimal(r["OT_HOURS"]),
                    OT_BEFORE      = r["OT_BEFORE"]?.ToString(),
                    OT_BEFORE_TIME = r["OT_BEFORE_TIME"]?.ToString(),
                    OT_AFTER       = r["OT_AFTER"]?.ToString(),
                    OT_AFTER_TIME  = r["OT_AFTER_TIME"]?.ToString(),
                    CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                    CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"])
                };
                try
                {
                    DateTime baseDate = workDate;
                    string sTime = (r["STIME"]?.ToString() ?? "0000").PadLeft(4, '0');
                    string eTime = (r["ETIME"]?.ToString() ?? "0000").PadLeft(4, '0');
                    if (model.OT_AFTER == "Y")
                    {
                        model.START_OT = DateTime.ParseExact(baseDate.ToString("yyyyMMdd") + eTime, "yyyyMMddHHmm", null);
                        model.END_OT   = model.START_OT.Value.AddHours((double)(model.OT_HOURS ?? 0));
                    }
                    else if (model.OT_BEFORE == "Y")
                    {
                        model.END_OT   = DateTime.ParseExact(baseDate.ToString("yyyyMMdd") + sTime, "yyyyMMddHHmm", null);
                        model.START_OT = model.END_OT.Value.AddHours(-(double)(model.OT_HOURS ?? 0));
                    }
                }
                catch { }
                return model;
            }, dataParams.ToArray());

            return Ok(new { success = true, summary, total = summary.TOTAL, page, page_size,
                            total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                            data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("hr/summary")]
    public async Task<IActionResult> GetOTHRSummary(string? work_date = null, string? dept_id = null)
    {
        try
        {
            DateTime workDate = (!string.IsNullOrEmpty(work_date) && DateTime.TryParse(work_date, out var _wd)) ? _wd : DateTime.Today;

            string sql = @"
                WITH OT AS (
                    SELECT EMPCD, MAX(OT_BEFORE) OT_BEFORE, MAX(OT_AFTER) OT_AFTER
                    FROM (
                        SELECT EMPCD, OT_BEFORE, OT_AFTER FROM HRMS.EBM300      WHERE DAT = :WORK_DATE
                        UNION ALL
                        SELECT EMPCD, OT_BEFORE, OT_AFTER FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2
                    )
                    GROUP BY EMPCD
                )
                SELECT
                    EC.DEPTCD                                                                               DEPT_ID,
                    MAX(B.DEPTNM)                                                                           DEPT_NAME,
                    COUNT(*)                                                                                TOTAL,
                    SUM(CASE WHEN NVL(R.CONFIRM_STATUS,'PENDING') = 'CONFIRMED' THEN 1 ELSE 0 END)         CONFIRMED,
                    SUM(CASE WHEN NVL(R.CONFIRM_STATUS,'PENDING') = 'REJECTED'  THEN 1 ELSE 0 END)         REJECTED,
                    SUM(CASE WHEN NVL(R.CONFIRM_STATUS,'PENDING') = 'PENDING'   THEN 1 ELSE 0 END)         PENDING,
                    CASE WHEN SUM(CASE WHEN NVL(R.CONFIRM_STATUS,'PENDING') = 'PENDING' THEN 1 ELSE 0 END) = 0
                         THEN 'DONE' ELSE 'IN_PROGRESS' END                                                STATUS
                FROM OT
                JOIN      HRMS.ECM100        EC ON EC.EMPCD = OT.EMPCD
                LEFT JOIN HRMS.EAM410         B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_OT_REQUEST  R ON R.EMPCD  = OT.EMPCD  AND R.WORK_DATE = :WORK_DATE3
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND (:DEPT_ID IS NULL OR EC.DEPTCD = :DEPT_ID2)
                GROUP BY EC.DEPTCD
                ORDER BY STATUS, EC.DEPTCD";

            var result = await _oracleService.ExecuteQueryAsync(sql, r => new OTHRSummaryModel
            {
                DEPT_ID   = r["DEPT_ID"]?.ToString(),
                DEPT_NAME = r["DEPT_NAME"]?.ToString(),
                TOTAL     = Convert.ToInt32(r["TOTAL"]),
                CONFIRMED = Convert.ToInt32(r["CONFIRMED"]),
                REJECTED  = Convert.ToInt32(r["REJECTED"]),
                PENDING   = Convert.ToInt32(r["PENDING"]),
                STATUS    = r["STATUS"]?.ToString()
            },
            new OracleParameter("WORK_DATE",  workDate),
            new OracleParameter("WORK_DATE2", workDate),
            new OracleParameter("WORK_DATE3", workDate),
            new OracleParameter("DEPT_ID",    (object?)dept_id ?? DBNull.Value),
            new OracleParameter("DEPT_ID2",   (object?)dept_id ?? DBNull.Value));

            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("hr/detail")]
    public async Task<IActionResult> GetOTHRDetail(
        string? work_date  = null,
        string? dept_id    = null,
        string? search     = null,
        string? status     = null,
        string? dept_name  = null,
        string? line_name  = null,
        string? line_id    = null,
        string? work_id    = null,
        int     page       = 1,
        int     page_size  = 100)
    {
        try
        {
            DateTime workDate;
            if (!DateTime.TryParseExact(work_date, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out workDate))
                workDate = DateTime.Today;

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;
            string searchPattern = string.IsNullOrEmpty(search) ? "%" : "%" + search.ToUpper() + "%";

            // Optimized WITH clause with MATERIALIZE hint for Oracle 10
            string withSql = @"
                WITH OT_BASE AS (
                    SELECT /*+ MATERIALIZE */ EMPCD, DAT, SHIFTCD,
                           MAX(OVER_TIME)      OT_HOURS,
                           MAX(OT_BEFORE)      OT_BEFORE,
                           MAX(OT_BEFORE_TIME) OT_BEFORE_TIME,
                           MAX(OT_AFTER)       OT_AFTER,
                           MAX(OT_AFTER_TIME)  OT_AFTER_TIME
                    FROM (
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                        FROM HRMS.EBM300      WHERE DAT = :W_DATE1
                        UNION ALL
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                        FROM HRMS.EBM300_WAIT WHERE DAT = :W_DATE2
                    )
                    GROUP BY EMPCD, DAT, SHIFTCD
                )";

            string fromSql = @"
                FROM OT_BASE OT
                JOIN      HRMS.ECM100        EC ON EC.EMPCD  = OT.EMPCD
                JOIN      HRMS.EBM100         S ON S.SHIFTCD = OT.SHIFTCD
                LEFT JOIN HRMS.EAM410         B ON B.DEPTCD  = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_OT_REQUEST  R ON R.EMPCD   = OT.EMPCD  AND R.WORK_DATE = :W_DATE3 AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)
                LEFT JOIN HRMS.HR_USERS       UR ON UR.EMPCD  = OT.EMPCD
                LEFT JOIN HRMS.HR_ROLES       RR ON RR.ID     = UR.ROLE_ID";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND (:S_FLAG  IS NULL OR UPPER(OT.EMPCD) LIKE :S_VAL1)
                  AND (:ST_FLAG IS NULL OR NVL(R.CONFIRM_STATUS,'PENDING') = :ST_VAL)
                  AND (:DF_FLAG IS NULL OR UPPER(B.DEPTNM) LIKE '%' || UPPER(:DF_VAL) || '%')
                  AND (:LF_FLAG IS NULL OR UPPER(B.TEAMNM) LIKE '%' || UPPER(:LF_VAL) || '%')
                  AND (:DID_FLAG IS NULL OR EC.DEPTCD = :DID_VAL)
                  AND (:LID_FLAG IS NULL OR EC.LINECD = :LID_VAL)
                  AND (:WID_FLAG IS NULL OR EC.WORKCD = :WID_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("W_DATE1",  OracleDbType.Date) { Value = workDate },
                new OracleParameter("W_DATE2",  OracleDbType.Date) { Value = workDate },
                new OracleParameter("W_DATE3",  OracleDbType.Date) { Value = workDate },
                new OracleParameter("S_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("S_VAL1",   OracleDbType.Varchar2) { Value = searchPattern },
                new OracleParameter("ST_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",   OracleDbType.Varchar2) { Value = (object?)status ?? DBNull.Value },
                new OracleParameter("DF_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_name) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DF_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_name ?? DBNull.Value },
                new OracleParameter("LF_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_name) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LF_VAL",   OracleDbType.Varchar2) { Value = (object?)line_name ?? DBNull.Value },
                new OracleParameter("DID_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DID_VAL",  OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LID_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LID_VAL",  OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WID_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WID_VAL",  OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value }
            };

            // 1. GET GLOBAL SUMMARY (Counts by Status)
            // Note: We use simpler joins for the summary if possible, but here we keep it consistent.
            string sqlSummary = withSql + @"
                SELECT 
                    COUNT(*) TOTAL,
                    SUM(CASE WHEN NVL(R.CONFIRM_STATUS, 'PENDING') = 'PENDING' THEN 1 ELSE 0 END) PENDING,
                    SUM(CASE WHEN R.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) CONFIRMED,
                    SUM(CASE WHEN R.CONFIRM_STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                " + fromSql + whereSql;

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new
            {
                TOTAL     = r["TOTAL"]     == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING   = r["PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                CONFIRMED = r["CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["CONFIRMED"]),
                REJECTED  = r["REJECTED"]  == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new { TOTAL = 0, PENDING = 0, CONFIRMED = 0, REJECTED = 0 };

            if (summary.TOTAL == 0)
            {
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<OTHRDetailModel>() });
            }

            // 2. GET PAGED DATA
            string sqlData = withSql + @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY CONFIRM_STATUS,
                                                             CASE WHEN REQUESTER_ROLE = 'Expat' THEN 1 WHEN REQUESTER_ROLE = 'Manager' THEN 2 WHEN REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN REQUESTER_ROLE = 'HR' THEN 5 WHEN REQUESTER_ROLE = 'Clerk' THEN 6 WHEN REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             DEPT_ID, LINE_ID, EMPCD) RN
                    FROM (
                        SELECT
                            OT.EMPCD, OT.DAT, OT.SHIFTCD, OT.OT_HOURS, OT.OT_BEFORE, OT.OT_BEFORE_TIME, OT.OT_AFTER, OT.OT_AFTER_TIME,
                            EC.CNAME EMP_NAME, EC.DEPTCD DEPT_ID, EC.LINECD LINE_ID, EC.WORKCD WORK_ID,
                            B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                            S.STIME, S.ETIME,
                            NVL(R.CONFIRM_STATUS,'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE,
                            RR.ROLE_NAME REQUESTER_ROLE
                        " + fromSql + whereSql + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var rows = await _oracleService.ExecuteQueryAsync(sqlData, r =>
            {
                var model = new OTHRDetailModel
                {
                    EMPCD          = r["EMPCD"]?.ToString() ?? string.Empty,
                    EMP_NAME       = r["EMP_NAME"]?.ToString(),
                    DEPT_ID        = r["DEPT_ID"]?.ToString(),
                    DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                    LINE_ID        = r["LINE_ID"]?.ToString(),
                    LINE_NAME      = r["LINE_NAME"]?.ToString(),
                    WORK_ID        = r["WORK_ID"]?.ToString(),
                    WORK_NAME      = r["WORK_NAME"]?.ToString(),
                    OT_HOURS       = r["OT_HOURS"] == DBNull.Value ? null : Convert.ToDecimal(r["OT_HOURS"]),
                    OT_BEFORE      = r["OT_BEFORE"]?.ToString(),
                    OT_BEFORE_TIME = r["OT_BEFORE_TIME"]?.ToString(),
                    OT_AFTER       = r["OT_AFTER"]?.ToString(),
                    OT_AFTER_TIME  = r["OT_AFTER_TIME"]?.ToString(),
                    CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                    CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                    TOTAL_COUNT    = summary.TOTAL
                };

                try
                {
                    DateTime baseDate = Convert.ToDateTime(r["DAT"]);
                    string sTime = (r["STIME"]?.ToString() ?? "0000").PadLeft(4, '0');
                    string eTime = (r["ETIME"]?.ToString() ?? "0000").PadLeft(4, '0');

                    if (model.OT_AFTER == "Y")
                    {
                        model.START_OT = DateTime.ParseExact(baseDate.ToString("yyyyMMdd") + eTime, "yyyyMMddHHmm", null);
                        model.END_OT   = model.START_OT.Value.AddHours((double)(model.OT_HOURS ?? 0));
                    }
                    else if (model.OT_BEFORE == "Y")
                    {
                        model.END_OT   = DateTime.ParseExact(baseDate.ToString("yyyyMMdd") + sTime, "yyyyMMddHHmm", null);
                        model.START_OT = model.END_OT.Value.AddHours(-(double)(model.OT_HOURS ?? 0));
                    }
                }
                catch { }

                return model;
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = rows
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/OT/supervisor?emp_cd=&work_date=&status=&page=&page_size=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("supervisor")]
    public async Task<IActionResult> GetOTSupervisor(
        string  supervisor_empcd,
        string  filter_type,
        string? work_date = null,
        string? status    = null,
        string? search    = null,
        string? dept_id   = null,
        string? line_id   = null,
        string? work_id   = null,
        int     page      = 1,
        int     page_size = 100)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(supervisor_empcd))
                return Ok(Helpers.OTScopeFilterHelper.NotAuthorizedResponse(page, page_size));

            DateTime workDate;
            if (!DateTime.TryParseExact(work_date, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out workDate))
                workDate = DateTime.Today;

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", supervisor_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(Helpers.OTScopeFilterHelper.NotAuthorizedResponse(page, page_size));

            // Filter exact (DEPTCD, LINECD, WORKCD) tuple — tránh false-positive khi code bị reuse
            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(supervisor_empcd, prefix: "SV");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;
            string searchPattern = string.IsNullOrEmpty(search) ? "%" : "%" + search.ToUpper() + "%";

            string withSql = @"
                WITH OT_BASE AS (
                    SELECT /*+ MATERIALIZE */ EMPCD, DAT, SHIFTCD,
                           MAX(OVER_TIME)      OT_HOURS,
                           MAX(OT_BEFORE)      OT_BEFORE,
                           MAX(OT_BEFORE_TIME) OT_BEFORE_TIME,
                           MAX(OT_AFTER)       OT_AFTER,
                           MAX(OT_AFTER_TIME)  OT_AFTER_TIME
                    FROM (
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                        FROM HRMS.EBM300      WHERE DAT = :W_DATE1
                        UNION ALL
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                        FROM HRMS.EBM300_WAIT WHERE DAT = :W_DATE2
                    )
                    GROUP BY EMPCD, DAT, SHIFTCD
                )";

            string fromSql = @"
                FROM OT_BASE OT
                JOIN      HRMS.ECM100       EC ON EC.EMPCD  = OT.EMPCD
                JOIN      HRMS.EBM100        S ON S.SHIFTCD = OT.SHIFTCD
                LEFT JOIN HRMS.EAM410        B ON B.DEPTCD  = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_OT_REQUEST R ON R.EMPCD   = OT.EMPCD  AND R.WORK_DATE = :W_DATE3
                                               AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)
                LEFT JOIN HRMS.HR_USERS      UR ON UR.EMPCD  = OT.EMPCD
                LEFT JOIN HRMS.HR_ROLES      RR ON RR.ID     = UR.ROLE_ID";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND (:S_FLAG   IS NULL OR UPPER(OT.EMPCD) LIKE :S_VAL1)
                  AND (:ST_FLAG  IS NULL OR NVL(R.CONFIRM_STATUS,'PENDING') = :ST_VAL)
                  AND (:DID_FLAG IS NULL OR EC.DEPTCD = :DID_VAL)
                  AND (:LID_FLAG IS NULL OR EC.LINECD = :LID_VAL)
                  AND (:WID_FLAG IS NULL OR EC.WORKCD = :WID_VAL)
                  " + scopeFilter.SqlClause;

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("W_DATE1",      OracleDbType.Date)     { Value = workDate },
                new OracleParameter("W_DATE2",      OracleDbType.Date)     { Value = workDate },
                new OracleParameter("W_DATE3",      OracleDbType.Date)     { Value = workDate },
                new OracleParameter("S_FLAG",       OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("S_VAL1",       OracleDbType.Varchar2) { Value = searchPattern },
                new OracleParameter("ST_FLAG",      OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",       OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("DID_FLAG",     OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DID_VAL",      OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LID_FLAG",     OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LID_VAL",      OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WID_FLAG",     OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WID_VAL",      OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };
            baseParams.AddRange(scopeFilter.Params);

            // 4. Summary
            string sqlSummary = withSql + @"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN NVL(R.CONFIRM_STATUS,'PENDING') = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) CONFIRMED,
                       SUM(CASE WHEN R.CONFIRM_STATUS = 'REJECTED'  THEN 1 ELSE 0 END) REJECTED
                " + fromSql + whereSql;

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new
            {
                TOTAL     = r["TOTAL"]     == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING   = r["PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                CONFIRMED = r["CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["CONFIRMED"]),
                REJECTED  = r["REJECTED"]  == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new { TOTAL=0, PENDING=0, CONFIRMED=0, REJECTED=0 };

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<OTHRDetailModel>() });

            // 5. Paged data
            string sqlData = withSql + @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY CONFIRM_STATUS,
                                                             CASE WHEN REQUESTER_ROLE = 'Expat' THEN 1 WHEN REQUESTER_ROLE = 'Manager' THEN 2 WHEN REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN REQUESTER_ROLE = 'HR' THEN 5 WHEN REQUESTER_ROLE = 'Clerk' THEN 6 WHEN REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             DEPT_ID, LINE_ID, EMPCD) RN
                    FROM (
                        SELECT OT.EMPCD, OT.DAT, OT.SHIFTCD, OT.OT_HOURS, OT.OT_BEFORE, OT.OT_BEFORE_TIME, OT.OT_AFTER, OT.OT_AFTER_TIME,
                               EC.CNAME EMP_NAME, EC.DEPTCD DEPT_ID, EC.LINECD LINE_ID, EC.WORKCD WORK_ID,
                               B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                               S.STIME, S.ETIME,
                               NVL(R.CONFIRM_STATUS,'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE,
                               RR.ROLE_NAME REQUESTER_ROLE
                        " + fromSql + whereSql + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var rows = await _oracleService.ExecuteQueryAsync(sqlData, r =>
            {
                var model = new OTHRDetailModel
                {
                    EMPCD          = r["EMPCD"]?.ToString() ?? "",
                    EMP_NAME       = r["EMP_NAME"]?.ToString(),
                    DEPT_ID        = r["DEPT_ID"]?.ToString(),
                    DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                    LINE_ID        = r["LINE_ID"]?.ToString(),
                    LINE_NAME      = r["LINE_NAME"]?.ToString(),
                    WORK_ID        = r["WORK_ID"]?.ToString(),
                    WORK_NAME      = r["WORK_NAME"]?.ToString(),
                    OT_HOURS       = r["OT_HOURS"] == DBNull.Value ? null : Convert.ToDecimal(r["OT_HOURS"]),
                    OT_BEFORE      = r["OT_BEFORE"]?.ToString(),
                    OT_BEFORE_TIME = r["OT_BEFORE_TIME"]?.ToString(),
                    OT_AFTER       = r["OT_AFTER"]?.ToString(),
                    OT_AFTER_TIME  = r["OT_AFTER_TIME"]?.ToString(),
                    CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                    CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                    TOTAL_COUNT    = summary.TOTAL
                };
                try
                {
                    DateTime baseDate = Convert.ToDateTime(r["DAT"]);
                    string sTime = (r["STIME"]?.ToString() ?? "0000").PadLeft(4, '0');
                    string eTime = (r["ETIME"]?.ToString() ?? "0000").PadLeft(4, '0');
                    if (model.OT_AFTER == "Y")
                    {
                        model.START_OT = DateTime.ParseExact(baseDate.ToString("yyyyMMdd") + eTime, "yyyyMMddHHmm", null);
                        model.END_OT   = model.START_OT.Value.AddHours((double)(model.OT_HOURS ?? 0));
                    }
                    else if (model.OT_BEFORE == "Y")
                    {
                        model.END_OT   = DateTime.ParseExact(baseDate.ToString("yyyyMMdd") + sTime, "yyyyMMddHHmm", null);
                        model.START_OT = model.END_OT.Value.AddHours(-(double)(model.OT_HOURS ?? 0));
                    }
                }
                catch { }
                return model;
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = rows
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message });
        }
    }

    [HttpPost("hr/notify-pending")]
    public async Task<IActionResult> NotifyPendingOT([FromBody] dynamic model)
    {
        try
        {
            string workDateStr = model.work_date;
            string deptId = model.dept_id;
            string createdBy = model.created_by;

            // Logic gửi thông báo cho những ai chưa ký OT
            // Ở đây gửi thông báo theo Department 
            _notiSvc.OTSignReminder(workDateStr, createdBy, string.IsNullOrEmpty(deptId) ? null : deptId);

            return Ok(new { success = true, message = "Đã gửi thông báo nhắc nhở" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
