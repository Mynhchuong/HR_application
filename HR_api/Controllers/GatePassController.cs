using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.GatePass;
using System.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class GatePassController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly Helpers.NotificationHelper _notiHelper;

    public GatePassController(OracleService oracleService, Helpers.NotificationHelper notiHelper)
    {
        _oracleService = oracleService;
        _notiHelper = notiHelper;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/GatePass/shift-info?empcd=&reg_date=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("shift-info")]
    public async Task<IActionResult> GetShiftInfo(string empcd, string? reg_date = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            DateTime regDate = (!string.IsNullOrEmpty(reg_date) && DateTime.TryParse(reg_date, out var _rd)) ? _rd : DateTime.Today;

            string sql = @"
                SELECT S.STIME, S.ETIME FROM (
                    SELECT SHIFTCD FROM HRMS.EBM300      WHERE EMPCD = :EMPCD  AND DAT = :REG_DATE  AND ROWNUM = 1
                    UNION ALL
                    SELECT SHIFTCD FROM HRMS.EBM300_WAIT WHERE EMPCD = :EMPCD1 AND DAT = :REG_DATE1 AND ROWNUM = 1
                ) T
                JOIN HRMS.EBM100 S ON S.SHIFTCD = T.SHIFTCD
                WHERE ROWNUM = 1";

            var result = await _oracleService.ExecuteQueryAsync(sql, r => new GpShiftInfoModel
            {
                STIME = r["STIME"]?.ToString(),
                ETIME = r["ETIME"]?.ToString()
            },
            new OracleParameter("EMPCD",     empcd),
            new OracleParameter("REG_DATE",  regDate),
            new OracleParameter("EMPCD1",    empcd),
            new OracleParameter("REG_DATE1", regDate));

            return Ok(new { success = true, data = result.FirstOrDefault() });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/GatePass/submit
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] GpSubmitRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            if (string.IsNullOrEmpty(model.GP_TYPE))
                return Ok(new { success = false, message = "Thiếu loại gate pass" });

            if (!DateTime.TryParse(model.REG_DATE, out DateTime regDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            var outTime = ParseDateTime(regDate, model.OUT_TIME);
            var inTime  = ParseDateTime(regDate, model.IN_TIME);

            // Validate time fields theo GP_TYPE
            if (model.GP_TYPE == "IN" && inTime == null)
                return Ok(new { success = false, message = "Vui lòng nhập giờ đến thực tế" });
            if (model.GP_TYPE == "OUT" && outTime == null)
                return Ok(new { success = false, message = "Vui lòng nhập giờ ra sớm" });
            if (model.GP_TYPE == "MID" && (outTime == null || inTime == null))
                return Ok(new { success = false, message = "Vui lòng nhập đầy đủ giờ ra và giờ vào" });

            // Lấy thông tin nhân viên từ ECM100
            var empRows = await _oracleService.ExecuteQueryAsync(
                "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["CNAME"]?.ToString(),
                new OracleParameter("EMPCD", model.EMPCD));

            string empName = empRows.FirstOrDefault() ?? "";

            // Insert HR_REQUEST (trigger sẽ tự tạo REQUEST_ID)
            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_REQUEST (REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE)
                VALUES ('GATEPASS', :EMPCD, :EMP_NAME, SYSDATE, 'PENDING', :EMPCD1, SYSDATE)",
                new OracleParameter("EMPCD",     model.EMPCD),
                new OracleParameter("EMP_NAME",  empName),
                new OracleParameter("EMPCD1",    model.EMPCD));

            // Lấy REQUEST_ID vừa tạo
            var reqIds = await _oracleService.ExecuteQueryAsync(@"
                SELECT REQUEST_ID FROM (
                    SELECT REQUEST_ID FROM HRMS.HR_REQUEST
                    WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'GATEPASS' AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE)
                    ORDER BY CREATED_DATE DESC
                ) WHERE ROWNUM = 1",
                r => r["REQUEST_ID"]?.ToString(),
                new OracleParameter("EMPCD", model.EMPCD));

            if (reqIds.Count == 0 || string.IsNullOrEmpty(reqIds[0]))
                return Ok(new { success = false, message = "Lỗi tạo REQUEST_ID" });

            string requestId = reqIds[0]!;

            // Insert HR_GATEPASS_REQUEST
            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_GATEPASS_REQUEST (REQUEST_ID, EMPCD, GP_TYPE, OUT_TIME, IN_TIME, REASON, CREATED_DATE)
                VALUES (:REQUEST_ID, :EMPCD, :GP_TYPE, :OUT_TIME, :IN_TIME, :REASON, SYSDATE)",
                new OracleParameter("REQUEST_ID", requestId),
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("GP_TYPE",    model.GP_TYPE),
                new OracleParameter("OUT_TIME",   (object?)outTime ?? DBNull.Value),
                new OracleParameter("IN_TIME",    (object?)inTime  ?? DBNull.Value),
                new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

            return Ok(new { success = true, message = "Đăng ký Gate Pass thành công", request_id = requestId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/GatePass/my-requests?empcd=&page=&page_size=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-requests")]
    public async Task<IActionResult> GetMyRequests(string empcd, int page = 1, int page_size = 20, string? date_from = null, string? date_to = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today;

            string dateFilter = " AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN :D_FROM AND :D_TO";

            string countSql = @"
                SELECT COUNT(*) CNT
                FROM HRMS.HR_GATEPASS_REQUEST GP
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = GP.REQUEST_ID
                WHERE GP.EMPCD = :EMPCD" + dateFilter;

            var totalRows = await _oracleService.ExecuteQueryAsync(countSql,
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("EMPCD",  empcd),
                new OracleParameter("D_FROM", dfrom.Date),
                new OracleParameter("D_TO",   dto.Date));

            int total = totalRows.FirstOrDefault();

            string dataSql = @"
                SELECT * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.CREATED_DATE DESC) RN
                    FROM (
                        SELECT GP.REQUEST_ID, GP.GP_TYPE, GP.OUT_TIME, GP.IN_TIME, GP.REASON,
                               R.STATUS, R.REMARK, GP.CREATED_DATE
                        FROM HRMS.HR_GATEPASS_REQUEST GP
                        JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = GP.REQUEST_ID
                        WHERE GP.EMPCD = :EMPCD1" + dateFilter.Replace(":D_FROM", ":D_FROM1").Replace(":D_TO", ":D_TO1") + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var list = await _oracleService.ExecuteQueryAsync(dataSql, r => new GpMyRequestModel
            {
                REQUEST_ID   = r["REQUEST_ID"]?.ToString() ?? "",
                GP_TYPE      = r["GP_TYPE"]?.ToString(),
                OUT_TIME     = r["OUT_TIME"]    == DBNull.Value ? null : Convert.ToDateTime(r["OUT_TIME"]),
                IN_TIME      = r["IN_TIME"]     == DBNull.Value ? null : Convert.ToDateTime(r["IN_TIME"]),
                REASON       = r["REASON"]?.ToString(),
                REMARK       = r["REMARK"]?.ToString(),
                STATUS       = r["STATUS"]?.ToString(),
                CREATED_DATE = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                IS_EDITABLE  = (r["STATUS"]?.ToString() ?? "PENDING") == "PENDING"
            },
            new OracleParameter("EMPCD1",  empcd),
            new OracleParameter("D_FROM1", dfrom.Date),
            new OracleParameter("D_TO1",   dto.Date),
            new OracleParameter("R_MIN",   offset),
            new OracleParameter("R_MAX",   maxRn));

            return Ok(new
            {
                success     = true,
                total,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)total / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /apiHR/GatePass/update
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("update")]
    public async Task<IActionResult> Update([FromBody] GpUpdateRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin cập nhật" });

            // Kiểm tra trạng thái — chỉ cho phép sửa khi PENDING
            var statusRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_GATEPASS_REQUEST GP ON GP.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND GP.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            if (statusRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (statusRows[0] != "PENDING")
                return Ok(new { success = false, message = "Chỉ có thể sửa yêu cầu đang chờ duyệt" });

            // Tính lại thời gian từ GP_TYPE và time strings
            // Lấy ngày gốc từ record hiện tại
            var dateRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT NVL(OUT_TIME, IN_TIME) REG_DT FROM HRMS.HR_GATEPASS_REQUEST
                WHERE REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => r["REG_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["REG_DT"]),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            DateTime regDate = dateRows.FirstOrDefault()?.Date ?? DateTime.Today;
            var outTime = ParseDateTime(regDate, model.OUT_TIME);
            var inTime  = ParseDateTime(regDate, model.IN_TIME);

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_GATEPASS_REQUEST
                SET GP_TYPE = :GP_TYPE, OUT_TIME = :OUT_TIME, IN_TIME = :IN_TIME, REASON = :REASON,
                    UPDATED_BY = :UPDATED_BY, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("GP_TYPE",     model.GP_TYPE),
                new OracleParameter("OUT_TIME",    (object?)outTime ?? DBNull.Value),
                new OracleParameter("IN_TIME",     (object?)inTime  ?? DBNull.Value),
                new OracleParameter("REASON",      (object?)model.REASON ?? DBNull.Value),
                new OracleParameter("UPDATED_BY",  model.EMPCD),
                new OracleParameter("REQUEST_ID",  model.REQUEST_ID),
                new OracleParameter("EMPCD",       model.EMPCD));

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST SET UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            return Ok(new { success = true, message = "Cập nhật Gate Pass thành công" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /apiHR/GatePass/delete?request_id=&empcd=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("delete")]
    public async Task<IActionResult> Delete(string request_id, string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(request_id) || string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu thông tin xoá" });

            var statusRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_GATEPASS_REQUEST GP ON GP.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND GP.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            if (statusRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (statusRows[0] != "PENDING")
                return Ok(new { success = false, message = "Chỉ có thể xoá yêu cầu đang chờ duyệt" });

            await _oracleService.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_GATEPASS_REQUEST WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            await _oracleService.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_REQUEST WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            return Ok(new { success = true, message = "Đã xoá yêu cầu Gate Pass" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/GatePass/supervisor?supervisor_empcd=&status=&search=&...&page=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("supervisor")]
    public async Task<IActionResult> GetSupervisorList(
        string  supervisor_empcd,
        string? status    = null,
        string? search    = null,
        string? dept_id   = null,
        string? line_id   = null,
        string? work_id   = null,
        string? date_from = null,
        string? date_to   = null,
        int     page      = 1,
        int     page_size = 50)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(supervisor_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", supervisor_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(supervisor_empcd, empAlias: "EC", prefix: "SV");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today;
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today;

            string fromSql = @"
                FROM HRMS.HR_GATEPASS_REQUEST GP
                JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID  = GP.REQUEST_ID
                JOIN HRMS.ECM100    EC  ON EC.EMPCD       = GP.EMPCD
                LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN :D_FROM AND :D_TO
                  " + scopeFilter.SqlClause + @"
                  AND (:ST_FLAG   IS NULL OR R.STATUS        = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(GP.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD       = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD        = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD        = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };
            baseParams.AddRange(scopeFilter.Params);

            string sqlSummary = @"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                " + fromSql + whereSql;

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new GpSummary
            {
                TOTAL    = r["TOTAL"]    == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING  = r["PENDING"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED = r["APPROVED"] == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED = r["REJECTED"] == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new GpSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<GpListModel>() });

            string sqlData = @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS, T.EMPCD) RN
                    FROM (
                        SELECT GP.REQUEST_ID, GP.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               GP.GP_TYPE, GP.OUT_TIME, GP.IN_TIME, GP.REASON,
                               R.STATUS, GP.CREATED_DATE,
                               R.FINAL_DATE, R.REMARK
                        " + fromSql + whereSql + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new GpListModel
            {
                REQUEST_ID   = r["REQUEST_ID"]?.ToString()  ?? "",
                EMPCD        = r["EMPCD"]?.ToString()        ?? "",
                EMP_NAME     = r["EMP_NAME"]?.ToString(),
                DEPT_ID      = r["DEPT_ID"]?.ToString(),
                DEPT_NAME    = r["DEPT_NAME"]?.ToString(),
                LINE_ID      = r["LINE_ID"]?.ToString(),
                LINE_NAME    = r["LINE_NAME"]?.ToString(),
                WORK_ID      = r["WORK_ID"]?.ToString(),
                WORK_NAME    = r["WORK_NAME"]?.ToString(),
                GP_TYPE      = r["GP_TYPE"]?.ToString(),
                OUT_TIME     = r["OUT_TIME"]     == DBNull.Value ? null : Convert.ToDateTime(r["OUT_TIME"]),
                IN_TIME      = r["IN_TIME"]      == DBNull.Value ? null : Convert.ToDateTime(r["IN_TIME"]),
                REASON       = r["REASON"]?.ToString(),
                STATUS       = r["STATUS"]?.ToString(),
                CREATED_DATE = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_DATE   = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK       = r["REMARK"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/GatePass/approve
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] GpApproveRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.APPROVER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin duyệt" });

            // Single PL/SQL block: UPDATE + SP_INSERT_GATE_PASS atomically.
            // If SP raises exception the whole block fails → no autocommit → UPDATE rolled back.
            string plsql = @"
DECLARE
    v_rows    NUMBER;
    v_nls_fmt VARCHAR2(100);
    v_empcd   HRMS.HR_REQUEST.EMPCD%TYPE;
    v_gp_type HRMS.HR_GATEPASS_REQUEST.GP_TYPE%TYPE;
    v_out_dt  HRMS.HR_GATEPASS_REQUEST.OUT_TIME%TYPE;
    v_in_dt   HRMS.HR_GATEPASS_REQUEST.IN_TIME%TYPE;
    v_dat     VARCHAR2(8);
    v_timeout VARCHAR2(4);
    v_timein  VARCHAR2(4);
BEGIN
    SELECT VALUE INTO v_nls_fmt FROM NLS_SESSION_PARAMETERS WHERE PARAMETER = 'NLS_DATE_FORMAT';
    EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''YYYYMMDD''';

    UPDATE HRMS.HR_REQUEST
    SET STATUS = 'APPROVED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
        REMARK = :REMARK_VAL, UPDATED_BY = :APPROVER, UPDATED_DATE = SYSDATE
    WHERE REQUEST_ID = :REQUEST_ID AND STATUS = 'PENDING';

    v_rows := SQL%ROWCOUNT;
    :ROW_COUNT := v_rows;

    IF v_rows > 0 THEN
        SELECT R.EMPCD, G.GP_TYPE, G.OUT_TIME, G.IN_TIME
        INTO v_empcd, v_gp_type, v_out_dt, v_in_dt
        FROM HRMS.HR_REQUEST R
        JOIN HRMS.HR_GATEPASS_REQUEST G ON G.REQUEST_ID = R.REQUEST_ID
        WHERE R.REQUEST_ID = :REQUEST_ID;

        -- lấy ngày thực tế từ OUT_TIME/IN_TIME, không phải ngày tạo đơn
        v_dat     := COALESCE(TO_CHAR(v_out_dt, 'YYYYMMDD'), TO_CHAR(v_in_dt, 'YYYYMMDD'));
        v_timeout := CASE WHEN v_out_dt IS NOT NULL THEN TO_CHAR(v_out_dt, 'HH24MI') ELSE NULL END;
        v_timein  := CASE WHEN v_in_dt  IS NOT NULL THEN TO_CHAR(v_in_dt,  'HH24MI') ELSE NULL END;

        HRMS.SP_INSERT_GATE_PASS(
            P_EMPCD       => v_empcd,
            P_DAT         => v_dat,
            P_TYPE        => v_gp_type,
            P_TIMEIN      => v_timein,
            P_TIMEOUT     => v_timeout,
            P_INID        => v_empcd,
            P_APPROVED_ID => :APPROVER
        );

        :EMP_CD := v_empcd;
    END IF;

    EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''' || v_nls_fmt || '''';
EXCEPTION WHEN OTHERS THEN
    EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''' || v_nls_fmt || '''';
    RAISE;
END;";

            var pApprover  = new OracleParameter("APPROVER",   model.APPROVER_EMPCD);
            var pRemark    = new OracleParameter("REMARK_VAL", (object?)model.COMMENT ?? DBNull.Value);
            var pReqId     = new OracleParameter("REQUEST_ID", model.REQUEST_ID);
            var pRowCount  = new OracleParameter("ROW_COUNT",  OracleDbType.Int32)
                             { Direction = ParameterDirection.Output };
            var pEmpCd     = new OracleParameter("EMP_CD",     OracleDbType.Varchar2, 20)
                             { Direction = ParameterDirection.Output };

            await _oracleService.ExecuteNonQueryAsync(plsql, pApprover, pRemark, pReqId, pRowCount, pEmpCd);

            int rows = pRowCount.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od ? (int)od.Value : 0;

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy hoặc đã được xử lý rồi" });

            string? empCd = pEmpCd.Value?.ToString();
            if (!string.IsNullOrEmpty(empCd) && empCd != "null")
            {
                _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                {
                    TITLE       = "Gate Pass được duyệt",
                    BODY        = "Yêu cầu ra/vào cổng của bạn đã được phê duyệt.",
                    NOTI_TYPE   = "EMPCD",
                    TARGET_VAL  = empCd,
                    LINK_ACTION = "GP_MY",
                    CREATED_BY  = model.APPROVER_EMPCD
                });
            }

            return Ok(new { success = true, message = "Đã duyệt Gate Pass" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/GatePass/reject
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] GpApproveRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.APPROVER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin từ chối" });

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST
                SET STATUS = 'REJECTED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
                    REMARK = :REMARK_VAL, UPDATED_BY = :APPROVER1, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND STATUS = 'PENDING'",
                new OracleParameter("APPROVER",   model.APPROVER_EMPCD),
                new OracleParameter("REMARK_VAL", (object?)model.COMMENT ?? DBNull.Value),
                new OracleParameter("APPROVER1",  model.APPROVER_EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy hoặc đã được xử lý rồi" });

            var empRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.EMPCD FROM HRMS.HR_REQUEST R WHERE R.REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => r["EMPCD"]?.ToString(),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (empRows.Count > 0 && !string.IsNullOrEmpty(empRows[0]))
            {
                _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                {
                    TITLE      = "Gate Pass bị từ chối",
                    BODY       = "Yêu cầu ra/vào cổng của bạn đã bị từ chối.",
                    NOTI_TYPE  = "EMPCD",
                    TARGET_VAL = empRows[0],
                    LINK_ACTION = "GP_MY",
                    CREATED_BY = model.APPROVER_EMPCD
                });
            }

            return Ok(new { success = true, message = "Đã từ chối Gate Pass" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/GatePass/clerk?clerk_empcd=&status=&search=&...&page=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("clerk")]
    public async Task<IActionResult> GetClerkList(
        string  clerk_empcd,
        string? status    = null,
        string? search    = null,
        string? dept_id   = null,
        string? line_id   = null,
        string? work_id   = null,
        string? date_from = null,
        string? date_to   = null,
        int     page      = 1,
        int     page_size = 50)
    {
        try
        {
            if (string.IsNullOrEmpty(clerk_empcd))
                return Ok(new { success = false, message = "Thiếu mã clerk" });

            var hasClerkScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :CE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CE", clerk_empcd));

            if (hasClerkScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Không tìm thấy thông tin clerk" });

            var clerkFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(clerk_empcd, empAlias: "EC", prefix: "CK");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today;
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today;

            string fromSql = @"
                FROM HRMS.HR_GATEPASS_REQUEST GP
                JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID  = GP.REQUEST_ID
                JOIN HRMS.ECM100    EC  ON EC.EMPCD       = GP.EMPCD
                LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100 AP ON AP.EMPCD = R.FINAL_APPROVER";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN :D_FROM AND :D_TO
                  " + clerkFilter.SqlClause + @"
                  AND (:ST_FLAG   IS NULL OR R.STATUS        = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(GP.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD       = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD        = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD        = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };
            baseParams.AddRange(clerkFilter.Params);

            string sqlSummary = @"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                " + fromSql + whereSql;

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new GpSummary
            {
                TOTAL    = r["TOTAL"]    == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING  = r["PENDING"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED = r["APPROVED"] == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED = r["REJECTED"] == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new GpSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<GpListModel>() });

            string sqlData = @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS, T.EMPCD) RN
                    FROM (
                        SELECT GP.REQUEST_ID, GP.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               GP.GP_TYPE, GP.OUT_TIME, GP.IN_TIME, GP.REASON,
                               R.STATUS, GP.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK
                        " + fromSql + whereSql + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new GpListModel
            {
                REQUEST_ID   = r["REQUEST_ID"]?.ToString()  ?? "",
                EMPCD        = r["EMPCD"]?.ToString()        ?? "",
                EMP_NAME     = r["EMP_NAME"]?.ToString(),
                DEPT_ID      = r["DEPT_ID"]?.ToString(),
                DEPT_NAME    = r["DEPT_NAME"]?.ToString(),
                LINE_ID      = r["LINE_ID"]?.ToString(),
                LINE_NAME    = r["LINE_NAME"]?.ToString(),
                WORK_ID      = r["WORK_ID"]?.ToString(),
                WORK_NAME    = r["WORK_NAME"]?.ToString(),
                GP_TYPE      = r["GP_TYPE"]?.ToString(),
                OUT_TIME     = r["OUT_TIME"]     == DBNull.Value ? null : Convert.ToDateTime(r["OUT_TIME"]),
                IN_TIME      = r["IN_TIME"]      == DBNull.Value ? null : Convert.ToDateTime(r["IN_TIME"]),
                REASON       = r["REASON"]?.ToString(),
                STATUS       = r["STATUS"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                APPROVER_NAME  = r["APPROVER_NAME"]?.ToString(),
                FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK         = r["REMARK"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/GatePass/hr?status=&search=&...&page=  (toàn công ty, HR only)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("hr")]
    public async Task<IActionResult> GetHRList(
        string? status    = null,
        string? search    = null,
        string? dept_id   = null,
        string? line_id   = null,
        string? work_id   = null,
        string? date_from = null,
        string? date_to   = null,
        int     page      = 1,
        int     page_size = 50)
    {
        try
        {
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today;
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today;

            string fromSql = @"
                FROM HRMS.HR_GATEPASS_REQUEST GP
                JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID  = GP.REQUEST_ID
                JOIN HRMS.ECM100    EC  ON EC.EMPCD       = GP.EMPCD
                LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100 AP ON AP.EMPCD = R.FINAL_APPROVER";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS        = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(GP.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD       = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD        = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD        = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };

            string sqlSummary = @"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                " + fromSql + whereSql;

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new GpSummary
            {
                TOTAL    = r["TOTAL"]    == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING  = r["PENDING"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED = r["APPROVED"] == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED = r["REJECTED"] == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new GpSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<GpListModel>() });

            string sqlData = @"
                SELECT /*+ FIRST_ROWS(" + page_size + @") */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS, T.EMPCD) RN
                    FROM (
                        SELECT GP.REQUEST_ID, GP.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               GP.GP_TYPE, GP.OUT_TIME, GP.IN_TIME, GP.REASON,
                               R.STATUS, GP.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK
                        " + fromSql + whereSql + @"
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new GpListModel
            {
                REQUEST_ID   = r["REQUEST_ID"]?.ToString()  ?? "",
                EMPCD        = r["EMPCD"]?.ToString()        ?? "",
                EMP_NAME     = r["EMP_NAME"]?.ToString(),
                DEPT_ID      = r["DEPT_ID"]?.ToString(),
                DEPT_NAME    = r["DEPT_NAME"]?.ToString(),
                LINE_ID      = r["LINE_ID"]?.ToString(),
                LINE_NAME    = r["LINE_NAME"]?.ToString(),
                WORK_ID      = r["WORK_ID"]?.ToString(),
                WORK_NAME    = r["WORK_NAME"]?.ToString(),
                GP_TYPE      = r["GP_TYPE"]?.ToString(),
                OUT_TIME     = r["OUT_TIME"]     == DBNull.Value ? null : Convert.ToDateTime(r["OUT_TIME"]),
                IN_TIME      = r["IN_TIME"]      == DBNull.Value ? null : Convert.ToDateTime(r["IN_TIME"]),
                REASON       = r["REASON"]?.ToString(),
                STATUS       = r["STATUS"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                APPROVER_NAME  = r["APPROVER_NAME"]?.ToString(),
                FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK         = r["REMARK"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: parse "HH:mm" + date → DateTime
    // ─────────────────────────────────────────────────────────────────────────
    private static DateTime? ParseDateTime(DateTime regDate, string? timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return null;
        var parts = timeStr.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
            return regDate.Date.AddHours(h).AddMinutes(m);
        return null;
    }
}
