using System.Globalization;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Inquiry;
using HR_api.Models.Notification;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class InquiryController : ControllerBase
{
    private readonly OracleService      _db;
    private readonly NotificationHelper _notiHelper;
    private readonly IServiceScopeFactory _scopeFactory;

    public InquiryController(OracleService db, NotificationHelper notiHelper, IServiceScopeFactory scopeFactory)
    {
        _db          = db;
        _notiHelper  = notiHelper;
        _scopeFactory = scopeFactory;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/topics
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("topics")]
    public async Task<IActionResult> GetTopics()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT TOPIC_CD, TOPIC_NAME, TOPIC_NAME_EN, ICON, COLOR, SORT_ORDER
                FROM HRMS.HR_INQUIRY_TOPIC
                WHERE IS_ACTIVE = 1
                ORDER BY SORT_ORDER",
                r => new InquiryTopicDto
                {
                    TopicCd     = r["TOPIC_CD"]?.ToString()    ?? "",
                    TopicName   = r["TOPIC_NAME"]?.ToString()  ?? "",
                    TopicNameEn = r["TOPIC_NAME_EN"]?.ToString(),
                    Icon        = r["ICON"]?.ToString(),
                    Color       = r["COLOR"]?.ToString(),
                    SortOrder   = r["SORT_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["SORT_ORDER"])
                });

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/create
    // Tạo cuộc hội thoại mới + tin nhắn đầu tiên
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] InquiryCreateRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.TopicCd))
                return Ok(new { success = false, message = "Thiếu chủ đề" });
            if (string.IsNullOrWhiteSpace(req.Content))
                return Ok(new { success = false, message = "Tin nhắn không được để trống" });
            if (req.ChatType == "DIRECT" && string.IsNullOrWhiteSpace(req.EmpCd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });
            if (req.ChatType == "ANON" && string.IsNullOrWhiteSpace(req.AnonToken))
                return Ok(new { success = false, message = "Thiếu token ẩn danh" });

            // Kiểm tra đã có conversation OPEN cho topic này chưa
            if (req.ChatType == "DIRECT")
            {
                var existing = await _db.ExecuteQueryAsync(@"
                    SELECT ID FROM HRMS.HR_INQUIRY
                    WHERE EMPCD = :EMP AND TOPIC_CD = :TOPIC AND STATUS = 'OPEN' AND ROWNUM = 1",
                    r => r["ID"]?.ToString(),
                    new OracleParameter("EMP",   req.EmpCd!),
                    new OracleParameter("TOPIC", req.TopicCd));
                if (existing.Count > 0)
                    return Ok(new { success = false, message = "Bạn đang có một hội thoại mở về chủ đề này. Vui lòng đóng hội thoại cũ trước." });
            }
            else
            {
                var existing = await _db.ExecuteQueryAsync(@"
                    SELECT ID FROM HRMS.HR_INQUIRY
                    WHERE ANON_TOKEN = :TOKEN AND TOPIC_CD = :TOPIC AND STATUS = 'OPEN' AND ROWNUM = 1",
                    r => r["ID"]?.ToString(),
                    new OracleParameter("TOKEN", req.AnonToken!),
                    new OracleParameter("TOPIC", req.TopicCd));
                if (existing.Count > 0)
                    return Ok(new { success = false, message = "Bạn đang có một hội thoại mở về chủ đề này." });
            }

            // Lấy tên nhân viên nếu là DIRECT
            string empName = "";
            if (req.ChatType == "DIRECT" && !string.IsNullOrWhiteSpace(req.EmpCd))
            {
                var names = await _db.ExecuteQueryAsync(
                    "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                    r => r["CNAME"]?.ToString() ?? "",
                    new OracleParameter("EMPCD", req.EmpCd));
                empName = names.FirstOrDefault() ?? "";
            }

            // Tạo ANON_DISPLAY từ 4 ký tự cuối token
            string? anonDisplay = null;
            if (req.ChatType == "ANON" && !string.IsNullOrWhiteSpace(req.AnonToken))
            {
                string tok = req.AnonToken.Replace("-", "");
                anonDisplay = "Ẩn danh #" + tok[^Math.Min(4, tok.Length)..].ToUpper();
            }

            // Insert HR_INQUIRY (trigger tự set ID + INQUIRY_NO)
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY
                    (CHAT_TYPE, EMPCD, EMP_NAME, ANON_TOKEN, ANON_DISPLAY,
                     TOPIC_CD, SUBJECT, STATUS, UNREAD_HR, UNREAD_EMP, MSG_COUNT, INST_DT)
                VALUES
                    (:CHAT_TYPE, :EMPCD, :EMP_NAME, :ANON_TOKEN, :ANON_DISPLAY,
                     :TOPIC_CD, :SUBJECT, 'OPEN', 0, 0, 0, SYSDATE)",
                new OracleParameter("CHAT_TYPE",    req.ChatType),
                new OracleParameter("EMPCD",        (object?)req.EmpCd     ?? DBNull.Value),
                new OracleParameter("EMP_NAME",     string.IsNullOrEmpty(empName) ? (object)DBNull.Value : empName),
                new OracleParameter("ANON_TOKEN",   (object?)req.AnonToken ?? DBNull.Value),
                new OracleParameter("ANON_DISPLAY", (object?)anonDisplay   ?? DBNull.Value),
                new OracleParameter("TOPIC_CD",     req.TopicCd),
                new OracleParameter("SUBJECT",      (object?)req.Subject   ?? DBNull.Value));

            // Lấy ID vừa tạo — filter theo EMPCD/TOKEN + TOPIC_CD + STATUS='OPEN'
            // (an toàn hơn TRUNC(SYSDATE) vì không bị lỗi nửa đêm)
            List<dynamic> ids;
            if (req.ChatType == "DIRECT")
            {
                ids = await _db.ExecuteQueryAsync(@"
                    SELECT ID, INQUIRY_NO FROM (
                        SELECT ID, INQUIRY_NO FROM HRMS.HR_INQUIRY
                        WHERE EMPCD = :EMPCD AND TOPIC_CD = :TOPIC AND STATUS = 'OPEN'
                        ORDER BY ID DESC
                    ) WHERE ROWNUM = 1",
                    r => (dynamic)new { Id = Convert.ToInt64(r["ID"]), No = r["INQUIRY_NO"]?.ToString() ?? "" },
                    new OracleParameter("EMPCD",  req.EmpCd!),
                    new OracleParameter("TOPIC",  req.TopicCd));
            }
            else
            {
                ids = await _db.ExecuteQueryAsync(@"
                    SELECT ID, INQUIRY_NO FROM (
                        SELECT ID, INQUIRY_NO FROM HRMS.HR_INQUIRY
                        WHERE ANON_TOKEN = :TOKEN AND TOPIC_CD = :TOPIC AND STATUS = 'OPEN'
                        ORDER BY ID DESC
                    ) WHERE ROWNUM = 1",
                    r => (dynamic)new { Id = Convert.ToInt64(r["ID"]), No = r["INQUIRY_NO"]?.ToString() ?? "" },
                    new OracleParameter("TOKEN", req.AnonToken!),
                    new OracleParameter("TOPIC", req.TopicCd));
            }

            if (ids.Count == 0)
                return Ok(new { success = false, message = "Lỗi tạo hội thoại" });

            long inquiryId  = ids[0].Id;
            string inquiryNo = ids[0].No;

            // Insert tin nhắn đầu tiên
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY_MSG
                    (INQUIRY_ID, SENDER_TYPE, SENDER_CD, SENDER_NAME, MSG_TYPE, CONTENT, IS_READ_HR, IS_READ_EMP, SENT_DT)
                VALUES
                    (:INQ_ID, 'EMP', :SENDER_CD, :SENDER_NAME, 'TEXT', :CONTENT, 0, 1, SYSDATE)",
                new OracleParameter("INQ_ID",      inquiryId),
                new OracleParameter("SENDER_CD",   (object?)req.EmpCd  ?? DBNull.Value),
                new OracleParameter("SENDER_NAME", string.IsNullOrEmpty(empName) ? (object)DBNull.Value : empName),
                new OracleParameter("CONTENT",     req.Content));

            // Update stats trên HR_INQUIRY
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET LAST_MSG_DT = SYSDATE, MSG_COUNT = 1, UNREAD_HR = 1
                WHERE ID = :ID",
                new OracleParameter("ID", inquiryId));

            return Ok(new { success = true, inquiryId, inquiryNo });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/my?empCd=
    // Danh sách conversation của nhân viên (DIRECT)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my")]
    public async Task<IActionResult> GetMyList(string empCd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empCd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            var rows = await _db.ExecuteQueryAsync(@"
                SELECT i.ID, i.INQUIRY_NO, i.CHAT_TYPE, i.TOPIC_CD, t.TOPIC_NAME,
                       t.COLOR AS TOPIC_COLOR, t.ICON AS TOPIC_ICON,
                       i.SUBJECT, i.EMPCD, i.EMP_NAME, NULL AS ANON_DISPLAY, NULL AS ANON_TOKEN, i.STATUS,
                       i.ASSIGNED_TO, i.ASSIGNED_NAME,
                       i.UNREAD_HR, i.UNREAD_EMP, i.MSG_COUNT, i.LAST_MSG_DT, i.INST_DT,
                       i.CLOSED_DT, i.CLOSED_BY_NAME, i.CLOSED_BY_TYPE, i.RATING
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE i.EMPCD = :EMPCD
                ORDER BY CASE WHEN i.STATUS = 'OPEN' THEN 0 ELSE 1 END, i.LAST_MSG_DT DESC",
                r => MapListItem(r),
                new OracleParameter("EMPCD", empCd));

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/anon?token=
    // Danh sách conversation của anonymous user
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("anon")]
    public async Task<IActionResult> GetAnonList(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return Ok(new { success = false, message = "Thiếu token" });

            var rows = await _db.ExecuteQueryAsync(@"
                SELECT i.ID, i.INQUIRY_NO, i.CHAT_TYPE, i.TOPIC_CD, t.TOPIC_NAME,
                       t.COLOR AS TOPIC_COLOR, t.ICON AS TOPIC_ICON,
                       i.SUBJECT, NULL AS EMPCD, NULL AS EMP_NAME, i.ANON_DISPLAY, NULL AS ANON_TOKEN, i.STATUS,
                       i.ASSIGNED_TO, i.ASSIGNED_NAME,
                       i.UNREAD_HR, i.UNREAD_EMP, i.MSG_COUNT, i.LAST_MSG_DT, i.INST_DT,
                       i.CLOSED_DT, i.CLOSED_BY_NAME, i.CLOSED_BY_TYPE, i.RATING
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE i.ANON_TOKEN = :TOKEN
                ORDER BY CASE WHEN i.STATUS = 'OPEN' THEN 0 ELSE 1 END, i.LAST_MSG_DT DESC",
                r => MapListItem(r),
                new OracleParameter("TOKEN", token));

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/messages?id=&afterMsgId=
    // Load messages (+ attachments) của 1 conversation
    // afterMsgId = 0 → lấy tất cả; > 0 → chỉ lấy tin mới hơn (polling)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(long id, long afterMsgId = 0)
    {
        try
        {
            if (id <= 0)
                return Ok(new { success = false, message = "Thiếu ID hội thoại" });

            // Lấy thông tin header của conversation (dùng MapListItemDetail để có LOCKED_DT, CLOSE_NOTE, RATING_NOTE)
            var headers = await _db.ExecuteQueryAsync(@"
                SELECT i.ID, i.INQUIRY_NO, i.CHAT_TYPE, i.TOPIC_CD, t.TOPIC_NAME,
                       t.COLOR AS TOPIC_COLOR, t.ICON AS TOPIC_ICON,
                       i.SUBJECT, i.EMPCD, i.EMP_NAME, i.ANON_DISPLAY, i.ANON_TOKEN, i.STATUS,
                       i.ASSIGNED_TO, i.ASSIGNED_NAME, i.LOCKED_DT,
                       i.UNREAD_HR, i.UNREAD_EMP, i.MSG_COUNT, i.LAST_MSG_DT, i.INST_DT,
                       i.CLOSED_DT, i.CLOSED_BY_NAME, i.CLOSED_BY_TYPE, i.CLOSE_NOTE,
                       i.RATING, i.RATING_NOTE
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE i.ID = :ID",
                r => MapListItemDetail(r),
                new OracleParameter("ID", id));

            if (headers.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy hội thoại" });

            // Lấy messages + attachments bằng LEFT JOIN
            var rawRows = await _db.ExecuteQueryAsync(@"
                SELECT m.ID          AS MSG_ID,
                       m.INQUIRY_ID,
                       m.SENDER_TYPE,
                       m.SENDER_CD,
                       m.SENDER_NAME,
                       m.MSG_TYPE,
                       m.CONTENT,
                       m.IS_READ_HR,
                       m.IS_READ_EMP,
                       m.IS_DELETED,
                       m.SENT_DT,
                       a.ID          AS ATCH_ID,
                       a.FILE_NAME,
                       a.FILE_PATH,
                       a.FILE_TYPE   AS ATCH_FILE_TYPE,
                       a.MIME_TYPE,
                       a.FILE_SIZE,
                       a.THUMB_PATH
                FROM HRMS.HR_INQUIRY_MSG m
                LEFT JOIN HRMS.HR_INQUIRY_ATTACH a ON a.MSG_ID = m.ID
                WHERE m.INQUIRY_ID = :ID
                  AND (:AFTER = 0 OR m.ID > :AFTER2)
                ORDER BY m.ID ASC",
                r => new
                {
                    MsgId      = Convert.ToInt64(r["MSG_ID"]),
                    InquiryId  = Convert.ToInt64(r["INQUIRY_ID"]),
                    SenderType = r["SENDER_TYPE"]?.ToString() ?? "",
                    SenderCd   = r["SENDER_CD"]?.ToString(),
                    SenderName = r["SENDER_NAME"]?.ToString(),
                    MsgType    = r["MSG_TYPE"]?.ToString()    ?? "TEXT",
                    Content    = r["CONTENT"]?.ToString(),
                    IsReadHr   = r["IS_READ_HR"]  != DBNull.Value && Convert.ToInt32(r["IS_READ_HR"])  == 1,
                    IsReadEmp  = r["IS_READ_EMP"] != DBNull.Value && Convert.ToInt32(r["IS_READ_EMP"]) == 1,
                    IsDeleted  = r["IS_DELETED"]  != DBNull.Value && Convert.ToInt32(r["IS_DELETED"])  == 1,
                    SentDt     = r["SENT_DT"] != DBNull.Value ? Convert.ToDateTime(r["SENT_DT"]) : DateTime.MinValue,
                    AtchId     = r["ATCH_ID"] != DBNull.Value ? (long?)Convert.ToInt64(r["ATCH_ID"]) : null,
                    FileName   = r["FILE_NAME"]?.ToString(),
                    FilePath   = r["FILE_PATH"]?.ToString(),
                    AtchType   = r["ATCH_FILE_TYPE"]?.ToString(),
                    MimeType   = r["MIME_TYPE"]?.ToString(),
                    FileSize   = r["FILE_SIZE"] != DBNull.Value ? (long?)Convert.ToInt64(r["FILE_SIZE"]) : null,
                    ThumbPath  = r["THUMB_PATH"]?.ToString()
                },
                new OracleParameter("ID",    id),
                new OracleParameter("AFTER",  afterMsgId),
                new OracleParameter("AFTER2", afterMsgId));

            // Group theo MSG_ID để gộp attachments
            var messages = rawRows
                .GroupBy(r => r.MsgId)
                .Select(g =>
                {
                    var first = g.First();
                    return new InquiryMsgDto
                    {
                        Id         = first.MsgId,
                        InquiryId  = first.InquiryId,
                        SenderType = first.SenderType,
                        SenderCd   = first.SenderCd,
                        SenderName = first.SenderName,
                        MsgType    = first.MsgType,
                        Content    = first.Content,
                        IsReadHr   = first.IsReadHr,
                        IsReadEmp  = first.IsReadEmp,
                        IsDeleted  = first.IsDeleted,
                        SentDt     = first.SentDt,
                        Attachments = g
                            .Where(x => x.AtchId.HasValue)
                            .Select(x => new InquiryAttachDto
                            {
                                Id        = x.AtchId!.Value,
                                FileName  = x.FileName  ?? "",
                                FilePath  = x.FilePath  ?? "",
                                FileType  = x.AtchType  ?? "",
                                MimeType  = x.MimeType,
                                FileSize  = x.FileSize,
                                ThumbPath = x.ThumbPath
                            }).ToList()
                    };
                }).ToList();

            return Ok(new { success = true, inquiry = headers[0], messages });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/send
    // Gửi tin nhắn (kèm file metadata nếu có)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] InquirySendRequest req)
    {
        try
        {
            if (req.InquiryId <= 0)
                return Ok(new { success = false, message = "Thiếu ID hội thoại" });

            bool hasContent = !string.IsNullOrWhiteSpace(req.Content);
            bool hasFiles   = req.Files.Count > 0;
            if (!hasContent && !hasFiles)
                return Ok(new { success = false, message = "Tin nhắn không được để trống" });

            // Lấy thông tin conversation hiện tại
            var convRows = await _db.ExecuteQueryAsync(@"
                SELECT STATUS, ASSIGNED_TO, ASSIGNED_NAME, EMPCD, ANON_TOKEN, INQUIRY_NO
                FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                r => new
                {
                    Status       = r["STATUS"]?.ToString()        ?? "",
                    AssignedTo   = r["ASSIGNED_TO"]?.ToString(),
                    AssignedName = r["ASSIGNED_NAME"]?.ToString(),
                    EmpCd        = r["EMPCD"]?.ToString(),
                    AnonToken    = r["ANON_TOKEN"]?.ToString(),
                    InquiryNo    = r["INQUIRY_NO"]?.ToString()    ?? ""
                },
                new OracleParameter("ID", req.InquiryId));

            if (convRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy hội thoại" });

            var conv = convRows[0];

            if (conv.Status != "OPEN")
                return Ok(new { success = false, message = "Hội thoại đã đóng, không thể gửi tin" });

            // Kiểm tra quyền gửi
            bool isHr    = req.SenderType == "HR";
            bool isAdmin = req.SenderType == "ADMIN";

            if (!isAdmin)
            {
                if (isHr)
                {
                    // HR chỉ gửi được nếu chưa có ai lock, hoặc lock là chính mình
                    bool lockedByOther = !string.IsNullOrEmpty(conv.AssignedTo)
                                        && conv.AssignedTo != req.EmpCd;
                    if (lockedByOther)
                        return Ok(new
                        {
                            success  = false,
                            message  = $"Hội thoại đang được xử lý bởi {conv.AssignedName}",
                            lockedBy = conv.AssignedName
                        });
                }
                else
                {
                    // Employee: phải là người tạo conversation
                    bool isOwner = (!string.IsNullOrEmpty(conv.EmpCd) && conv.EmpCd == req.EmpCd)
                                || (!string.IsNullOrEmpty(conv.AnonToken) && conv.AnonToken == req.AnonToken);
                    if (!isOwner)
                        return Ok(new { success = false, message = "Bạn không có quyền gửi tin vào hội thoại này" });
                }
            }

            // Lock conversation nếu HR/Admin gửi lần đầu (ASSIGNED_TO IS NULL)
            if ((isHr || isAdmin) && string.IsNullOrEmpty(conv.AssignedTo))
            {
                string lockEmpCd   = req.EmpCd   ?? "";
                string lockName    = req.SenderName ?? "";
                int lockRows = await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY
                    SET ASSIGNED_TO = :HR_CD, ASSIGNED_NAME = :HR_NAME, LOCKED_DT = SYSDATE
                    WHERE ID = :ID AND ASSIGNED_TO IS NULL AND STATUS = 'OPEN'",
                    new OracleParameter("HR_CD",   lockEmpCd),
                    new OracleParameter("HR_NAME", lockName),
                    new OracleParameter("ID",      req.InquiryId));

                if (lockRows == 0)
                {
                    // Race condition: người khác đã lock trước
                    var locker = await _db.ExecuteQueryAsync(
                        "SELECT ASSIGNED_NAME FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                        r => r["ASSIGNED_NAME"]?.ToString() ?? "",
                        new OracleParameter("ID", req.InquiryId));
                    string lockerName = locker.FirstOrDefault() ?? "HR khác";
                    return Ok(new
                    {
                        success  = false,
                        message  = $"Hội thoại vừa được tiếp nhận bởi {lockerName}",
                        lockedBy = lockerName
                    });
                }
            }
            else if ((isHr || isAdmin) && conv.AssignedTo != req.EmpCd)
            {
                // Admin gửi vào lock của HR khác → chuyển lock sang Admin
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY
                    SET ASSIGNED_TO = :HR_CD, ASSIGNED_NAME = :HR_NAME, LOCKED_DT = SYSDATE
                    WHERE ID = :ID AND STATUS = 'OPEN'",
                    new OracleParameter("HR_CD",   req.EmpCd   ?? ""),
                    new OracleParameter("HR_NAME", req.SenderName ?? ""),
                    new OracleParameter("ID",      req.InquiryId));
            }

            // Insert tin nhắn
            string msgType = hasFiles && !hasContent ? "FILE"
                           : hasFiles && hasContent  ? "TEXT"
                           : "TEXT";
            // Nếu files có ảnh → dùng IMAGE type nếu chỉ gửi ảnh
            if (hasFiles && !hasContent && req.Files.All(f => f.FileType == "IMAGE"))
                msgType = "IMAGE";

            string senderTypeForDb = isAdmin ? "HR" : req.SenderType; // ADMIN lưu DB là HR

            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY_MSG
                    (INQUIRY_ID, SENDER_TYPE, SENDER_CD, SENDER_NAME, MSG_TYPE, CONTENT,
                     IS_READ_HR, IS_READ_EMP, IS_DELETED, SENT_DT)
                VALUES
                    (:INQ_ID, :SENDER_TYPE, :SENDER_CD, :SENDER_NAME, :MSG_TYPE, :CONTENT,
                     :IS_READ_HR, :IS_READ_EMP, 0, SYSDATE)",
                new OracleParameter("INQ_ID",      req.InquiryId),
                new OracleParameter("SENDER_TYPE", senderTypeForDb),
                new OracleParameter("SENDER_CD",   (object?)req.EmpCd      ?? DBNull.Value),
                new OracleParameter("SENDER_NAME", (object?)req.SenderName ?? DBNull.Value),
                new OracleParameter("MSG_TYPE",    msgType),
                new OracleParameter("CONTENT",     (object?)req.Content    ?? DBNull.Value),
                new OracleParameter("IS_READ_HR",  isHr || isAdmin ? 1 : 0),   // HR tự đọc tin mình gửi
                new OracleParameter("IS_READ_EMP", !isHr && !isAdmin ? 1 : 0));// EMP tự đọc tin mình gửi

            // Lấy MSG_ID vừa insert — filter thêm SENDER_TYPE + SENDER_CD để tránh race condition
            var msgIds = await _db.ExecuteQueryAsync(@"
                SELECT ID FROM (
                    SELECT ID FROM HRMS.HR_INQUIRY_MSG
                    WHERE INQUIRY_ID  = :INQ_ID
                      AND SENDER_TYPE = :S_TYPE
                      AND (:S_CD IS NULL OR SENDER_CD = :S_CD2)
                    ORDER BY ID DESC
                ) WHERE ROWNUM = 1",
                r => Convert.ToInt64(r["ID"]),
                new OracleParameter("INQ_ID", req.InquiryId),
                new OracleParameter("S_TYPE", senderTypeForDb),
                new OracleParameter("S_CD",   (object?)req.EmpCd ?? DBNull.Value),
                new OracleParameter("S_CD2",  (object?)req.EmpCd ?? DBNull.Value));

            if (msgIds.Count == 0)
                return Ok(new { success = false, message = "Lỗi lưu tin nhắn" });

            long msgId = msgIds[0];

            // Insert attachments nếu có
            foreach (var f in req.Files)
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_INQUIRY_ATTACH
                        (MSG_ID, INQUIRY_ID, FILE_NAME, FILE_PATH, FILE_TYPE, MIME_TYPE, FILE_SIZE, THUMB_PATH, UPLOAD_DT)
                    VALUES
                        (:MSG_ID, :INQ_ID, :FILE_NAME, :FILE_PATH, :FILE_TYPE, :MIME_TYPE, :FILE_SIZE, :THUMB_PATH, SYSDATE)",
                    new OracleParameter("MSG_ID",    msgId),
                    new OracleParameter("INQ_ID",    req.InquiryId),
                    new OracleParameter("FILE_NAME", f.FileName),
                    new OracleParameter("FILE_PATH", f.FilePath),
                    new OracleParameter("FILE_TYPE", f.FileType),
                    new OracleParameter("MIME_TYPE", (object?)f.MimeType  ?? DBNull.Value),
                    new OracleParameter("FILE_SIZE", (object?)f.FileSize  ?? DBNull.Value),
                    new OracleParameter("THUMB_PATH",(object?)f.ThumbPath ?? DBNull.Value));
            }

            // Update HR_INQUIRY stats
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET LAST_MSG_DT = SYSDATE,
                    MSG_COUNT   = MSG_COUNT + 1,
                    UNREAD_HR   = CASE WHEN :ST = 'EMP' THEN UNREAD_HR  + 1 ELSE UNREAD_HR  END,
                    UNREAD_EMP  = CASE WHEN :ST2 = 'HR' THEN UNREAD_EMP + 1 ELSE UNREAD_EMP END
                WHERE ID = :ID",
                new OracleParameter("ST",  senderTypeForDb),
                new OracleParameter("ST2", senderTypeForDb),
                new OracleParameter("ID",  req.InquiryId));

            // Fire-and-forget notification
            string capInquiryNo  = conv.InquiryNo;
            string? capAssigned  = conv.AssignedTo;
            string? capEmpCd     = conv.EmpCd;
            string capSenderName = req.SenderName ?? (senderTypeForDb == "EMP" ? "Nhân viên" : "HR");
            long   capInquiryId  = req.InquiryId;
            string? capCreatedBy = req.EmpCd;

            _ = Task.Run(async () =>
            {
                using var scope      = _scopeFactory.CreateScope();
                var       notiHelper = scope.ServiceProvider.GetRequiredService<NotificationHelper>();
                try
                {
                    var ph = new Dictionary<string, string>
                    {
                        { "senderName", capSenderName },
                        { "inquiryNo",  capInquiryNo  }
                    };

                    if (senderTypeForDb == "EMP")
                    {
                        var (tVi, bVi, tEn, bEn) = await notiHelper.GetTemplateAsync("INQUIRY_EMP_MSG", ph);
                        if (!string.IsNullOrEmpty(capAssigned))
                        {
                            await notiHelper.SendNotificationAsync(new SendNotificationRequest
                            {
                                TITLE = tVi, BODY = bVi, TITLE_EN = tEn, BODY_EN = bEn,
                                NOTI_TYPE = "EMPCD", TARGET_VAL = capAssigned,
                                LINK_ACTION = $"inquiry/{capInquiryId}", CREATED_BY = capCreatedBy
                            });
                        }
                        else
                        {
                            foreach (var empCd in await GetHrAdminEmpCdsAsync())
                                await notiHelper.SendNotificationAsync(new SendNotificationRequest
                                {
                                    TITLE = tVi, BODY = bVi, TITLE_EN = tEn, BODY_EN = bEn,
                                    NOTI_TYPE = "EMPCD", TARGET_VAL = empCd,
                                    LINK_ACTION = $"inquiry/{capInquiryId}", CREATED_BY = capCreatedBy
                                });
                        }
                    }
                    else if (!string.IsNullOrEmpty(capEmpCd)) // HR/Admin → DIRECT employee only
                    {
                        var (tVi, bVi, tEn, bEn) = await notiHelper.GetTemplateAsync("INQUIRY_HR_MSG", ph);
                        await notiHelper.SendNotificationAsync(new SendNotificationRequest
                        {
                            TITLE = tVi, BODY = bVi, TITLE_EN = tEn, BODY_EN = bEn,
                            NOTI_TYPE = "EMPCD", TARGET_VAL = capEmpCd,
                            LINK_ACTION = $"inquiry/{capInquiryId}", CREATED_BY = capCreatedBy
                        });
                    }
                }
                catch { }
            });

            bool justLocked = (isHr || isAdmin) && string.IsNullOrEmpty(conv.AssignedTo);
            return Ok(new { success = true, msgId, justLocked });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/mark-read
    // Đánh dấu đã đọc + reset unread counter
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead([FromBody] InquiryMarkReadRequest req)
    {
        try
        {
            if (req.InquiryId <= 0)
                return Ok(new { success = false, message = "Thiếu ID" });

            if (req.ReaderType == "HR")
            {
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY_MSG
                    SET IS_READ_HR = 1
                    WHERE INQUIRY_ID = :ID AND SENDER_TYPE = 'EMP' AND IS_READ_HR = 0",
                    new OracleParameter("ID", req.InquiryId));

                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY SET UNREAD_HR = 0 WHERE ID = :ID",
                    new OracleParameter("ID", req.InquiryId));
            }
            else
            {
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY_MSG
                    SET IS_READ_EMP = 1
                    WHERE INQUIRY_ID = :ID AND SENDER_TYPE = 'HR' AND IS_READ_EMP = 0",
                    new OracleParameter("ID", req.InquiryId));

                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY SET UNREAD_EMP = 0 WHERE ID = :ID",
                    new OracleParameter("ID", req.InquiryId));
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/recall
    // Thu hồi tin nhắn (IS_DELETED = 1 khi bên kia chưa đọc)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("recall")]
    public async Task<IActionResult> Recall([FromBody] RecallRequest req)
    {
        try
        {
            if (req.MsgId <= 0)
                return Ok(new { success = false, message = "Thiếu ID tin nhắn" });

            // Lấy thông tin tin nhắn + conversation để validate
            var msgs = await _db.ExecuteQueryAsync(@"
                SELECT m.INQUIRY_ID, m.SENDER_TYPE, m.SENDER_CD, m.IS_READ_HR, m.IS_READ_EMP, m.IS_DELETED,
                       i.EMPCD, i.ANON_TOKEN
                FROM HRMS.HR_INQUIRY_MSG m
                JOIN HRMS.HR_INQUIRY i ON i.ID = m.INQUIRY_ID
                WHERE m.ID = :MSG_ID",
                r => new
                {
                    InquiryId  = Convert.ToInt64(r["INQUIRY_ID"]),
                    SenderType = r["SENDER_TYPE"]?.ToString() ?? "",
                    SenderCd   = r["SENDER_CD"]?.ToString(),
                    IsReadHr   = Convert.ToInt32(r["IS_READ_HR"]),
                    IsReadEmp  = Convert.ToInt32(r["IS_READ_EMP"]),
                    IsDeleted  = Convert.ToInt32(r["IS_DELETED"]),
                    EmpCd      = r["EMPCD"]?.ToString(),
                    AnonToken  = r["ANON_TOKEN"]?.ToString()
                },
                new OracleParameter("MSG_ID", req.MsgId));

            if (msgs.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy tin nhắn" });

            var msg = msgs[0];

            if (msg.IsDeleted == 1)
                return Ok(new { success = false, message = "Tin nhắn đã bị thu hồi" });

            // Kiểm tra quyền sở hữu — dùng !IsNullOrEmpty để tránh "" == "" khi Oracle NULL → ""
            bool isOwner;
            if (req.SenderType == "HR")
                isOwner = msg.SenderType == "HR"
                       && !string.IsNullOrEmpty(req.EmpCd)
                       && msg.SenderCd == req.EmpCd;
            else
                isOwner = msg.SenderType == "EMP"
                       && (
                           (!string.IsNullOrEmpty(req.EmpCd)     && msg.SenderCd == req.EmpCd)
                        || (!string.IsNullOrEmpty(req.AnonToken) && msg.AnonToken == req.AnonToken)
                       );

            if (!isOwner)
                return Ok(new { success = false, message = "Bạn chỉ có thể thu hồi tin nhắn của chính mình" });

            // Kiểm tra bên kia đã đọc chưa
            bool canRecall;
            if (msg.SenderType == "EMP")
                canRecall = msg.IsReadHr  == 0; // HR chưa đọc
            else
                canRecall = msg.IsReadEmp == 0; // EMP chưa đọc

            if (!canRecall)
                return Ok(new { success = false, message = "Không thể thu hồi vì đối phương đã đọc tin này" });

            await _db.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_INQUIRY_MSG SET IS_DELETED = 1 WHERE ID = :MSG_ID",
                new OracleParameter("MSG_ID", req.MsgId));

            // Giảm unread counter — tin bị thu hồi chưa được đọc nên vẫn đang tính vào badge
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET UNREAD_HR  = CASE WHEN :ST  = 'EMP' AND UNREAD_HR  > 0 THEN UNREAD_HR  - 1 ELSE UNREAD_HR  END,
                    UNREAD_EMP = CASE WHEN :ST2 = 'HR'  AND UNREAD_EMP > 0 THEN UNREAD_EMP - 1 ELSE UNREAD_EMP END
                WHERE ID = :INQ_ID",
                new OracleParameter("ST",     msg.SenderType),
                new OracleParameter("ST2",    msg.SenderType),
                new OracleParameter("INQ_ID", msg.InquiryId));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/close
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] InquiryCloseRequest req)
    {
        try
        {
            if (req.InquiryId <= 0)
                return Ok(new { success = false, message = "Thiếu ID hội thoại" });

            var convRows = await _db.ExecuteQueryAsync(@"
                SELECT STATUS, ASSIGNED_TO, EMPCD, ANON_TOKEN, INQUIRY_NO
                FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                r => new
                {
                    Status     = r["STATUS"]?.ToString()     ?? "",
                    AssignedTo = r["ASSIGNED_TO"]?.ToString(),
                    EmpCd      = r["EMPCD"]?.ToString(),
                    AnonToken  = r["ANON_TOKEN"]?.ToString(),
                    InquiryNo  = r["INQUIRY_NO"]?.ToString() ?? ""
                },
                new OracleParameter("ID", req.InquiryId));

            if (convRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy hội thoại" });

            var conv = convRows[0];
            if (conv.Status != "OPEN")
                return Ok(new { success = false, message = "Hội thoại đã đóng rồi" });

            // Kiểm tra quyền đóng
            if (req.CloserType == "HR")
            {
                // HR chỉ đóng được conversation mà mình đang phụ trách
                if (!string.IsNullOrEmpty(conv.AssignedTo) && conv.AssignedTo != req.EmpCd)
                    return Ok(new { success = false, message = "Bạn chỉ có thể đóng conversation mà mình đang phụ trách" });
            }
            else if (req.CloserType == "ADMIN")
            {
                // Admin đóng được bất kỳ conversation
            }
            else
            {
                // Employee: phải là chủ conversation
                bool isOwner = (!string.IsNullOrEmpty(conv.EmpCd) && conv.EmpCd == req.EmpCd)
                            || (!string.IsNullOrEmpty(conv.AnonToken) && conv.AnonToken == req.AnonToken);
                if (!isOwner)
                    return Ok(new { success = false, message = "Bạn không có quyền đóng hội thoại này" });
            }

            string closerName   = req.CloserName ?? "HR";
            string dbCloserType = req.CloserType; // DB constraint CHK_INQUIRY_CLOSED_TYPE allows 'HR' | 'EMP' | 'ADMIN'

            // Đóng conversation — atomic: check STATUS='OPEN' + ownership trong cùng 1 UPDATE
            int closeRows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET STATUS = 'CLOSED', CLOSED_DT = SYSDATE,
                    CLOSED_BY = :BY, CLOSED_BY_NAME = :BY_NAME,
                    CLOSED_BY_TYPE = :BY_TYPE, CLOSE_NOTE = :NOTE
                WHERE ID = :ID
                  AND STATUS = 'OPEN'
                  AND (
                       :CLOSER_TYPE = 'ADMIN'
                    OR (:CLOSER_TYPE = 'HR'  AND (ASSIGNED_TO IS NULL OR ASSIGNED_TO = :EMP_CD))
                    OR (:CLOSER_TYPE = 'EMP' AND (EMPCD = :EMP_CD2 OR ANON_TOKEN = :ANON_TOKEN))
                  )",
                new OracleParameter("BY",          req.EmpCd        ?? ""),
                new OracleParameter("BY_NAME",     closerName),
                new OracleParameter("BY_TYPE",     dbCloserType),
                new OracleParameter("NOTE",        (object?)req.CloseNote ?? DBNull.Value),
                new OracleParameter("ID",          req.InquiryId),
                new OracleParameter("CLOSER_TYPE", req.CloserType),
                new OracleParameter("EMP_CD",      (object?)req.EmpCd    ?? DBNull.Value),
                new OracleParameter("EMP_CD2",     (object?)req.EmpCd    ?? DBNull.Value),
                new OracleParameter("ANON_TOKEN",  (object?)req.AnonToken ?? DBNull.Value));
            if (closeRows == 0)
                return Ok(new { success = false, message = "Hội thoại đã đóng hoặc bạn không còn quyền đóng" });

            // System message
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY_MSG
                    (INQUIRY_ID, SENDER_TYPE, SENDER_CD, SENDER_NAME, MSG_TYPE, CONTENT,
                     IS_READ_HR, IS_READ_EMP, IS_DELETED, SENT_DT)
                VALUES
                    (:INQ_ID, 'SYS', :CD, :NAME, 'SYSTEM', :CONTENT, 1, 1, 0, SYSDATE)",
                new OracleParameter("INQ_ID",  req.InquiryId),
                new OracleParameter("CD",      req.EmpCd  ?? ""),
                new OracleParameter("NAME",    closerName),
                new OracleParameter("CONTENT", closerName + " đã đóng hội thoại này"));

            // Fire-and-forget notification
            string capCloseInqNo   = conv.InquiryNo;
            string? capCloseEmpCd  = conv.EmpCd;
            string? capCloseAssign = conv.AssignedTo;
            string  capCloserName  = closerName;
            string  capCloserType  = req.CloserType;
            long    capCloseId     = req.InquiryId;
            string? capCloseBy     = req.EmpCd;

            _ = Task.Run(async () =>
            {
                using var scope      = _scopeFactory.CreateScope();
                var       notiHelper = scope.ServiceProvider.GetRequiredService<NotificationHelper>();
                try
                {
                    var ph = new Dictionary<string, string>
                    {
                        { "closerName", capCloserName },
                        { "inquiryNo",  capCloseInqNo }
                    };

                    if ((capCloserType == "HR" || capCloserType == "ADMIN") && !string.IsNullOrEmpty(capCloseEmpCd))
                    {
                        // HR/Admin đóng → báo employee (chỉ DIRECT)
                        var (tVi, bVi, tEn, bEn) = await notiHelper.GetTemplateAsync("INQUIRY_CLOSED_HR", ph);
                        await notiHelper.SendNotificationAsync(new SendNotificationRequest
                        {
                            TITLE = tVi, BODY = bVi, TITLE_EN = tEn, BODY_EN = bEn,
                            NOTI_TYPE = "EMPCD", TARGET_VAL = capCloseEmpCd,
                            LINK_ACTION = $"inquiry/{capCloseId}", CREATED_BY = capCloseBy
                        });
                    }
                    else if (capCloserType == "EMP" && !string.IsNullOrEmpty(capCloseAssign))
                    {
                        // Employee đóng → báo HR đang phụ trách
                        var (tVi, bVi, tEn, bEn) = await notiHelper.GetTemplateAsync("INQUIRY_CLOSED_EMP", ph);
                        await notiHelper.SendNotificationAsync(new SendNotificationRequest
                        {
                            TITLE = tVi, BODY = bVi, TITLE_EN = tEn, BODY_EN = bEn,
                            NOTI_TYPE = "EMPCD", TARGET_VAL = capCloseAssign,
                            LINK_ACTION = $"inquiry/{capCloseId}", CREATED_BY = capCloseBy
                        });
                    }
                }
                catch { }
            });

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/unlock  — CHỈ ADMIN
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock([FromBody] InquiryUnlockRequest req)
    {
        try
        {
            if (req.InquiryId <= 0 || string.IsNullOrWhiteSpace(req.AdminEmpCd))
                return Ok(new { success = false, message = "Thiếu thông tin" });

            var convRows = await _db.ExecuteQueryAsync(@"
                SELECT STATUS, ASSIGNED_TO FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                r => new { Status = r["STATUS"]?.ToString() ?? "", AssignedTo = r["ASSIGNED_TO"]?.ToString() },
                new OracleParameter("ID", req.InquiryId));

            if (convRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy hội thoại" });

            if (convRows[0].Status != "OPEN")
                return Ok(new { success = false, message = "Hội thoại đã đóng, không cần unlock" });

            if (string.IsNullOrEmpty(convRows[0].AssignedTo))
                return Ok(new { success = false, message = "Hội thoại chưa bị lock" });

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET ASSIGNED_TO = NULL, ASSIGNED_NAME = NULL, LOCKED_DT = NULL
                WHERE ID = :ID AND STATUS = 'OPEN'",
                new OracleParameter("ID", req.InquiryId));

            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY_MSG
                    (INQUIRY_ID, SENDER_TYPE, SENDER_CD, SENDER_NAME, MSG_TYPE, CONTENT,
                     IS_READ_HR, IS_READ_EMP, IS_DELETED, SENT_DT)
                VALUES
                    (:INQ_ID, 'SYS', :CD, :NAME, 'SYSTEM', :CONTENT, 1, 1, 0, SYSDATE)",
                new OracleParameter("INQ_ID",  req.InquiryId),
                new OracleParameter("CD",      req.AdminEmpCd),
                new OracleParameter("NAME",    req.AdminName),
                new OracleParameter("CONTENT", "Admin " + req.AdminName + " đã mở khóa hội thoại"));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/hr-list?status=&topicCd=&assignedTo=&search=&page=
    // Dành cho HR / Admin xem tất cả conversations
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("hr-list")]
    public async Task<IActionResult> HrList(
        string? status     = null,
        string? topicCd    = null,
        string? chatType   = null,
        string? assignedTo = null,
        string? empCd      = null,
        string? search     = null,
        int     page       = 1,
        int     pageSize   = 30)
    {
        try
        {
            int offset = (page - 1) * pageSize;
            int maxRn  = offset + pageSize;

            // Xây filter động
            string whereStatus   = string.IsNullOrEmpty(status)    ? "" : "AND i.STATUS = :STATUS";
            string whereTopic    = string.IsNullOrEmpty(topicCd)   ? "" : "AND i.TOPIC_CD = :TOPIC";
            string whereChatType = string.IsNullOrEmpty(chatType)  ? "" : "AND i.CHAT_TYPE = :CHAT_TYPE";
            string whereAssign   = string.IsNullOrEmpty(assignedTo)
                                    ? ""
                                    : assignedTo == "UNASSIGNED"
                                        ? "AND i.ASSIGNED_TO IS NULL"
                                        : "AND i.ASSIGNED_TO = :ASSIGNED";
            string whereEmpCd    = string.IsNullOrEmpty(empCd)     ? "" : "AND i.EMPCD LIKE :EMPCD";
            string whereSearch   = string.IsNullOrEmpty(search)
                                    ? ""
                                    : "AND (i.INQUIRY_NO LIKE :SEARCH OR i.EMP_NAME LIKE :SEARCH2 OR i.ANON_DISPLAY LIKE :SEARCH3 OR i.SUBJECT LIKE :SEARCH4 OR i.EMPCD LIKE :SEARCH5)";

            string sql = $@"
                SELECT * FROM (
                    SELECT ROWNUM AS RN, q.* FROM (
                        SELECT i.ID, i.INQUIRY_NO, i.CHAT_TYPE, i.TOPIC_CD, t.TOPIC_NAME,
                               t.COLOR AS TOPIC_COLOR, t.ICON AS TOPIC_ICON,
                               i.SUBJECT, i.EMPCD, i.EMP_NAME, i.ANON_DISPLAY, NULL AS ANON_TOKEN, i.STATUS,
                               i.ASSIGNED_TO, i.ASSIGNED_NAME,
                               i.UNREAD_HR, i.UNREAD_EMP, i.MSG_COUNT, i.LAST_MSG_DT, i.INST_DT,
                               i.CLOSED_DT, i.CLOSED_BY_NAME, i.CLOSED_BY_TYPE, i.RATING
                        FROM HRMS.HR_INQUIRY i
                        LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                        WHERE 1=1
                        {whereStatus}
                        {whereTopic}
                        {whereChatType}
                        {whereAssign}
                        {whereEmpCd}
                        {whereSearch}
                        ORDER BY i.UNREAD_HR DESC, i.LAST_MSG_DT DESC
                    ) q WHERE ROWNUM <= :MAXRN
                ) WHERE RN > :OFFSET";

            var parameters = new List<OracleParameter>
            {
                new("MAXRN",  maxRn),
                new("OFFSET", offset)
            };

            if (!string.IsNullOrEmpty(status))     parameters.Add(new OracleParameter("STATUS",    status));
            if (!string.IsNullOrEmpty(topicCd))    parameters.Add(new OracleParameter("TOPIC",     topicCd));
            if (!string.IsNullOrEmpty(chatType))   parameters.Add(new OracleParameter("CHAT_TYPE", chatType));
            if (!string.IsNullOrEmpty(assignedTo) && assignedTo != "UNASSIGNED")
                                                    parameters.Add(new OracleParameter("ASSIGNED",  assignedTo));
            if (!string.IsNullOrEmpty(empCd))      parameters.Add(new OracleParameter("EMPCD",     "%" + empCd + "%"));
            if (!string.IsNullOrEmpty(search))
            {
                string like = "%" + search + "%";
                parameters.Add(new OracleParameter("SEARCH",  like));
                parameters.Add(new OracleParameter("SEARCH2", like));
                parameters.Add(new OracleParameter("SEARCH3", like));
                parameters.Add(new OracleParameter("SEARCH4", like));
                parameters.Add(new OracleParameter("SEARCH5", like));
            }

            var rows = await _db.ExecuteQueryAsync(sql, r => MapListItem(r), parameters.ToArray());

            // Summary COUNT (same filters, no pagination)
            string sqlCount = $@"
                SELECT COUNT(*)                                                 AS TOTAL,
                       SUM(CASE WHEN i.STATUS = 'OPEN'   THEN 1 ELSE 0 END)   AS CNT_OPEN,
                       SUM(CASE WHEN i.STATUS = 'CLOSED' THEN 1 ELSE 0 END)   AS CNT_CLOSED,
                       NVL(SUM(i.UNREAD_HR), 0)                               AS TOTAL_UNREAD
                FROM HRMS.HR_INQUIRY i
                WHERE 1=1
                {whereStatus}
                {whereTopic}
                {whereChatType}
                {whereAssign}
                {whereEmpCd}
                {whereSearch}";

            var countParams = new List<OracleParameter>();
            if (!string.IsNullOrEmpty(status))     countParams.Add(new OracleParameter("STATUS",    status));
            if (!string.IsNullOrEmpty(topicCd))    countParams.Add(new OracleParameter("TOPIC",     topicCd));
            if (!string.IsNullOrEmpty(chatType))   countParams.Add(new OracleParameter("CHAT_TYPE", chatType));
            if (!string.IsNullOrEmpty(assignedTo) && assignedTo != "UNASSIGNED")
                                                    countParams.Add(new OracleParameter("ASSIGNED",  assignedTo));
            if (!string.IsNullOrEmpty(empCd))      countParams.Add(new OracleParameter("EMPCD",     "%" + empCd + "%"));
            if (!string.IsNullOrEmpty(search))
            {
                string like = "%" + search + "%";
                countParams.Add(new OracleParameter("SEARCH",  like));
                countParams.Add(new OracleParameter("SEARCH2", like));
                countParams.Add(new OracleParameter("SEARCH3", like));
                countParams.Add(new OracleParameter("SEARCH4", like));
                countParams.Add(new OracleParameter("SEARCH5", like));
            }

            var counts = await _db.ExecuteQueryAsync(sqlCount, r => new {
                total        = Convert.ToInt32(r["TOTAL"]),
                cntOpen      = Convert.ToInt32(r["CNT_OPEN"]),
                cntClosed    = Convert.ToInt32(r["CNT_CLOSED"]),
                totalUnread  = Convert.ToInt32(r["TOTAL_UNREAD"])
            }, countParams.ToArray());

            var c = counts.FirstOrDefault();
            int total       = c?.total       ?? 0;
            int cntOpen     = c?.cntOpen     ?? 0;
            int cntClosed   = c?.cntClosed   ?? 0;
            int totalUnread = c?.totalUnread ?? 0;
            int totalPages  = pageSize > 0 ? (int)Math.Ceiling((double)total / pageSize) : 1;

            return Ok(new { success = true, data = rows, page, pageSize, total, totalPages, cntOpen, cntClosed, totalUnread });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/report?type=WEEK&year=2026&week=25
    // GET /apiHR/Inquiry/report?type=MONTH&year=2026&month=6
    // Báo cáo thống kê inquiry — chỉ Admin
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("report")]
    public async Task<IActionResult> Report(
        string type  = "WEEK",
        int    year  = 0,
        int    week  = 0,
        int    month = 0)
    {
        try
        {
            if (year <= 0) year = DateTime.Now.Year;

            DateTime startDt, endDt;
            if (type == "MONTH")
            {
                if (month < 1 || month > 12) month = DateTime.Now.Month;
                startDt = new DateTime(year, month, 1);
                endDt   = startDt.AddMonths(1);
            }
            else // WEEK
            {
                int maxWeek = ISOWeek.GetWeeksInYear(year);
                if (week < 1 || week > maxWeek) week = ISOWeek.GetWeekOfYear(DateTime.Now);
                startDt = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
                endDt   = startDt.AddDays(7);
            }

            // ── 1. Summary ──────────────────────────────────────────────────
            var summaries = await _db.ExecuteQueryAsync(@"
                SELECT
                    COUNT(*) AS TOTAL,
                    SUM(CASE WHEN STATUS = 'OPEN'   THEN 1 ELSE 0 END) AS CNT_OPEN,
                    SUM(CASE WHEN STATUS = 'CLOSED' THEN 1 ELSE 0 END) AS CNT_CLOSED,
                    SUM(CASE WHEN CHAT_TYPE = 'DIRECT' THEN 1 ELSE 0 END) AS CNT_DIRECT,
                    SUM(CASE WHEN CHAT_TYPE = 'ANON'   THEN 1 ELSE 0 END) AS CNT_ANON,
                    ROUND(AVG(RATING), 1)    AS AVG_RATING,
                    ROUND(AVG(MSG_COUNT), 1) AS AVG_MSG,
                    SUM(CASE WHEN CLOSED_BY_TYPE = 'HR'    THEN 1 ELSE 0 END) AS CLOSED_BY_HR,
                    SUM(CASE WHEN CLOSED_BY_TYPE = 'EMP'   THEN 1 ELSE 0 END) AS CLOSED_BY_EMP,
                    SUM(CASE WHEN CLOSED_BY_TYPE = 'ADMIN' THEN 1 ELSE 0 END) AS CLOSED_BY_ADMIN,
                    ROUND(AVG(
                        CASE WHEN CLOSED_DT IS NOT NULL AND LOCKED_DT IS NOT NULL
                             THEN (CLOSED_DT - LOCKED_DT) * 24 * 60
                        END
                    ), 0) AS AVG_HANDLE_MIN
                FROM HRMS.HR_INQUIRY
                WHERE TRUNC(INST_DT) >= :START_DT AND TRUNC(INST_DT) < :END_DT",
                r =>
                {
                    static int     I(object v) => v == DBNull.Value ? 0    : Convert.ToInt32(v);
                    static double? D(object v) => v == DBNull.Value ? null : (double?)Math.Round(Convert.ToDouble(v), 1);
                    return new InquiryReportSummaryDto
                    {
                        Total         = I(r["TOTAL"]),
                        CntOpen       = I(r["CNT_OPEN"]),
                        CntClosed     = I(r["CNT_CLOSED"]),
                        CntDirect     = I(r["CNT_DIRECT"]),
                        CntAnon       = I(r["CNT_ANON"]),
                        AvgRating     = D(r["AVG_RATING"]),
                        AvgMsg        = D(r["AVG_MSG"]),
                        ClosedByHr    = I(r["CLOSED_BY_HR"]),
                        ClosedByEmp   = I(r["CLOSED_BY_EMP"]),
                        ClosedByAdmin = I(r["CLOSED_BY_ADMIN"]),
                        AvgHandleMin  = D(r["AVG_HANDLE_MIN"])
                    };
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            // ── 2. Theo chủ đề ──────────────────────────────────────────────
            var byTopic = await _db.ExecuteQueryAsync(@"
                SELECT i.TOPIC_CD, t.TOPIC_NAME, t.COLOR, t.ICON,
                       COUNT(*) AS TOTAL,
                       SUM(CASE WHEN i.STATUS = 'OPEN'   THEN 1 ELSE 0 END) AS CNT_OPEN,
                       SUM(CASE WHEN i.STATUS = 'CLOSED' THEN 1 ELSE 0 END) AS CNT_CLOSED,
                       ROUND(AVG(i.RATING), 1) AS AVG_RATING
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE TRUNC(i.INST_DT) >= :START_DT AND TRUNC(i.INST_DT) < :END_DT
                GROUP BY i.TOPIC_CD, t.TOPIC_NAME, t.COLOR, t.ICON
                ORDER BY COUNT(*) DESC",
                r =>
                {
                    static string? S(object v) => v == DBNull.Value ? null : v.ToString();
                    static double? D(object v) => v == DBNull.Value ? null : (double?)Math.Round(Convert.ToDouble(v), 1);
                    return new InquiryReportTopicRowDto
                    {
                        TopicCd   = r["TOPIC_CD"]?.ToString() ?? "",
                        TopicName = S(r["TOPIC_NAME"]),
                        Color     = S(r["COLOR"]),
                        Icon      = S(r["ICON"]),
                        Total     = Convert.ToInt32(r["TOTAL"]),
                        CntOpen   = Convert.ToInt32(r["CNT_OPEN"]),
                        CntClosed = Convert.ToInt32(r["CNT_CLOSED"]),
                        AvgRating = D(r["AVG_RATING"])
                    };
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            // ── 3. Workload HR ──────────────────────────────────────────────
            var byHr = await _db.ExecuteQueryAsync(@"
                SELECT i.ASSIGNED_TO AS HR_CD, i.ASSIGNED_NAME AS HR_NAME,
                       COUNT(*) AS HANDLED,
                       SUM(CASE WHEN i.STATUS = 'CLOSED' THEN 1 ELSE 0 END) AS CNT_CLOSED,
                       ROUND(AVG(i.RATING), 1) AS AVG_RATING,
                       ROUND(AVG(
                           CASE WHEN i.CLOSED_DT IS NOT NULL AND i.LOCKED_DT IS NOT NULL
                                THEN (i.CLOSED_DT - i.LOCKED_DT) * 24 * 60
                           END
                       ), 0) AS AVG_HANDLE_MIN
                FROM HRMS.HR_INQUIRY i
                WHERE TRUNC(i.INST_DT) >= :START_DT AND TRUNC(i.INST_DT) < :END_DT
                  AND i.ASSIGNED_TO IS NOT NULL
                GROUP BY i.ASSIGNED_TO, i.ASSIGNED_NAME
                ORDER BY COUNT(*) DESC",
                r =>
                {
                    static string? S(object v) => v == DBNull.Value ? null : v.ToString();
                    static double? D(object v) => v == DBNull.Value ? null : (double?)Math.Round(Convert.ToDouble(v), 1);
                    return new InquiryReportHrRowDto
                    {
                        HrCd        = S(r["HR_CD"]),
                        HrName      = S(r["HR_NAME"]),
                        Handled     = Convert.ToInt32(r["HANDLED"]),
                        CntClosed   = Convert.ToInt32(r["CNT_CLOSED"]),
                        AvgRating   = D(r["AVG_RATING"]),
                        AvgHandleMin= D(r["AVG_HANDLE_MIN"])
                    };
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            return Ok(new
            {
                success = true,
                summary = summaries.FirstOrDefault(),
                byTopic,
                byHr
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/rate
    // Đánh giá sau khi CLOSED (chỉ Employee, chỉ 1 lần)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("rate")]
    public async Task<IActionResult> Rate([FromBody] InquiryRatingRequest req)
    {
        try
        {
            if (req.InquiryId <= 0 || req.Rating < 1 || req.Rating > 5)
                return Ok(new { success = false, message = "Dữ liệu đánh giá không hợp lệ (1-5 sao)" });

            var convRows = await _db.ExecuteQueryAsync(@"
                SELECT STATUS, RATING, EMPCD, ANON_TOKEN
                FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                r => new
                {
                    Status    = r["STATUS"]?.ToString()    ?? "",
                    Rating    = r["RATING"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["RATING"]),
                    EmpCd     = r["EMPCD"]?.ToString(),
                    AnonToken = r["ANON_TOKEN"]?.ToString()
                },
                new OracleParameter("ID", req.InquiryId));

            if (convRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy hội thoại" });

            var conv = convRows[0];
            if (conv.Status != "CLOSED")
                return Ok(new { success = false, message = "Chỉ đánh giá được hội thoại đã đóng" });
            if (conv.Rating.HasValue)
                return Ok(new { success = false, message = "Bạn đã đánh giá hội thoại này rồi" });

            // Kiểm tra chủ conversation
            bool isOwner = (!string.IsNullOrEmpty(conv.EmpCd) && conv.EmpCd == req.EmpCd)
                        || (!string.IsNullOrEmpty(conv.AnonToken) && conv.AnonToken == req.AnonToken);
            if (!isOwner)
                return Ok(new { success = false, message = "Không có quyền đánh giá" });

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET RATING = :RATING, RATING_NOTE = :NOTE
                WHERE ID = :ID",
                new OracleParameter("RATING", req.Rating),
                new OracleParameter("NOTE",   (object?)req.RatingNote ?? DBNull.Value),
                new OracleParameter("ID",     req.InquiryId));

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: map OracleDataReader → InquiryListItemDto
    // Tất cả columns trong query đều được SELECT nên safe to access directly
    // ─────────────────────────────────────────────────────────────────────────
    private static InquiryListItemDto MapListItem(OracleDataReader r)
    {
        static DateTime? D(object v) => v == DBNull.Value ? null : Convert.ToDateTime(v);
        static int?     N(object v) => v == DBNull.Value ? null : Convert.ToInt32(v);
        static int      I(object v) => v == DBNull.Value ? 0    : Convert.ToInt32(v);
        static string?  S(object v) => v == DBNull.Value ? null : v.ToString();

        return new InquiryListItemDto
        {
            Id           = r["ID"] != DBNull.Value ? Convert.ToInt64(r["ID"]) : 0,
            InquiryNo    = r["INQUIRY_NO"]?.ToString()    ?? "",
            ChatType     = r["CHAT_TYPE"]?.ToString()     ?? "",
            TopicCd      = r["TOPIC_CD"]?.ToString()      ?? "",
            TopicName    = S(r["TOPIC_NAME"]),
            TopicColor   = S(r["TOPIC_COLOR"]),
            TopicIcon    = S(r["TOPIC_ICON"]),
            Subject      = S(r["SUBJECT"]),
            EmpCd        = S(r["EMPCD"]),
            EmpName      = S(r["EMP_NAME"]),
            AnonDisplay  = S(r["ANON_DISPLAY"]),
            AnonToken    = S(r["ANON_TOKEN"]),
            Status       = r["STATUS"]?.ToString()        ?? "",
            AssignedTo   = S(r["ASSIGNED_TO"]),
            AssignedName = S(r["ASSIGNED_NAME"]),
            UnreadHr     = I(r["UNREAD_HR"]),
            UnreadEmp    = I(r["UNREAD_EMP"]),
            MsgCount     = I(r["MSG_COUNT"]),
            LastMsgDt    = D(r["LAST_MSG_DT"]),
            InstDt       = D(r["INST_DT"]),
            ClosedDt     = D(r["CLOSED_DT"]),
            ClosedByName = S(r["CLOSED_BY_NAME"]),
            ClosedByType = S(r["CLOSED_BY_TYPE"]),
            Rating       = N(r["RATING"])
            // CloseNote, LockedDt, RatingNote chỉ có trong GetMessages (không có trong list queries)
        };
    }

    private static InquiryListItemDto MapListItemDetail(OracleDataReader r)
    {
        static DateTime? D(object v) => v == DBNull.Value ? null : Convert.ToDateTime(v);
        static string?   S(object v) => v == DBNull.Value ? null : v.ToString();

        var dto = MapListItem(r);
        dto.LockedDt   = D(r["LOCKED_DT"]);
        dto.CloseNote  = S(r["CLOSE_NOTE"]);
        dto.RatingNote = S(r["RATING_NOTE"]);
        return dto;
    }

    private async Task<List<string>> GetHrAdminEmpCdsAsync()
    {
        try
        {
            return await _db.ExecuteQueryAsync(@"
                SELECT U.EMPCD FROM HRMS.HR_USERS U
                JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID
                WHERE R.ROLE_NAME IN ('HR', 'Admin') AND U.IS_ACTIVE = 1",
                r => r["EMPCD"]?.ToString() ?? "");
        }
        catch { return new(); }
    }
}

// ─── Extra request models (chỉ dùng nội bộ controller này) ──────────────────

public class RecallRequest
{
    public long    MsgId      { get; set; }
    public string  SenderType { get; set; } = "EMP";
    public string? EmpCd      { get; set; }
    public string? AnonToken  { get; set; }
}
