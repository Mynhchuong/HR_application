using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Services;

namespace HR_api.Controllers;

/// <summary>
/// Đăng ký Kaizen: mọi nhân viên gửi ý tưởng + SĐT/Zalo, bộ phận Lean xem log và liên lạc (xem Scripts/create_kaizen.sql).
/// An toàn khi DB chưa có bảng HR_KAIZEN_REG: mọi endpoint trả "chức năng chưa được bật", không ảnh hưởng chức năng khác.
/// </summary>
[ApiController]
[Route("apiHR/[controller]")]
public class KaizenController : ControllerBase
{
    private const int MinIdeaLength = 10;
    private const int MaxIdeaLength = 2000;

    private readonly OracleService _db;
    private readonly KaizenMailService _mail;
    public KaizenController(OracleService db, KaizenMailService mail) { _db = db; _mail = mail; }

    // ── Dò bảng có chưa (kiểm tra lại mỗi 60 giây cho tới khi có) ────────────────────────────────
    private static volatile bool _available;
    private static DateTime _nextProbeUtc = DateTime.MinValue;

    private async Task<bool> IsAvailableAsync()
    {
        if (_available) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;
        bool ok;
        try
        {
            var rows = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_KAIZEN_REG'",
                r => Convert.ToInt32(r["C"]));
            ok = rows.FirstOrDefault() > 0;
        }
        catch { ok = false; }
        _available = ok;
        _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    private static readonly object NotReady = new { success = false, available = false, message = "Chức năng Kaizen chưa được bật trên hệ thống" };

    public class SubmitKaizenRequest
    {
        public string EMPCD { get; set; } = "";
        public string? PHONE { get; set; }
        public string? IDEA { get; set; }
    }

    // POST /apiHR/Kaizen/submit  { EMPCD, PHONE, IDEA }
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitKaizenRequest req)
    {
        try
        {
            if (!await IsAvailableAsync()) return Ok(NotReady);
            var empcd = req?.EMPCD?.Trim() ?? "";
            if (empcd.Length == 0) return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            // SĐT: bỏ khoảng trắng . - ( ), cho phép + đầu; 9–12 chữ số
            var phone = new string((req!.PHONE ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
            if (phone.StartsWith("+")) phone = "+" + phone.Substring(1).Replace("+", "");
            else phone = phone.Replace("+", "");
            var digits = phone.TrimStart('+');
            if (digits.Length < 9 || digits.Length > 12)
                return Ok(new { success = false, message = "Số điện thoại / Zalo không hợp lệ (9–12 chữ số)" });

            var idea = (req.IDEA ?? "").Trim();
            if (idea.Length < MinIdeaLength)
                return Ok(new { success = false, message = $"Vui lòng mô tả ý tưởng rõ hơn (ít nhất {MinIdeaLength} ký tự)" });
            if (idea.Length > MaxIdeaLength)
                return Ok(new { success = false, message = $"Mô tả ý tưởng quá dài (tối đa {MaxIdeaLength} ký tự)" });

            // Thông tin nhân viên lúc nộp
            var emp = (await _db.ExecuteQueryAsync(@"
                SELECT EC.EMPCD, EC.CNAME, EC.DEPTCD, EC.LINECD, EC.WORKCD,
                       B.DEPTNM, B.TEAMNM, B.WORKNM
                  FROM HRMS.ECM100 EC
                  LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                 WHERE EC.EMPCD = :E AND ROWNUM = 1",
                r => new
                {
                    Name  = r["CNAME"]?.ToString(),
                    Dept  = r["DEPTCD"]?.ToString(), DeptNm = r["DEPTNM"]?.ToString(),
                    Line  = r["LINECD"]?.ToString(), LineNm = r["TEAMNM"]?.ToString(),
                    Work  = r["WORKCD"]?.ToString(), WorkNm = r["WORKNM"]?.ToString()
                },
                new OracleParameter("E", empcd))).FirstOrDefault();
            if (emp == null) return Ok(new { success = false, message = "Không tìm thấy nhân viên" });

            // KHÔNG giới hạn số ý tưởng mỗi ngày (yêu cầu 2026-10-28); chỉ chặn bấm gửi 2 lần liên tiếp cùng nội dung
            var dup = (await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) AS C FROM HRMS.HR_KAIZEN_REG WHERE EMPCD = :E AND IDEA = :I AND SUBMIT_DT > SYSDATE - 10/1440",
                r => Convert.ToInt32(r["C"]),
                new OracleParameter("E", empcd),
                new OracleParameter("I", OracleDbType.NVarchar2) { Value = idea })).FirstOrDefault();
            if (dup > 0)
                return Ok(new { success = true, duplicate = true, message = "Ý tưởng này vừa được đăng ký rồi" });

            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_KAIZEN_REG
                    (EMPCD, EMP_NAME, DEPTCD, DEPT_NAME, LINECD, LINE_NAME, WORKCD, WORK_NAME, PHONE, IDEA, KAIZEN_MONTH, SUBMIT_DT)
                VALUES
                    (:EMPCD, :ENAME, :DEPTCD, :DEPTNM, :LINECD, :LINENM, :WORKCD, :WORKNM, :PHONE, :IDEA, TO_CHAR(SYSDATE,'YYYY-MM'), SYSDATE)",
                new OracleParameter("EMPCD",  empcd),
                new OracleParameter("ENAME",  (object?)emp.Name ?? DBNull.Value),
                new OracleParameter("DEPTCD", (object?)emp.Dept ?? DBNull.Value),
                new OracleParameter("DEPTNM", (object?)emp.DeptNm ?? DBNull.Value),
                new OracleParameter("LINECD", (object?)emp.Line ?? DBNull.Value),
                new OracleParameter("LINENM", (object?)emp.LineNm ?? DBNull.Value),
                new OracleParameter("WORKCD", (object?)emp.Work ?? DBNull.Value),
                new OracleParameter("WORKNM", (object?)emp.WorkNm ?? DBNull.Value),
                new OracleParameter("PHONE",  phone),
                new OracleParameter("IDEA",   OracleDbType.NVarchar2) { Value = idea });

            return Ok(new { success = true, message = "Đã đăng ký Kaizen. Bộ phận Lean sẽ liên lạc với bạn sau." });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Kaizen/my?empcd=  — lịch sử đã nộp của chính nhân viên (mới nhất trước, tối đa 200)
    [HttpGet("my")]
    public async Task<IActionResult> My(string empcd)
    {
        try
        {
            if (!await IsAvailableAsync()) return Ok(NotReady);
            if (string.IsNullOrWhiteSpace(empcd)) return Ok(new { success = true, available = true, data = new List<object>() });

            var data = await _db.ExecuteQueryAsync(@"
                SELECT * FROM (
                    SELECT ID, KAIZEN_MONTH, PHONE, IDEA, SUBMIT_DT
                      FROM HRMS.HR_KAIZEN_REG WHERE EMPCD = :E
                     ORDER BY SUBMIT_DT DESC, ID DESC
                ) WHERE ROWNUM <= 200",
                r => new
                {
                    id    = Convert.ToInt32(r["ID"]),
                    month = r["KAIZEN_MONTH"]?.ToString(),
                    phone = r["PHONE"]?.ToString(),
                    idea  = r["IDEA"]?.ToString(),
                    submitDt = Convert.ToDateTime(r["SUBMIT_DT"])
                },
                new OracleParameter("E", empcd.Trim()));
            return Ok(new { success = true, available = true, data });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/Kaizen/admin/list?month=&deptcd=&search=&page=&page_size=  — bộ phận Lean xem log toàn công ty
    [HttpGet("admin/list")]
    public async Task<IActionResult> AdminList(string? month = null, string? deptcd = null, string? search = null,
                                               int page = 1, int page_size = 50)
    {
        try
        {
            if (!await IsAvailableAsync()) return Ok(NotReady);
            if (page < 1) page = 1;
            if (page_size < 1) page_size = 50;
            if (page_size > 5000) page_size = 5000;

            var where = new List<string> { "1 = 1" };
            var ps = new List<OracleParameter>();
            if (!string.IsNullOrWhiteSpace(month))  { where.Add("K.KAIZEN_MONTH = :MONTH"); ps.Add(new OracleParameter("MONTH", month.Trim())); }
            if (!string.IsNullOrWhiteSpace(deptcd)) { where.Add("K.DEPTCD = :DEPTCD");      ps.Add(new OracleParameter("DEPTCD", deptcd.Trim())); }
            if (!string.IsNullOrWhiteSpace(search))
            {
                where.Add("(UPPER(K.EMPCD) LIKE :SRCH OR K.PHONE LIKE :SRCH OR UPPER(K.IDEA) LIKE :SRCH_N)");
                ps.Add(new OracleParameter("SRCH", "%" + search.Trim().ToUpper() + "%"));
                ps.Add(new OracleParameter("SRCH_N", OracleDbType.NVarchar2) { Value = "%" + search.Trim().ToUpper() + "%" });
            }
            var whereSql = " WHERE " + string.Join(" AND ", where);

            OracleParameter[] Clone() => ps.Select(p => (OracleParameter)p.Clone()).ToArray();

            var total = (await _db.ExecuteQueryAsync("SELECT COUNT(*) AS C FROM HRMS.HR_KAIZEN_REG K" + whereSql,
                r => Convert.ToInt32(r["C"]), Clone())).FirstOrDefault();

            int offset = (page - 1) * page_size;
            var dataParams = Clone().ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", offset + page_size));
            var data = await _db.ExecuteQueryAsync($@"
                SELECT * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.SUBMIT_DT DESC, T.ID DESC) RN FROM (
                        SELECT K.ID, K.EMPCD, K.EMP_NAME, K.DEPTCD, K.DEPT_NAME, K.LINECD, K.LINE_NAME,
                               K.WORKCD, K.WORK_NAME, K.PHONE, K.IDEA, K.KAIZEN_MONTH, K.SUBMIT_DT
                          FROM HRMS.HR_KAIZEN_REG K{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX",
                r => new
                {
                    id = Convert.ToInt32(r["ID"]),
                    empcd = r["EMPCD"]?.ToString(), empName = r["EMP_NAME"]?.ToString(),
                    deptcd = r["DEPTCD"]?.ToString(), deptName = r["DEPT_NAME"]?.ToString(),
                    linecd = r["LINECD"]?.ToString(), lineName = r["LINE_NAME"]?.ToString(),
                    workcd = r["WORKCD"]?.ToString(), workName = r["WORK_NAME"]?.ToString(),
                    phone = r["PHONE"]?.ToString(), idea = r["IDEA"]?.ToString(),
                    month = r["KAIZEN_MONTH"]?.ToString(),
                    submitDt = Convert.ToDateTime(r["SUBMIT_DT"])
                }, dataParams.ToArray());

            // Dữ liệu cho ô lọc: danh sách tháng + dept đã có đăng ký
            var months = await _db.ExecuteQueryAsync(
                "SELECT DISTINCT KAIZEN_MONTH AS M FROM HRMS.HR_KAIZEN_REG ORDER BY KAIZEN_MONTH DESC",
                r => r["M"]?.ToString() ?? "");
            var depts = await _db.ExecuteQueryAsync(
                "SELECT DISTINCT DEPTCD, DEPT_NAME FROM HRMS.HR_KAIZEN_REG WHERE DEPTCD IS NOT NULL ORDER BY DEPTCD",
                r => new { code = r["DEPTCD"]?.ToString(), name = r["DEPT_NAME"]?.ToString() });

            return Ok(new
            {
                success = true, available = true, data, total, page, page_size,
                total_pages = (int)Math.Ceiling((double)total / page_size),
                months, depts
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ════════════════════════════════════════════════════════════════════════════
    // EMAIL BÁO CÁO HẰNG TUẦN (thứ Sáu 08:00) — Lean quản lý danh sách người nhận
    // ════════════════════════════════════════════════════════════════════════════
    private static readonly System.Text.RegularExpressions.Regex EmailRx =
        new(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public class MailRecipientRequest
    {
        public int    ID { get; set; }
        public string? EMAIL { get; set; }
        public string? LOGIN_USER { get; set; }
    }

    // GET /apiHR/Kaizen/mail/list -> người nhận + lần gửi tự động gần nhất
    [HttpGet("mail/list")]
    public async Task<IActionResult> MailList()
    {
        try
        {
            if (!await _mail.TablesReadyAsync()) return Ok(new { success = false, available = false, message = "Chức năng gửi email chưa được bật trên hệ thống" });
            var recipients = (await _mail.GetRecipientsAsync()).Select(x => new { id = x.Id, email = x.Email }).ToList();
            var last = (await _db.ExecuteQueryAsync(@"
                SELECT * FROM (SELECT SENT_DT, PERIOD_FROM, PERIOD_TO, ROW_COUNT, STATUS, ERR
                                 FROM HRMS.HR_KAIZEN_MAIL_LOG ORDER BY SENT_DT DESC, ID DESC) WHERE ROWNUM = 1",
                r => new
                {
                    sentDt = Convert.ToDateTime(r["SENT_DT"]),
                    rows = r["ROW_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["ROW_COUNT"]),
                    status = r["STATUS"]?.ToString(),
                    err = r["ERR"]?.ToString()
                })).FirstOrDefault();
            return Ok(new { success = true, available = true, recipients, last, schedule = $"Thứ Sáu hằng tuần, {KaizenMailService.SendHour:00}:00", max = KaizenMailService.MaxRecipients });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Kaizen/mail/add { EMAIL, LOGIN_USER }
    [HttpPost("mail/add")]
    public async Task<IActionResult> MailAdd([FromBody] MailRecipientRequest req)
    {
        try
        {
            if (!await _mail.TablesReadyAsync()) return Ok(new { success = false, message = "Chức năng gửi email chưa được bật trên hệ thống" });
            var email = (req?.EMAIL ?? "").Trim().ToLowerInvariant();
            if (email.Length == 0 || email.Length > 200 || !EmailRx.IsMatch(email))
                return Ok(new { success = false, message = "Địa chỉ email không hợp lệ" });
            var all = await _mail.GetRecipientsAsync();
            if (all.Any(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
                return Ok(new { success = false, message = "Email này đã có trong danh sách" });
            if (all.Count >= KaizenMailService.MaxRecipients)
                return Ok(new { success = false, message = $"Tối đa {KaizenMailService.MaxRecipients} email nhận báo cáo" });
            await _db.ExecuteNonQueryAsync("INSERT INTO HRMS.HR_KAIZEN_MAIL_TO (EMAIL, INST_ID, INST_DT) VALUES (:E, :U, SYSDATE)",
                new OracleParameter("E", email),
                new OracleParameter("U", (object?)req?.LOGIN_USER ?? DBNull.Value));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Kaizen/mail/remove { ID }
    [HttpPost("mail/remove")]
    public async Task<IActionResult> MailRemove([FromBody] MailRecipientRequest req)
    {
        try
        {
            if (!await _mail.TablesReadyAsync()) return Ok(new { success = false, message = "Chức năng gửi email chưa được bật trên hệ thống" });
            var n = await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_KAIZEN_MAIL_TO WHERE ID = :I", new OracleParameter("I", req?.ID ?? 0));
            return Ok(new { success = n > 0 });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Kaizen/mail/test { EMAIL } — gửi thử báo cáo 7 ngày gần nhất tới 1 địa chỉ (không ghi log lịch gửi)
    [HttpPost("mail/test")]
    public async Task<IActionResult> MailTest([FromBody] MailRecipientRequest req)
    {
        try
        {
            if (!await _mail.TablesReadyAsync()) return Ok(new { success = false, message = "Chức năng gửi email chưa được bật trên hệ thống" });
            var email = (req?.EMAIL ?? "").Trim();
            if (!EmailRx.IsMatch(email)) return Ok(new { success = false, message = "Địa chỉ email không hợp lệ" });
            // Chỉ cho gửi thử tới địa chỉ đang nằm trong danh sách (tránh biến thành công cụ gửi mail tới địa chỉ bất kỳ)
            var all = await _mail.GetRecipientsAsync();
            if (!all.Any(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
                return Ok(new { success = false, message = "Email này chưa có trong danh sách nhận báo cáo" });
            var (ok, msg) = await _mail.SendTestAsync(email);
            return Ok(new { success = ok, message = msg });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }
}
