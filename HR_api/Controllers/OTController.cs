using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.OT;
using System.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class OTController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly Helpers.NotificationHelper _notiHelper;

    public OTController(OracleService oracleService, Helpers.NotificationHelper notiHelper)
    {
        _oracleService = oracleService;
        _notiHelper = notiHelper;
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetOTToday(string empcd, string? work_date = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            DateTime workDate = string.IsNullOrEmpty(work_date) ? DateTime.Today : DateTime.Parse(work_date);

            string sql = @"
                SELECT E.EMPCD, E.DAT WORK_DATE, E.OVER_TIME OT_HOURS, E.OT_BEFORE, E.OT_BEFORE_TIME, E.OT_AFTER, E.OT_AFTER_TIME, E.OT_REST,
                       CASE WHEN E.OT_BEFORE = 'Y' OR E.OT_AFTER = 'Y' THEN 'Y' ELSE 'N' END HAS_OT,
                       CASE WHEN E.OT_BEFORE = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.STIME,'YYYYMMDDHH24MI') - E.OT_BEFORE_TIME / 24
                            WHEN E.OT_AFTER = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.ETIME,'YYYYMMDDHH24MI')
                       END START_OT,
                       CASE WHEN E.OT_AFTER = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.ETIME,'YYYYMMDDHH24MI') + E.OT_AFTER_TIME / 24
                            WHEN E.OT_BEFORE = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.STIME,'YYYYMMDDHH24MI')
                       END END_OT,
                       NVL(R.CONFIRM_STATUS, 'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE,
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
                LEFT JOIN (SELECT EMPCD, CONFIRM_STATUS, CONFIRM_DATE, OT_HOURS FROM HRMS.HR_OT_REQUEST WHERE WORK_DATE = :WORK_DATE3) R ON R.EMPCD = E.EMPCD AND NVL(R.OT_HOURS,0) = NVL(E.OVER_TIME,0)
                WHERE ROWNUM = 1
                ";

            var result = await _oracleService.ExecuteQueryAsync(sql, r => new OTTodayModel
            {
                EMPCD = r["EMPCD"]?.ToString() ?? string.Empty,
                WORK_DATE = r["WORK_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["WORK_DATE"]),
                OT_HOURS = r["OT_HOURS"] == DBNull.Value ? null : Convert.ToDecimal(r["OT_HOURS"]),
                OT_BEFORE = r["OT_BEFORE"]?.ToString(),
                OT_BEFORE_TIME = r["OT_BEFORE_TIME"]?.ToString(),
                OT_AFTER = r["OT_AFTER"]?.ToString(),
                OT_AFTER_TIME = r["OT_AFTER_TIME"]?.ToString(),
                OT_REST = r["OT_REST"]?.ToString(),
                HAS_OT = r["HAS_OT"]?.ToString(),
                START_OT = r["START_OT"] == DBNull.Value ? null : Convert.ToDateTime(r["START_OT"]),
                END_OT = r["END_OT"] == DBNull.Value ? null : Convert.ToDateTime(r["END_OT"]),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                SUM_WEEK = Convert.ToDecimal(r["SUM_WEEK"]),
                SUM_MONTH = Convert.ToDecimal(r["SUM_MONTH"]),
                SUM_YEAR = Convert.ToDecimal(r["SUM_YEAR"])
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

            DateTime workDate = string.IsNullOrEmpty(model.WORK_DATE) ? DateTime.Today : DateTime.Parse(model.WORK_DATE);

            if (model.CONFIRM_STATUS != "CONFIRMED" && model.CONFIRM_STATUS != "REJECTED")
                return Ok(new { success = false, message = "Trạng thái không hợp lệ" });

            string sqlCheckERP = @"
                SELECT COUNT(*) CNT FROM (SELECT EMPCD FROM HRMS.EBM300 WHERE DAT = :WORK_DATE AND EMPCD = :EMPCD
                                          UNION ALL
                                          SELECT EMPCD FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2 AND EMPCD = :EMPCD1)";

            var hasOT = await _oracleService.ExecuteQueryAsync(sqlCheckERP, r => Convert.ToInt32(r["CNT"]), 
                new OracleParameter("WORK_DATE", workDate), 
                new OracleParameter("WORK_DATE2", workDate), 
                new OracleParameter("EMPCD", model.EMPCD),
                new OracleParameter("EMPCD1", model.EMPCD));

            if (hasOT.Count == 0 || hasOT[0] == 0)
                return Ok(new { success = false, message = "Không có kế hoạch tăng ca trong ngày này" });

            string sqlCheckConfirm = "SELECT COUNT(*) CNT FROM HRMS.HR_OT_REQUEST WHERE EMPCD = :EMPCD AND WORK_DATE = :WORK_DATE AND NVL(OT_HOURS,0) = NVL(:OT_HOURS,0)";
            var already = await _oracleService.ExecuteQueryAsync(sqlCheckConfirm, r => Convert.ToInt32(r["CNT"]), 
                new OracleParameter("EMPCD", model.EMPCD), 
                new OracleParameter("WORK_DATE", workDate),
                new OracleParameter("OT_HOURS", (object?)model.OT_HOURS ?? DBNull.Value));

            if (already.Count > 0 && already[0] > 0)
                return Ok(new { success = false, message = "Bạn đã xác nhận tăng ca ngày này với số giờ này rồi, không thể thay đổi" });

            string sqlInsertReq = "INSERT INTO HRMS.HR_REQUEST (REQUEST_TYPE, EMPCD, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE) VALUES ('OT', :EMPCD, SYSDATE, :STATUS, :EMPCD1, SYSDATE)";
            await _oracleService.ExecuteNonQueryAsync(sqlInsertReq, 
                new OracleParameter("EMPCD", model.EMPCD), 
                new OracleParameter("STATUS", model.CONFIRM_STATUS),
                new OracleParameter("EMPCD1", model.EMPCD));

            string sqlGetReqId = "SELECT REQUEST_ID FROM (SELECT REQUEST_ID FROM HRMS.HR_REQUEST WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'OT' AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE) ORDER BY CREATED_DATE DESC) WHERE ROWNUM = 1";
            var reqIds = await _oracleService.ExecuteQueryAsync(sqlGetReqId, r => r["REQUEST_ID"]?.ToString(), new OracleParameter("EMPCD", model.EMPCD));

            if (reqIds.Count == 0) return Ok(new { success = false, message = "Lỗi tạo REQUEST_ID" });
            string requestId = reqIds[0]!;

            string sqlInsertOT = "INSERT INTO HRMS.HR_OT_REQUEST (REQUEST_ID, EMPCD, WORK_DATE, OT_HOURS, CONFIRM_STATUS, CONFIRM_DATE, CREATED_DATE) VALUES (:REQUEST_ID, :EMPCD, :WORK_DATE, :OT_HOURS, :CONFIRM_STATUS, SYSDATE, SYSDATE)";
            await _oracleService.ExecuteNonQueryAsync(sqlInsertOT,
                new OracleParameter("REQUEST_ID", requestId),
                new OracleParameter("EMPCD", model.EMPCD),
                new OracleParameter("WORK_DATE", workDate),
                new OracleParameter("OT_HOURS", (object?)model.OT_HOURS ?? DBNull.Value),
                new OracleParameter("CONFIRM_STATUS", model.CONFIRM_STATUS));

            string msg = model.CONFIRM_STATUS == "CONFIRMED" ? "Xác nhận tăng ca thành công" : "Từ chối tăng ca thành công";
            return Ok(new { success = true, message = msg, request_id = requestId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("clerk")]
    public async Task<IActionResult> GetOTClerk(string clerk_empcd, string? work_date = null,
        string? status = null, string? search = null,
        string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(clerk_empcd)) return Ok(new { success = false, message = "Thiếu mã clerk" });

            DateTime workDate = string.IsNullOrEmpty(work_date) ? DateTime.Today : DateTime.Parse(work_date);
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            string sqlGetInfo = @"
                SELECT E.DEPTCD, E.LINECD, E.WORKCD, B.DEPTNM FROM HRMS.HR_USERS U
                JOIN HRMS.ECM100 E ON E.EMPCD = U.EMPCD
                LEFT JOIN HRMS.EAM410 B ON E.DEPTCD = B.DEPTCD AND E.LINECD = B.LINECD AND E.WORKCD = B.WORKCD
                WHERE U.EMPCD = :CLERK_EMPCD AND ROWNUM = 1";

            var clerkInfos = await _oracleService.ExecuteQueryAsync(sqlGetInfo, r => new {
                DEPTCD = r["DEPTCD"]?.ToString(),
                LINECD = r["LINECD"]?.ToString(),
                DEPTNM = r["DEPTNM"]?.ToString()
            }, new OracleParameter("CLERK_EMPCD", clerk_empcd));

            if (clerkInfos.Count == 0) return Ok(new { success = false, message = "Không tìm thấy thông tin clerk" });
            var info = clerkInfos[0];

            bool isOffice = info.DEPTNM == "OFFICE STAFF";
            string filterVal = isOffice ? info.LINECD! : info.DEPTCD!;
            var clerkFilter = Helpers.OTScopeFilterHelper.ForClerk(isOffice, filterVal);

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
                                               AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)";

            string whereSql = @"
                WHERE NVL(EC.RETDAT,'9999') > TO_CHAR(SYSDATE,'YYYYMMDD')
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  " + clerkFilter.SqlClause + @"
                  AND (:ST_FLAG IS NULL OR NVL(R.CONFIRM_STATUS,'PENDING') = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(EC.EMPCD) LIKE :SRCH_VAL1 OR UPPER(EC.CNAME) LIKE :SRCH_VAL2)
                  AND (:LN_FLAG IS NULL OR EC.LINECD = :LN_VAL)
                  AND (:WK_FLAG IS NULL OR EC.WORKCD = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("WORK_DATE",  workDate),
                new OracleParameter("WORK_DATE2", workDate),
                new OracleParameter("WORK_DATE3", workDate),
                new OracleParameter("ST_FLAG",    OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",     OracleDbType.Varchar2) { Value = (object?)status ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL1",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL2",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
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
                return Ok(new { success = true, dept_id = info.DEPTCD, line_id = info.LINECD, is_office = isOffice,
                                summary, total = 0, page, page_size, total_pages = 0, data = new List<OTClerkModel>() });

            // 2. Paged data
            string sqlData = withSql + @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.CONFIRM_STATUS, T.LINE_ID, T.EMPCD) RN
                    FROM (
                        SELECT OT.EMPCD, EC.CNAME EMP_NAME, EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME, EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               OT.OT_HOURS, OT.OT_BEFORE, OT.OT_BEFORE_TIME, OT.OT_AFTER, OT.OT_AFTER_TIME,
                               S.STIME, S.ETIME, NVL(R.CONFIRM_STATUS,'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE
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

            return Ok(new { success = true, dept_id = info.DEPTCD, line_id = info.LINECD, is_office = isOffice,
                            summary, total = summary.TOTAL, page, page_size,
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
            DateTime workDate = string.IsNullOrEmpty(work_date) ? DateTime.Today : DateTime.Parse(work_date);

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
                LEFT JOIN HRMS.HR_OT_REQUEST  R ON R.EMPCD   = OT.EMPCD  AND R.WORK_DATE = :W_DATE3 AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND (:S_FLAG  IS NULL OR (OT.EMPCD LIKE :S_VAL1 OR UPPER(EC.CNAME) LIKE :S_VAL2))
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
                new OracleParameter("S_VAL2",   OracleDbType.Varchar2) { Value = searchPattern },
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
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY CONFIRM_STATUS, DEPT_ID, LINE_ID, EMPCD) RN
                    FROM (
                        SELECT
                            OT.EMPCD, OT.DAT, OT.SHIFTCD, OT.OT_HOURS, OT.OT_BEFORE, OT.OT_BEFORE_TIME, OT.OT_AFTER, OT.OT_AFTER_TIME,
                            EC.CNAME EMP_NAME, EC.DEPTCD DEPT_ID, EC.LINECD LINE_ID, EC.WORKCD WORK_ID,
                            B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                            S.STIME, S.ETIME,
                            NVL(R.CONFIRM_STATUS,'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE
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
        string  filter_type,
        string  filter_codes,
        string? filter_line_codes = null,
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
            if (string.IsNullOrEmpty(filter_codes))
                return Ok(new { success = true, summary = new { TOTAL=0,PENDING=0,CONFIRMED=0,REJECTED=0 },
                                total = 0, page, page_size, total_pages = 0, data = new List<OTHRDetailModel>(),
                                message = "Chưa được phân quyền bộ phận" });

            DateTime workDate;
            if (!DateTime.TryParseExact(work_date, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out workDate))
                workDate = DateTime.Today;

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScope(filter_type, filter_codes, filter_line_codes);

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
                                               AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND (:S_FLAG   IS NULL OR (OT.EMPCD LIKE :S_VAL1 OR UPPER(EC.CNAME) LIKE :S_VAL2))
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
                new OracleParameter("S_VAL2",       OracleDbType.Varchar2) { Value = searchPattern },
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
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY CONFIRM_STATUS, DEPT_ID, LINE_ID, EMPCD) RN
                    FROM (
                        SELECT OT.EMPCD, OT.DAT, OT.SHIFTCD, OT.OT_HOURS, OT.OT_BEFORE, OT.OT_BEFORE_TIME, OT.OT_AFTER, OT.OT_AFTER_TIME,
                               EC.CNAME EMP_NAME, EC.DEPTCD DEPT_ID, EC.LINECD LINE_ID, EC.WORKCD WORK_ID,
                               B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                               S.STIME, S.ETIME,
                               NVL(R.CONFIRM_STATUS,'PENDING') CONFIRM_STATUS, R.CONFIRM_DATE
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
            _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
            {
                TITLE = "Xác nhận tăng ca",
                BODY = $"Vui lòng kiểm tra và ký xác nhận tăng ca ngày {workDateStr}.",
                NOTI_TYPE = string.IsNullOrEmpty(deptId) ? "COMPANY" : "DEPT",
                TARGET_VAL = string.IsNullOrEmpty(deptId) ? "ALL" : deptId,
                LINK_ACTION = "OT_SIGN",
                CREATED_BY = createdBy
            });

            return Ok(new { success = true, message = "Đã gửi thông báo nhắc nhở" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
