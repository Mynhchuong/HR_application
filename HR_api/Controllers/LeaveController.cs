using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Leave;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly Helpers.NotificationHelper _notiHelper;

    public LeaveController(OracleService oracleService, Helpers.NotificationHelper notiHelper)
    {
        _oracleService = oracleService;
        _notiHelper    = notiHelper;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/submit  — worker submits SELF leave
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] LeaveSubmitRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            if (string.IsNullOrEmpty(model.LEAVE_TYPE))
                return Ok(new { success = false, message = "Thiếu loại nghỉ phép" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate))
                return Ok(new { success = false, message = "Ngày bắt đầu không hợp lệ" });

            if (!DateTime.TryParse(model.TO_DATE, out DateTime toDate))
                return Ok(new { success = false, message = "Ngày kết thúc không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            var empRows = await _oracleService.ExecuteQueryAsync(
                "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["CNAME"]?.ToString(),
                new OracleParameter("EMPCD", model.EMPCD));

            string empName = empRows.FirstOrDefault() ?? "";

            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_REQUEST (REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE)
                VALUES ('LEAVE', :EMPCD, :EMP_NAME, SYSDATE, 'PENDING', :EMPCD1, SYSDATE)",
                new OracleParameter("EMPCD",    model.EMPCD),
                new OracleParameter("EMP_NAME", empName),
                new OracleParameter("EMPCD1",   model.EMPCD));

            var reqIds = await _oracleService.ExecuteQueryAsync(@"
                SELECT REQUEST_ID FROM (
                    SELECT REQUEST_ID FROM HRMS.HR_REQUEST
                    WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'LEAVE' AND STATUS = 'PENDING'
                      AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE)
                    ORDER BY CREATED_DATE DESC
                ) WHERE ROWNUM = 1",
                r => r["REQUEST_ID"]?.ToString(),
                new OracleParameter("EMPCD", model.EMPCD));

            if (reqIds.Count == 0 || string.IsNullOrEmpty(reqIds[0]))
                return Ok(new { success = false, message = "Lỗi tạo REQUEST_ID" });

            string requestId = reqIds[0]!;

            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_LEAVE_REQUEST
                    (REQUEST_ID, EMPCD, LEAVE_TYPE, FROM_DATE, TO_DATE, TOTAL_DAYS, REASON, CREATED_DATE, SOURCE)
                VALUES (:REQUEST_ID, :EMPCD, :LEAVE_TYPE, :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'SELF')",
                new OracleParameter("REQUEST_ID", requestId),
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("LEAVE_TYPE", model.LEAVE_TYPE),
                new OracleParameter("FROM_DATE",  fromDate),
                new OracleParameter("TO_DATE",    toDate),
                new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

            return Ok(new { success = true, message = "Đăng ký nghỉ phép thành công", request_id = requestId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/my-requests?empcd=&source=SELF|ASSIGNED&page=&date_from=&date_to=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-requests")]
    public async Task<IActionResult> GetMyRequests(
        string  empcd,
        string? source    = null,
        int     page      = 1,
        int     page_size = 20,
        string? date_from = null,
        string? date_to   = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-3);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(3);

            var srcFlagVal = string.IsNullOrEmpty(source) ? (object)DBNull.Value : "Y";
            var srcVal     = string.IsNullOrEmpty(source) ? (object)DBNull.Value : source;

            string countSql = @"
                SELECT COUNT(*) CNT
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.EMPCD = :EMPCD
                  AND R.REQUEST_TYPE = 'LEAVE'
                  AND L.FROM_DATE >= :D_FROM AND L.FROM_DATE <= :D_TO
                  AND (:SRC_FLAG IS NULL OR L.SOURCE = :SRC_VAL)";

            var totalRows = await _oracleService.ExecuteQueryAsync(countSql,
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("EMPCD",    empcd),
                new OracleParameter("D_FROM",   dfrom.Date),
                new OracleParameter("D_TO",     dto.Date),
                new OracleParameter("SRC_FLAG", OracleDbType.Varchar2) { Value = srcFlagVal },
                new OracleParameter("SRC_VAL",  OracleDbType.Varchar2) { Value = srcVal });

            int total = totalRows.FirstOrDefault();

            string dataSql = @"
                SELECT * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS,
                               L.REASON, L.SOURCE, L.CONFIRM_STATUS, L.CONFIRM_DATE,
                               R.STATUS, R.REMARK, L.CREATED_DATE
                        FROM HRMS.HR_LEAVE_REQUEST L
                        JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                        WHERE L.EMPCD = :EMPCD1
                          AND R.REQUEST_TYPE = 'LEAVE'
                          AND L.FROM_DATE >= :D_FROM1 AND L.FROM_DATE <= :D_TO1
                          AND (:SRC_FLAG1 IS NULL OR L.SOURCE = :SRC_VAL1)
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var list = await _oracleService.ExecuteQueryAsync(dataSql, r => new LeaveMyRequestModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()    ?? "",
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]        == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]     == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE   = r["CONFIRM_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                STATUS         = r["STATUS"]?.ToString(),
                REMARK         = r["REMARK"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                IS_EDITABLE    = r["STATUS"]?.ToString() == "PENDING" && r["SOURCE"]?.ToString() == "SELF"
            },
            new OracleParameter("EMPCD1",    empcd),
            new OracleParameter("D_FROM1",   dfrom.Date),
            new OracleParameter("D_TO1",     dto.Date),
            new OracleParameter("SRC_FLAG1", OracleDbType.Varchar2) { Value = srcFlagVal },
            new OracleParameter("SRC_VAL1",  OracleDbType.Varchar2) { Value = srcVal },
            new OracleParameter("R_MIN", offset),
            new OracleParameter("R_MAX", maxRn));

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
    // PUT /apiHR/Leave/update
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("update")]
    public async Task<IActionResult> Update([FromBody] LeaveUpdateRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin cập nhật" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate) ||
                !DateTime.TryParse(model.TO_DATE,   out DateTime toDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            var statusRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'SELF' AND ROWNUM = 1",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            if (statusRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (statusRows[0] != "PENDING")
                return Ok(new { success = false, message = "Chỉ có thể sửa yêu cầu đang chờ duyệt" });

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET LEAVE_TYPE = :LEAVE_TYPE, FROM_DATE = :FROM_DATE, TO_DATE = :TO_DATE,
                    TOTAL_DAYS = :TOTAL_DAYS, REASON = :REASON,
                    UPDATED_BY = :UPDATED_BY, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("LEAVE_TYPE",  model.LEAVE_TYPE),
                new OracleParameter("FROM_DATE",   fromDate),
                new OracleParameter("TO_DATE",     toDate),
                new OracleParameter("TOTAL_DAYS",  model.TOTAL_DAYS),
                new OracleParameter("REASON",      (object?)model.REASON ?? DBNull.Value),
                new OracleParameter("UPDATED_BY",  model.EMPCD),
                new OracleParameter("REQUEST_ID",  model.REQUEST_ID),
                new OracleParameter("EMPCD",       model.EMPCD));

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST SET UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            return Ok(new { success = true, message = "Cập nhật đơn nghỉ phép thành công" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /apiHR/Leave/delete?request_id=&empcd=
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
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'SELF' AND ROWNUM = 1",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            if (statusRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (statusRows[0] != "PENDING")
                return Ok(new { success = false, message = "Chỉ có thể xoá yêu cầu đang chờ duyệt" });

            await _oracleService.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_LEAVE_REQUEST WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            await _oracleService.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_REQUEST WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            return Ok(new { success = true, message = "Đã xoá đơn nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/confirm — worker confirms ASSIGNED leave
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] LeaveConfirmRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin xác nhận" });

            var infoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS, R.CREATED_BY FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'ASSIGNED' AND ROWNUM = 1",
                r => new { Status = r["STATUS"]?.ToString(), Assigner = r["CREATED_BY"]?.ToString() },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            var info = infoRows.FirstOrDefault();
            if (info == null)
                return Ok(new { success = false, message = "Không tìm thấy lịch nghỉ được sắp" });

            if (info.Status != "ASSIGNED")
                return Ok(new { success = false, message = "Lịch nghỉ đã được xử lý rồi" });

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST
                SET STATUS = 'CONFIRMED', UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET CONFIRM_STATUS = 'CONFIRMED', CONFIRM_DATE = SYSDATE,
                    UPDATED_BY = :UPDATED_BY, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("UPDATED_BY",  model.EMPCD),
                new OracleParameter("REQUEST_ID",  model.REQUEST_ID),
                new OracleParameter("EMPCD",       model.EMPCD));

            if (!string.IsNullOrEmpty(info.Assigner))
            {
                _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                {
                    TITLE       = "Công nhân đã xác nhận lịch nghỉ",
                    BODY        = $"Nhân viên {model.EMPCD} đã xác nhận lịch nghỉ phép được sắp.",
                    NOTI_TYPE   = "EMPCD",
                    TARGET_VAL  = info.Assigner,
                    LINK_ACTION = "LEAVE_TEAM",
                    CREATED_BY  = model.EMPCD
                });
            }

            return Ok(new { success = true, message = "Đã xác nhận lịch nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/worker-reject — worker rejects ASSIGNED leave
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("worker-reject")]
    public async Task<IActionResult> WorkerReject([FromBody] LeaveConfirmRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin từ chối" });

            var infoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS, R.CREATED_BY FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'ASSIGNED' AND ROWNUM = 1",
                r => new { Status = r["STATUS"]?.ToString(), Assigner = r["CREATED_BY"]?.ToString() },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            var info = infoRows.FirstOrDefault();
            if (info == null)
                return Ok(new { success = false, message = "Không tìm thấy lịch nghỉ được sắp" });

            if (info.Status != "ASSIGNED")
                return Ok(new { success = false, message = "Lịch nghỉ đã được xử lý rồi" });

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST
                SET STATUS = 'WORKER_REJECTED', UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET CONFIRM_STATUS = 'WORKER_REJECTED', CONFIRM_DATE = SYSDATE,
                    UPDATED_BY = :UPDATED_BY, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("UPDATED_BY",  model.EMPCD),
                new OracleParameter("REQUEST_ID",  model.REQUEST_ID),
                new OracleParameter("EMPCD",       model.EMPCD));

            if (!string.IsNullOrEmpty(info.Assigner))
            {
                _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                {
                    TITLE       = "Công nhân từ chối lịch nghỉ",
                    BODY        = $"Nhân viên {model.EMPCD} đã từ chối lịch nghỉ phép được sắp.",
                    NOTI_TYPE   = "EMPCD",
                    TARGET_VAL  = info.Assigner,
                    LINK_ACTION = "LEAVE_TEAM",
                    CREATED_BY  = model.EMPCD
                });
            }

            return Ok(new { success = true, message = "Đã từ chối lịch nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/approval-list — Supervisor/Manager/Deputy/Expat duyệt SELF leave
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("approval-list")]
    public async Task<IActionResult> GetApprovalList(
        string  approver_empcd,
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
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", approver_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "SV");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(2);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410    B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_USERS  UR ON UR.EMPCD = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES  RR ON RR.ID    = UR.ROLE_ID";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE = 'SELF'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  " + scopeFilter.SqlClause + @"
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)";

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

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                {fromSql}{whereSql}";

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL    = r["TOTAL"]    == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING  = r["PENDING"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED = r["APPROVED"] == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED = r["REJECTED"] == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS, T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_DATE, R.REMARK, RR.ROLE_NAME REQUESTER_ROLE
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveListModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()   ?? "",
                EMPCD          = r["EMPCD"]?.ToString()         ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_ID        = r["DEPT_ID"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_ID        = r["LINE_ID"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_ID        = r["WORK_ID"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK         = r["REMARK"]?.ToString(),
                REQUESTER_ROLE = r["REQUESTER_ROLE"]?.ToString()
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
    // POST /apiHR/Leave/approve
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] LeaveApproveRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.APPROVER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin duyệt" });

            var approverRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.APPROVER_EMPCD));

            string? approverRole = approverRoleRows.FirstOrDefault();

            if (!Helpers.RoleHierarchyHelper.HasApprovalPermission(approverRole))
                return Ok(new { success = false, message = "Bạn không có quyền phê duyệt nghỉ phép" });

            var requestInfoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, RR.ROLE_NAME REQ_ROLE
                FROM HRMS.HR_LEAVE_REQUEST L
                LEFT JOIN HRMS.HR_USERS UR ON UR.EMPCD = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID    = UR.ROLE_ID
                WHERE L.REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new { Empcd = r["EMPCD"]?.ToString(), Role = r["REQ_ROLE"]?.ToString() },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var requestInfo = requestInfoRows.FirstOrDefault();
            if (requestInfo == null)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (requestInfo.Empcd == model.APPROVER_EMPCD)
                return Ok(new { success = false, message = "Không thể tự duyệt đơn của mình" });

            if (!Helpers.RoleHierarchyHelper.CanApprove(approverRole, requestInfo.Role))
                return Ok(new { success = false, message = $"Phiếu này cần {Helpers.RoleHierarchyHelper.RequiredApproverName(requestInfo.Role)} phê duyệt." });

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST
                SET STATUS = 'APPROVED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
                    REMARK = :REMARK_VAL, UPDATED_BY = :APPROVER1, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND STATUS = 'PENDING'",
                new OracleParameter("APPROVER",   model.APPROVER_EMPCD),
                new OracleParameter("REMARK_VAL", (object?)model.COMMENT ?? DBNull.Value),
                new OracleParameter("APPROVER1",  model.APPROVER_EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy hoặc đã được xử lý rồi" });

            // TODO: ERP insert — SQL sẽ được cung cấp sau

            if (!string.IsNullOrEmpty(requestInfo.Empcd))
            {
                _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                {
                    TITLE       = "Đơn nghỉ phép được duyệt",
                    BODY        = "Đơn xin nghỉ phép của bạn đã được phê duyệt.",
                    NOTI_TYPE   = "EMPCD",
                    TARGET_VAL  = requestInfo.Empcd,
                    LINK_ACTION = "LEAVE_MY",
                    CREATED_BY  = model.APPROVER_EMPCD
                });
            }

            return Ok(new { success = true, message = "Đã duyệt đơn nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/reject
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] LeaveApproveRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.APPROVER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin từ chối" });

            var rejectRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.APPROVER_EMPCD));

            string? rejectRole = rejectRoleRows.FirstOrDefault();

            if (!Helpers.RoleHierarchyHelper.HasApprovalPermission(rejectRole))
                return Ok(new { success = false, message = "Bạn không có quyền từ chối nghỉ phép" });

            var rejectInfoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, RR.ROLE_NAME REQ_ROLE
                FROM HRMS.HR_LEAVE_REQUEST L
                LEFT JOIN HRMS.HR_USERS UR ON UR.EMPCD = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID    = UR.ROLE_ID
                WHERE L.REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new { Empcd = r["EMPCD"]?.ToString(), Role = r["REQ_ROLE"]?.ToString() },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var rejectInfo = rejectInfoRows.FirstOrDefault();
            if (rejectInfo == null)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (rejectInfo.Empcd == model.APPROVER_EMPCD)
                return Ok(new { success = false, message = "Không thể tự từ chối đơn của mình" });

            if (!Helpers.RoleHierarchyHelper.CanApprove(rejectRole, rejectInfo.Role))
                return Ok(new { success = false, message = $"Phiếu này cần {Helpers.RoleHierarchyHelper.RequiredApproverName(rejectInfo.Role)} xử lý." });

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

            if (!string.IsNullOrEmpty(rejectInfo.Empcd))
            {
                _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                {
                    TITLE       = "Đơn nghỉ phép bị từ chối",
                    BODY        = "Đơn xin nghỉ phép của bạn đã bị từ chối.",
                    NOTI_TYPE   = "EMPCD",
                    TARGET_VAL  = rejectInfo.Empcd,
                    LINK_ACTION = "LEAVE_MY",
                    CREATED_BY  = model.APPROVER_EMPCD
                });
            }

            return Ok(new { success = true, message = "Đã từ chối đơn nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/assign — Supervisor/Deputy/Manager sắp lịch AL cho worker(s)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromBody] LeaveAssignRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.ASSIGNER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin người sắp lịch" });

            if (model.TARGET_EMPCDS == null || model.TARGET_EMPCDS.Count == 0)
                return Ok(new { success = false, message = "Chưa chọn nhân viên" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate) ||
                !DateTime.TryParse(model.TO_DATE,   out DateTime toDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            var assignerRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ASSIGNER_EMPCD));

            string? assignerRole = assignerRoleRows.FirstOrDefault();

            if (!Helpers.RoleHierarchyHelper.HasApprovalPermission(assignerRole) ||
                string.Equals(assignerRole, "Expat", StringComparison.OrdinalIgnoreCase))
                return Ok(new { success = false, message = "Bạn không có quyền sắp lịch nghỉ" });

            var results   = new List<object>();
            int successCt = 0;

            foreach (var targetEmpcd in model.TARGET_EMPCDS)
            {
                try
                {
                    var empRows = await _oracleService.ExecuteQueryAsync(
                        "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                        r => r["CNAME"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd));

                    string empName = empRows.FirstOrDefault() ?? "";

                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_REQUEST
                            (REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE)
                        VALUES ('LEAVE', :EMPCD, :EMP_NAME, SYSDATE, 'ASSIGNED', :CREATED_BY, SYSDATE)",
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("EMP_NAME",   empName),
                        new OracleParameter("CREATED_BY", model.ASSIGNER_EMPCD));

                    var reqIds = await _oracleService.ExecuteQueryAsync(@"
                        SELECT REQUEST_ID FROM (
                            SELECT REQUEST_ID FROM HRMS.HR_REQUEST
                            WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'LEAVE' AND STATUS = 'ASSIGNED'
                              AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE)
                            ORDER BY CREATED_DATE DESC
                        ) WHERE ROWNUM = 1",
                        r => r["REQUEST_ID"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd));

                    if (reqIds.Count == 0 || string.IsNullOrEmpty(reqIds[0]))
                    {
                        results.Add(new { empcd = targetEmpcd, success = false, message = "Lỗi tạo REQUEST_ID" });
                        continue;
                    }

                    string requestId = reqIds[0]!;

                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_LEAVE_REQUEST
                            (REQUEST_ID, EMPCD, LEAVE_TYPE, FROM_DATE, TO_DATE, TOTAL_DAYS, REASON, CREATED_DATE, SOURCE)
                        VALUES (:REQUEST_ID, :EMPCD, 'AL', :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'ASSIGNED')",
                        new OracleParameter("REQUEST_ID", requestId),
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("FROM_DATE",  fromDate),
                        new OracleParameter("TO_DATE",    toDate),
                        new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                        new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

                    _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
                    {
                        TITLE       = "Bạn được sắp lịch nghỉ phép",
                        BODY        = $"Lịch nghỉ phép năm {fromDate:dd/MM/yyyy} – {toDate:dd/MM/yyyy} đã được sắp. Vui lòng xác nhận.",
                        NOTI_TYPE   = "EMPCD",
                        TARGET_VAL  = targetEmpcd,
                        LINK_ACTION = "LEAVE_ASSIGNED",
                        CREATED_BY  = model.ASSIGNER_EMPCD
                    });

                    results.Add(new { empcd = targetEmpcd, success = true, request_id = requestId });
                    successCt++;
                }
                catch (Exception ex)
                {
                    results.Add(new { empcd = targetEmpcd, success = false, message = ex.Message });
                }
            }

            return Ok(new
            {
                success = successCt > 0,
                message = $"Đã sắp lịch cho {successCt}/{model.TARGET_EMPCDS.Count} nhân viên",
                results
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/team-schedule?approver_empcd=&month=&year=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("team-schedule")]
    public async Task<IActionResult> GetTeamSchedule(
        string  approver_empcd,
        int?    month = null,
        int?    year  = null)
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

            int m = month ?? DateTime.Today.Month;
            int y = year  ?? DateTime.Today.Year;
            DateTime dfrom = new DateTime(y, m, 1);
            DateTime dto   = dfrom.AddMonths(1).AddDays(-1);

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "TS");

            string sql = $@"
                SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                       L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS,
                       R.STATUS, L.CONFIRM_STATUS,
                       B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE <= :D_TO AND L.TO_DATE >= :D_FROM
                  {scopeFilter.SqlClause}
                ORDER BY L.FROM_DATE, L.EMPCD";

            var p = new List<OracleParameter>
            {
                new OracleParameter("D_FROM", OracleDbType.Date) { Value = dfrom },
                new OracleParameter("D_TO",   OracleDbType.Date) { Value = dto }
            };
            p.AddRange(scopeFilter.Params);

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new LeaveScheduleModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()  ?? "",
                EMPCD          = r["EMPCD"]?.ToString()        ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString()
            }, p.ToArray());

            return Ok(new { success = true, month = m, year = y, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/hr-list — HR xem toàn công ty
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("hr-list")]
    public async Task<IActionResult> GetHRList(
        string? status    = null,
        string? source    = null,
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

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(2);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410    B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100    AP ON AP.EMPCD  = R.FINAL_APPROVER";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRC_FLAG  IS NULL OR L.SOURCE       = :SRC_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("SRC_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(source)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRC_VAL",   OracleDbType.Varchar2) { Value = (object?)source  ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                {fromSql}{whereSql}";

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL    = r["TOTAL"]    == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING  = r["PENDING"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED = r["APPROVED"] == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED = r["REJECTED"] == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS, T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveListModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()   ?? "",
                EMPCD          = r["EMPCD"]?.ToString()         ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_ID        = r["DEPT_ID"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_ID        = r["LINE_ID"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_ID        = r["WORK_ID"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
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
}
