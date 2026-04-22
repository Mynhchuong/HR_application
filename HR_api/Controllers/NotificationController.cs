using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Notification;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly IConfiguration _configuration;

    public NotificationController(OracleService oracleService, IConfiguration configuration)
    {
        _oracleService = oracleService;
        _configuration = configuration;
    }

    // ============================================================
    // 1. REGISTER DEVICE TOKEN
    // ============================================================
    [HttpPost("register-token")]
    public async Task<IActionResult> RegisterToken([FromBody] TokenRegistrationRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.EMPCD) || string.IsNullOrEmpty(model.TOKEN))
                return Ok(new { success = false, message = "Thiếu mã nhân viên hoặc Token" });

            string sql = @"
                MERGE INTO HRMS.HR_USER_TOKENS T
                USING (SELECT :EMPCD E, :TOKEN TK FROM DUAL) S
                ON (T.EMPCD = S.E AND T.TOKEN = S.TK)
                WHEN MATCHED THEN 
                    UPDATE SET LAST_UPDATED = SYSDATE, OS_TYPE = :OS_TYPE, DEVICE_MODEL = :DEVICE_MODEL
                WHEN NOT MATCHED THEN 
                    INSERT (EMPCD, TOKEN, OS_TYPE, DEVICE_MODEL, LAST_UPDATED)
                    VALUES (:EMPCD, :TOKEN, :OS_TYPE, :DEVICE_MODEL, SYSDATE)";

            await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("EMPCD", model.EMPCD),
                new OracleParameter("TOKEN", model.TOKEN),
                new OracleParameter("OS_TYPE", (object?)model.OS_TYPE ?? DBNull.Value),
                new OracleParameter("DEVICE_MODEL", (object?)model.DEVICE_MODEL ?? DBNull.Value));

            return Ok(new { success = true, message = "Đăng ký Token thành công" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 2. GET MY NOTIFICATIONS (For Mobile App)
    // ============================================================
    [HttpGet("my")]
    public async Task<IActionResult> GetMyNotifications(string empcd, int page = 1, int page_size = 20)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd)) return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            int offset = (page - 1) * page_size;

            // Lấy thông báo cá nhân, thông báo bộ phận và thông báo toàn công ty
            // Oracle 10g doesn't have OFFSET/FETCH, using ROW_NUMBER()
            string sql = @"
                SELECT * FROM (
                    SELECT N.*, NVL(L.IS_READ, 0) IS_READ_VAL, ROW_NUMBER() OVER (ORDER BY N.CREATED_DATE DESC) RN
                    FROM HRMS.HR_NOTIFICATIONS N
                    LEFT JOIN HRMS.HR_NOTIFICATION_LOG L ON L.NOTI_ID = N.ID AND L.EMPCD = :EMPCD
                    WHERE N.NOTI_TYPE = 'COMPANY'
                       OR (N.NOTI_TYPE = 'PERSONAL' AND N.TARGET_VAL = :EMPCD2)
                       OR (N.NOTI_TYPE = 'DEPT' AND N.TARGET_VAL = (SELECT DEPTCD FROM HRMS.ECM100 WHERE EMPCD = :EMPCD3 AND ROWNUM = 1))
                ) WHERE RN > :OFFSET AND RN <= :OFFSET + :PAGE_SIZE";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new NotificationModel
            {
                ID = Convert.ToDecimal(r["ID"]),
                TITLE = r["TITLE"]?.ToString() ?? string.Empty,
                BODY = r["BODY"]?.ToString() ?? string.Empty,
                NOTI_TYPE = r["NOTI_TYPE"]?.ToString(),
                TARGET_VAL = r["TARGET_VAL"]?.ToString(),
                LINK_ACTION = r["LINK_ACTION"]?.ToString(),
                CREATED_DATE = Convert.ToDateTime(r["CREATED_DATE"]),
                IS_READ = Convert.ToInt32(r["IS_READ_VAL"])
            }, 
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("EMPCD2", empcd),
            new OracleParameter("EMPCD3", empcd),
            new OracleParameter("OFFSET", offset),
            new OracleParameter("PAGE_SIZE", page_size));

            return Ok(new { success = true, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 3. MARK AS READ
    // ============================================================
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkAsRead(decimal notiId, string empcd)
    {
        try
        {
            string sql = @"
                MERGE INTO HRMS.HR_NOTIFICATION_LOG T
                USING (SELECT :NOTI_ID NI, :EMPCD E FROM DUAL) S
                ON (T.NOTI_ID = S.NI AND T.EMPCD = S.E)
                WHEN MATCHED THEN UPDATE SET IS_READ = 1, READ_DATE = SYSDATE
                WHEN NOT MATCHED THEN INSERT (NOTI_ID, EMPCD, IS_READ, READ_DATE) VALUES (:NOTI_ID, :EMPCD, 1, SYSDATE)";

            await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("NOTI_ID", notiId),
                new OracleParameter("EMPCD", empcd));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 4. SEND NOTIFICATION (Admin/Trigger)
    // ============================================================
    [HttpPost("send")]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest model)
    {
        try
        {
            // 1. Lưu vào database trước
            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS (TITLE, BODY, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE)
                VALUES (:TITLE, :BODY, :NOTI_TYPE, :TARGET_VAL, :LINK_ACTION, :CREATED_BY, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE", model.TITLE),
                new OracleParameter("BODY", model.BODY),
                new OracleParameter("NOTI_TYPE", model.NOTI_TYPE),
                new OracleParameter("TARGET_VAL", model.TARGET_VAL),
                new OracleParameter("LINK_ACTION", (object?)model.LINK_ACTION ?? DBNull.Value),
                new OracleParameter("CREATED_BY", (object?)model.CREATED_BY ?? DBNull.Value),
                outIdParam);

            decimal notiId = (decimal)outIdParam.Value;

            // 2. TODO: Gửi tới Firebase (Chờ bạn setup Firebase sẽ viết tiếp phần này)
            // Tạm thời mình sẽ viết hàm giả lập
            await SendToFirebasePlaceholder(model);

            return Ok(new { success = true, message = "Đã tạo thông báo", notification_id = notiId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    private async Task SendToFirebasePlaceholder(SendNotificationRequest request)
    {
        // Khi nào bạn có file service-account.json, mình sẽ dùng thư viện FirebaseAdmin 
        // để gửi tin nhắn thực tế tới các thiết bị hoặc Topics.
        await Task.CompletedTask;
    }
}
