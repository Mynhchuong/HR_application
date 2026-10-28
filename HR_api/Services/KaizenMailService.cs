using System.Net;
using System.Net.Mail;
using ClosedXML.Excel;
using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

/// <summary>
/// Gửi file Excel đăng ký Kaizen qua email cho bộ phận Lean (danh sách email do Lean nhập trên trang Quản lý Kaizen).
/// Gửi tự động mỗi THỨ SÁU 08:00 bởi HostedServices/KaizenWeeklyMailService.cs; "Gửi thử" gọi SendTestAsync.
/// SMTP cấu hình ở appsettings.json mục "Mail". Bảng: Scripts/create_kaizen.sql.
/// </summary>
public class KaizenMailService
{
    public const int SendHour = 8;                    // giờ gửi (giờ DB) vào thứ Sáu
    public const int MaxRecipients = 20;
    private static readonly TimeSpan RetryAfterFail = TimeSpan.FromMinutes(30);

    private readonly OracleService _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<KaizenMailService> _log;

    public KaizenMailService(OracleService db, IConfiguration cfg, ILogger<KaizenMailService> log)
    {
        _db = db; _cfg = cfg; _log = log;
    }

    // ── Dò 3 bảng (kiểm tra lại mỗi 60 giây cho tới khi có) ─────────────────────────────────────
    private static volatile bool _ready;
    private static DateTime _nextProbeUtc = DateTime.MinValue;

    public async Task<bool> TablesReadyAsync()
    {
        if (_ready) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;
        bool ok;
        try
        {
            var n = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS'
                   AND TABLE_NAME IN ('HR_KAIZEN_REG', 'HR_KAIZEN_MAIL_TO', 'HR_KAIZEN_MAIL_LOG')",
                r => Convert.ToInt32(r["C"]))).FirstOrDefault();
            ok = n == 3;
        }
        catch { ok = false; }
        _ready = ok;
        _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    // ── Người nhận ──────────────────────────────────────────────────────────────────────────────
    public Task<List<(int Id, string Email)>> GetRecipientsAsync() =>
        _db.ExecuteQueryAsync("SELECT ID, EMAIL FROM HRMS.HR_KAIZEN_MAIL_TO ORDER BY EMAIL",
            r => (Convert.ToInt32(r["ID"]), r["EMAIL"]?.ToString() ?? ""));

    // ── Báo cáo Excel theo khoảng thời gian nộp (from, to] ───────────────────────────────────────
    public async Task<(byte[]? Xlsx, int Rows)> BuildReportAsync(DateTime from, DateTime to)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT K.SUBMIT_DT, K.KAIZEN_MONTH, K.EMPCD, K.EMP_NAME, K.DEPT_NAME, K.DEPTCD, K.LINE_NAME, K.LINECD,
                   K.WORK_NAME, K.WORKCD, K.PHONE, K.IDEA
              FROM HRMS.HR_KAIZEN_REG K
             WHERE K.SUBMIT_DT > :F AND K.SUBMIT_DT <= :T
             ORDER BY K.SUBMIT_DT, K.ID",
            r => new
            {
                Dt = Convert.ToDateTime(r["SUBMIT_DT"]), Month = r["KAIZEN_MONTH"]?.ToString(),
                Emp = r["EMPCD"]?.ToString(), Name = r["EMP_NAME"]?.ToString(),
                Dept = r["DEPT_NAME"]?.ToString() ?? r["DEPTCD"]?.ToString(),
                Line = r["LINE_NAME"]?.ToString() ?? r["LINECD"]?.ToString(),
                Work = r["WORK_NAME"]?.ToString() ?? r["WORKCD"]?.ToString(),
                Phone = r["PHONE"]?.ToString(), Idea = r["IDEA"]?.ToString()
            },
            new OracleParameter("F", OracleDbType.Date) { Value = from },
            new OracleParameter("T", OracleDbType.Date) { Value = to });

        if (rows.Count == 0) return (null, 0);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Kaizen");
        string[] heads = { "Ngày nộp", "Tháng", "Mã NV", "Họ tên", "Dept", "Line", "Work", "SĐT / Zalo", "Ý tưởng" };
        for (int c = 0; c < heads.Length; c++) { ws.Cell(1, c + 1).Value = heads[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
        int i = 2;
        foreach (var x in rows)
        {
            ws.Cell(i, 1).Value = x.Dt.ToString("dd/MM/yyyy HH:mm");
            ws.Cell(i, 2).Value = x.Month ?? "";
            ws.Cell(i, 3).Value = x.Emp ?? "";
            ws.Cell(i, 4).Value = x.Name ?? "";
            ws.Cell(i, 5).Value = x.Dept ?? "";
            ws.Cell(i, 6).Value = x.Line ?? "";
            ws.Cell(i, 7).Value = x.Work ?? "";
            ws.Cell(i, 8).Value = "'" + (x.Phone ?? "");       // giữ số 0 đầu
            ws.Cell(i, 9).Value = x.Idea ?? "";
            i++;
        }
        ws.Columns().AdjustToContents(1, Math.Min(i, 200));
        ws.Column(9).Width = 70;
        ws.Column(9).Style.Alignment.WrapText = true;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), rows.Count);
    }

    // ── Gửi mail (SMTP cấu hình ở appsettings "Mail") ───────────────────────────────────────────
    public async Task SendAsync(IEnumerable<string> to, string subject, string body, byte[]? xlsx, string fileName)
    {
        var host = _cfg["Mail:Host"];
        var user = _cfg["Mail:User"];
        var pass = _cfg["Mail:Password"];
        var from = _cfg["Mail:FromAddress"] ?? user;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Chưa cấu hình SMTP (appsettings.json mục Mail)");
        int port = int.TryParse(_cfg["Mail:Port"], out var p) ? p : 587;
        bool ssl = bool.TryParse(_cfg["Mail:EnableSsl"], out var s) && s;

        using var msg = new MailMessage
        {
            From = new MailAddress(from, _cfg["Mail:FromName"] ?? "My Samho"),
            Subject = subject, Body = body, IsBodyHtml = false,
            SubjectEncoding = System.Text.Encoding.UTF8, BodyEncoding = System.Text.Encoding.UTF8
        };
        foreach (var addr in to) msg.To.Add(addr);
        MemoryStream? ms = null;
        try
        {
            if (xlsx != null)
            {
                ms = new MemoryStream(xlsx);
                msg.Attachments.Add(new Attachment(ms, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            }
            using var smtp = new SmtpClient
            {
                Host = host, Port = port, EnableSsl = ssl,
                DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false,
                Timeout = 60_000
            };
            if (!string.IsNullOrWhiteSpace(user)) smtp.Credentials = new NetworkCredential(user, pass);
            await smtp.SendMailAsync(msg);
        }
        finally { ms?.Dispose(); }
    }

    /// <summary>Gửi thử tới 1 địa chỉ: báo cáo 7 ngày gần nhất. KHÔNG ghi log và KHÔNG ảnh hưởng lịch gửi tự động.</summary>
    public async Task<(bool Ok, string Message)> SendTestAsync(string email)
    {
        try
        {
            var now = (await _db.ExecuteQueryAsync("SELECT SYSDATE AS D FROM DUAL", r => Convert.ToDateTime(r["D"]))).First();
            var from = now.AddDays(-7);
            var (xlsx, rows) = await BuildReportAsync(from, now);
            await SendAsync(new[] { email },
                $"[My Samho] (GỬI THỬ) Đăng ký Kaizen {from:dd/MM} - {now:dd/MM/yyyy}",
                BuildBody(from, now, rows, test: true), xlsx, $"Kaizen_{now:yyyyMMdd}.xlsx");
            return (true, $"Đã gửi thử tới {email} ({rows} đăng ký trong 7 ngày gần nhất)");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Kaizen test mail failed");
            return (false, "Gửi không được: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    private static string BuildBody(DateTime from, DateTime to, int rows, bool test = false)
    {
        var head = test ? "(Email gửi thử từ trang Quản lý Kaizen)\n\n" : "";
        if (rows == 0)
            return head + $"Không có đăng ký Kaizen mới từ {from:dd/MM/yyyy HH:mm} đến {to:dd/MM/yyyy HH:mm}.\n\n— My Samho";
        return head + $"Danh sách đăng ký Kaizen từ {from:dd/MM/yyyy HH:mm} đến {to:dd/MM/yyyy HH:mm}: {rows} đăng ký (xem file Excel đính kèm).\n\n— My Samho";
    }

    // ── Gửi định kỳ: gọi mỗi phút bởi KaizenWeeklyMailService ───────────────────────────────────
    public async Task TryRunWeeklyAsync()
    {
        if (!await TablesReadyAsync()) return;

        var clock = (await _db.ExecuteQueryAsync(
            "SELECT SYSDATE AS D, TO_CHAR(SYSDATE, 'DY', 'NLS_DATE_LANGUAGE=ENGLISH') AS DOW FROM DUAL",
            r => (Now: Convert.ToDateTime(r["D"]), Dow: r["DOW"]?.ToString()?.Trim().ToUpperInvariant()))).First();
        // Thứ Sáu, từ 08:00 trở đi (nếu 8h máy chủ đang ngủ/khởi động lại thì tự bù lại ngay khi chạy, trong cùng ngày thứ Sáu)
        if (clock.Dow != "FRI" || clock.Now.Hour < SendHour) return;

        // Hôm nay đã gửi OK chưa?
        var sentToday = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) AS C FROM HRMS.HR_KAIZEN_MAIL_LOG WHERE STATUS = 'OK' AND SENT_DT >= TRUNC(SYSDATE)",
            r => Convert.ToInt32(r["C"]))).First();
        if (sentToday > 0) return;

        // Vừa thất bại -> chờ 30 phút rồi mới thử lại (không dội server mail)
        var recentFail = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) AS C FROM HRMS.HR_KAIZEN_MAIL_LOG WHERE STATUS = 'FAIL' AND SENT_DT > SYSDATE - :M / 1440",
            r => Convert.ToInt32(r["C"]), new OracleParameter("M", (int)RetryAfterFail.TotalMinutes))).First();
        if (recentFail > 0) return;

        var recipients = (await GetRecipientsAsync()).Select(x => x.Email).ToList();
        if (recipients.Count == 0) return;   // chưa có ai nhận -> bỏ qua (không ghi log)

        // Kỳ báo cáo: từ lần gửi OK gần nhất tới bây giờ (lần đầu: 7 ngày gần nhất)
        var last = (await _db.ExecuteQueryAsync(
            "SELECT MAX(PERIOD_TO) AS D FROM HRMS.HR_KAIZEN_MAIL_LOG WHERE STATUS = 'OK'",
            r => r["D"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["D"]))).First();
        var from = last ?? clock.Now.AddDays(-7);
        var to = clock.Now;

        int rows = 0;
        try
        {
            var (xlsx, n) = await BuildReportAsync(from, to);
            rows = n;
            await SendAsync(recipients,
                $"[My Samho] Đăng ký Kaizen {from:dd/MM} - {to:dd/MM/yyyy}",
                BuildBody(from, to, n), xlsx, $"Kaizen_{to:yyyyMMdd}.xlsx");
            await WriteLogAsync("OK", from, to, rows, recipients, null);
            _log.LogInformation("Kaizen weekly mail sent to {Count} recipients, {Rows} rows", recipients.Count, rows);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Kaizen weekly mail failed");
            await WriteLogAsync("FAIL", from, to, rows, recipients, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private Task WriteLogAsync(string status, DateTime from, DateTime to, int rows, List<string> recipients, string? err)
    {
        var rec = string.Join(", ", recipients);
        if (rec.Length > 1000) rec = rec.Substring(0, 1000);
        if (err != null && err.Length > 500) err = err.Substring(0, 500);
        return _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_KAIZEN_MAIL_LOG (SENT_DT, PERIOD_FROM, PERIOD_TO, ROW_COUNT, RECIPIENTS, STATUS, ERR)
            VALUES (SYSDATE, :F, :T, :N, :R, :S, :E)",
            new OracleParameter("F", OracleDbType.Date) { Value = from },
            new OracleParameter("T", OracleDbType.Date) { Value = to },
            new OracleParameter("N", rows),
            new OracleParameter("R", rec),
            new OracleParameter("S", status),
            new OracleParameter("E", (object?)err ?? DBNull.Value));
    }
}
