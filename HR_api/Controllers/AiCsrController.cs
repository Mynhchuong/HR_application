using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using HR_api.Data;
using HR_api.Services;

namespace HR_api.Controllers;

// "AI SAMHO - CSR" — NV hỏi AI, CSR/Admin trả lời tay. Lưu RIÊNG ở HR_AI_CHAT / HR_AI_CHAT_MSG,
// KHÔNG dùng chung HR_INQUIRY. Xác thực chủ thread bằng EMPCD (kênh này không có ẩn danh).
[ApiController]
[Route("apiHR/[controller]")]
public class AiCsrController : ControllerBase
{
    private readonly OracleService _db;
    private readonly AiCsrService _ai;
    private readonly NotificationService _noti;

    public AiCsrController(OracleService db, AiCsrService ai, NotificationService noti)
    {
        _db = db; _ai = ai; _noti = noti;
    }

    private const string AiFallbackMsg =
        "Xin lỗi, hệ thống AI đang bận. CSR sẽ xem và hỗ trợ bạn sớm nhất.";

    // ─────────────────────────────────────────────────────────────
    // NV: gửi câu hỏi -> lưu tin EMP -> gọi AI -> lưu tin AI (hoặc fallback)
    // POST apiHR/AiCsr/ask
    // ─────────────────────────────────────────────────────────────
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskBody body)
    {
        try
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Empcd) || string.IsNullOrWhiteSpace(body.Question))
                return Ok(new { success = false, message = "Thiếu mã NV hoặc câu hỏi" });

            string question = body.Question.Trim();
            if (question.Length > 4000) question = question.Substring(0, 4000);

            // 1. Lấy/tạo thread OPEN
            var openIdRows = await _db.ExecuteQueryAsync(
                @"SELECT ID FROM (SELECT ID FROM HRMS.HR_AI_CHAT WHERE EMPCD = :E AND STATUS = 'OPEN' ORDER BY ID DESC) WHERE ROWNUM = 1",
                r => Convert.ToDecimal(r["ID"]),
                new OracleParameter("E", body.Empcd));

            decimal chatId;
            if (openIdRows.Count > 0)
            {
                chatId = openIdRows[0];
            }
            else
            {
                string title = question.Length > 60 ? question.Substring(0, 60) : question;
                var outId = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_AI_CHAT (EMPCD, EMP_NAME, TITLE, STATUS, MSG_COUNT, INST_ID)
                    VALUES (:E, :NM, :T, 'OPEN', 0, :E2)
                    RETURNING ID INTO :OUT_ID",
                    new OracleParameter("E", body.Empcd),
                    new OracleParameter("NM", (object?)body.EmpName ?? DBNull.Value),
                    new OracleParameter("T", title),
                    new OracleParameter("E2", body.Empcd),
                    outId);
                chatId = outId.Value is OracleDecimal od && !od.IsNull ? od.Value : 0;
                if (chatId <= 0) return Ok(new { success = false, message = "Không tạo được đoạn chat" });
            }

            // 2. Lưu tin NV
            await InsertMsgAsync(chatId, "EMP", body.Empcd, body.EmpName, question, aiFailed: false);

            // 3. Gọi AI
            var (ok, answer) = await _ai.AskAiAsync(question, body.Language ?? "vi");
            string aiContent = ok ? answer : AiFallbackMsg;
            await InsertMsgAsync(chatId, "AI", null, "AI SAMHO - CSR", aiContent, aiFailed: !ok);

            // 4. Cập nhật thread. AI lỗi -> reset LAST_CSR_READ_DT để nổi lên đầu list CSR.
            await _db.ExecuteNonQueryAsync($@"
                UPDATE HRMS.HR_AI_CHAT
                SET MSG_COUNT = MSG_COUNT + 2, LAST_MSG_DT = SYSDATE, UNREAD_EMP = 0
                    {(ok ? "" : ", LAST_CSR_READ_DT = NULL")}
                WHERE ID = :ID",
                new OracleParameter("ID", chatId));

            var msgs = await LoadMessagesAsync(chatId, 0);
            return Ok(new { success = true, chatId, aiOk = ok, messages = msgs });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET apiHR/AiCsr/my-thread?empcd=  — đoạn chat OPEN hiện tại của NV + toàn bộ tin nhắn.
    // Dùng lúc mở trang. Không có thì trả thread = null.
    [HttpGet("my-thread")]
    public async Task<IActionResult> MyThread([FromQuery] string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd)) return Ok(new { success = false, message = "Thiếu mã NV" });
            var idRows = await _db.ExecuteQueryAsync(
                @"SELECT ID FROM (SELECT ID FROM HRMS.HR_AI_CHAT WHERE EMPCD = :E AND STATUS = 'OPEN' ORDER BY ID DESC) WHERE ROWNUM = 1",
                r => Convert.ToDecimal(r["ID"]), new OracleParameter("E", empcd));
            if (idRows.Count == 0) return Ok(new { success = true, thread = (object?)null, messages = Array.Empty<object>() });

            decimal chatId = idRows[0];
            var head = await LoadHeadAsync(chatId);
            var msgs = await LoadMessagesAsync(chatId, 0);
            return Ok(new { success = true, thread = new { chatId, status = head?.Status, unreadEmp = head?.UnreadEmp ?? 0 }, messages = msgs });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET apiHR/AiCsr/messages?chatId=&empcd=&afterMsgId=
    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] decimal chatId, [FromQuery] string empcd, [FromQuery] decimal afterMsgId = 0)
    {
        try
        {
            if (chatId <= 0 || string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu tham số" });

            var head = await LoadHeadAsync(chatId);
            if (head == null) return Ok(new { success = false, message = "Không tìm thấy đoạn chat" });
            if (!string.Equals(head.Empcd, empcd, StringComparison.OrdinalIgnoreCase))
                return Ok(new { success = false, message = "Không có quyền xem đoạn chat này" });

            var msgs = await LoadMessagesAsync(chatId, afterMsgId);
            return Ok(new { success = true, status = head.Status, unreadEmp = head.UnreadEmp, messages = msgs });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/AiCsr/mark-read-emp  { chatId, empcd }
    [HttpPost("mark-read-emp")]
    public async Task<IActionResult> MarkReadEmp([FromBody] ChatEmpBody body)
    {
        try
        {
            if (body == null || body.ChatId <= 0 || string.IsNullOrEmpty(body.Empcd))
                return Ok(new { success = false, message = "Thiếu tham số" });
            await _db.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_AI_CHAT SET UNREAD_EMP = 0 WHERE ID = :ID AND EMPCD = :E",
                new OracleParameter("ID", body.ChatId), new OracleParameter("E", body.Empcd));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/AiCsr/new-thread  { empcd }  — đóng thread OPEN hiện tại, lần ask sau tự tạo mới
    [HttpPost("new-thread")]
    public async Task<IActionResult> NewThread([FromBody] EmpBody body)
    {
        try
        {
            if (body == null || string.IsNullOrEmpty(body.Empcd))
                return Ok(new { success = false, message = "Thiếu mã NV" });
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_AI_CHAT SET STATUS = 'CLOSED', CLOSED_DT = SYSDATE, CLOSED_BY = :E
                WHERE EMPCD = :E2 AND STATUS = 'OPEN'",
                new OracleParameter("E", body.Empcd), new OracleParameter("E2", body.Empcd));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────
    // CSR / Admin
    // ─────────────────────────────────────────────────────────────

    // GET apiHR/AiCsr/list?status=&search=&page=&pageSize=
    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        try
        {
            int offset = (page - 1) * pageSize;
            int maxRn  = offset + pageSize;

            string whereStatus = string.IsNullOrEmpty(status) ? "" : "AND c.STATUS = :STATUS";
            string whereSearch = string.IsNullOrEmpty(search) ? ""
                : "AND (c.EMPCD LIKE :SRC OR c.EMP_NAME LIKE :SRC2 OR c.TITLE LIKE :SRC3)";

            const string unreadExpr = @"
                (SELECT COUNT(*) FROM HRMS.HR_AI_CHAT_MSG m
                 WHERE m.CHAT_ID = c.ID AND m.SENDER_TYPE = 'EMP'
                   AND m.SENT_DT > NVL(c.LAST_CSR_READ_DT, DATE '1900-01-01'))";

            string sql = $@"
                SELECT * FROM (
                    SELECT ROWNUM RN, q.* FROM (
                        SELECT c.ID, c.EMPCD, c.EMP_NAME, c.TITLE, c.STATUS, c.MSG_COUNT,
                               c.LAST_MSG_DT, c.INST_DT, c.UNREAD_EMP,
                               b.DEPTNM AS DEPT_NAME, b.TEAMNM AS LINE_NAME, b.WORKNM AS WORK_NAME,
                               {unreadExpr} AS UNREAD_CSR
                        FROM HRMS.HR_AI_CHAT c
                        LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = c.EMPCD
                        LEFT JOIN HRMS.EAM410 b  ON b.DEPTCD = ec.DEPTCD AND b.LINECD = ec.LINECD AND b.WORKCD = ec.WORKCD
                        WHERE 1 = 1
                        {whereStatus}
                        {whereSearch}
                        ORDER BY UNREAD_CSR DESC, c.LAST_MSG_DT DESC NULLS LAST, c.ID DESC
                    ) q WHERE ROWNUM <= :MAXRN
                ) WHERE RN > :OFFSET";

            var pars = new List<OracleParameter>
            {
                new("MAXRN", maxRn), new("OFFSET", offset)
            };
            if (!string.IsNullOrEmpty(status)) pars.Add(new OracleParameter("STATUS", status));
            if (!string.IsNullOrEmpty(search))
            {
                string like = "%" + search + "%";
                pars.Add(new OracleParameter("SRC", like));
                pars.Add(new OracleParameter("SRC2", like));
                pars.Add(new OracleParameter("SRC3", like));
            }

            var rows = await _db.ExecuteQueryAsync(sql, r => new
            {
                id         = Convert.ToDecimal(r["ID"]),
                empcd      = r["EMPCD"]?.ToString(),
                empName    = r["EMP_NAME"]?.ToString(),
                title      = r["TITLE"]?.ToString(),
                status     = r["STATUS"]?.ToString(),
                msgCount   = r["MSG_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["MSG_COUNT"]),
                lastMsgDt  = r["LAST_MSG_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LAST_MSG_DT"]),
                instDt     = r["INST_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["INST_DT"]),
                deptName   = r["DEPT_NAME"]?.ToString(),
                lineName   = r["LINE_NAME"]?.ToString(),
                workName   = r["WORK_NAME"]?.ToString(),
                unreadCsr  = r["UNREAD_CSR"] == DBNull.Value ? 0 : Convert.ToInt32(r["UNREAD_CSR"]),
                unreadEmp  = r["UNREAD_EMP"] == DBNull.Value ? 0 : Convert.ToInt32(r["UNREAD_EMP"]),
            }, pars.ToArray());

            // Summary
            string cntSql = $@"
                SELECT COUNT(*) TOTAL,
                       NVL(SUM(CASE WHEN c.STATUS = 'OPEN' THEN 1 ELSE 0 END), 0) CNT_OPEN,
                       NVL(SUM(CASE WHEN {unreadExpr} > 0 THEN 1 ELSE 0 END), 0) CNT_UNREAD
                FROM HRMS.HR_AI_CHAT c
                LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = c.EMPCD
                WHERE 1 = 1 {whereStatus} {whereSearch}";
            var cntPars = new List<OracleParameter>();
            if (!string.IsNullOrEmpty(status)) cntPars.Add(new OracleParameter("STATUS", status));
            if (!string.IsNullOrEmpty(search))
            {
                string like = "%" + search + "%";
                cntPars.Add(new OracleParameter("SRC", like));
                cntPars.Add(new OracleParameter("SRC2", like));
                cntPars.Add(new OracleParameter("SRC3", like));
            }
            var c = (await _db.ExecuteQueryAsync(cntSql, r => new
            {
                total     = Convert.ToInt32(r["TOTAL"]),
                cntOpen   = Convert.ToInt32(r["CNT_OPEN"]),
                cntUnread = Convert.ToInt32(r["CNT_UNREAD"]),
            }, cntPars.ToArray())).FirstOrDefault();

            int total = c?.total ?? 0;
            return Ok(new
            {
                success = true, data = rows, page, pageSize, total,
                totalPages = pageSize > 0 ? (int)Math.Ceiling((double)total / pageSize) : 0,
                cntOpen = c?.cntOpen ?? 0, cntUnread = c?.cntUnread ?? 0
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET apiHR/AiCsr/thread?chatId=
    [HttpGet("thread")]
    public async Task<IActionResult> Thread([FromQuery] decimal chatId)
    {
        try
        {
            if (chatId <= 0) return Ok(new { success = false, message = "Thiếu chatId" });
            var head = await LoadHeadAsync(chatId);
            if (head == null) return Ok(new { success = false, message = "Không tìm thấy đoạn chat" });
            var msgs = await LoadMessagesAsync(chatId, 0);
            return Ok(new { success = true, head, messages = msgs });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET apiHR/AiCsr/thread-messages?chatId=&afterMsgId=  (poll cho CSR)
    [HttpGet("thread-messages")]
    public async Task<IActionResult> ThreadMessages([FromQuery] decimal chatId, [FromQuery] decimal afterMsgId = 0)
    {
        try
        {
            if (chatId <= 0) return Ok(new { success = false, message = "Thiếu chatId" });
            var head = await LoadHeadAsync(chatId);
            if (head == null) return Ok(new { success = false, message = "Không tìm thấy đoạn chat" });
            var msgs = await LoadMessagesAsync(chatId, afterMsgId);
            return Ok(new { success = true, status = head.Status, messages = msgs });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/AiCsr/csr-reply  { chatId, csrEmpcd, csrName, content }
    [HttpPost("csr-reply")]
    public async Task<IActionResult> CsrReply([FromBody] CsrReplyBody body)
    {
        try
        {
            if (body == null || body.ChatId <= 0 || string.IsNullOrWhiteSpace(body.CsrEmpcd) || string.IsNullOrWhiteSpace(body.Content))
                return Ok(new { success = false, message = "Thiếu thông tin trả lời" });

            string content = body.Content.Trim();
            if (content.Length > 4000) content = content.Substring(0, 4000);

            var head = await LoadHeadAsync(body.ChatId);
            if (head == null) return Ok(new { success = false, message = "Không tìm thấy đoạn chat" });

            await InsertMsgAsync(body.ChatId, "CSR", body.CsrEmpcd, body.CsrName, content, aiFailed: false);
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_AI_CHAT
                SET MSG_COUNT = MSG_COUNT + 1, LAST_MSG_DT = SYSDATE, UNREAD_EMP = 1,
                    LAST_CSR_READ_DT = SYSDATE, UPDT_ID = :U
                WHERE ID = :ID",
                new OracleParameter("U", body.CsrEmpcd), new OracleParameter("ID", body.ChatId));

            _noti.AiCsrReplied(head.Empcd ?? "", body.CsrEmpcd, content);

            var msgs = await LoadMessagesAsync(body.ChatId, 0);
            return Ok(new { success = true, messages = msgs });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/AiCsr/csr-mark-read  { chatId }
    [HttpPost("csr-mark-read")]
    public async Task<IActionResult> CsrMarkRead([FromBody] ChatIdBody body)
    {
        try
        {
            if (body == null || body.ChatId <= 0) return Ok(new { success = false, message = "Thiếu chatId" });
            await _db.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_AI_CHAT SET LAST_CSR_READ_DT = SYSDATE WHERE ID = :ID",
                new OracleParameter("ID", body.ChatId));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/AiCsr/set-status  { chatId, actorEmpcd, status }  status = OPEN | CLOSED
    [HttpPost("set-status")]
    public async Task<IActionResult> SetStatus([FromBody] SetStatusBody body)
    {
        try
        {
            if (body == null || body.ChatId <= 0 || (body.Status != "OPEN" && body.Status != "CLOSED"))
                return Ok(new { success = false, message = "Tham số không hợp lệ" });
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_AI_CHAT
                SET STATUS = :ST,
                    CLOSED_DT = CASE WHEN :ST2 = 'CLOSED' THEN SYSDATE ELSE NULL END,
                    CLOSED_BY = CASE WHEN :ST3 = 'CLOSED' THEN :BY ELSE NULL END,
                    UPDT_ID = :BY2
                WHERE ID = :ID",
                new OracleParameter("ST", body.Status), new OracleParameter("ST2", body.Status),
                new OracleParameter("ST3", body.Status), new OracleParameter("BY", (object?)body.ActorEmpcd ?? DBNull.Value),
                new OracleParameter("BY2", (object?)body.ActorEmpcd ?? DBNull.Value),
                new OracleParameter("ID", body.ChatId));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────
    private sealed class HeadRow
    {
        public decimal Id; public string? Empcd; public string? EmpName; public string? Title;
        public string? Status; public int UnreadEmp;
    }

    private async Task<HeadRow?> LoadHeadAsync(decimal chatId)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT ID, EMPCD, EMP_NAME, TITLE, STATUS, UNREAD_EMP FROM HRMS.HR_AI_CHAT WHERE ID = :ID",
            r => new HeadRow
            {
                Id = Convert.ToDecimal(r["ID"]),
                Empcd = r["EMPCD"]?.ToString(),
                EmpName = r["EMP_NAME"]?.ToString(),
                Title = r["TITLE"]?.ToString(),
                Status = r["STATUS"]?.ToString(),
                UnreadEmp = r["UNREAD_EMP"] == DBNull.Value ? 0 : Convert.ToInt32(r["UNREAD_EMP"]),
            },
            new OracleParameter("ID", chatId));
        return rows.FirstOrDefault();
    }

    private async Task<object> LoadMessagesAsync(decimal chatId, decimal afterMsgId)
    {
        return await _db.ExecuteQueryAsync(@"
            SELECT ID, SENDER_TYPE, SENDER_CD, SENDER_NAME, CONTENT, AI_FAILED, SENT_DT
            FROM HRMS.HR_AI_CHAT_MSG
            WHERE CHAT_ID = :ID AND ID > :AFTER
            ORDER BY ID",
            r => new
            {
                id         = Convert.ToDecimal(r["ID"]),
                senderType = r["SENDER_TYPE"]?.ToString(),
                senderCd   = r["SENDER_CD"]?.ToString(),
                senderName = r["SENDER_NAME"]?.ToString(),
                content    = r["CONTENT"]?.ToString(),
                aiFailed   = r["AI_FAILED"] != DBNull.Value && Convert.ToInt32(r["AI_FAILED"]) == 1,
                sentDt     = r["SENT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["SENT_DT"]),
            },
            new OracleParameter("ID", chatId), new OracleParameter("AFTER", afterMsgId));
    }

    private Task InsertMsgAsync(decimal chatId, string senderType, string? senderCd, string? senderName, string content, bool aiFailed)
        => _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_AI_CHAT_MSG (CHAT_ID, SENDER_TYPE, SENDER_CD, SENDER_NAME, CONTENT, AI_FAILED)
            VALUES (:CID, :ST, :CD, :NM, :CT, :AF)",
            new OracleParameter("CID", chatId),
            new OracleParameter("ST", senderType),
            new OracleParameter("CD", (object?)senderCd ?? DBNull.Value),
            new OracleParameter("NM", (object?)senderName ?? DBNull.Value),
            new OracleParameter("CT", (object?)content ?? DBNull.Value),
            new OracleParameter("AF", aiFailed ? 1 : 0));

    // ── request models ──────────────────────────────────────────
    public class AskBody      { public string? Empcd { get; set; } public string? EmpName { get; set; } public string? Question { get; set; } public string? Language { get; set; } }
    public class EmpBody      { public string? Empcd { get; set; } }
    public class ChatEmpBody  { public decimal ChatId { get; set; } public string? Empcd { get; set; } }
    public class ChatIdBody   { public decimal ChatId { get; set; } }
    public class CsrReplyBody { public decimal ChatId { get; set; } public string? CsrEmpcd { get; set; } public string? CsrName { get; set; } public string? Content { get; set; } }
    public class SetStatusBody{ public decimal ChatId { get; set; } public string? ActorEmpcd { get; set; } public string Status { get; set; } = "CLOSED"; }
}
