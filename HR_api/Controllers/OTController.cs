using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.OT;
using HR_api.Services;
using System.Collections.Concurrent;
using System.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class OTController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly NotificationService _notiSvc;

    // Khoá song song theo (EMPCD|WORK_DATE) chống double-click race khi ConfirmOT
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _confirmLocks = new();

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
                DateTime? startOt  = r["START_OT"] == DBNull.Value ? null : Convert.ToDateTime(r["START_OT"]);

                // Khoá xác nhận/đổi ý ngay khi tới giờ bắt đầu tăng ca — tránh trường hợp
                // nhân viên đổi ý sau khi ca tăng ca đã bắt đầu.
                bool isEditable = workDate.Date >= DateTime.Today.Date
                    && (!startOt.HasValue || DateTime.Now < startOt.Value);

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
                    START_OT       = startOt,
                    END_OT         = r["END_OT"]      == DBNull.Value ? null : Convert.ToDateTime(r["END_OT"]),
                    CONFIRM_STATUS = confirmStatus,
                    CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                    SUM_WEEK       = Convert.ToDecimal(r["SUM_WEEK"]),
                    SUM_MONTH      = Convert.ToDecimal(r["SUM_MONTH"]),
                    SUM_YEAR       = Convert.ToDecimal(r["SUM_YEAR"]),
                    IS_EDITABLE    = isEditable,
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
        if (model == null || string.IsNullOrEmpty(model.EMPCD))
            return Ok(new { success = false, message = "Thiếu mã nhân viên" });

        DateTime workDate = (!string.IsNullOrEmpty(model.WORK_DATE) && DateTime.TryParse(model.WORK_DATE, out var _wd2)) ? _wd2 : DateTime.Today;

        if (model.CONFIRM_STATUS != "CONFIRMED" && model.CONFIRM_STATUS != "REJECTED")
            return Ok(new { success = false, message = "Trạng thái không hợp lệ" });

        // Khoá xác nhận/đổi ý ngay khi tới giờ bắt đầu tăng ca (kiểm tra lại ở server,
        // phòng trường hợp client gửi request trễ sau khi form đã hết hạn chỉnh sửa).
        // otWindow cũng được dùng để set OT_TYPE/OT_START/OT_END khi lưu HR_OT_REQUEST bên dưới.
        var otWindow = await GetOtWindowAsync(model.EMPCD, workDate);
        if (otWindow.Start.HasValue && DateTime.Now >= otWindow.Start.Value)
            return Ok(new { success = false, message = "Đã tới giờ tăng ca, không thể xác nhận hoặc đổi ý nữa" });

        var lockKey = $"{model.EMPCD}|{workDate:yyyyMMdd}";
        var sem = _confirmLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
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
                // Đã có trong HR_OT_REQUEST → Kiểm tra nếu có thay đổi trạng thái hoặc số giờ thì xoá và tạo lại
                var existing = existingRows[0];
                bool hoursChanged = existing.OT_HOURS.HasValue && model.OT_HOURS.HasValue && existing.OT_HOURS != model.OT_HOURS;

                if (existing.CONFIRM_STATUS != model.CONFIRM_STATUS || hoursChanged)
                {
                    // Lấy tất cả REQUEST_ID liên quan đến nhân viên và ngày làm việc này
                    string sqlGetReqIds = "SELECT REQUEST_ID FROM HRMS.HR_OT_REQUEST WHERE EMPCD = :EMPCD AND WORK_DATE = :WORK_DATE";
                    var reqIds = await _oracleService.ExecuteQueryAsync(sqlGetReqIds, r => r["REQUEST_ID"]?.ToString(),
                        new OracleParameter("EMPCD", model.EMPCD),
                        new OracleParameter("WORK_DATE", workDate));

                    if (reqIds.Count > 0)
                    {
                        foreach (var reqId in reqIds)
                        {
                            if (string.IsNullOrEmpty(reqId)) continue;

                            // Xoá bảng con HR_OT_REQUEST trước do ràng buộc khoá ngoại FK_OT_REQ
                            string sqlDeleteOt = "DELETE FROM HRMS.HR_OT_REQUEST WHERE REQUEST_ID = :REQUEST_ID";
                            await _oracleService.ExecuteNonQueryAsync(sqlDeleteOt, new OracleParameter("REQUEST_ID", reqId));

                            // Xoá bảng cha HR_REQUEST
                            string sqlDeleteReq = "DELETE FROM HRMS.HR_REQUEST WHERE REQUEST_ID = :REQUEST_ID";
                            await _oracleService.ExecuteNonQueryAsync(sqlDeleteReq, new OracleParameter("REQUEST_ID", reqId));
                        }
                    }

                    // Không return nữa mà trôi xuống Bước 2 phía dưới để tiến hành INSERT mới
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

            try
            {
                await _oracleService.ExecuteProcedureAsync("HRMS.SP_OT_CONFIRM_INSERT",
                    new OracleParameter("P_REQUEST_ID",     requestId),
                    new OracleParameter("P_EMPCD",          model.EMPCD),
                    new OracleParameter("P_WORK_DATE",      workDate),
                    new OracleParameter("P_OT_HOURS",       (object?)model.OT_HOURS ?? DBNull.Value),
                    new OracleParameter("P_CONFIRM_STATUS", model.CONFIRM_STATUS),
                    pResult,
                    pMessage);
            }
            catch (OracleException ex) when (ex.Number == 1)
            {
                // ORA-00001: race lost — request khác đã INSERT trước. Re-select rồi UPDATE.
                return await RetryAsUpdateAsync(model, workDate, otWindow);
            }

            if (int.Parse(pResult.Value?.ToString() ?? "0") != 0)
            {
                // SP_OT_CONFIRM_INSERT có thể return code lỗi nếu nó tự catch ORA-00001 bên trong
                if ((pMessage.Value?.ToString() ?? "").Contains("ORA-00001", StringComparison.OrdinalIgnoreCase))
                    return await RetryAsUpdateAsync(model, workDate, otWindow);
                return Ok(new { success = false, message = $"Lỗi hệ thống, vui lòng thử lại. ({pMessage.Value})" });
            }

            // SP báo thành công không đồng nghĩa dòng chắc chắn đã persist — xác minh lại bằng
            // SELECT độc lập trước khi đụng ERP (tránh case ERP đã ký mà MySamho không có data).
            if (!await VerifyRequestPersistedAsync(requestId, model.CONFIRM_STATUS))
            {
                string retryRequestId = requestId + "R";
                var pResult2  = new OracleParameter("P_RESULT",  OracleDbType.Int32)          { Direction = System.Data.ParameterDirection.Output };
                var pMessage2 = new OracleParameter("P_MESSAGE", OracleDbType.Varchar2, 500)  { Direction = System.Data.ParameterDirection.Output };

                try
                {
                    await _oracleService.ExecuteProcedureAsync("HRMS.SP_OT_CONFIRM_INSERT",
                        new OracleParameter("P_REQUEST_ID",     retryRequestId),
                        new OracleParameter("P_EMPCD",          model.EMPCD),
                        new OracleParameter("P_WORK_DATE",      workDate),
                        new OracleParameter("P_OT_HOURS",       (object?)model.OT_HOURS ?? DBNull.Value),
                        new OracleParameter("P_CONFIRM_STATUS", model.CONFIRM_STATUS),
                        pResult2,
                        pMessage2);
                }
                catch (OracleException ex) when (ex.Number == 1)
                {
                    return await RetryAsUpdateAsync(model, workDate, otWindow);
                }

                if (int.Parse(pResult2.Value?.ToString() ?? "0") != 0)
                {
                    // Giống lần insert đầu: SP có thể tự bắt ORA-00001 nội bộ thay vì ném exception
                    if ((pMessage2.Value?.ToString() ?? "").Contains("ORA-00001", StringComparison.OrdinalIgnoreCase))
                        return await RetryAsUpdateAsync(model, workDate, otWindow);
                    return Ok(new { success = false, message = $"Lỗi hệ thống, vui lòng thử lại. ({pMessage2.Value})" });
                }

                if (!await VerifyRequestPersistedAsync(retryRequestId, model.CONFIRM_STATUS))
                    return Ok(new { success = false, message = "Lỗi hệ thống, vui lòng thử lại." });

                requestId = retryRequestId;
            }

            await EnrichOtRequestAsync(requestId, otWindow);
            await UpdateErpSignedAsync(model.EMPCD, workDate, model.CONFIRM_STATUS);

            string msg = model.CONFIRM_STATUS == "CONFIRMED" ? "Xác nhận tăng ca thành công" : "Từ chối tăng ca thành công";
            return Ok(new { success = true, message = msg, request_id = requestId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = $"Lỗi hệ thống, vui lòng thử lại. ({ex.Message})" });
        }
        finally
        {
            sem.Release();
        }
    }

    // Khi INSERT thua race (đã có dòng cho EMPCD+WORK_DATE), re-select và UPDATE
    // theo intent mới của user. Dùng chung cho cả 2 path: OracleException ORA-00001
    // và SP_OT_CONFIRM_INSERT trả về P_MESSAGE chứa ORA-00001.
    private async Task<IActionResult> RetryAsUpdateAsync(OTConfirmRequest model, DateTime workDate, OtWindowInfo otWindow)
    {
        var existing = (await _oracleService.ExecuteQueryAsync(
            "SELECT REQUEST_ID FROM HRMS.HR_OT_REQUEST WHERE EMPCD = :EMPCD AND WORK_DATE = :WORK_DATE AND ROWNUM = 1",
            r => r["REQUEST_ID"]?.ToString(),
            new OracleParameter("EMPCD", model.EMPCD),
            new OracleParameter("WORK_DATE", workDate))).FirstOrDefault();

        if (string.IsNullOrEmpty(existing))
            return Ok(new { success = false, message = "Lỗi hệ thống, vui lòng thử lại." });

        string sqlUpdateStatus =
            "UPDATE HRMS.HR_OT_REQUEST SET CONFIRM_STATUS = :CONFIRM_STATUS, OT_HOURS = :OT_HOURS, CONFIRM_DATE = SYSDATE WHERE REQUEST_ID = :REQUEST_ID";

        await _oracleService.ExecuteNonQueryAsync(sqlUpdateStatus,
            new OracleParameter("CONFIRM_STATUS", model.CONFIRM_STATUS),
            new OracleParameter("OT_HOURS", (object?)model.OT_HOURS ?? DBNull.Value),
            new OracleParameter("REQUEST_ID", existing));

        // Xác minh UPDATE có thực sự áp dụng — thử lại 1 lần nếu chưa khớp, tránh case
        // ERP đã ký mà MySamho lại giữ trạng thái cũ.
        if (!await VerifyRequestPersistedAsync(existing, model.CONFIRM_STATUS!))
        {
            await _oracleService.ExecuteNonQueryAsync(sqlUpdateStatus,
                new OracleParameter("CONFIRM_STATUS", model.CONFIRM_STATUS),
                new OracleParameter("OT_HOURS", (object?)model.OT_HOURS ?? DBNull.Value),
                new OracleParameter("REQUEST_ID", existing));

            if (!await VerifyRequestPersistedAsync(existing, model.CONFIRM_STATUS!))
                return Ok(new { success = false, message = "Lỗi hệ thống, vui lòng thử lại." });
        }

        await _oracleService.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_REQUEST SET STATUS = :STATUS, UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE WHERE REQUEST_ID = :REQUEST_ID",
            new OracleParameter("STATUS", model.CONFIRM_STATUS),
            new OracleParameter("EMPCD", model.EMPCD),
            new OracleParameter("REQUEST_ID", existing));

        await EnrichOtRequestAsync(existing, otWindow);
        await UpdateErpSignedAsync(model.EMPCD, workDate, model.CONFIRM_STATUS!);

        string msg = model.CONFIRM_STATUS == "CONFIRMED" ? "Xác nhận tăng ca thành công" : "Từ chối tăng ca thành công";
        return Ok(new { success = true, message = msg, request_id = existing });
    }

    // SP báo P_RESULT=0 không đảm bảo dòng thật sự nằm trong DB (transaction lệch pha,
    // SP nuốt exception nội bộ...) — verify độc lập trước khi ghi ERP SIGNED_STATUS.
    private async Task<bool> VerifyRequestPersistedAsync(string requestId, string confirmStatus)
    {
        var rows = await _oracleService.ExecuteQueryAsync(
            "SELECT CONFIRM_STATUS FROM HRMS.HR_OT_REQUEST WHERE REQUEST_ID = :R AND ROWNUM = 1",
            r => r["CONFIRM_STATUS"]?.ToString(),
            new OracleParameter("R", requestId));

        return rows.Count > 0 && rows[0] == confirmStatus;
    }

    // Set OT_TYPE/OT_START/OT_END (đang luôn NULL vì SP_OT_CONFIRM_INSERT không nhận các cột này)
    // để có dữ liệu đối chiếu với ERP.
    private async Task EnrichOtRequestAsync(string requestId, OtWindowInfo window)
    {
        await _oracleService.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_OT_REQUEST SET OT_TYPE = :T, OT_START = :S, OT_END = :E WHERE REQUEST_ID = :R",
            new OracleParameter("T", (object?)window.Type  ?? DBNull.Value),
            new OracleParameter("S", (object?)window.Start ?? DBNull.Value),
            new OracleParameter("E", (object?)window.End   ?? DBNull.Value),
            new OracleParameter("R", requestId));
    }

    private record OtWindowInfo(string? Type, DateTime? Start, DateTime? End);

    // Tính khung giờ tăng ca từ ERP (EBM300/EBM300_WAIT + EBM100) — cùng công thức với GetOTToday.
    private async Task<OtWindowInfo> GetOtWindowAsync(string empcd, DateTime workDate)
    {
        string sql = @"
            SELECT E.OT_BEFORE, E.OT_AFTER,
                   CASE WHEN E.OT_BEFORE = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.STIME,'YYYYMMDDHH24MI') - E.OT_BEFORE_TIME / 24
                        WHEN E.OT_AFTER  = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.ETIME,'YYYYMMDDHH24MI')
                   END START_OT,
                   CASE WHEN E.OT_AFTER  = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.ETIME,'YYYYMMDDHH24MI') + E.OT_AFTER_TIME / 24
                        WHEN E.OT_BEFORE = 'Y' THEN TO_DATE(TO_CHAR(E.DAT,'YYYYMMDD') || S.STIME,'YYYYMMDDHH24MI')
                   END END_OT
            FROM (SELECT EMPCD, DAT, SHIFTCD, MAX(OT_BEFORE) OT_BEFORE, MAX(OT_BEFORE_TIME) OT_BEFORE_TIME, MAX(OT_AFTER) OT_AFTER, MAX(OT_AFTER_TIME) OT_AFTER_TIME
                  FROM (SELECT EMPCD, DAT, SHIFTCD, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME FROM HRMS.EBM300      WHERE DAT = :WORK_DATE  AND EMPCD = :EMPCD  AND OVER_TIME IS NOT NULL AND OVER_TIME > 0
                        UNION ALL
                        SELECT EMPCD, DAT, SHIFTCD, OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2 AND EMPCD = :EMPCD1 AND OVER_TIME IS NOT NULL AND OVER_TIME > 0)
                  GROUP BY EMPCD, DAT, SHIFTCD) E
            JOIN HRMS.EBM100 S ON S.SHIFTCD = E.SHIFTCD
            WHERE ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r =>
        {
            string? type = r["OT_BEFORE"]?.ToString() == "Y" ? "BEFORE" : (r["OT_AFTER"]?.ToString() == "Y" ? "AFTER" : null);
            DateTime? start = r["START_OT"] == DBNull.Value ? null : Convert.ToDateTime(r["START_OT"]);
            DateTime? end   = r["END_OT"]   == DBNull.Value ? null : Convert.ToDateTime(r["END_OT"]);
            return new OtWindowInfo(type, start, end);
        },
            new OracleParameter("WORK_DATE", workDate),
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("WORK_DATE2", workDate),
            new OracleParameter("EMPCD1", empcd));

        return rows.FirstOrDefault() ?? new OtWindowInfo(null, null, null);
    }

    private async Task UpdateErpSignedAsync(string empcd, DateTime workDate, string confirmStatus)
    {
        string signed = confirmStatus == "CONFIRMED" ? "Y" : "N";

        await _oracleService.ExecuteNonQueryAsync(
            "UPDATE HRMS.EBM300 SET SIGNED_STATUS = :SIGNED, SIGNED_LOG = SYSDATE WHERE EMPCD = :EMPCD AND DAT = :WORK_DATE",
            new OracleParameter("SIGNED", signed),
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("WORK_DATE", workDate));

        await _oracleService.ExecuteNonQueryAsync(
            "UPDATE HRMS.EBM300_WAIT SET SIGNED_STATUS = :SIGNED, SIGNED_LOG = SYSDATE WHERE EMPCD = :EMPCD AND DAT = :WORK_DATE",
            new OracleParameter("SIGNED", signed),
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("WORK_DATE", workDate));
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
                  AND NVL(OT.OT_HOURS, 0) > 0
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
        int     page_size  = 100,
        int     admin      = 0)
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

            // Admin mode: bỏ match OT_HOURS trong JOIN → luôn hiện trạng thái thực tế của HR_OT_REQUEST
            string joinHRRequest = admin == 1
                ? "LEFT JOIN HRMS.HR_OT_REQUEST  R ON R.EMPCD = OT.EMPCD AND R.WORK_DATE = :W_DATE3"
                : "LEFT JOIN HRMS.HR_OT_REQUEST  R ON R.EMPCD = OT.EMPCD AND R.WORK_DATE = :W_DATE3 AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)";

            string fromSql = @"
                FROM OT_BASE OT
                JOIN      HRMS.ECM100        EC ON EC.EMPCD  = OT.EMPCD
                JOIN      HRMS.EBM100         S ON S.SHIFTCD = OT.SHIFTCD
                LEFT JOIN HRMS.EAM410         B ON B.DEPTCD  = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                " + joinHRRequest + @"
                LEFT JOIN HRMS.HR_USERS       UR ON UR.EMPCD  = OT.EMPCD
                LEFT JOIN HRMS.HR_ROLES       RR ON RR.ID     = UR.ROLE_ID";

            string whereSql = @"
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND NVL(OT.OT_HOURS, 0) > 0
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
                  AND NVL(OT.OT_HOURS, 0) > 0
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

    // POST /apiHR/OT/clerk/remind-pending — Thư ký nhắc nhở nhân viên chưa ký OT
    [HttpPost("clerk/remind-pending")]
    public async Task<IActionResult> RemindPendingOTSign([FromBody] dynamic model)
    {
        try
        {
            string clerkEmpCd = (string)model.clerk_empcd;
            string workDateStr = (string)model.work_date;
            string? deptId  = (string?)model.dept_id;
            string? lineId  = (string?)model.line_id;
            string? workId  = (string?)model.work_id;

            if (string.IsNullOrWhiteSpace(clerkEmpCd))
                return Ok(new { success = false, message = "Thiếu mã thư ký" });
            if (string.IsNullOrWhiteSpace(workDateStr) || !DateTime.TryParse(workDateStr, out var workDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            var clerkFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(clerkEmpCd, prefix: "CK");

            string withSql = @"
                WITH OT_BASE AS (
                    SELECT EMPCD, DAT, SHIFTCD,
                           MAX(OVER_TIME) OT_HOURS, MAX(OT_BEFORE) OT_BEFORE, MAX(OT_AFTER) OT_AFTER
                    FROM (
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_AFTER FROM HRMS.EBM300      WHERE DAT = :WORK_DATE
                        UNION ALL
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_AFTER FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2
                    ) GROUP BY EMPCD, DAT, SHIFTCD
                )";

            string sqlPending = withSql + $@"
                SELECT DISTINCT EC.EMPCD
                FROM OT_BASE OT
                JOIN HRMS.ECM100 EC ON EC.EMPCD = OT.EMPCD
                LEFT JOIN HRMS.HR_OT_REQUEST R ON R.EMPCD = OT.EMPCD AND R.WORK_DATE = :WORK_DATE3
                                               AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND NVL(OT.OT_HOURS,0) > 0
                  AND NVL(R.CONFIRM_STATUS,'PENDING') = 'PENDING'
                  {clerkFilter.SqlClause}
                  AND (:DPT_FLAG IS NULL OR EC.DEPTCD = :DPT_VAL)
                  AND (:LN_FLAG  IS NULL OR EC.LINECD  = :LN_VAL)
                  AND (:WK_FLAG  IS NULL OR EC.WORKCD  = :WK_VAL)";

            var queryParams = new List<OracleParameter>
            {
                new OracleParameter("WORK_DATE",  OracleDbType.Date) { Value = workDate },
                new OracleParameter("WORK_DATE2", OracleDbType.Date) { Value = workDate },
                new OracleParameter("WORK_DATE3", OracleDbType.Date) { Value = workDate },
                new OracleParameter("DPT_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(deptId) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",  OracleDbType.Varchar2) { Value = (object?)deptId ?? DBNull.Value },
                new OracleParameter("LN_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(lineId) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",   OracleDbType.Varchar2) { Value = (object?)lineId ?? DBNull.Value },
                new OracleParameter("WK_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(workId) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",   OracleDbType.Varchar2) { Value = (object?)workId ?? DBNull.Value }
            };
            queryParams.AddRange(clerkFilter.Params);

            var pendingEmpCds = await _oracleService.ExecuteQueryAsync(
                sqlPending, r => r["EMPCD"]?.ToString() ?? "", queryParams.ToArray());

            if (pendingEmpCds.Count == 0)
                return Ok(new { success = true, message = "Không có nhân viên nào đang chờ ký", sent = 0 });

            _notiSvc.OTSignReminderToEmployees(pendingEmpCds, clerkEmpCd, workDate.ToString("dd/MM/yyyy"));

            return Ok(new { success = true, message = $"Đã gửi nhắc nhở cho {pendingEmpCds.Count} nhân viên", sent = pendingEmpCds.Count });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("hr/notify-pending")]
    public async Task<IActionResult> NotifyPendingOT([FromBody] dynamic model)
    {
        try
        {
            string workDateStr = (string)model.work_date;
            string? deptId    = (string?)model.dept_id;
            string createdBy  = (string)model.created_by;

            if (string.IsNullOrWhiteSpace(workDateStr) || !DateTime.TryParse(workDateStr, out var workDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            const string sqlPending = @"
                WITH OT_BASE AS (
                    SELECT EMPCD, DAT, SHIFTCD,
                           MAX(OVER_TIME) OT_HOURS, MAX(OT_BEFORE) OT_BEFORE, MAX(OT_AFTER) OT_AFTER
                    FROM (
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_AFTER FROM HRMS.EBM300      WHERE DAT = :WORK_DATE
                        UNION ALL
                        SELECT EMPCD, DAT, SHIFTCD, OVER_TIME, OT_BEFORE, OT_AFTER FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2
                    ) GROUP BY EMPCD, DAT, SHIFTCD
                )
                SELECT DISTINCT EC.EMPCD
                FROM OT_BASE OT
                JOIN HRMS.ECM100 EC ON EC.EMPCD = OT.EMPCD
                LEFT JOIN HRMS.HR_OT_REQUEST R ON R.EMPCD = OT.EMPCD AND R.WORK_DATE = :WORK_DATE3
                                               AND NVL(R.OT_HOURS,0) = NVL(OT.OT_HOURS,0)
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (OT.OT_BEFORE = 'Y' OR OT.OT_AFTER = 'Y')
                  AND NVL(OT.OT_HOURS,0) > 0
                  AND NVL(R.CONFIRM_STATUS,'PENDING') = 'PENDING'
                  AND (:DPT_FLAG IS NULL OR EC.DEPTCD = :DPT_VAL)";

            var pendingEmpCds = await _oracleService.ExecuteQueryAsync(
                sqlPending, r => r["EMPCD"]?.ToString() ?? "",
                new OracleParameter("WORK_DATE",  OracleDbType.Date) { Value = workDate },
                new OracleParameter("WORK_DATE2", OracleDbType.Date) { Value = workDate },
                new OracleParameter("WORK_DATE3", OracleDbType.Date) { Value = workDate },
                new OracleParameter("DPT_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(deptId) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",  OracleDbType.Varchar2) { Value = (object?)deptId ?? DBNull.Value });

            if (pendingEmpCds.Count == 0)
                return Ok(new { success = true, message = "Không có nhân viên nào đang chờ ký", sent = 0 });

            _notiSvc.OTSignReminderToEmployees(pendingEmpCds, createdBy, workDate.ToString("dd/MM/yyyy"));

            return Ok(new { success = true, message = $"Đã gửi nhắc nhở cho {pendingEmpCds.Count} nhân viên", sent = pendingEmpCds.Count });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ADMIN — Bulk OT Management (Sign-For / Update / Delete)
    // ═════════════════════════════════════════════════════════════════════════

    // POST /apiHR/OT/admin/bulk-signfor
    // Ký giùm bulk: dùng CÙNG LOGIC với ConfirmOT (SP_OT_CONFIRM_INSERT), chỉ khác là loop cho nhiều NV.
    // KHÔNG update ERP (nguyên tắc admin: chỉ đọc ERP, không write).
    [HttpPost("admin/bulk-signfor")]
    public async Task<IActionResult> AdminBulkSignFor([FromBody] OTAdminBulkSignForRequest body)
    {
        try
        {
            if (body == null || body.ITEMS == null || body.ITEMS.Count == 0)
                return Ok(new OTAdminBulkResponse { success = false, message = "Danh sách rỗng" });
            if (!await IsAdminOrHRAsync(body.ACTOR_EMPCD))
                return Ok(new OTAdminBulkResponse { success = false, message = "Bạn không có quyền thực hiện thao tác này" });
            if (!DateTime.TryParseExact(body.WORK_DATE, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var workDate))
                return Ok(new OTAdminBulkResponse { success = false, message = "Ngày làm việc không hợp lệ" });

            var res = new OTAdminBulkResponse { success = true };

            foreach (var it in body.ITEMS)
            {
                if (string.IsNullOrEmpty(it.EMPCD)) continue;

                try
                {
                    // 1. Lookup ERP OVER_TIME (để insert đúng hours → danh sách join match)
                    var erpHoursRows = await _oracleService.ExecuteQueryAsync(
                        @"SELECT MAX(OVER_TIME) OT_HOURS FROM (
                            SELECT OVER_TIME FROM HRMS.EBM300      WHERE DAT = :WORK_DATE  AND EMPCD = :EMPCD  AND OVER_TIME IS NOT NULL AND OVER_TIME > 0
                            UNION ALL
                            SELECT OVER_TIME FROM HRMS.EBM300_WAIT WHERE DAT = :WORK_DATE2 AND EMPCD = :EMPCD1 AND OVER_TIME IS NOT NULL AND OVER_TIME > 0
                        )",
                        r => r["OT_HOURS"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["OT_HOURS"]),
                        new OracleParameter("WORK_DATE",  workDate),
                        new OracleParameter("WORK_DATE2", workDate),
                        new OracleParameter("EMPCD",      it.EMPCD),
                        new OracleParameter("EMPCD1",     it.EMPCD));

                    var erpHours = erpHoursRows.FirstOrDefault();
                    if (!erpHours.HasValue || erpHours.Value <= 0)
                    {
                        res.failed++;
                        res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = "Không có kế hoạch tăng ca trong ngày này" });
                        continue;
                    }

                    // OT_HOURS: ưu tiên từ item (nếu FE truyền), fallback ERP OVER_TIME
                    var finalHours = it.OT_HOURS ?? erpHours.Value;

                    // 2. Check HR_OT_REQUEST đã có chưa (giống ConfirmOT)
                    var existing = (await _oracleService.ExecuteQueryAsync(
                        "SELECT REQUEST_ID FROM HRMS.HR_OT_REQUEST WHERE EMPCD = :EMPCD AND WORK_DATE = :WORK_DATE AND ROWNUM = 1",
                        r => r["REQUEST_ID"]?.ToString(),
                        new OracleParameter("EMPCD", it.EMPCD),
                        new OracleParameter("WORK_DATE", workDate))).FirstOrDefault();

                    if (!string.IsNullOrEmpty(existing))
                    {
                        res.skipped++;
                        res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = "Đã có bản ghi" });
                        continue;
                    }

                    // 3. INSERT qua stored procedure SP_OT_CONFIRM_INSERT (giống ConfirmOT)
                    string requestId = DateTime.Now.ToString("yyyyMMddHHmmss") + it.EMPCD;
                    var pResult  = new OracleParameter("P_RESULT",  OracleDbType.Int32)         { Direction = System.Data.ParameterDirection.Output };
                    var pMessage = new OracleParameter("P_MESSAGE", OracleDbType.Varchar2, 500) { Direction = System.Data.ParameterDirection.Output };

                    await _oracleService.ExecuteProcedureAsync("HRMS.SP_OT_CONFIRM_INSERT",
                        new OracleParameter("P_REQUEST_ID",     requestId),
                        new OracleParameter("P_EMPCD",          it.EMPCD),
                        new OracleParameter("P_WORK_DATE",      workDate),
                        new OracleParameter("P_OT_HOURS",       finalHours),
                        new OracleParameter("P_CONFIRM_STATUS", "CONFIRMED"),
                        pResult,
                        pMessage);

                    if (int.Parse(pResult.Value?.ToString() ?? "0") != 0)
                    {
                        res.failed++;
                        res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = pMessage.Value?.ToString() ?? "SP báo lỗi" });
                        continue;
                    }

                    // KHÔNG đụng ERP — admin chỉ đọc ERP, không write
                    res.processed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = true, MESSAGE = requestId });
                }
                catch (Exception exi)
                {
                    res.failed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = exi.Message });
                }
            }

            res.message = $"Ký giùm: OK {res.processed}, Skip {res.skipped}, Lỗi {res.failed}";
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Ok(new OTAdminBulkResponse { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/OT/admin/bulk-update
    // Cập nhật OT_HOURS + CONFIRM_STATUS (option) cho danh sách EMPCD đã có bản ghi.
    [HttpPost("admin/bulk-update")]
    public async Task<IActionResult> AdminBulkUpdate([FromBody] OTAdminBulkUpdateRequest body)
    {
        try
        {
            if (body == null || body.ITEMS == null || body.ITEMS.Count == 0)
                return Ok(new OTAdminBulkResponse { success = false, message = "Danh sách rỗng" });
            if (!await IsAdminOrHRAsync(body.ACTOR_EMPCD))
                return Ok(new OTAdminBulkResponse { success = false, message = "Bạn không có quyền thực hiện thao tác này" });
            if (!DateTime.TryParseExact(body.WORK_DATE, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var workDate))
                return Ok(new OTAdminBulkResponse { success = false, message = "Ngày làm việc không hợp lệ" });

            var res = new OTAdminBulkResponse { success = true };
            var actor = string.IsNullOrEmpty(body.ACTOR_EMPCD) ? "ADMIN" : body.ACTOR_EMPCD;

            foreach (var it in body.ITEMS)
            {
                if (string.IsNullOrEmpty(it.EMPCD) || !it.OT_HOURS.HasValue || it.OT_HOURS.Value <= 0)
                {
                    res.failed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = "Dữ liệu không hợp lệ" });
                    continue;
                }

                try
                {
                    // Lookup existing
                    var rows = await _oracleService.ExecuteQueryAsync(
                        @"SELECT REQUEST_ID, OT_START, OT_TYPE
                          FROM HRMS.HR_OT_REQUEST
                          WHERE EMPCD = :E AND WORK_DATE = :D AND ROWNUM = 1",
                        r => new
                        {
                            REQUEST_ID = r["REQUEST_ID"]?.ToString(),
                            OT_START   = r["OT_START"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["OT_START"]),
                            OT_TYPE    = r["OT_TYPE"]?.ToString(),
                        },
                        new OracleParameter("E", it.EMPCD),
                        new OracleParameter("D", workDate));

                    if (rows.Count == 0)
                    {
                        res.skipped++;
                        res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = "Chưa có bản ghi để update" });
                        continue;
                    }

                    var row = rows[0];
                    var reqId = row.REQUEST_ID;
                    var newHours = it.OT_HOURS.Value;
                    var confirmStatus = string.IsNullOrEmpty(it.CONFIRM_STATUS) ? "CONFIRMED" : it.CONFIRM_STATUS;

                    // Tính OT_END mới nếu có OT_START
                    DateTime? newEnd = null;
                    if (row.OT_START.HasValue)
                    {
                        // OT_START là mốc bắt đầu OT thực tế, cứ + newHours
                        newEnd = row.OT_START.Value.AddHours((double)newHours);
                    }

                    await _oracleService.ExecuteNonQueryAsync(
                        @"UPDATE HRMS.HR_OT_REQUEST
                          SET OT_HOURS = :H, OT_END = NVL(:Ed, OT_END), CONFIRM_STATUS = :C, CONFIRM_DATE = SYSDATE
                          WHERE REQUEST_ID = :R",
                        new OracleParameter("H",  newHours),
                        new OracleParameter("Ed", (object?)newEnd ?? DBNull.Value),
                        new OracleParameter("C",  confirmStatus),
                        new OracleParameter("R",  reqId));

                    await _oracleService.ExecuteNonQueryAsync(
                        @"UPDATE HRMS.HR_REQUEST
                          SET STATUS = :S, UPDATED_BY = :A, UPDATED_DATE = SYSDATE
                          WHERE REQUEST_ID = :R",
                        new OracleParameter("S", confirmStatus),
                        new OracleParameter("A", actor),
                        new OracleParameter("R", reqId));

                    // KHÔNG đụng ERP — nguyên tắc: OT admin chỉ đọc ERP, không write

                    res.processed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = true, MESSAGE = reqId });
                }
                catch (Exception exi)
                {
                    res.failed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = it.EMPCD, OK = false, MESSAGE = exi.Message });
                }
            }

            res.message = $"Cập nhật: OK {res.processed}, Skip {res.skipped}, Lỗi {res.failed}";
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Ok(new OTAdminBulkResponse { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/OT/admin/bulk-delete
    // Hard delete HR_OT_REQUEST + HR_REQUEST, reset EBM300.SIGNED_STATUS = 'N'.
    [HttpPost("admin/bulk-delete")]
    public async Task<IActionResult> AdminBulkDelete([FromBody] OTAdminBulkDeleteRequest body)
    {
        try
        {
            if (body == null || body.EMPCDS == null || body.EMPCDS.Count == 0)
                return Ok(new OTAdminBulkResponse { success = false, message = "Danh sách rỗng" });
            if (!await IsAdminOrHRAsync(body.ACTOR_EMPCD))
                return Ok(new OTAdminBulkResponse { success = false, message = "Bạn không có quyền thực hiện thao tác này" });
            if (!DateTime.TryParseExact(body.WORK_DATE, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var workDate))
                return Ok(new OTAdminBulkResponse { success = false, message = "Ngày làm việc không hợp lệ" });

            var res = new OTAdminBulkResponse { success = true };

            foreach (var empcd in body.EMPCDS)
            {
                if (string.IsNullOrEmpty(empcd)) continue;

                try
                {
                    var reqIds = await _oracleService.ExecuteQueryAsync(
                        "SELECT REQUEST_ID FROM HRMS.HR_OT_REQUEST WHERE EMPCD = :E AND WORK_DATE = :D",
                        r => r["REQUEST_ID"]?.ToString(),
                        new OracleParameter("E", empcd),
                        new OracleParameter("D", workDate));

                    if (reqIds.Count == 0)
                    {
                        res.skipped++;
                        res.results.Add(new OTAdminBulkResult { EMPCD = empcd, OK = false, MESSAGE = "Không có bản ghi" });
                        continue;
                    }

                    foreach (var rid in reqIds)
                    {
                        if (string.IsNullOrEmpty(rid)) continue;
                        await _oracleService.ExecuteNonQueryAsync(
                            "DELETE FROM HRMS.HR_ROUTE_APPROVE WHERE REQUEST_ID = :R",
                            new OracleParameter("R", rid));
                        await _oracleService.ExecuteNonQueryAsync(
                            "DELETE FROM HRMS.HR_OT_REQUEST WHERE REQUEST_ID = :R",
                            new OracleParameter("R", rid));
                        await _oracleService.ExecuteNonQueryAsync(
                            "DELETE FROM HRMS.HR_REQUEST WHERE REQUEST_ID = :R",
                            new OracleParameter("R", rid));
                    }

                    // KHÔNG đụng ERP (EBM300 / EBM300_WAIT) — chỉ xoá phía MySamho

                    res.processed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = empcd, OK = true, MESSAGE = $"Deleted {reqIds.Count}" });
                }
                catch (Exception exi)
                {
                    res.failed++;
                    res.results.Add(new OTAdminBulkResult { EMPCD = empcd, OK = false, MESSAGE = exi.Message });
                }
            }

            res.message = $"Xoá: OK {res.processed}, Skip {res.skipped}, Lỗi {res.failed}";
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Ok(new OTAdminBulkResponse { success = false, message = ex.Message });
        }
    }

    // Xác thực ACTOR_EMPCD thực sự có role Admin/HR trong DB — tránh việc chỉ dựa vào
    // [Authorize(Roles=...)] phía HR_web (ai gọi thẳng apiHR/OT/admin/* sẽ bỏ qua được lớp đó).
    private async Task<bool> IsAdminOrHRAsync(string? empCd)
    {
        if (string.IsNullOrEmpty(empCd)) return false;

        var roleRows = await _oracleService.ExecuteQueryAsync(@"
            SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
            LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
            WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
            r => r["ROLE_NAME"]?.ToString(),
            new OracleParameter("EMPCD", empCd));

        var role = roleRows.FirstOrDefault();
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "HR", StringComparison.OrdinalIgnoreCase);
    }
}
