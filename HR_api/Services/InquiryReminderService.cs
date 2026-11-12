using System.Text;
using HR_api.Data;
using HR_api.Helpers;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

/// <summary>
/// Email nhắc PHÒNG PHỤ TRÁCH (PIC) + CSR trước hạn phản hồi 1 ngày (Phase 2 — yêu cầu khách #4).
///   • Hội thoại: đang MỞ, có hạn (DEADLINE_DT) là hôm nay hoặc ngày mai, CHƯA có tin trả lời nào của HR/GA/CSR.
///   • Người nhận (user chỉ định 2026-11-06, bảng HR_INQUIRY_REMIND_TO, CSR/Admin sửa ở trang "Chủ đề hội thoại"):
///     email của CHỦ ĐỀ → nếu trống: email của PHÒNG PIC → nếu trống: các CSR (để case không bị bỏ sót).
///     Mỗi người nhận 1 email tổng hợp gồm đúng các case thuộc về mình.
///   • Nội dung email CHỈ gồm số hội thoại, chủ đề, mức độ, hạn, PIC — KHÔNG có tên/mã người phản ánh hay nội dung chat
///     (giữ đúng chính sách ẩn danh).
///   • Mỗi hội thoại chỉ nhắc 1 lần (HR_INQUIRY_REMIND_LOG). Gửi 08:00–17:59 giờ DB; lỗi SMTP thì thử lại sau 30 phút.
/// Dùng SMTP dùng chung của KaizenMailService (appsettings "Mail"). Email KHÔNG kèm link: người nhận tự mở My Samho
/// (user chốt 2026-11-06); nếu sau này cần link, điền appsettings "Inquiry:ReminderWebUrl".
/// </summary>
public class InquiryReminderService
{
    public const int FromHour = 8, ToHour = 18;
    private const string RemindType = "D1";
    private static readonly TimeSpan RetryAfterFail = TimeSpan.FromMinutes(30);

    private readonly OracleService _db;
    private readonly KaizenMailService _mail;
    private readonly IConfiguration _cfg;
    private readonly ILogger<InquiryReminderService> _log;
    private readonly IHostEnvironment _env;

    public InquiryReminderService(OracleService db, KaizenMailService mail, IConfiguration cfg, ILogger<InquiryReminderService> log, IHostEnvironment env)
    { _db = db; _mail = mail; _cfg = cfg; _log = log; _env = env; }

    public record DueCase(long Id, string No, string TopicCd, string Topic, string? Severity, DateTime Deadline, string? PicName);

    private static volatile bool _ready;
    private static DateTime _nextProbeUtc = DateTime.MinValue;
    private static DateTime _lastFailUtc = DateTime.MinValue;

    public async Task<bool> TableReadyAsync()
    {
        if (_ready) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;
        bool ok;
        try
        {
            var n = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_INQUIRY_REMIND_LOG'",
                r => Convert.ToInt32(r["C"]));
            ok = n.FirstOrDefault() > 0;
        }
        catch { ok = false; }
        if (ok) _ready = true; else _nextProbeUtc = DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    /// <summary>Hội thoại cần nhắc. includeReminded=true (xem thử / gửi thử) bỏ qua nhật ký đã nhắc.</summary>
    public Task<List<DueCase>> FindDueAsync(bool includeReminded, int max = 500)
    {
        string notReminded = includeReminded ? "" : @"
            AND NOT EXISTS (SELECT 1 FROM HRMS.HR_INQUIRY_REMIND_LOG l WHERE l.INQUIRY_ID = i.ID AND l.REMIND_TYPE = 'D1')";
        return _db.ExecuteQueryAsync($@"
            SELECT * FROM (
                SELECT i.ID, i.INQUIRY_NO, i.TOPIC_CD, t.TOPIC_NAME, s.NAME_VI AS SEV_NAME, i.DEADLINE_DT, dr.ROLE_NAME AS PIC_NAME
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC    t  ON t.TOPIC_CD    = i.TOPIC_CD
                LEFT JOIN HRMS.HR_INQUIRY_SEVERITY s  ON s.SEVERITY_CD = i.SEVERITY_CD
                LEFT JOIN HRMS.HR_DEPT_ROLES       dr ON dr.ID         = i.PIC_DEPT_ROLE_ID
                WHERE i.STATUS = 'OPEN' AND i.DEADLINE_DT IS NOT NULL
                  AND TRUNC(i.DEADLINE_DT) BETWEEN TRUNC(SYSDATE) AND TRUNC(SYSDATE) + 1
                  AND NOT EXISTS (SELECT 1 FROM HRMS.HR_INQUIRY_MSG m
                                   WHERE m.INQUIRY_ID = i.ID AND m.SENDER_TYPE = 'HR' AND m.IS_DELETED = 0)
                  {notReminded}
                ORDER BY i.DEADLINE_DT, i.ID
            ) WHERE ROWNUM <= :MAXN",
            r => new DueCase(
                Convert.ToInt64(r["ID"]), r["INQUIRY_NO"]?.ToString() ?? "", r["TOPIC_CD"]?.ToString() ?? "",
                r["TOPIC_NAME"] == DBNull.Value ? "—" : r["TOPIC_NAME"].ToString()!,
                r["SEV_NAME"] == DBNull.Value ? null : r["SEV_NAME"].ToString(),
                Convert.ToDateTime(r["DEADLINE_DT"]),
                r["PIC_NAME"] == DBNull.Value ? null : r["PIC_NAME"].ToString()),
            new OracleParameter("MAXN", max));
    }

    // Tách + lọc email hợp lệ từ chuỗi người dùng nhập (ngăn cách ; , xuống dòng, khoảng trắng)
    public static List<string> ParseEmails(string? raw)
        => (raw ?? "").Split(new[] { ';', ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length <= 120 && System.Text.RegularExpressions.Regex.IsMatch(x, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            .Distinct().ToList();

    private async Task<Dictionary<string, List<string>>> LoadConfigAsync()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rows = await _db.ExecuteQueryAsync("SELECT SCOPE_TYPE, SCOPE_KEY, EMAILS FROM HRMS.HR_INQUIRY_REMIND_TO",
                r => (T: r["SCOPE_TYPE"]?.ToString() ?? "", K: r["SCOPE_KEY"]?.ToString() ?? "", E: r["EMAILS"] == DBNull.Value ? "" : r["EMAILS"].ToString()!));
            foreach (var x in rows) map[x.T + ":" + x.K] = ParseEmails(x.E);
        }
        catch { /* bảng cấu hình chưa có → chỉ dùng CSR */ }
        return map;
    }

    // ĐÃ BỎ CsrEmailsAsync(): trước đây lấy EMAIL/EMAIL_VS trong ECM100 của mọi tài khoản CSR để
    // làm địa chỉ dự phòng. Hệ quả: NV nhận mail công việc vào hộp thư cá nhân (kể cả Gmail riêng)
    // dù không hề khai ở AdminInquiry/RemindEmails, và không tắt được từ giao diện.
    // Từ 2026-11-12 chỉ gửi tới địa chỉ khai tường minh ở trang đó. ĐỪNG thêm lại nhánh này.

    /// <summary>
    /// email → các case người đó cần xử lý. Thứ tự: email theo CHỦ ĐỀ → email theo PHÒNG (PIC).
    ///
    /// CHỈ gửi tới địa chỉ đã khai ở trang AdminInquiry/RemindEmails (yêu cầu 2026-11-12).
    /// TRƯỚC ĐÂY còn nhánh dự phòng lấy email cá nhân của từng tài khoản CSR trong ECM100 —
    /// bỏ hẳn: NV nhận mail công việc vào hộp thư CÁ NHÂN (có cả Gmail riêng) mà không hiểu vì sao,
    /// và không có cách nào tắt từ giao diện. Chủ đề/phòng chưa khai email thì KHÔNG gửi,
    /// nhưng có ghi log cảnh báo để HR biết mà vào khai.
    /// </summary>
    public async Task<Dictionary<string, List<DueCase>>> BuildDigestsAsync(List<DueCase> cases)
    {
        var map = new Dictionary<string, List<DueCase>>(StringComparer.OrdinalIgnoreCase);
        var cfg = await LoadConfigAsync();
        var skipped = new List<string>();
        foreach (var c in cases)
        {
            List<string>? to = null;
            if (cfg.TryGetValue("TOPIC:" + c.TopicCd, out var t) && t.Count > 0) to = t;
            else if (!string.IsNullOrEmpty(c.PicName) && cfg.TryGetValue("PIC:" + c.PicName, out var p) && p.Count > 0) to = p;

            if (to == null || to.Count == 0)
            {
                skipped.Add($"{c.TopicCd}/{c.PicName ?? "—"}");
                continue;
            }
            foreach (var m in to)
            {
                if (!map.TryGetValue(m, out var l)) map[m] = l = new List<DueCase>();
                l.Add(c);
            }
        }
        if (skipped.Count > 0)
            _log.LogWarning("InquiryReminder: bỏ qua {N} case vì chủ đề/phòng chưa khai email ở RemindEmails ({List})",
                            skipped.Count, string.Join(", ", skipped.Distinct().Take(10)));
        return map;
    }

    private string BuildBody(List<DueCase> cases)
    {
        var baseUrl = (_cfg["Inquiry:ReminderWebUrl"] ?? "").TrimEnd('/');
        var today = DateTime.Today;
        var sb = new StringBuilder();
        sb.AppendLine($"Có {cases.Count} hội thoại CHƯA có phản hồi và sắp đến hạn (hôm nay hoặc ngày mai):");
        sb.AppendLine();
        int k = 1;
        foreach (var c in cases)
        {
            string when = c.Deadline.Date <= today ? "HÔM NAY" : "ngày mai";
            sb.AppendLine($"{k++}) {c.No} — {c.Topic}");
            sb.AppendLine($"   Mức độ: {c.Severity ?? "—"} · Hạn: {c.Deadline:dd/MM/yyyy} ({when}) · PIC: {c.PicName ?? "chưa gán"}");
            if (baseUrl.Length > 0) sb.AppendLine($"   Mở: {baseUrl}/HrInquiry/Chat?id={c.Id}");
        }
        sb.AppendLine();
        sb.AppendLine("Vui lòng soạn phản hồi (HR/GA gửi bản nháp để CSR duyệt) trước hạn.");
        sb.AppendLine("— My Samho (email tự động, vui lòng không trả lời)");
        return sb.ToString();
    }

    private static string Subject(int n) => $"[My Samho] Nhắc hạn phản hồi: {n} hội thoại sắp đến hạn";

    /// <summary>Chạy mỗi vài phút bởi InquiryReminderBackgroundService.</summary>
    public async Task TryRunAsync()
    {
        // Máy dev chạy local nối DB + SMTP THẬT → tuyệt đối không tự gửi mail cho nhân viên từ máy dev (chỉ môi trường Production).
        if (!_env.IsProduction()) return;
        if (!await TableReadyAsync()) return;
        if (DateTime.UtcNow - _lastFailUtc < RetryAfterFail) return;

        int hour = (await _db.ExecuteQueryAsync("SELECT TO_NUMBER(TO_CHAR(SYSDATE,'HH24')) AS H FROM DUAL", r => Convert.ToInt32(r["H"]))).First();
        if (hour < FromHour || hour >= ToHour) return;

        var cases = await FindDueAsync(includeReminded: false);
        if (cases.Count == 0) return;

        var digests = await BuildDigestsAsync(cases);
        if (digests.Count == 0) { _log.LogWarning("InquiryReminder: có {N} hội thoại sắp đến hạn nhưng không tìm thấy email người nhận", cases.Count); return; }

        var okMails = new Dictionary<long, List<string>>();
        int fail = 0;
        foreach (var (mail, list) in digests)
        {
            try
            {
                await _mail.SendAsync(new[] { mail }, Subject(list.Count), BuildBody(list), null, "");
                foreach (var c in list) { if (!okMails.TryGetValue(c.Id, out var l)) okMails[c.Id] = l = new(); l.Add(mail); }
            }
            catch (Exception ex) { fail++; _log.LogError(ex, "InquiryReminder: gửi tới {Mail} lỗi", mail); }
        }

        foreach (var (id, mails) in okMails)
        {
            try
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_INQUIRY_REMIND_LOG (INQUIRY_ID, REMIND_TYPE, SENT_DT, RECIPIENTS) VALUES (:I, :T, SYSDATE, :R)",
                    new OracleParameter("I", id), new OracleParameter("T", RemindType),
                    new OracleParameter("R", OracleDbType.NVarchar2) { Value = string.Join(", ", mails).Length > 1000 ? string.Join(", ", mails)[..1000] : string.Join(", ", mails) });
            }
            catch (OracleException ex) when (ex.Number == 1) { /* đã có (chạy song song) */ }
        }
        if (okMails.Count == 0) _lastFailUtc = DateTime.UtcNow;
        _log.LogInformation("InquiryReminder: {Cases} hội thoại, {Mails} email, {Fail} lỗi", cases.Count, digests.Count, fail);
    }


    // ── Email báo CSR có bản nháp trả lời chờ duyệt (HR/GA soạn → CSR duyệt) ─────────────────────────────
    // Lý do dùng email: Admin/CSR/HR không nhận thông báo cá nhân trong app (quy tắc 2026-09-12) nên không thể báo bằng push.
    // Gom theo đợt (nháp đã chờ ≥ 2 phút) để không gửi dồn dập; mỗi bản nháp chỉ báo 1 lần (nhật ký REMIND_TYPE = 'DR' + ID nháp).
    // Người nhận: email cấu hình cho phòng CSR ở trang "Email nhắc hạn" (PIC = CSR), chưa cấu hình thì email của các CSR.
    // Nội dung chỉ có số hội thoại, chủ đề, phòng người soạn — không có tên người soạn/người phản ánh hay nội dung.
    private static volatile bool _draftReady;
    private static DateTime _draftNextProbeUtc = DateTime.MinValue;
    private async Task<bool> DraftTableReadyAsync()
    {
        if (_draftReady) return true;
        if (DateTime.UtcNow < _draftNextProbeUtc) return false;
        bool ok;
        try
        {
            var n = await _db.ExecuteQueryAsync("SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_INQUIRY_DRAFT'", r => Convert.ToInt32(r["C"]));
            ok = n.FirstOrDefault() > 0;
        }
        catch { ok = false; }
        if (ok) _draftReady = true; else _draftNextProbeUtc = DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    private static DateTime _lastDraftFailUtc = DateTime.MinValue;

    public record DraftDue(long DraftId, long InquiryId, string No, string Topic, string? Role, DateTime Created);

    public async Task<List<DraftDue>> FindDraftsToNotifyAsync(bool includeNotified, int max = 200)
    {
        string notNotified = includeNotified ? "" : @"
            AND NOT EXISTS (SELECT 1 FROM HRMS.HR_INQUIRY_REMIND_LOG l WHERE l.INQUIRY_ID = d.INQUIRY_ID AND l.REMIND_TYPE = 'DR' || TO_CHAR(d.ID))";
        string aged = includeNotified ? "" : "AND d.CREATED_DT <= SYSDATE - 2 / 1440";
        return await _db.ExecuteQueryAsync($@"
            SELECT * FROM (
                SELECT d.ID, d.INQUIRY_ID, i.INQUIRY_NO, t.TOPIC_NAME, d.AUTHOR_ROLE, d.CREATED_DT
                FROM HRMS.HR_INQUIRY_DRAFT d
                JOIN HRMS.HR_INQUIRY i ON i.ID = d.INQUIRY_ID
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE d.STATUS = 'PENDING' AND i.STATUS = 'OPEN'
                  {aged}
                  {notNotified}
                ORDER BY d.CREATED_DT
            ) WHERE ROWNUM <= :MAXN",
            r => new DraftDue(Convert.ToInt64(r["ID"]), Convert.ToInt64(r["INQUIRY_ID"]), r["INQUIRY_NO"]?.ToString() ?? "",
                r["TOPIC_NAME"] == DBNull.Value ? "—" : r["TOPIC_NAME"].ToString()!,
                r["AUTHOR_ROLE"] == DBNull.Value ? null : r["AUTHOR_ROLE"].ToString(), Convert.ToDateTime(r["CREATED_DT"])),
            new OracleParameter("MAXN", max));
    }

    private async Task<List<string>> CsrRecipientsAsync()
    {
        // CHỈ dùng email khai ở AdminInquiry/RemindEmails (ô "CSR"). Bỏ nhánh dự phòng lấy email
        // cá nhân của tài khoản CSR — xem ghi chú ở BuildDigestsAsync (yêu cầu 2026-11-12).
        var cfg = await LoadConfigAsync();
        return cfg.TryGetValue("PIC:CSR", out var l) && l.Count > 0 ? l : new List<string>();
    }

    private static string DraftBody(List<DraftDue> list, int totalPending)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Có {list.Count} bản nháp trả lời mới đang chờ CSR duyệt (tổng đang chờ: {totalPending}):");
        sb.AppendLine();
        int k = 1;
        foreach (var d in list)
            sb.AppendLine($"{k++}) {d.No} — {d.Topic} — do {d.Role ?? "HR/GA"} soạn lúc {d.Created:HH:mm dd/MM}");
        sb.AppendLine();
        sb.AppendLine("Vui lòng đăng nhập My Samho → Hộp thư → lọc \"Có bản nháp chờ duyệt\" để duyệt, sửa hoặc trả lại.");
        sb.AppendLine("— My Samho (email tự động, vui lòng không trả lời)");
        return sb.ToString();
    }

    /// <summary>Chạy cùng nhịp với TryRunAsync. Chỉ Production, 07:00–20:59.</summary>
    public async Task TryRunDraftDigestAsync()
    {
        if (!_env.IsProduction()) return;
        if (!await TableReadyAsync() || !await DraftTableReadyAsync()) return;
        if (DateTime.UtcNow - _lastDraftFailUtc < RetryAfterFail) return;
        int hour = (await _db.ExecuteQueryAsync("SELECT TO_NUMBER(TO_CHAR(SYSDATE,'HH24')) AS H FROM DUAL", r => Convert.ToInt32(r["H"]))).First();
        if (hour < 7 || hour >= 21) return;

        var due = await FindDraftsToNotifyAsync(includeNotified: false);
        if (due.Count == 0) return;
        var to = await CsrRecipientsAsync();
        if (to.Count == 0)
        {
            _log.LogWarning("InquiryReminder: có {N} bản nháp chờ duyệt nhưng ô email CSR ở AdminInquiry/RemindEmails đang trống -> KHÔNG gửi cho ai", due.Count);
            return;
        }

        int total = (await _db.ExecuteQueryAsync("SELECT COUNT(*) AS C FROM HRMS.HR_INQUIRY_DRAFT WHERE STATUS = 'PENDING'", r => Convert.ToInt32(r["C"]))).First();
        int ok = 0;
        foreach (var m in to)
        {
            try { await _mail.SendAsync(new[] { m }, $"[My Samho] {due.Count} bản nháp trả lời chờ CSR duyệt", DraftBody(due, total), null, ""); ok++; }
            catch (Exception ex) { _log.LogError(ex, "InquiryReminder: gửi báo bản nháp tới {Mail} lỗi", m); }
        }
        if (ok == 0) { _lastDraftFailUtc = DateTime.UtcNow; return; }
        foreach (var d in due)
        {
            try
            {
                await _db.ExecuteNonQueryAsync("INSERT INTO HRMS.HR_INQUIRY_REMIND_LOG (INQUIRY_ID, REMIND_TYPE, SENT_DT, RECIPIENTS) VALUES (:I, :T, SYSDATE, :R)",
                    new OracleParameter("I", d.InquiryId), new OracleParameter("T", "DR" + d.DraftId),
                    new OracleParameter("R", OracleDbType.NVarchar2) { Value = string.Join(", ", to).Length > 1000 ? string.Join(", ", to)[..1000] : string.Join(", ", to) });
            }
            catch (OracleException ex) when (ex.Number == 1) { }
        }
        _log.LogInformation("InquiryReminder: báo {N} bản nháp chờ duyệt tới {M} CSR", due.Count, ok);
    }

    /// <summary>Xem trước: ai sẽ nhận gì — KHÔNG gửi, KHÔNG ghi nhật ký. Email che bớt.</summary>
    public async Task<object> PreviewAsync()
    {
        var cases = await FindDueAsync(includeReminded: true, max: 100);
        var digests = await BuildDigestsAsync(cases);
        static string Mask(string m) { var i = m.IndexOf('@'); return i <= 1 ? m : m[0] + new string('*', Math.Min(i - 1, 6)) + m[i..]; }
        return new
        {
            tableReady = await TableReadyAsync(),
            smtpConfigured = !string.IsNullOrWhiteSpace(_cfg["Mail:Host"]),
            webUrl = _cfg["Inquiry:ReminderWebUrl"],
            cases = cases.Select(c => new { c.Id, c.No, c.TopicCd, c.Topic, c.Severity, deadline = c.Deadline, pic = c.PicName }),
            recipients = digests.Select(d => new { email = Mask(d.Key), cases = d.Value.Count }),
            draftsPending = await DraftTableReadyAsync() ? (await FindDraftsToNotifyAsync(includeNotified: true, max: 100)).Count : 0,
            draftMailTo = (await CsrRecipientsAsync()).Count,
            sampleSubject = digests.Count > 0 ? Subject(digests.First().Value.Count) : null,
            sampleBody = digests.Count > 0 ? BuildBody(digests.OrderByDescending(d => d.Value.Count).First().Value) : null
        };
    }

    /// <summary>Gửi thử 1 email tổng hợp (tối đa 5 hội thoại đang sắp hạn, kể cả đã nhắc) tới địa chỉ chỉ định. Không ghi nhật ký.</summary>
    public async Task<int> SendTestAsync(string email)
    {
        var cases = (await FindDueAsync(includeReminded: true, max: 5));
        if (cases.Count == 0)
            cases = new List<DueCase> { new(0, "INQ-TEST-0000", "TEST", "Chủ đề thử nghiệm", "High", DateTime.Today.AddDays(1), "HR") };
        await _mail.SendAsync(new[] { email }, "[Thử] " + Subject(cases.Count), BuildBody(cases), null, "");
        return cases.Count;
    }
}
