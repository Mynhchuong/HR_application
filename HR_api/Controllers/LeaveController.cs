using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Leave;
using HR_api.Services;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly NotificationService _notiSvc;

    public LeaveController(OracleService oracleService, NotificationService notiSvc)
    {
        _oracleService = oracleService;
        _notiSvc       = notiSvc;
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
                    SELECT T.*, ROW_NUMBER() OVER (
                        ORDER BY
                            CASE WHEN T.FROM_DATE >= TRUNC(SYSDATE) THEN 0 ELSE 1 END ASC,
                            CASE WHEN T.FROM_DATE >= TRUNC(SYSDATE) THEN T.FROM_DATE END ASC,
                            T.FROM_DATE DESC
                    ) RN
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
    // POST /apiHR/Leave/confirm — worker acknowledges ASSIGNED leave notification
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] LeaveConfirmRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin xác nhận" });

            var infoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.CONFIRM_STATUS, R.CREATED_BY FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'ASSIGNED' AND ROWNUM = 1",
                r => new {
                    ConfirmStatus = r["CONFIRM_STATUS"]?.ToString(),
                    Assigner      = r["CREATED_BY"]?.ToString()
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            var info = infoRows.FirstOrDefault();
            if (info == null)
                return Ok(new { success = false, message = "Không tìm thấy lịch nghỉ được sắp" });

            if (info.ConfirmStatus == "CONFIRMED")
                return Ok(new { success = false, message = "Đã nhận thông báo rồi" });

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
                _notiSvc.LeaveAcknowledged(info.Assigner, model.EMPCD);
            }

            return Ok(new { success = true, message = "Đã nhận thông báo lịch nghỉ phép" });
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
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY CASE WHEN T.STATUS = 'PENDING' AND T.FROM_DATE >= TRUNC(SYSDATE) THEN 0 WHEN T.STATUS = 'PENDING' AND T.FROM_DATE < TRUNC(SYSDATE) THEN 2 ELSE 1 END,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.FROM_DATE DESC) RN
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

            // ERP: call SP_015_NEW after approval
            var ldRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT FROM_DATE, TO_DATE, LEAVE_TYPE FROM HRMS.HR_LEAVE_REQUEST
                WHERE REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new {
                    FromDate  = Convert.ToDateTime(r["FROM_DATE"]),
                    ToDate    = Convert.ToDateTime(r["TO_DATE"]),
                    LeaveType = r["LEAVE_TYPE"]?.ToString()
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var ld = ldRows.FirstOrDefault();
            if (ld != null && !string.IsNullOrEmpty(requestInfo.Empcd))
            {
                static string leaveTypeName(string? code) => code switch
                {
                    "SL"  => "Nghỉ bệnh",
                    "NPL" => "Không lương",
                    "OTH" => "Khác",
                    _     => code ?? ""
                };

                string erpCd     = ld.LeaveType switch { "AL" => "PN", "CL" => "BH", _ => "CP" };
                string erpRemark = erpCd == "CP" ? "VR " + leaveTypeName(ld.LeaveType) : "VR";

                var erpHolidays = (await _oracleService.ExecuteQueryAsync(
                    @"SELECT TRUNC(HUILDAY) AS HUILDAY FROM HRMS.EAM800
                      WHERE TRUNC(HUILDAY) BETWEEN TRUNC(:FROM_DATE) AND TRUNC(:TO_DATE)",
                    r => Convert.ToDateTime(r["HUILDAY"]).Date,
                    new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = ld.FromDate },
                    new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = ld.ToDate }
                )).ToHashSet();

                string? erpError = null;
                try
                {
                    for (var day = ld.FromDate.Date; day <= ld.ToDate.Date; day = day.AddDays(1))
                    {
                        if (erpHolidays.Contains(day)) continue;
                        await _oracleService.ExecuteProcedureAsync("HRMS.SP_015_NEW",
                            new OracleParameter("AS_EMPCD",   requestInfo.Empcd),
                            new OracleParameter("AS_LEAVECD", erpCd),
                            new OracleParameter { ParameterName = "AD_ST_DAT", OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.Date, Value = day },
                            new OracleParameter { ParameterName = "AD_ED_DAT", OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.Date, Value = day },
                            new OracleParameter("AS_IN_ID",   model.APPROVER_EMPCD),
                            new OracleParameter("AS_REMAR",   erpRemark));
                    }

                    await _oracleService.ExecuteNonQueryAsync(
                        "UPDATE HRMS.EFM410 SET APPROVED_BY = :APPROVED_BY WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FR_DAT AND :TO_DAT",
                        new OracleParameter("APPROVED_BY", model.APPROVER_EMPCD),
                        new OracleParameter("EMPCD",       requestInfo.Empcd),
                        new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = ld.FromDate },
                        new OracleParameter { ParameterName = "TO_DAT", OracleDbType = OracleDbType.Date, Value = ld.ToDate });
                }
                catch (Exception ex) { erpError = ex.Message; }

                if (erpError != null)
                    return Ok(new { success = true, message = "Đã duyệt đơn nghỉ phép", erpError });
            }

            if (!string.IsNullOrEmpty(requestInfo.Empcd))
            {
                _notiSvc.LeaveApproved(requestInfo.Empcd, model.APPROVER_EMPCD);
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
                _notiSvc.LeaveRejected(rejectInfo.Empcd, model.APPROVER_EMPCD);
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

            if (fromDate.Date <= DateTime.Today)
                return Ok(new { success = false, message = "Chỉ được sắp lịch từ ngày mai trở đi" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            var validLeaveTypes = new[] { "AL", "CL", "SL", "NPL", "OTH" };
            if (string.IsNullOrEmpty(model.LEAVE_TYPE) || !validLeaveTypes.Contains(model.LEAVE_TYPE))
                model.LEAVE_TYPE = "AL";

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
                        VALUES (:REQUEST_ID, :EMPCD, :LEAVE_TYPE, :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'ASSIGNED')",
                        new OracleParameter("REQUEST_ID", requestId),
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("LEAVE_TYPE", model.LEAVE_TYPE),
                        new OracleParameter("FROM_DATE",  fromDate),
                        new OracleParameter("TO_DATE",    toDate),
                        new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                        new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

                    // ERP: call SP_015_NEW immediately after assign (no worker confirm needed)
                    var erpLeaveNames = new Dictionary<string,string>
                    {
                        ["SL"] = "Nghỉ bệnh", ["NPL"] = "Không lương", ["OTH"] = "Khác"
                    };
                    string erpCd     = model.LEAVE_TYPE switch { "AL" => "PN", "CL" => "BH", _ => "CP" };
                    string erpRemark = erpCd == "CP"
                        ? "ASSIGNED " + erpLeaveNames.GetValueOrDefault(model.LEAVE_TYPE, model.LEAVE_TYPE)
                        : "ASSIGNED";
                    var erpHolidays = (await _oracleService.ExecuteQueryAsync(
                        @"SELECT TRUNC(HUILDAY) AS HUILDAY FROM HRMS.EAM800
                          WHERE TRUNC(HUILDAY) BETWEEN TRUNC(:FROM_DATE) AND TRUNC(:TO_DATE)",
                        r => Convert.ToDateTime(r["HUILDAY"]).Date,
                        new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = fromDate },
                        new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = toDate }
                    )).ToHashSet();
                    try
                    {
                        for (var day = fromDate.Date; day <= toDate.Date; day = day.AddDays(1))
                        {
                            if (erpHolidays.Contains(day)) continue;
                            await _oracleService.ExecuteProcedureAsync("HRMS.SP_015_NEW",
                                new OracleParameter("AS_EMPCD",   targetEmpcd),
                                new OracleParameter("AS_LEAVECD", erpCd),
                                new OracleParameter { ParameterName = "AD_ST_DAT", OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.Date, Value = day },
                                new OracleParameter { ParameterName = "AD_ED_DAT", OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.Date, Value = day },
                                new OracleParameter("AS_IN_ID",   model.ASSIGNER_EMPCD),
                                new OracleParameter("AS_REMAR",   erpRemark));
                        }

                        await _oracleService.ExecuteNonQueryAsync(
                            "UPDATE HRMS.EFM410 SET APPROVED_BY = :APPROVED_BY WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FR_DAT AND :TO_DAT",
                            new OracleParameter("APPROVED_BY", model.ASSIGNER_EMPCD),
                            new OracleParameter("EMPCD",       targetEmpcd),
                            new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = fromDate },
                            new OracleParameter { ParameterName = "TO_DAT", OracleDbType = OracleDbType.Date, Value = toDate });
                    }
                    catch { /* ERP failure không block assign */ }

                    var leaveTypeNames = new Dictionary<string,string>
                    {
                        ["AL"] = "Phép năm", ["CL"] = "BHXH", ["SL"] = "Nghỉ bệnh",
                        ["NPL"] = "Không lương", ["OTH"] = "Khác"
                    };
                    string leaveTypeName = leaveTypeNames.GetValueOrDefault(model.LEAVE_TYPE, model.LEAVE_TYPE);
                    _notiSvc.LeaveAssigned(targetEmpcd, model.ASSIGNER_EMPCD, leaveTypeName, fromDate, toDate);

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
                       B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                       CASE WHEN L.SOURCE = 'ASSIGNED' THEN CB.FULL_NAME ELSE AP.FULL_NAME END APPROVED_BY,
                       CASE WHEN L.SOURCE = 'ASSIGNED' THEN R.CREATED_DATE ELSE R.FINAL_DATE END APPROVED_DATE
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410   B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_USERS AP ON AP.EMPCD = R.FINAL_APPROVER
                LEFT JOIN HRMS.HR_USERS CB ON CB.EMPCD = R.CREATED_BY
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE <= :D_TO AND L.TO_DATE >= :D_FROM
                  AND (
                      (L.SOURCE = 'SELF'     AND R.STATUS = 'APPROVED') OR
                      (L.SOURCE = 'ASSIGNED')
                  )
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
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                APPROVED_BY    = r["APPROVED_BY"]?.ToString(),
                APPROVED_DATE  = r["APPROVED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["APPROVED_DATE"])
            }, p.ToArray());

            return Ok(new { success = true, month = m, year = y, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/my-assignments — Supervisor xem lịch mình đã sắp
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-assignments")]
    public async Task<IActionResult> GetMyAssignments(
        string  assigner_empcd,
        string? status    = null,
        string? search    = null,
        string? date_from = null,
        string? date_to   = null,
        int     page      = 1,
        int     page_size = 20)
    {
        try
        {
            if (string.IsNullOrEmpty(assigner_empcd))
                return Ok(new { success = false, message = "Thiếu mã người sắp lịch" });

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-3);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(3);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE       = 'ASSIGNED'
                  AND R.CREATED_BY   = :ASSIGNER
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("ASSIGNER",  OracleDbType.Varchar2) { Value = assigner_empcd },
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
            };

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN L.CONFIRM_STATUS IS NULL       THEN 1 ELSE 0 END) PENDING_CONFIRM,
                       SUM(CASE WHEN L.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) CONFIRMED
                {fromSql}{whereSql}";

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveAssignSummary
            {
                TOTAL           = r["TOTAL"]           == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING_CONFIRM = r["PENDING_CONFIRM"] == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING_CONFIRM"]),
                CONFIRMED       = r["CONFIRMED"]       == DBNull.Value ? 0 : Convert.ToInt32(r["CONFIRMED"])
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new LeaveAssignSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<object>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME,
                               L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CONFIRM_DATE, R.CREATED_DATE ASSIGN_DATE
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveAssignmentModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()    ?? "",
                EMPCD          = r["EMPCD"]?.ToString()          ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]     == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]       == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]    == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE   = r["CONFIRM_DATE"]  == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                ASSIGN_DATE    = r["ASSIGN_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["ASSIGN_DATE"]),
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
    // GET /apiHR/Leave/assignment-log — HR xem log toàn bộ việc sắp lịch
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("assignment-log")]
    public async Task<IActionResult> GetAssignmentLog(
        string? assigner_cd = null,
        string? search      = null,
        string? dept_id     = null,
        string? line_id     = null,
        string? work_id     = null,
        string? status      = null,
        string? date_from   = null,
        string? date_to     = null,
        int     page        = 1,
        int     page_size   = 50)
    {
        try
        {
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-3);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(3);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R   ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC  ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410 B   ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100 ASN ON ASN.EMPCD   = R.CREATED_BY";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE       = 'ASSIGNED'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR NVL(L.CONFIRM_STATUS,'ASSIGNED') = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)
                  AND (:ASN_FLAG  IS NULL OR R.CREATED_BY    = :ASN_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)      ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status      ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)      ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)      ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id)     ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id     ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id)     ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id     ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id)     ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id     ?? DBNull.Value },
                new OracleParameter("ASN_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(assigner_cd) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ASN_VAL",   OracleDbType.Varchar2) { Value = (object?)assigner_cd ?? DBNull.Value },
            };

            string sqlCount = $"SELECT COUNT(*) CNT {fromSql}{whereSql}";
            var totalRows = await _oracleService.ExecuteQueryAsync(sqlCount,
                r => Convert.ToInt32(r["CNT"]),
                baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            int total = totalRows.FirstOrDefault();

            if (total == 0)
                return Ok(new { success = true, total = 0, page, page_size, total_pages = 0, data = new List<object>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.ASSIGN_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CONFIRM_DATE,
                               R.CREATED_BY ASSIGNED_BY, ASN.CNAME ASSIGNER_NAME,
                               R.CREATED_DATE ASSIGN_DATE
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveAssignmentLogModel
            {
                REQUEST_ID    = r["REQUEST_ID"]?.ToString()    ?? "",
                EMPCD         = r["EMPCD"]?.ToString()          ?? "",
                EMP_NAME      = r["EMP_NAME"]?.ToString(),
                DEPT_ID       = r["DEPT_ID"]?.ToString(),
                DEPT_NAME     = r["DEPT_NAME"]?.ToString(),
                LINE_ID       = r["LINE_ID"]?.ToString(),
                LINE_NAME     = r["LINE_NAME"]?.ToString(),
                WORK_ID       = r["WORK_ID"]?.ToString(),
                WORK_NAME     = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE    = r["LEAVE_TYPE"]?.ToString(),
                FROM_DATE     = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE       = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS    = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON        = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]).ToString("yyyy-MM-ddTHH:mm:ss"),
                ASSIGNED_BY    = r["ASSIGNED_BY"]?.ToString(),
                ASSIGNER_NAME = r["ASSIGNER_NAME"]?.ToString(),
                ASSIGN_DATE   = r["ASSIGN_DATE"]  == DBNull.Value ? null : Convert.ToDateTime(r["ASSIGN_DATE"]),
            }, dataParams.ToArray());

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
    // GET /apiHR/Leave/annual-balance?approver_empcd=
    // Trả về phép năm (RECEIVE/USED/LEFT) cho toàn bộ nhân viên trong phạm vi
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("annual-balance")]
    public async Task<IActionResult> GetAnnualBalance(string approver_empcd)
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

            const string sql = @"
                WITH SCOPE_EMP AS (
                    SELECT DISTINCT EC.EMPCD
                    FROM HRMS.HR_USERS_DEPT UD
                    JOIN HRMS.ECM100 EC
                        ON EC.DEPTCD = UD.DEPTCD
                       AND EC.LINECD = UD.LINECD
                       AND EC.WORKCD = UD.WORKCD
                    WHERE UD.EMPCD = :APPROVER
                      AND EC.JEAJIKGB = 'Y'
                      AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                ),
                USED AS (
                    SELECT EMPCD,
                           COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                      AND EMPCD IN (SELECT EMPCD FROM SCOPE_EMP)
                    GROUP BY EMPCD
                ),
                ALLOC AS (
                    SELECT EMPCD, MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                      AND EMPCD IN (SELECT EMPCD FROM SCOPE_EMP)
                    GROUP BY EMPCD
                )
                SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                       B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME,
                       NVL(AL.RECEIVE_NUM, 0)                       AS RECEIVE_NUM,
                       NVL(U.USED_NUM, 0)                           AS USED_NUM,
                       NVL(AL.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0) AS LEFT_NUM
                FROM SCOPE_EMP SE
                JOIN HRMS.ECM100 EC  ON EC.EMPCD = SE.EMPCD
                LEFT JOIN HRMS.EAM410 B
                    ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN ALLOC AL ON AL.EMPCD = EC.EMPCD
                LEFT JOIN USED  U  ON U.EMPCD  = EC.EMPCD
                ORDER BY EC.CNAME";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                EMPCD       = r["EMPCD"]?.ToString()     ?? "",
                EMP_NAME    = r["EMP_NAME"]?.ToString()  ?? "",
                DEPT_NAME   = r["DEPT_NAME"]?.ToString(),
                LINE_NAME   = r["LINE_NAME"]?.ToString(),
                RECEIVE_NUM = r["RECEIVE_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["RECEIVE_NUM"]),
                USED_NUM    = r["USED_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["USED_NUM"]),
                LEFT_NUM    = r["LEFT_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"])
            }, new OracleParameter("APPROVER", approver_empcd));

            return Ok(new { success = true, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/my-balance?empcd= — Số ngày phép năm của 1 nhân viên
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-balance")]
    public async Task<IActionResult> GetMyBalance(string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            const string sql = @"
                WITH ALLOC AS (
                    SELECT MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE EMPCD = :EMPCD
                      AND SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                ),
                USED AS (
                    SELECT COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE EMPCD = :EMPCD2
                      AND TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                )
                SELECT NVL(A.RECEIVE_NUM, 0) AS RECEIVE_NUM,
                       NVL(U.USED_NUM, 0)    AS USED_NUM,
                       NVL(A.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0) AS LEFT_NUM
                FROM ALLOC A, USED U";

            var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                RECEIVE_NUM = r["RECEIVE_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["RECEIVE_NUM"]),
                USED_NUM    = r["USED_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["USED_NUM"]),
                LEFT_NUM    = r["LEFT_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"])
            },
            new OracleParameter("EMPCD",  empcd),
            new OracleParameter("EMPCD2", empcd));

            var row = rows.FirstOrDefault() ?? new { RECEIVE_NUM = 0, USED_NUM = 0, LEFT_NUM = 0 };
            return Ok(new { success = true, data = row });
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
                LEFT JOIN HRMS.ECM100    AP ON AP.EMPCD  = R.FINAL_APPROVER
                LEFT JOIN HRMS.HR_USERS  UR ON UR.EMPCD  = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES  RR ON RR.ID     = UR.ROLE_ID";

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
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED,
                       SUM(CASE WHEN R.STATUS = 'ASSIGNED' AND NVL(L.CONFIRM_STATUS,'ASSIGNED') NOT IN ('CONFIRMED','WORKER_REJECTED') THEN 1 ELSE 0 END) ASSIGNED_PENDING,
                       SUM(CASE WHEN L.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) ASSIGNED_CONFIRMED
                {fromSql}{whereSql}";

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL              = r["TOTAL"]              == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING            = r["PENDING"]            == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED           = r["APPROVED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED           = r["REJECTED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"]),
                ASSIGNED_PENDING   = r["ASSIGNED_PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_PENDING"]),
                ASSIGNED_CONFIRMED = r["ASSIGNED_CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_CONFIRMED"]),
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK,
                               RR.ROLE_NAME REQUESTER_ROLE
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
    // GET /apiHR/Leave/clerk — Thư ký xem nghỉ phép theo scope HR_USERS_DEPT
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("clerk")]
    public async Task<IActionResult> GetClerkList(
        string  clerk_empcd,
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
            if (string.IsNullOrWhiteSpace(clerk_empcd))
                return BadRequest(new { success = false, message = "Thiếu mã thư ký" });

            var hasScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :CE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CE", clerk_empcd));

            if (!hasScope.Any() || hasScope[0] == 0)
                return Ok(new { success = false, message = "Thư ký chưa được phân bộ phận" });

            var scopeFilter = OTScopeFilterHelper.ForScopeByTuple(clerk_empcd, empAlias: "EC", prefix: "CK");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(2);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410    B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100    AP ON AP.EMPCD  = R.FINAL_APPROVER
                LEFT JOIN HRMS.HR_USERS  UR ON UR.EMPCD  = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES  RR ON RR.ID     = UR.ROLE_ID";

            string whereSql = $@"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRC_FLAG  IS NULL OR L.SOURCE       = :SRC_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)
                  {scopeFilter.SqlClause}";

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
            baseParams.AddRange(scopeFilter.Params);

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED,
                       SUM(CASE WHEN R.STATUS = 'ASSIGNED' AND NVL(L.CONFIRM_STATUS,'ASSIGNED') NOT IN ('CONFIRMED','WORKER_REJECTED') THEN 1 ELSE 0 END) ASSIGNED_PENDING,
                       SUM(CASE WHEN L.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) ASSIGNED_CONFIRMED
                {fromSql}{whereSql}";

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL              = r["TOTAL"]              == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING            = r["PENDING"]            == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED           = r["APPROVED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED           = r["REJECTED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"]),
                ASSIGNED_PENDING   = r["ASSIGNED_PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_PENDING"]),
                ASSIGNED_CONFIRMED = r["ASSIGNED_CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_CONFIRMED"]),
            }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK,
                               RR.ROLE_NAME REQUESTER_ROLE
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
    // GET /apiHR/Leave/admin-emp-list — Toàn bộ NV + phép năm còn lại (Admin)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("admin-emp-list")]
    public async Task<IActionResult> GetAdminEmpList(
        string? search  = null,
        string? dept_id = null,
        string? line_id = null,
        string? work_id = null)
    {
        try
        {
            var whereParts = new List<string>
            {
                "EC.JEAJIKGB = 'Y'",
                "(EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))"
            };
            var parameters = new List<OracleParameter>();

            if (!string.IsNullOrEmpty(dept_id))
            {
                whereParts.Add("EC.DEPTCD = :DEPT_ID");
                parameters.Add(new OracleParameter("DEPT_ID", dept_id));
            }
            if (!string.IsNullOrEmpty(line_id))
            {
                whereParts.Add("EC.LINECD = :LINE_ID");
                parameters.Add(new OracleParameter("LINE_ID", line_id));
            }
            if (!string.IsNullOrEmpty(work_id))
            {
                whereParts.Add("EC.WORKCD = :WORK_ID");
                parameters.Add(new OracleParameter("WORK_ID", work_id));
            }
            if (!string.IsNullOrEmpty(search))
            {
                whereParts.Add("(UPPER(EC.CNAME) LIKE '%' || UPPER(:SEARCH) || '%' OR EC.EMPCD LIKE '%' || :SEARCH2 || '%')");
                parameters.Add(new OracleParameter("SEARCH",  search));
                parameters.Add(new OracleParameter("SEARCH2", search));
            }

            string whereClause = string.Join(" AND ", whereParts);

            string sql = $@"
                WITH ALLOC AS (
                    SELECT EMPCD, MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                    GROUP BY EMPCD
                ),
                USED AS (
                    SELECT EMPCD,
                           COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                    GROUP BY EMPCD
                )
                SELECT EC.EMPCD,
                       EC.CNAME  AS EMP_NAME,
                       EC.DEPTCD AS DEPT_ID,
                       EA.DEPTNM AS DEPT_NAME,
                       EC.LINECD AS LINE_ID,
                       EA.TEAMNM AS LINE_NAME,
                       EC.WORKCD AS WORK_ID,
                       EA.WORKNM AS WORK_NAME,
                       NVL(AL.RECEIVE_NUM, 0)                        AS RECEIVE_NUM,
                       NVL(U.USED_NUM, 0)                            AS USED_NUM,
                       NVL(AL.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0)  AS LEFT_NUM
                FROM HRMS.ECM100 EC
                LEFT JOIN HRMS.EAM410 EA
                    ON EA.DEPTCD = EC.DEPTCD AND EA.LINECD = EC.LINECD AND EA.WORKCD = EC.WORKCD
                LEFT JOIN ALLOC AL ON AL.EMPCD = EC.EMPCD
                LEFT JOIN USED  U  ON U.EMPCD  = EC.EMPCD
                WHERE {whereClause}
                ORDER BY EA.DEPTNM, EA.TEAMNM, EA.WORKNM, EC.CNAME";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                EMPCD       = r["EMPCD"]?.ToString()    ?? "",
                EMP_NAME    = r["EMP_NAME"]?.ToString() ?? "",
                DEPT_ID     = r["DEPT_ID"]?.ToString(),
                DEPT_NAME   = r["DEPT_NAME"]?.ToString(),
                LINE_ID     = r["LINE_ID"]?.ToString(),
                LINE_NAME   = r["LINE_NAME"]?.ToString(),
                WORK_ID     = r["WORK_ID"]?.ToString(),
                WORK_NAME   = r["WORK_NAME"]?.ToString(),
                RECEIVE_NUM = r["RECEIVE_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["RECEIVE_NUM"]),
                USED_NUM    = r["USED_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["USED_NUM"]),
                LEFT_NUM    = r["LEFT_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"])
            }, parameters.ToArray());

            return Ok(new { success = true, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/admin-assign — Admin sắp lịch nghỉ toàn công ty
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("admin-assign")]
    public async Task<IActionResult> AdminAssign([FromBody] LeaveAssignRequest model)
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

            if (fromDate.Date <= DateTime.Today)
                return Ok(new { success = false, message = "Chỉ được sắp lịch từ ngày mai trở đi" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            var validLeaveTypes = new[] { "AL", "CL", "SL", "NPL", "OTH" };
            if (string.IsNullOrEmpty(model.LEAVE_TYPE) || !validLeaveTypes.Contains(model.LEAVE_TYPE))
                model.LEAVE_TYPE = "AL";

            var assignerRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ASSIGNER_EMPCD));

            string? assignerRole = assignerRoleRows.FirstOrDefault();
            if (!string.Equals(assignerRole, "Admin", StringComparison.OrdinalIgnoreCase))
                return Ok(new { success = false, message = "Chỉ Admin mới có quyền sắp lịch toàn công ty" });

            var leaveTypeNames = new Dictionary<string, string>
            {
                ["AL"] = "Phép năm", ["CL"] = "BHXH", ["SL"] = "Nghỉ bệnh",
                ["NPL"] = "Không lương", ["OTH"] = "Khác"
            };
            var erpLeaveNames = new Dictionary<string, string>
            {
                ["SL"] = "Nghỉ bệnh", ["NPL"] = "Không lương", ["OTH"] = "Khác"
            };
            string erpCd         = model.LEAVE_TYPE switch { "AL" => "PN", "CL" => "BH", _ => "CP" };
            string erpRemark     = erpCd == "CP"
                ? "ASSIGNED " + erpLeaveNames.GetValueOrDefault(model.LEAVE_TYPE, model.LEAVE_TYPE)
                : "ASSIGNED";
            string leaveTypeName = leaveTypeNames.GetValueOrDefault(model.LEAVE_TYPE, model.LEAVE_TYPE);

            var results   = new List<object>();
            var warnings  = new List<object>();
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

                    if (model.LEAVE_TYPE == "AL")
                    {
                        var balRows = await _oracleService.ExecuteQueryAsync(@"
                            WITH ALLOC AS (
                                SELECT MAX(RECEIVE_NUM) AS RECEIVE_NUM
                                FROM HRMS.EFM100
                                WHERE EMPCD = :EMPCD
                                  AND SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                            ),
                            USED AS (
                                SELECT COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                                FROM HRMS.EFM410
                                WHERE EMPCD = :EMPCD2
                                  AND TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                            )
                            SELECT NVL(A.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0) AS LEFT_NUM
                            FROM ALLOC A, USED U",
                            r => r["LEFT_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"]),
                            new OracleParameter("EMPCD",  targetEmpcd),
                            new OracleParameter("EMPCD2", targetEmpcd));

                        int leftNum = balRows.FirstOrDefault();
                        if (leftNum <= 0)
                            warnings.Add(new { empcd = targetEmpcd, emp_name = empName, left_num = leftNum });
                    }

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
                        VALUES (:REQUEST_ID, :EMPCD, :LEAVE_TYPE, :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'ASSIGNED')",
                        new OracleParameter("REQUEST_ID", requestId),
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("LEAVE_TYPE", model.LEAVE_TYPE),
                        new OracleParameter("FROM_DATE",  fromDate),
                        new OracleParameter("TO_DATE",    toDate),
                        new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                        new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

                    var erpHolidays = (await _oracleService.ExecuteQueryAsync(
                        @"SELECT TRUNC(HUILDAY) AS HUILDAY FROM HRMS.EAM800
                          WHERE TRUNC(HUILDAY) BETWEEN TRUNC(:FROM_DATE) AND TRUNC(:TO_DATE)",
                        r => Convert.ToDateTime(r["HUILDAY"]).Date,
                        new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = fromDate },
                        new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = toDate }
                    )).ToHashSet();
                    try
                    {
                        for (var day = fromDate.Date; day <= toDate.Date; day = day.AddDays(1))
                        {
                            if (erpHolidays.Contains(day)) continue;
                            await _oracleService.ExecuteProcedureAsync("HRMS.SP_015_NEW",
                                new OracleParameter("AS_EMPCD",   targetEmpcd),
                                new OracleParameter("AS_LEAVECD", erpCd),
                                new OracleParameter { ParameterName = "AD_ST_DAT", OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.Date, Value = day },
                                new OracleParameter { ParameterName = "AD_ED_DAT", OracleDbType = Oracle.ManagedDataAccess.Client.OracleDbType.Date, Value = day },
                                new OracleParameter("AS_IN_ID",   model.ASSIGNER_EMPCD),
                                new OracleParameter("AS_REMAR",   erpRemark));
                        }

                        await _oracleService.ExecuteNonQueryAsync(
                            "UPDATE HRMS.EFM410 SET APPROVED_BY = :APPROVED_BY WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FR_DAT AND :TO_DAT",
                            new OracleParameter("APPROVED_BY", model.ASSIGNER_EMPCD),
                            new OracleParameter("EMPCD",       targetEmpcd),
                            new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = fromDate },
                            new OracleParameter { ParameterName = "TO_DAT", OracleDbType = OracleDbType.Date, Value = toDate });
                    }
                    catch { /* ERP failure không block assign */ }

                    _notiSvc.LeaveAssigned(targetEmpcd, model.ASSIGNER_EMPCD, leaveTypeName, fromDate, toDate);

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
                success        = successCt > 0,
                message        = $"Đã sắp lịch cho {successCt}/{model.TARGET_EMPCDS.Count} nhân viên",
                total_inserted = successCt,
                warnings,
                results
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Leave/admin-confirmed-leaves
    [HttpGet("admin-confirmed-leaves")]
    public async Task<IActionResult> GetAdminConfirmedLeaves(
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null, string? status = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            DateTime? dFrom = null, dTo = null;
            if (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var df)) dFrom = df;
            if (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var dt)) dTo   = dt;

            var validStatuses = new[] { "PENDING", "APPROVED", "ASSIGNED" };
            string? statusFilter = !string.IsNullOrEmpty(status) && validStatuses.Contains(status.ToUpper())
                ? status.ToUpper() : null;

            const string baseSql = @"
                SELECT R.REQUEST_ID, R.EMPCD, EC.CNAME EMP_NAME,
                       EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                       EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                       EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                       L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                       R.STATUS, L.CONFIRM_STATUS, R.FINAL_DATE, R.FINAL_APPROVER, R.CREATED_DATE
                FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                JOIN HRMS.ECM100 EC           ON EC.EMPCD    = R.EMPCD
                LEFT JOIN HRMS.EAM410 B       ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                WHERE R.STATUS != 'REJECTED'
                  AND (:ST_FLAG  IS NULL OR R.STATUS      = :ST_VAL)
                  AND (:DPT_FLAG IS NULL OR EC.DEPTCD    = :DPT_VAL)
                  AND (:LN_FLAG  IS NULL OR EC.LINECD    = :LN_VAL)
                  AND (:WK_FLAG  IS NULL OR EC.WORKCD    = :WK_VAL)
                  AND (:FR_FLAG  IS NULL OR L.FROM_DATE >= :FR_VAL)
                  AND (:TO_FLAG  IS NULL OR L.TO_DATE   <= :TO_VAL)";

            OracleParameter[] MakePs() => new[]
            {
                new OracleParameter("ST_FLAG",  (object?)(statusFilter != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("ST_VAL",   (object?)statusFilter ?? DBNull.Value),
                new OracleParameter("DPT_FLAG", (object?)(dept_id != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("DPT_VAL",  (object?)dept_id ?? DBNull.Value),
                new OracleParameter("LN_FLAG",  (object?)(line_id != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("LN_VAL",   (object?)line_id ?? DBNull.Value),
                new OracleParameter("WK_FLAG",  (object?)(work_id != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("WK_VAL",   (object?)work_id ?? DBNull.Value),
                new OracleParameter("FR_FLAG",  (object?)(dFrom != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("FR_VAL",   (object?)dFrom ?? DBNull.Value),
                new OracleParameter("TO_FLAG",  (object?)(dTo   != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("TO_VAL",   (object?)dTo   ?? DBNull.Value),
            };

            var cntRows = await _oracleService.ExecuteQueryAsync(
                $"SELECT COUNT(*) CNT FROM ({baseSql})",
                r => Convert.ToInt32(r["CNT"]), MakePs());
            int total = cntRows.FirstOrDefault();

            if (total == 0)
                return Ok(new { success = true, total = 0, page, page_size, total_pages = 0, data = Array.Empty<object>() });

            var dataPs = MakePs().ToList();
            dataPs.Add(new OracleParameter("P_END",   page * page_size));
            dataPs.Add(new OracleParameter("P_START", (page - 1) * page_size));

            var rows = await _oracleService.ExecuteQueryAsync($@"
                SELECT * FROM (
                    SELECT A.*, ROWNUM RN
                    FROM ({baseSql} ORDER BY R.FINAL_DATE DESC NULLS LAST, R.CREATED_DATE DESC) A
                    WHERE ROWNUM <= :P_END
                ) WHERE RN > :P_START",
                r => new
                {
                    REQUEST_ID     = r["REQUEST_ID"]?.ToString() ?? "",
                    EMPCD          = r["EMPCD"]?.ToString() ?? "",
                    EMP_NAME       = r["EMP_NAME"]?.ToString(),
                    DEPT_ID        = r["DEPT_ID"]?.ToString(),
                    DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                    LINE_ID        = r["LINE_ID"]?.ToString(),
                    LINE_NAME      = r["LINE_NAME"]?.ToString(),
                    WORK_ID        = r["WORK_ID"]?.ToString(),
                    WORK_NAME      = r["WORK_NAME"]?.ToString(),
                    LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                    SOURCE         = r["SOURCE"]?.ToString(),
                    FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FROM_DATE"]),
                    TO_DATE        = r["TO_DATE"]      == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["TO_DATE"]),
                    TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? (decimal?)null  : Convert.ToDecimal(r["TOTAL_DAYS"]),
                    REASON         = r["REASON"]?.ToString(),
                    STATUS         = r["STATUS"]?.ToString(),
                    CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                    FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FINAL_DATE"]),
                    FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                    CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CREATED_DATE"]),
                }, dataPs.ToArray());

            return Ok(new { success = true, total, page, page_size, total_pages = (int)Math.Ceiling((double)total / page_size), data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Leave/admin-delete-leaves
    [HttpPost("admin-delete-leaves")]
    public async Task<IActionResult> AdminDeleteLeaves([FromBody] AdminBulkDeleteRequest model)
    {
        if (model.REQUEST_IDS == null || model.REQUEST_IDS.Count == 0)
            return Ok(new { success = false, message = "Không có đơn nào được chọn" });
        try
        {
            var roleRows = await _oracleService.ExecuteQueryAsync(
                "SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ACTOR_EMPCD));
            if (roleRows.FirstOrDefault() != "Admin")
                return Ok(new { success = false, message = "Chỉ Admin mới có quyền xóa" });

            var ids = string.Join(",", model.REQUEST_IDS.Select(id =>
                $"'{System.Text.RegularExpressions.Regex.Replace(id, "[^A-Za-z0-9_-]", "")}'"));
            await _oracleService.ExecuteNonQueryAsync($@"
                BEGIN
                    DELETE FROM HRMS.HR_LEAVE_REQUEST WHERE REQUEST_ID IN ({ids});
                    DELETE FROM HRMS.HR_REQUEST        WHERE REQUEST_ID IN ({ids}) AND REQUEST_TYPE = 'LEAVE';
                    COMMIT;
                END;");

            return Ok(new { success = true, message = $"Đã xóa {model.REQUEST_IDS.Count} đơn nghỉ phép khỏi hệ thống", total_deleted = model.REQUEST_IDS.Count });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }
}

// ── TEMP TEST: remove after testing ──────────────────────────────────────────
