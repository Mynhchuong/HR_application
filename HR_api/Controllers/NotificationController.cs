using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Notification;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly NotificationHelper _notiHelper;

    public NotificationController(OracleService oracleService, NotificationHelper notiHelper)
    {
        _oracleService = oracleService;
        _notiHelper = notiHelper;
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

            // BƯỚC 1: Xoá mapping của token này với user khác (1 device = 1 user)
            // → khi user mới login trên cùng device, user cũ không còn nhận noti.
            await _oracleService.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_USER_TOKENS WHERE TOKEN = :TOKEN AND EMPCD != :EMPCD",
                new OracleParameter("TOKEN", model.TOKEN),
                new OracleParameter("EMPCD", model.EMPCD));

            // BƯỚC 2: MERGE token cho user hiện tại
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
    // 1b. UNREGISTER TOKEN — gọi khi NV logout app mobile
    // POST /apiHR/Notification/unregister-token  { TOKEN, EMPCD? }
    //   Xoá mapping (EMPCD,TOKEN). Nếu không truyền EMPCD → xoá mọi mapping
    //   của TOKEN đó (an toàn khi NV đã đăng xuất khỏi web).
    // ============================================================
    [HttpPost("unregister-token")]
    public async Task<IActionResult> UnregisterToken([FromBody] TokenRegistrationRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.TOKEN))
                return Ok(new { success = false, message = "Thiếu Token" });

            if (string.IsNullOrEmpty(model.EMPCD))
            {
                await _oracleService.ExecuteNonQueryAsync(
                    "DELETE FROM HRMS.HR_USER_TOKENS WHERE TOKEN = :TOKEN",
                    new OracleParameter("TOKEN", model.TOKEN));
            }
            else
            {
                await _oracleService.ExecuteNonQueryAsync(
                    "DELETE FROM HRMS.HR_USER_TOKENS WHERE TOKEN = :TOKEN AND EMPCD = :EMPCD",
                    new OracleParameter("TOKEN", model.TOKEN),
                    new OracleParameter("EMPCD", model.EMPCD));
            }

            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
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

            // Lấy thông báo: COMPANY / DEPT / LINE / WORK / EMPCD / MULTI (qua HR_NOTIFICATION_TARGET)
            // Oracle 10g doesn't have OFFSET/FETCH, using ROW_NUMBER()
            string sql = @"
                WITH ME AS (
                    SELECT DEPTCD, LINECD, WORKCD
                    FROM HRMS.ECM100 WHERE EMPCD = :EMPCD3 AND ROWNUM = 1
                )
                SELECT * FROM (
                    SELECT N.ID, N.TITLE, N.BODY, N.TITLE_EN, N.BODY_EN,
                           N.NOTI_TYPE, N.TARGET_VAL, N.LINK_ACTION, N.CREATED_DATE,
                           N.PRIORITY, N.SOURCE,
                           NVL(L.IS_READ, 0) IS_READ_VAL,
                           U.FULL_NAME SENDER_NAME,
                           N.CREATED_BY SENDER_EMPCD,
                           ROW_NUMBER() OVER (ORDER BY
                               CASE WHEN NVL(L.IS_READ,0) = 0 AND N.PRIORITY = 'HIGH' THEN 0 ELSE 1 END,
                               N.CREATED_DATE DESC) RN
                    FROM HRMS.HR_NOTIFICATIONS N
                    LEFT JOIN HRMS.HR_NOTIFICATION_LOG L ON L.NOTI_ID = N.ID AND L.EMPCD = :EMPCD
                    LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = N.CREATED_BY
                    WHERE N.NOTI_TYPE = 'COMPANY'
                       OR (N.NOTI_TYPE = 'EMPCD' AND N.TARGET_VAL = :EMPCD2)
                       OR (N.NOTI_TYPE = 'DEPT'  AND N.TARGET_VAL = (SELECT DEPTCD FROM ME))
                       OR (N.NOTI_TYPE = 'LINE'  AND N.TARGET_VAL = (SELECT LINECD FROM ME))
                       OR (N.NOTI_TYPE = 'WORK'  AND N.TARGET_VAL = (SELECT WORKCD FROM ME))
                       OR (N.NOTI_TYPE = 'MULTI' AND EXISTS (
                           SELECT 1 FROM HRMS.HR_NOTIFICATION_TARGET T
                           WHERE T.NOTI_ID = N.ID
                             AND ( (T.TARGET_TYPE = 'EMPCD' AND T.TARGET_VAL = :EMPCD4)
                                OR (T.TARGET_TYPE = 'DEPT'  AND T.TARGET_VAL = (SELECT DEPTCD FROM ME))
                                OR (T.TARGET_TYPE = 'LINE'  AND T.TARGET_VAL = (SELECT LINECD FROM ME))
                                OR (T.TARGET_TYPE = 'WORK'  AND T.TARGET_VAL = (SELECT WORKCD FROM ME)) )
                       ))
                ) WHERE RN > :OFFSET AND RN <= :OFFSET + :PAGE_SIZE";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new NotificationModel
            {
                ID = Convert.ToDecimal(r["ID"]),
                TITLE = r["TITLE"]?.ToString() ?? string.Empty,
                BODY = r["BODY"]?.ToString() ?? string.Empty,
                TITLE_EN = r["TITLE_EN"]?.ToString(),
                BODY_EN  = r["BODY_EN"]?.ToString(),
                NOTI_TYPE = r["NOTI_TYPE"]?.ToString(),
                TARGET_VAL = r["TARGET_VAL"]?.ToString(),
                LINK_ACTION = r["LINK_ACTION"]?.ToString(),
                CREATED_DATE = Convert.ToDateTime(r["CREATED_DATE"]),
                IS_READ = Convert.ToInt32(r["IS_READ_VAL"]),
                SENDER_NAME = r["SENDER_NAME"]?.ToString(),
                SENDER_EMPCD = r["SENDER_EMPCD"]?.ToString(),
                PRIORITY = r["PRIORITY"]?.ToString() ?? "NORMAL",
                SOURCE   = r["SOURCE"]?.ToString()   ?? "SYSTEM"
            },
            new OracleParameter("EMPCD",  empcd),
            new OracleParameter("EMPCD2", empcd),
            new OracleParameter("EMPCD3", empcd),
            new OracleParameter("EMPCD4", empcd),
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
    // 4. MARK ALL AS READ
    // ============================================================
    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead(string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            // Insert log cho tất cả notification chưa đọc của empcd này
            string sql = @"
                INSERT INTO HRMS.HR_NOTIFICATION_LOG (NOTI_ID, EMPCD, IS_READ, READ_DATE)
                WITH ME AS (
                    SELECT DEPTCD, LINECD, WORKCD FROM HRMS.ECM100
                    WHERE EMPCD = :EMPCD4 AND ROWNUM = 1
                )
                SELECT N.ID, :EMPCD, 1, SYSDATE
                FROM HRMS.HR_NOTIFICATIONS N
                WHERE ( N.NOTI_TYPE = 'COMPANY'
                     OR (N.NOTI_TYPE = 'EMPCD' AND N.TARGET_VAL = :EMPCD2)
                     OR (N.NOTI_TYPE = 'DEPT'  AND N.TARGET_VAL = (SELECT DEPTCD FROM ME))
                     OR (N.NOTI_TYPE = 'LINE'  AND N.TARGET_VAL = (SELECT LINECD FROM ME))
                     OR (N.NOTI_TYPE = 'WORK'  AND N.TARGET_VAL = (SELECT WORKCD FROM ME))
                     OR (N.NOTI_TYPE = 'MULTI' AND EXISTS (
                         SELECT 1 FROM HRMS.HR_NOTIFICATION_TARGET T
                         WHERE T.NOTI_ID = N.ID
                           AND ( (T.TARGET_TYPE = 'EMPCD' AND T.TARGET_VAL = :EMPCD5)
                              OR (T.TARGET_TYPE = 'DEPT'  AND T.TARGET_VAL = (SELECT DEPTCD FROM ME))
                              OR (T.TARGET_TYPE = 'LINE'  AND T.TARGET_VAL = (SELECT LINECD FROM ME))
                              OR (T.TARGET_TYPE = 'WORK'  AND T.TARGET_VAL = (SELECT WORKCD FROM ME)) )
                     )) )
                  AND NOT EXISTS (
                      SELECT 1 FROM HRMS.HR_NOTIFICATION_LOG L
                      WHERE L.NOTI_ID = N.ID AND L.EMPCD = :EMPCD3 AND L.IS_READ = 1
                  )";

            await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("EMPCD",  empcd),
                new OracleParameter("EMPCD2", empcd),
                new OracleParameter("EMPCD3", empcd),
                new OracleParameter("EMPCD4", empcd),
                new OracleParameter("EMPCD5", empcd));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 5. UNREAD COUNT
    // ============================================================
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, count = 0 });

            string sql = @"
                WITH ME AS (
                    SELECT DEPTCD, LINECD, WORKCD FROM HRMS.ECM100
                    WHERE EMPCD = :EMPCD3 AND ROWNUM = 1
                )
                SELECT COUNT(*) CNT
                FROM HRMS.HR_NOTIFICATIONS N
                WHERE ( N.NOTI_TYPE = 'COMPANY'
                     OR (N.NOTI_TYPE = 'EMPCD' AND N.TARGET_VAL = :EMPCD)
                     OR (N.NOTI_TYPE = 'DEPT'  AND N.TARGET_VAL = (SELECT DEPTCD FROM ME))
                     OR (N.NOTI_TYPE = 'LINE'  AND N.TARGET_VAL = (SELECT LINECD FROM ME))
                     OR (N.NOTI_TYPE = 'WORK'  AND N.TARGET_VAL = (SELECT WORKCD FROM ME))
                     OR (N.NOTI_TYPE = 'MULTI' AND EXISTS (
                         SELECT 1 FROM HRMS.HR_NOTIFICATION_TARGET T
                         WHERE T.NOTI_ID = N.ID
                           AND ( (T.TARGET_TYPE = 'EMPCD' AND T.TARGET_VAL = :EMPCD4)
                              OR (T.TARGET_TYPE = 'DEPT'  AND T.TARGET_VAL = (SELECT DEPTCD FROM ME))
                              OR (T.TARGET_TYPE = 'LINE'  AND T.TARGET_VAL = (SELECT LINECD FROM ME))
                              OR (T.TARGET_TYPE = 'WORK'  AND T.TARGET_VAL = (SELECT WORKCD FROM ME)) )
                     )) )
                  AND NOT EXISTS (
                      SELECT 1 FROM HRMS.HR_NOTIFICATION_LOG L
                      WHERE L.NOTI_ID = N.ID AND L.EMPCD = :EMPCD2 AND L.IS_READ = 1
                  )";

            var rows = await _oracleService.ExecuteQueryAsync(sql,
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("EMPCD",  empcd),
                new OracleParameter("EMPCD2", empcd),
                new OracleParameter("EMPCD3", empcd),
                new OracleParameter("EMPCD4", empcd));

            return Ok(new { success = true, count = rows.FirstOrDefault() });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, count = 0, message = ex.Message });
        }
    }

    // ============================================================
    // 6. SEND NOTIFICATION (Admin/Trigger)
    // ============================================================
    [HttpPost("send")]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest model)
    {
        try
        {
            // 1. Lưu vào database trước
            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS (TITLE, BODY, TITLE_EN, BODY_EN, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE, PRIORITY, SOURCE)
                VALUES (:TITLE, :BODY, :TITLE_EN, :BODY_EN, :NOTI_TYPE, :TARGET_VAL, :LINK_ACTION, :CREATED_BY, SYSDATE, :PRIORITY, :SOURCE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE",      model.TITLE),
                new OracleParameter("BODY",       model.BODY),
                new OracleParameter("TITLE_EN",   (object?)model.TITLE_EN ?? DBNull.Value),
                new OracleParameter("BODY_EN",    (object?)model.BODY_EN  ?? DBNull.Value),
                new OracleParameter("NOTI_TYPE",  model.NOTI_TYPE),
                new OracleParameter("TARGET_VAL", model.TARGET_VAL),
                new OracleParameter("LINK_ACTION",(object?)model.LINK_ACTION ?? DBNull.Value),
                new OracleParameter("CREATED_BY", (object?)model.CREATED_BY  ?? DBNull.Value),
                new OracleParameter("PRIORITY",   model.PRIORITY ?? "NORMAL"),
                new OracleParameter("SOURCE",     model.SOURCE   ?? "SYSTEM"),
                outIdParam);

            decimal notiId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                ? od.Value : 0;

            // 2. Gửi FCM push
            _ = Task.Run(() => _notiHelper.SendFcmPublicAsync(model));

            return Ok(new { success = true, message = "Đã tạo thông báo", notification_id = notiId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 7. SEND MULTI — Admin tạo thông báo với nhiều target
    //   Body:
    //     { title, body, link_action, priority, source, created_by,
    //       send_all_company: bool,
    //       targets: [ {type:"DEPT"|"LINE"|"WORK"|"EMPCD", val:""} ] }
    // ============================================================
    [HttpPost("send-multi")]
    public async Task<IActionResult> SendMulti([FromBody] SendMultiNotificationRequest model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.TITLE) || string.IsNullOrWhiteSpace(model.BODY))
                return Ok(new { success = false, message = "Thiếu tiêu đề hoặc nội dung" });

            // Toàn công ty → vẫn dùng NOTI_TYPE='COMPANY' để query nhanh
            bool isCompany = model.SEND_ALL_COMPANY || (model.TARGETS.Count == 0);
            string notiType = isCompany ? "COMPANY"
                              : (model.TARGETS.Count == 1
                                  ? model.TARGETS[0].TYPE
                                  : "MULTI");
            string targetVal = isCompany ? "*"
                              : (model.TARGETS.Count == 1 ? model.TARGETS[0].VAL : "MULTI");

            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS (TITLE, BODY, TITLE_EN, BODY_EN, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE, PRIORITY, SOURCE)
                VALUES (:TITLE, :BODY, :TITLE_EN, :BODY_EN, :NOTI_TYPE, :TARGET_VAL, :LINK_ACTION, :CREATED_BY, SYSDATE, :PRIORITY, :SOURCE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE",      model.TITLE),
                new OracleParameter("BODY",       model.BODY),
                new OracleParameter("TITLE_EN",   (object?)model.TITLE_EN ?? DBNull.Value),
                new OracleParameter("BODY_EN",    (object?)model.BODY_EN  ?? DBNull.Value),
                new OracleParameter("NOTI_TYPE",  notiType),
                new OracleParameter("TARGET_VAL", targetVal),
                new OracleParameter("LINK_ACTION",(object?)model.LINK_ACTION ?? DBNull.Value),
                new OracleParameter("CREATED_BY", (object?)model.CREATED_BY  ?? DBNull.Value),
                new OracleParameter("PRIORITY",   model.PRIORITY),
                new OracleParameter("SOURCE",     model.SOURCE),
                outIdParam);

            decimal notiId = outIdParam.Value is OracleDecimal od && !od.IsNull ? od.Value : 0;

            // Nếu là MULTI → insert target rows
            if (notiType == "MULTI" && notiId > 0)
            {
                foreach (var t in model.TARGETS)
                {
                    if (string.IsNullOrWhiteSpace(t.VAL)) continue;
                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_NOTIFICATION_TARGET (NOTI_ID, TARGET_TYPE, TARGET_VAL)
                        VALUES (:NID, :TT, :TV)",
                        new OracleParameter("NID", notiId),
                        new OracleParameter("TT",  t.TYPE),
                        new OracleParameter("TV",  t.VAL));
                }
            }

            // FCM push
            _ = Task.Run(() => _notiHelper.SendFcmForMultiAsync(notiId, model));

            return Ok(new { success = true, message = "Đã gửi thông báo", notification_id = notiId, noti_type = notiType });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 8. SEARCH EMPLOYEE — Admin chọn nhân viên (server-side filter + paging)
    //    Query string: q (empcd), dept, line, work, page, page_size
    // ============================================================
    [HttpGet("search-emp")]
    public async Task<IActionResult> SearchEmp(string? q = null, string? dept = null, string? line = null, string? work = null, int page = 1, int page_size = 50)
    {
        try
        {
            page      = page <= 0 ? 1 : page;
            page_size = page_size <= 0 ? 50 : Math.Min(page_size, 200);
            int offset = (page - 1) * page_size;

            string baseSql = @"
                FROM HRMS.ECM100 EC
                LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (:Q IS NULL OR UPPER(EC.EMPCD) LIKE :TERM)
                  AND (:DEPT IS NULL OR EC.DEPTCD = :DEPT)
                  AND (:LINE IS NULL OR EC.LINECD = :LINE)
                  AND (:WORK IS NULL OR EC.WORKCD = :WORK)";

            string qNorm = string.IsNullOrWhiteSpace(q) ? string.Empty : q.Trim().ToUpper();
            string term  = "%" + qNorm + "%";

            // Total count
            int total = 0;
            try
            {
                var cntRows = await _oracleService.ExecuteQueryAsync(
                    "SELECT COUNT(*) C " + baseSql,
                    r => Convert.ToInt32(r["C"]),
                    new OracleParameter("Q",    string.IsNullOrEmpty(qNorm) ? (object)DBNull.Value : qNorm),
                    new OracleParameter("TERM", term),
                    new OracleParameter("DEPT", (object?)dept ?? DBNull.Value),
                    new OracleParameter("LINE", (object?)line ?? DBNull.Value),
                    new OracleParameter("WORK", (object?)work ?? DBNull.Value));
                total = cntRows.FirstOrDefault();
            }
            catch { }

            string pagedSql = @"
                SELECT * FROM (
                    SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                           B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                           ROW_NUMBER() OVER (ORDER BY EC.EMPCD) RN
                    " + baseSql + @"
                ) WHERE RN > :OFFSET AND RN <= :OFFSET + :PAGE_SIZE";

            var data = await _oracleService.ExecuteQueryAsync(pagedSql,
                r => new EmployeeLookupItem
                {
                    EMPCD     = r["EMPCD"]?.ToString() ?? "",
                    EMP_NAME  = r["EMP_NAME"]?.ToString() ?? "",
                    DEPT_NAME = r["DEPT_NAME"]?.ToString(),
                    LINE_NAME = r["LINE_NAME"]?.ToString(),
                    WORK_NAME = r["WORK_NAME"]?.ToString()
                },
                new OracleParameter("Q",    string.IsNullOrEmpty(qNorm) ? (object)DBNull.Value : qNorm),
                new OracleParameter("TERM", term),
                new OracleParameter("DEPT", (object?)dept ?? DBNull.Value),
                new OracleParameter("LINE", (object?)line ?? DBNull.Value),
                new OracleParameter("WORK", (object?)work ?? DBNull.Value),
                new OracleParameter("OFFSET", offset),
                new OracleParameter("PAGE_SIZE", page_size));

            return Ok(new { success = true, total, page, page_size, has_more = offset + data.Count < total, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 9. SEARCH EMPLOYEE CODES — Lấy toàn bộ EMPCD khớp filter (cho "Chọn tất cả khớp filter")
    // ============================================================
    [HttpGet("search-emp-codes")]
    public async Task<IActionResult> SearchEmpCodes(string? q = null, string? dept = null, string? line = null, string? work = null)
    {
        try
        {
            string qNorm = string.IsNullOrWhiteSpace(q) ? string.Empty : q.Trim().ToUpper();
            string term  = "%" + qNorm + "%";

            string sql = @"
                SELECT EC.EMPCD
                FROM HRMS.ECM100 EC
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (:Q IS NULL OR UPPER(EC.EMPCD) LIKE :TERM)
                  AND (:DEPT IS NULL OR EC.DEPTCD = :DEPT)
                  AND (:LINE IS NULL OR EC.LINECD = :LINE)
                  AND (:WORK IS NULL OR EC.WORKCD = :WORK)";

            var codes = await _oracleService.ExecuteQueryAsync(sql,
                r => r["EMPCD"]?.ToString() ?? "",
                new OracleParameter("Q",    string.IsNullOrEmpty(qNorm) ? (object)DBNull.Value : qNorm),
                new OracleParameter("TERM", term),
                new OracleParameter("DEPT", (object?)dept ?? DBNull.Value),
                new OracleParameter("LINE", (object?)line ?? DBNull.Value),
                new OracleParameter("WORK", (object?)work ?? DBNull.Value));

            return Ok(new { success = true, total = codes.Count, codes });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 10. LOOKUP — Danh sách Dept / Line / Work (dùng cho dropdown)
    //     ?type=DEPT|LINE|WORK
    // ============================================================
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup(string type = "DEPT")
    {
        try
        {
            type = (type ?? "DEPT").ToUpper();

            string col = type switch
            {
                "DEPT" => "DEPTCD",
                "LINE" => "LINECD",
                "WORK" => "WORKCD",
                _      => "DEPTCD"
            };
            string nameCol = type switch
            {
                "DEPT" => "DEPTNM",
                "LINE" => "TEAMNM",
                "WORK" => "WORKNM",
                _      => "DEPTNM"
            };

            string sql = $@"
                SELECT B.{col} CODE, MAX(B.{nameCol}) NAME, COUNT(DISTINCT EC.EMPCD) EMP_COUNT
                FROM HRMS.EAM410 B
                LEFT JOIN HRMS.ECM100 EC ON EC.{col} = B.{col}
                    AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                WHERE B.{col} IS NOT NULL AND B.{nameCol} IS NOT NULL
                GROUP BY B.{col}
                ORDER BY MAX(B.{nameCol})";

            var data = await _oracleService.ExecuteQueryAsync(sql,
                r => new OrgLookupItem
                {
                    CODE = r["CODE"]?.ToString() ?? "",
                    NAME = r["NAME"]?.ToString() ?? "",
                    EMP_COUNT = r["EMP_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["EMP_COUNT"])
                });

            return Ok(new { success = true, type, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 11b. ADMIN: DELETE noti — chỉ cho phép trong vòng 5 phút sau khi gửi
    // DELETE /apiHR/Notification/admin/{id}
    // ============================================================
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> AdminDelete(long id)
    {
        try
        {
            if (id <= 0) return Ok(new { success = false, message = "Thiếu ID" });

            // 1) Check tồn tại + thời gian
            var rows = await _oracleService.ExecuteQueryAsync(
                @"SELECT CREATED_DATE,
                         ROUND((SYSDATE - CREATED_DATE) * 24 * 60, 2) AS MINUTES_OLD
                  FROM HRMS.HR_NOTIFICATIONS WHERE ID = :ID",
                r => new {
                    createdDate = Convert.ToDateTime(r["CREATED_DATE"]),
                    minutesOld  = Convert.ToDecimal(r["MINUTES_OLD"])
                },
                new OracleParameter("ID", id));

            var row = rows.FirstOrDefault();
            if (row == null) return Ok(new { success = false, message = "Không tìm thấy thông báo" });

            if (row.minutesOld > 5)
                return Ok(new {
                    success = false,
                    code    = "TOO_LATE",
                    message = $"Quá hạn xoá. Thông báo đã gửi {Math.Floor(row.minutesOld)} phút trước (chỉ được xoá trong 5 phút đầu)."
                });

            // 2) Xoá con trước
            await _oracleService.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_NOTIFICATION_TARGET WHERE NOTI_ID = :ID",
                new OracleParameter("ID", id));
            await _oracleService.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_NOTIFICATION_LOG WHERE NOTI_ID = :ID",
                new OracleParameter("ID", id));

            // 3) Xoá noti
            int n = await _oracleService.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_NOTIFICATIONS WHERE ID = :ID",
                new OracleParameter("ID", id));

            if (n == 0) return Ok(new { success = false, message = "Xoá không thành công" });
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 11c. ADMIN: DETAIL — Chi tiết thông báo + danh sách NV nhận
    // GET /apiHR/Notification/admin/detail/{id}
    // ============================================================
    [HttpGet("admin/detail/{id:long}")]
    public async Task<IActionResult> AdminDetail(long id)
    {
        try
        {
            if (id <= 0) return Ok(new { success = false, message = "Thiếu ID" });

            var notiList = await _oracleService.ExecuteQueryAsync(
                @"SELECT N.ID, N.TITLE, N.BODY, N.NOTI_TYPE, N.TARGET_VAL,
                         N.PRIORITY, N.SOURCE, N.LINK_ACTION,
                         N.CREATED_BY, U.FULL_NAME SENDER_NAME, N.CREATED_DATE
                  FROM HRMS.HR_NOTIFICATIONS N
                  LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = N.CREATED_BY
                  WHERE N.ID = :ID",
                r => new {
                    id          = Convert.ToDecimal(r["ID"]),
                    title       = r["TITLE"]?.ToString() ?? "",
                    body        = r["BODY"]?.ToString() ?? "",
                    notiType    = r["NOTI_TYPE"]?.ToString() ?? "",
                    targetVal   = r["TARGET_VAL"]?.ToString(),
                    priority    = r["PRIORITY"]?.ToString(),
                    source      = r["SOURCE"]?.ToString(),
                    linkAction  = r["LINK_ACTION"]?.ToString(),
                    senderName  = r["SENDER_NAME"]?.ToString(),
                    senderEmpCd = r["CREATED_BY"]?.ToString(),
                    createdDate = Convert.ToDateTime(r["CREATED_DATE"])
                },
                new OracleParameter("ID", id));

            var noti = notiList.FirstOrDefault();
            if (noti == null) return Ok(new { success = false, message = "Không tìm thấy thông báo" });

            // Resolve danh sách NV nhận theo NOTI_TYPE
            string audienceSql;
            var pars = new List<OracleParameter> { new OracleParameter("ID", id) };

            switch (noti.notiType)
            {
                case "COMPANY":
                    audienceSql = @"
                        SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                               B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME
                        FROM HRMS.ECM100 EC
                        LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                        ORDER BY EC.EMPCD";
                    break;

                case "DEPT":
                    audienceSql = @"
                        SELECT EC.EMPCD, EC.CNAME EMP_NAME, B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME
                        FROM HRMS.ECM100 EC
                        LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.DEPTCD = :TVAL
                          AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                        ORDER BY EC.EMPCD";
                    pars.Add(new OracleParameter("TVAL", noti.targetVal));
                    break;

                case "LINE":
                    audienceSql = @"
                        SELECT EC.EMPCD, EC.CNAME EMP_NAME, B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME
                        FROM HRMS.ECM100 EC
                        LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.LINECD = :TVAL
                          AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                        ORDER BY EC.EMPCD";
                    pars.Add(new OracleParameter("TVAL", noti.targetVal));
                    break;

                case "WORK":
                    audienceSql = @"
                        SELECT EC.EMPCD, EC.CNAME EMP_NAME, B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME
                        FROM HRMS.ECM100 EC
                        LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.WORKCD = :TVAL
                          AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                        ORDER BY EC.EMPCD";
                    pars.Add(new OracleParameter("TVAL", noti.targetVal));
                    break;

                case "EMPCD":
                    audienceSql = @"
                        SELECT EC.EMPCD, EC.CNAME EMP_NAME, B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME
                        FROM HRMS.ECM100 EC
                        LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.EMPCD = :TVAL";
                    pars.Add(new OracleParameter("TVAL", noti.targetVal));
                    break;

                case "MULTI":
                    audienceSql = @"
                        SELECT DISTINCT EC.EMPCD, EC.CNAME EMP_NAME,
                               B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME
                        FROM HRMS.HR_NOTIFICATION_TARGET T
                        JOIN HRMS.ECM100 EC ON
                                (T.TARGET_TYPE = 'EMPCD' AND EC.EMPCD = T.TARGET_VAL)
                             OR (T.TARGET_TYPE = 'DEPT'  AND EC.DEPTCD = T.TARGET_VAL)
                             OR (T.TARGET_TYPE = 'LINE'  AND EC.LINECD = T.TARGET_VAL)
                             OR (T.TARGET_TYPE = 'WORK'  AND EC.WORKCD = T.TARGET_VAL)
                        LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE T.NOTI_ID = :ID
                          AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                        ORDER BY EC.EMPCD";
                    break;

                default:
                    audienceSql = "SELECT 1 FROM DUAL WHERE 1=0";
                    break;
            }

            var audience = await _oracleService.ExecuteQueryAsync(audienceSql, r => new {
                empCd     = r["EMPCD"]?.ToString() ?? "",
                empName   = r["EMP_NAME"]?.ToString() ?? "",
                deptName  = r["DEPT_NAME"]?.ToString() ?? "",
                lineName  = r["LINE_NAME"]?.ToString() ?? "",
                workName  = r["WORK_NAME"]?.ToString() ?? ""
            }, pars.ToArray());

            // Đếm đã đọc trong audience
            int readCount = 0;
            if (audience.Count > 0)
            {
                var cnt = await _oracleService.ExecuteQueryAsync(
                    "SELECT COUNT(*) C FROM HRMS.HR_NOTIFICATION_LOG WHERE NOTI_ID = :ID AND IS_READ = 1",
                    r => Convert.ToInt32(r["C"]),
                    new OracleParameter("ID", id));
                readCount = cnt.FirstOrDefault();
            }

            return Ok(new {
                success = true,
                noti,
                audience,
                totalReceiver = audience.Count,
                readCount,
                unreadCount   = audience.Count - readCount
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ============================================================
    // 11. ADMIN: LIST SENT — Danh sách thông báo Admin đã gửi
    // ============================================================
    [HttpGet("admin/sent")]
    public async Task<IActionResult> AdminListSent(int page = 1, int page_size = 30)
    {
        try
        {
            page      = page <= 0 ? 1 : page;
            page_size = Math.Min(Math.Max(page_size, 1), 100);
            int offset = (page - 1) * page_size;

            int total = 0;
            try
            {
                var c = await _oracleService.ExecuteQueryAsync(
                    "SELECT COUNT(*) C FROM HRMS.HR_NOTIFICATIONS WHERE SOURCE IN ('ADMIN','HR')",
                    r => Convert.ToInt32(r["C"]));
                total = c.FirstOrDefault();
            }
            catch { }

            string sql = @"
                SELECT * FROM (
                    SELECT N.ID, N.TITLE, N.BODY, N.NOTI_TYPE, N.TARGET_VAL,
                           N.PRIORITY, N.SOURCE, N.LINK_ACTION,
                           N.CREATED_BY, U.FULL_NAME SENDER_NAME, N.CREATED_DATE,
                           (SELECT COUNT(*) FROM HRMS.HR_NOTIFICATION_TARGET T WHERE T.NOTI_ID = N.ID) TARGET_COUNT,
                           ROW_NUMBER() OVER (ORDER BY N.CREATED_DATE DESC) RN
                    FROM HRMS.HR_NOTIFICATIONS N
                    LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = N.CREATED_BY
                    WHERE N.SOURCE IN ('ADMIN','HR')
                ) WHERE RN > :OFFSET AND RN <= :OFFSET + :PAGE_SIZE";

            var data = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                id           = Convert.ToDecimal(r["ID"]),
                title        = r["TITLE"]?.ToString() ?? "",
                body         = r["BODY"]?.ToString() ?? "",
                notiType     = r["NOTI_TYPE"]?.ToString(),
                targetVal    = r["TARGET_VAL"]?.ToString(),
                priority     = r["PRIORITY"]?.ToString(),
                source       = r["SOURCE"]?.ToString(),
                linkAction   = r["LINK_ACTION"]?.ToString(),
                senderName   = r["SENDER_NAME"]?.ToString(),
                senderEmpCd  = r["CREATED_BY"]?.ToString(),
                createdDate  = Convert.ToDateTime(r["CREATED_DATE"]),
                targetCount  = r["TARGET_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["TARGET_COUNT"])
            },
            new OracleParameter("OFFSET", offset),
            new OracleParameter("PAGE_SIZE", page_size));

            return Ok(new { success = true, total, page, page_size, data });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
