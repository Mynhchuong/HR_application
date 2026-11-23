using System.Net;
using System.Net.Mail;
using System.Text;
using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

/// <summary>
/// Email chốt số suất ăn hằng ngày cho căn tin (yêu cầu 2026-11-23): tới giờ chốt HR đặt (mặc định 21:00) gửi số suất
/// bữa trưa của NGÀY LÀM VIỆC KẾ TIẾP theo giờ nghỉ trưa — "Dự kiến thực ăn" (nên nấu) + "Theo đăng ký" — để căn tin
/// đi chợ. Chạy mỗi phút bởi HostedServices/CanteenDailyMailHostedService.cs. SMTP ở appsettings "Mail".
/// Bảng: Scripts/create_canteen_daily_mail.sql.
/// </summary>
public class CanteenDailyMailService
{
    public const int MaxRecipients = 20;
    private static readonly TimeSpan RetryAfterFail = TimeSpan.FromMinutes(30);

    private readonly OracleService _db;
    private readonly CanteenSummaryService _sum;
    private readonly IConfiguration _cfg;
    private readonly ILogger<CanteenDailyMailService> _log;

    public CanteenDailyMailService(OracleService db, CanteenSummaryService sum, IConfiguration cfg, ILogger<CanteenDailyMailService> log)
    {
        _db = db; _sum = sum; _cfg = cfg; _log = log;
    }

    public Task<List<(int Id, string Email)>> GetRecipientsAsync() =>
        _db.ExecuteQueryAsync("SELECT ID, EMAIL FROM HRMS.HR_CANTEEN_MAIL_TO ORDER BY EMAIL",
            r => (Convert.ToInt32(r["ID"]), r["EMAIL"]?.ToString() ?? ""));

    private async Task<DateTime> DbNowAsync() =>
        (await _db.ExecuteQueryAsync("SELECT SYSDATE AS D FROM DUAL", r => Convert.ToDateTime(r["D"]))).First();

    /// <summary>Ngày làm việc kế tiếp sau <paramref name="now"/> (bỏ CN + ngày lễ EAM800).</summary>
    public async Task<DateTime> TargetDateAsync(DateTime now)
    {
        var hol = await _sum.GetHolidaysAsync(now.Date, now.Date.AddDays(20));
        return CanteenSummaryService.NextWorkingDay(now.Date, hol);
    }

    // ── Gửi định kỳ: gọi mỗi phút ──────────────────────────────────────────────────────────────
    public async Task TryRunDailyAsync()
    {
        var cfg = await _sum.GetCutoffConfigAsync();
        if (cfg == null || !cfg.Active) return;

        var now = await DbNowAsync();
        // Từ giờ chốt tới hết ngày (máy chủ khởi động lại sau giờ chốt thì tự bù trong cùng ngày)
        if (now < now.Date.AddHours(cfg.Hour).AddMinutes(cfg.Minute)) return;

        var target = await TargetDateAsync(now);

        // Ngày này đã gửi OK chưa? (theo ngày suất ăn — thứ Bảy đã gửi số thứ Hai thì Chủ nhật không gửi lại)
        var sent = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) AS C FROM HRMS.HR_CANTEEN_MAIL_LOG WHERE STATUS = 'OK' AND TARGET_DAT = :D",
            r => Convert.ToInt32(r["C"]), new OracleParameter("D", OracleDbType.Date) { Value = target })).First();
        if (sent > 0) return;

        var recentFail = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) AS C FROM HRMS.HR_CANTEEN_MAIL_LOG WHERE STATUS = 'FAIL' AND SENT_DT > SYSDATE - :M / 1440",
            r => Convert.ToInt32(r["C"]), new OracleParameter("M", (int)RetryAfterFail.TotalMinutes))).First();
        if (recentFail > 0) return;

        var recipients = (await GetRecipientsAsync()).Select(x => x.Email).ToList();
        if (recipients.Count == 0) return;   // chưa có ai nhận -> bỏ qua (không ghi log)

        CanteenSummaryService.DaySummary? day = null;
        try
        {
            day = await BuildDayAsync(target);
            // ERP chưa có lịch ca ngày đó → không gửi email 0 suất; ghi FAIL để 30 phút sau thử lại
            if (day.lunch.total == 0)
                throw new InvalidOperationException($"Chưa có lịch ca / suất ăn ngày {target:dd/MM/yyyy} trong ERP");
            var (subject, html) = BuildMail(target, day, cfg, test: false);
            await SendAsync(recipients, subject, html);
            await WriteLogAsync("OK", target, day, recipients, null, null);
            _log.LogInformation("Canteen daily mail {Date} sent to {Count} recipients", target.ToString("yyyy-MM-dd"), recipients.Count);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Canteen daily mail failed");
            await WriteLogAsync("FAIL", target, day, recipients, ex.InnerException?.Message ?? ex.Message, null);
        }
    }

    /// <summary>Gửi thử ngay (số của ngày làm việc kế tiếp) tới toàn bộ danh sách. Ghi log TEST, không tính là đã gửi.</summary>
    public async Task<(bool Ok, string Message)> SendTestAsync(string? by)
    {
        var recipients = (await GetRecipientsAsync()).Select(x => x.Email).ToList();
        if (recipients.Count == 0) return (false, "Chưa có email nhận nào");
        var cfg = await _sum.GetCutoffConfigAsync() ?? new CanteenSummaryService.CutoffConfig(false, 21, 0);
        var target = await TargetDateAsync(await DbNowAsync());
        CanteenSummaryService.DaySummary? day = null;
        try
        {
            day = await BuildDayAsync(target);
            var (subject, html) = BuildMail(target, day, cfg, test: true);
            await SendAsync(recipients, subject, html);
            await WriteLogAsync("TEST", target, day, recipients, null, by);
            return (true, $"Đã gửi thử số suất ngày {target:dd/MM/yyyy} tới {recipients.Count} email");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Canteen test mail failed");
            return (false, "Gửi không được: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    public async Task<CanteenSummaryService.DaySummary> BuildDayAsync(DateTime target)
    {
        var day = (await _sum.GetAsync(target, target)).FirstOrDefault()
                  ?? new CanteenSummaryService.DaySummary { dat = target.ToString("yyyyMMdd") };
        CanteenSummaryService.ApplyExpected(new[] { day }, await _sum.GetEatRatioAsync(), await _sum.GetSpecialDaysAsync(target, target), await _sum.GetBufferPctAsync());
        return day;
    }

    // ── Nội dung mail (HTML, bảng theo giờ nghỉ trưa) ───────────────────────────────────────────
    private static readonly string[] Wd = { "Chủ nhật", "Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7" };

    private static (string Subject, string Html) BuildMail(DateTime target, CanteenSummaryService.DaySummary d,
        CanteenSummaryService.CutoffConfig cfg, bool test)
    {
        static string N(int v) => v.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
        static string Slot(string s) => s.Length == 9 && s[4] == '-' ? $"{s[..2]}:{s[2..4]} – {s[5..7]}:{s[7..9]}" : (s == "-" ? "Chưa xếp ca" : s);
        var exp = d.expected ?? d.lunch;
        var dayName = $"{Wd[(int)target.DayOfWeek]} {target:dd/MM/yyyy}";
        var subject = (test ? "[My Samho] (GỬI THỬ) " : "[My Samho] ") + $"Cần nấu {N(exp.total)} suất ăn trưa {dayName}";

        const string th = "padding:6px 12px;border:1px solid #cbd5e1;background:#f1f5f9;font-size:12px;text-align:right;white-space:nowrap";
        const string td = "padding:6px 12px;border:1px solid #e2e8f0;text-align:right;white-space:nowrap";
        // Chỉ ghi số CẦN NẤU (= dự kiến thực ăn) — căn tin không cần số đăng ký
        string Row(string label, CanteenSummaryService.MealCount e, bool sum = false)
        {
            var sb = new StringBuilder($"<tr{(sum ? " style=\"background:#fef3c7;font-weight:800\"" : "")}><td style=\"{td};text-align:left;font-weight:{(sum ? 800 : 600)}\">{label}</td>");
            foreach (var f in CanteenSummaryService.Foods) sb.Append($"<td style=\"{td}\">{N(e.Get(f))}</td>");
            sb.Append($"<td style=\"{td};background:#eef2ff;font-weight:800;font-size:15px\">{N(e.total)}</td></tr>");
            return sb.ToString();
        }

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#0f172a\">");
        if (test) html.Append("<p style=\"color:#b45309\"><b>(Email gửi thử — số có thể còn thay đổi tới giờ chốt)</b></p>");
        html.Append($"<h2 style=\"margin:0 0 4px;font-size:18px\">Số suất cần nấu — bữa trưa {dayName}</h2>");
        html.Append($"<p style=\"margin:0 0 12px;color:#475569\">Tổng cần nấu: <b style=\"color:#0f172a;font-size:16px\">{N(exp.total)} suất</b> · chốt lúc {cfg.Time}</p>");
        if (d.special) html.Append("<p style=\"margin:0 0 12px;color:#b45309\"><b>Ngày có CA 2 ĐẶC BIỆT</b> — đã tính theo số người đi ăn của các ngày đặc biệt trước (thường đông hơn ngày thường).</p>");
        html.Append("<table style=\"border-collapse:collapse\">");
        html.Append($"<tr><th style=\"{th};text-align:left\">Giờ nghỉ trưa</th><th style=\"{th}\">Mặn</th><th style=\"{th}\">Nhẹ</th><th style=\"{th}\">Chay</th><th style=\"{th}\">Bánh</th><th style=\"{th}\">Tổng</th></tr>");
        foreach (var s in d.slots) html.Append(Row("🕐 " + Slot(s.slot), s.expected ?? s.count));
        html.Append(Row("Cộng", exp, sum: true));
        html.Append("</table>");
        html.Append("<p style=\"margin:12px 0 0;color:#64748b;font-size:12px;line-height:1.6\">");
        html.Append("Số suất đã tính theo số người thật sự đi ăn những ngày gần đây (đã trừ người nghỉ phép, nghỉ thai sản).<br>");
        html.Append("Sau giờ chốt nhân viên không đổi món ngày này được nữa.");
        html.Append("</p><p style=\"color:#94a3b8;font-size:11px\">— My Samho</p></div>");
        return (subject, html.ToString());
    }

    private async Task SendAsync(IEnumerable<string> to, string subject, string html)
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
            Subject = subject, Body = html, IsBodyHtml = true,
            SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8
        };
        foreach (var addr in to) msg.To.Add(addr);
        using var smtp = new SmtpClient
        {
            Host = host, Port = port, EnableSsl = ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false,
            Timeout = 60_000
        };
        if (!string.IsNullOrWhiteSpace(user)) smtp.Credentials = new NetworkCredential(user, pass);
        await smtp.SendMailAsync(msg);
    }

    private Task WriteLogAsync(string status, DateTime target, CanteenSummaryService.DaySummary? d,
        List<string> recipients, string? err, string? by)
    {
        var rec = string.Join(", ", recipients);
        if (rec.Length > 1000) rec = rec.Substring(0, 1000);
        if (err != null && err.Length > 500) err = err.Substring(0, 500);
        return _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_CANTEEN_MAIL_LOG (TARGET_DAT, SENT_DT, STATUS, TOTAL_REG, TOTAL_EXP, RECIPIENTS, ERR, SENT_BY)
            VALUES (:D, SYSDATE, :S, :R, :E, :RC, :ER, :BY_EMP)",
            new OracleParameter("D", OracleDbType.Date) { Value = target },
            new OracleParameter("S", status),
            new OracleParameter("R", (object?)d?.lunch.total ?? DBNull.Value),
            new OracleParameter("E", (object?)d?.expected?.total ?? DBNull.Value),
            new OracleParameter("RC", rec),
            new OracleParameter("ER", (object?)err ?? DBNull.Value),
            new OracleParameter("BY_EMP", (object?)by ?? DBNull.Value));
    }
}
