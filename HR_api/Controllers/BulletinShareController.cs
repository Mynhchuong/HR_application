using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;

namespace HR_api.Controllers;

/// <summary>
/// Chia sẻ bản tin qua EMAIL và FACEBOOK (2026-11-20). Mỗi bản tin × kênh có 1 dòng HR_BULLETIN_SHARE
/// (nháp → gửi). Nội dung = TOÀN BỘ bài tiếng Việt + "English below:" + tiếng Anh, lấy từ HR_BULLETIN
/// tại thời điểm gửi. Script: Scripts/create_bulletin_share.sql.
/// Quyền: web layer (BulletinAdminController: Admin/HR/CSR/Lean); ở đây kiểm thêm Lean/CSR chỉ được
/// gửi bản tin của phòng mình (giống trang quản lý bản tin).
/// </summary>
[ApiController]
[Route("apiHR/[controller]")]
public class BulletinShareController : ControllerBase
{
    private readonly OracleService _db;
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _http;
    // Không ghim cứng phiên bản Graph API (mỗi phiên bản Facebook chỉ giữ ~2 năm): mặc định gọi KHÔNG kèm
    // phiên bản = dùng phiên bản mặc định của app; muốn ghim thì đặt BulletinShare:GraphVersion (vd "v23.0").
    private string Graph() => "https://graph.facebook.com" + (string.IsNullOrWhiteSpace(_cfg["BulletinShare:GraphVersion"]) ? "" : "/" + _cfg["BulletinShare:GraphVersion"]!.Trim('/'));

    public BulletinShareController(OracleService db, IConfiguration cfg, IHttpClientFactory http)
    { _db = db; _cfg = cfg; _http = http; }

    // ─────────────────────────── Kiểm bảng đã tạo chưa ───────────────────────────
    private static volatile bool _ready;
    private async Task<bool> ReadyAsync()
    {
        if (_ready) return true;
        try
        {
            var n = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME IN ('HR_BULLETIN_SHARE','HR_BULLETIN_SHARE_CFG')",
                r => Convert.ToInt32(r["C"]));
            _ready = n.FirstOrDefault() == 2;
        }
        catch { _ready = false; }
        return _ready;
    }
    // Đang gửi bản tin X qua kênh Y → lượt bấm thứ 2 bị từ chối (tránh cả công ty nhận 2 mail / Fanpage 2 bài)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _sending = new();
    private static bool TryLock(int bulletinId, string ch) => _sending.TryAdd($"{bulletinId}:{ch}", 0);
    private static void Unlock(int bulletinId, string ch) => _sending.TryRemove($"{bulletinId}:{ch}", out _);

    /// <summary>Vừa gửi/đăng thành công trong 1 phút qua → từ chối (chặn bấm trùng; gửi lại sau đó vẫn được — HR cần gửi nhiều lần).</summary>
    private async Task<bool> JustSentAsync(int bulletinId, string ch) =>
        (await _db.ExecuteQueryAsync(@"SELECT 1 FROM HRMS.HR_BULLETIN_SHARE
                WHERE BULLETIN_ID = :B AND CHANNEL = :C AND STATUS = 'SENT' AND SENT_DT > SYSDATE - 1/1440",
            r => 1, new OracleParameter("B", bulletinId), new OracleParameter("C", ch))).Count > 0;

    // ── Lịch sử gửi (HR_BULLETIN_SHARE_LOG) — bảng chưa tạo thì bỏ qua, không chặn việc gửi ──
    private static volatile bool _logReady;
    private async Task<bool> LogReadyAsync()
    {
        if (_logReady) return true;
        try
        {
            _logReady = (await _db.ExecuteQueryAsync("SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_BULLETIN_SHARE_LOG'",
                r => Convert.ToInt32(r["C"]))).FirstOrDefault() > 0;
        }
        catch { }
        return _logReady;
    }
    private async Task LogSendAsync(int bulletinId, string ch, bool isTest, bool ok, IEnumerable<string>? to, string? subject, string? error, string? user)
    {
        try
        {
            if (!await LogReadyAsync()) return;
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_BULLETIN_SHARE_LOG (ID, BULLETIN_ID, CHANNEL, IS_TEST, OK, RECIPIENTS, SUBJECT, ERROR_MSG, SENT_BY, SENT_DT)
                VALUES (HRMS.SEQ_HR_BULLETIN_SHARE_LOG.NEXTVAL, :B, :C, :T, :OK, :R, :S, :E, :U, SYSDATE)",
                new OracleParameter("B", OracleDbType.Int32) { Value = bulletinId },
                new OracleParameter("C", OracleDbType.Varchar2) { Value = ch },
                new OracleParameter("T", OracleDbType.Int32) { Value = isTest ? 1 : 0 },
                new OracleParameter("OK", OracleDbType.Int32) { Value = ok ? 1 : 0 },
                new OracleParameter("R", OracleDbType.Varchar2) { Value = (object?)Trunc(to == null ? null : string.Join(", ", to), 2000) ?? DBNull.Value },
                new OracleParameter("S", OracleDbType.NVarchar2) { Value = (object?)Trunc(subject, 500) ?? DBNull.Value },
                new OracleParameter("E", OracleDbType.NVarchar2) { Value = (object?)Trunc(error, 1000) ?? DBNull.Value },
                new OracleParameter("U", OracleDbType.Varchar2) { Value = (object?)user ?? DBNull.Value });
        }
        catch { /* ghi lịch sử lỗi không được làm hỏng kết quả gửi */ }
    }

    // GET apiHR/BulletinShare/{id}/history?channel=EMAIL — lịch sử gửi (mới nhất trước)
    [HttpGet("{bulletinId:int}/history")]
    public async Task<IActionResult> History(int bulletinId, [FromQuery] string? channel, [FromQuery] string? loginUser)
    {
        try
        {
            if (!await LogReadyAsync()) return Ok(new { success = true, data = Array.Empty<object>() });
            var b = await LoadBulletinBasicAsync(bulletinId);
            if (b == null) return Ok(new { success = false, message = "Không tìm thấy bản tin" });
            if (!await CanAccessAsync(b, loginUser)) return Ok(new { success = false, message = "Không có quyền" });
            var ch = string.IsNullOrWhiteSpace(channel) ? "EMAIL" : channel.ToUpperInvariant();
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT * FROM (
                    SELECT L.IS_TEST, L.OK, L.RECIPIENTS, L.SUBJECT, L.ERROR_MSG, L.SENT_BY, L.SENT_DT
                    FROM HRMS.HR_BULLETIN_SHARE_LOG L
                    WHERE L.BULLETIN_ID = :B AND L.CHANNEL = :C
                    ORDER BY L.SENT_DT DESC, L.ID DESC
                ) WHERE ROWNUM <= 50",
                r => new
                {
                    isTest = Convert.ToInt32(r["IS_TEST"]) == 1, ok = Convert.ToInt32(r["OK"]) == 1,
                    recipients = r["RECIPIENTS"] == DBNull.Value ? null : r["RECIPIENTS"].ToString(),
                    subject = r["SUBJECT"] == DBNull.Value ? null : r["SUBJECT"].ToString(),
                    error = r["ERROR_MSG"] == DBNull.Value ? null : r["ERROR_MSG"].ToString(),
                    sentBy = r["SENT_BY"] == DBNull.Value ? null : r["SENT_BY"].ToString(),
                    sentDt = Convert.ToDateTime(r["SENT_DT"])
                }, new OracleParameter("B", bulletinId), new OracleParameter("C", ch));
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    private IActionResult NotReady() => Ok(new { success = false, needDdl = true, message = "Chưa chạy script Scripts/create_bulletin_share.sql" });

    // ─────────────────────────── Đọc bản tin ───────────────────────────
    private sealed class Blt
    {
        public int Id; public string Title = ""; public string? TitleEn; public string Content = ""; public string? ContentEn;
        public string? Cover; public string? InstId; public string Role = "HR"; public int IsPublished; public int IsActive;
    }
    private async Task<Blt?> LoadBulletinAsync(int id)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT B.ID, B.TITLE, B.TITLE_EN, B.COVER_IMG, B.INST_ID, B.IS_PUBLISHED, B.IS_ACTIVE
            FROM HRMS.HR_BULLETIN B WHERE B.ID = :ID",
            r =>
            {
                string S(string c) => r[c] == DBNull.Value ? "" : r[c]?.ToString() ?? "";
                return new Blt
                {
                    Id = Convert.ToInt32(r["ID"]), Title = S("TITLE"),
                    TitleEn = string.IsNullOrWhiteSpace(S("TITLE_EN")) ? null : S("TITLE_EN"),
                    Cover = string.IsNullOrWhiteSpace(S("COVER_IMG")) ? null : S("COVER_IMG"),
                    InstId = S("INST_ID"), IsPublished = Convert.ToInt32(r["IS_PUBLISHED"]), IsActive = Convert.ToInt32(r["IS_ACTIVE"])
                };
            }, new OracleParameter("ID", id));
        var b = rows.FirstOrDefault();
        if (b == null) return null;
        b.Content   = await ReadClobAsync("HR_BULLETIN", "CONTENT", "ID = :ID", new OracleParameter("ID", id)) ?? "";
        var en      = await ReadClobAsync("HR_BULLETIN", "CONTENT_EN", "ID = :ID", new OracleParameter("ID", id));
        b.ContentEn = string.IsNullOrWhiteSpace(en) ? null : en;
        b.Role = await TrainingScopeHelper.GetCreatorRoleAsync(_db, b.InstId);
        return b;
    }

    /// <summary>Chỉ thông tin để kiểm quyền (không đọc nội dung bài).</summary>
    private async Task<Blt?> LoadBulletinBasicAsync(int id)
    {
        var b = (await _db.ExecuteQueryAsync("SELECT ID, INST_ID FROM HRMS.HR_BULLETIN WHERE ID = :ID",
            r => new Blt { Id = Convert.ToInt32(r["ID"]), InstId = r["INST_ID"] == DBNull.Value ? null : r["INST_ID"].ToString() },
            new OracleParameter("ID", id))).FirstOrDefault();
        if (b != null) b.Role = await TrainingScopeHelper.GetCreatorRoleAsync(_db, b.InstId);
        return b;
    }

    /// <summary>
    /// Đọc TRỌN 1 cột NCLOB. Oracle 10g không trả NCLOB thẳng qua reader nên phải cắt khúc 2000 ký tự
    /// (DBMS_LOB.SUBSTR); số khúc tính theo DBMS_LOB.GETLENGTH — không cắt cứng (bài dài sẽ bị cụt khi gửi).
    /// </summary>
    private async Task<string?> ReadClobAsync(string table, string col, string where, OracleParameter key)
    {
        OracleParameter K() => new(key.ParameterName, key.Value);
        var len = (await _db.ExecuteQueryAsync($"SELECT NVL(DBMS_LOB.GETLENGTH({col}), -1) AS L FROM HRMS.{table} WHERE {where}",
            r => Convert.ToInt64(r["L"]), K())).FirstOrDefault(-1);
        if (len < 0) return null;
        if (len == 0) return "";
        const int CHUNK = 2000, PER_QUERY = 10;     // 10 khúc / lượt truy vấn
        var sb = new StringBuilder((int)Math.Min(len, int.MaxValue));
        for (long off = 1; off <= len; off += CHUNK * PER_QUERY)
        {
            var cols = string.Join(", ", Enumerable.Range(0, PER_QUERY).Select(i => $"DBMS_LOB.SUBSTR({col}, {CHUNK}, {off + (long)i * CHUNK}) AS P{i}"));
            var parts = (await _db.ExecuteQueryAsync($"SELECT {cols} FROM HRMS.{table} WHERE {where}",
                r => Enumerable.Range(0, PER_QUERY).Select(i => r["P" + i] == DBNull.Value ? "" : r["P" + i]?.ToString() ?? "").ToArray(), K()))
                .FirstOrDefault() ?? Array.Empty<string>();
            foreach (var part in parts) sb.Append(part);
        }
        return sb.ToString();
    }

    /// <summary>Lean/CSR chỉ được gửi bản tin của phòng mình; HR/Admin gửi được tất cả.</summary>
    private async Task<bool> CanAccessAsync(Blt b, string? loginUser)
    {
        if (string.IsNullOrWhiteSpace(loginUser)) return false;
        var viewer = await TrainingScopeHelper.GetViewerRolesAsync(_db, loginUser);
        return viewer == null || viewer.Contains(b.Role);
    }

    private static (string Vi, string En) DeptName(string role) => role switch
    {
        "CSR"  => ("CSR", "CSR"),
        "Lean" => ("Lean", "Lean"),
        _      => ("Nhân sự", "HR")
    };

    // ─────────────────────────── Đọc / lưu nháp ───────────────────────────
    private sealed class Share
    {
        public long RowId; public string Channel = ""; public string Status = "NEW"; public string? Subject; public string? Recipients;
        public string? IntroVi; public string? IntroEn; public string? FbMessage; public int FbWithImage = 1;
        public string? FbPostId; public string? LastError; public DateTime? SentDt; public string? SentBy;
        public DateTime? UpdtDt; public string? UpdtId;
    }
    private async Task<Share?> LoadShareAsync(int bulletinId, string channel)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT CHANNEL, STATUS, SUBJECT, RECIPIENTS, INTRO_VI, INTRO_EN, FB_WITH_IMAGE, FB_POST_ID, LAST_ERROR,
                   SENT_DT, SENT_BY, NVL(UPDT_DT, INST_DT) AS UDT, NVL(UPDT_ID, INST_ID) AS UID_, ID
            FROM HRMS.HR_BULLETIN_SHARE WHERE BULLETIN_ID = :B AND CHANNEL = :C",
            r =>
            {
                string? S(string c) => r[c] == DBNull.Value ? null : r[c]?.ToString();
                return new Share
                {
                    RowId = Convert.ToInt64(r["ID"]),
                    Channel = S("CHANNEL") ?? channel, Status = S("STATUS") ?? "DRAFT", Subject = S("SUBJECT"),
                    Recipients = S("RECIPIENTS"), IntroVi = S("INTRO_VI"), IntroEn = S("INTRO_EN"),
                    FbWithImage = r["FB_WITH_IMAGE"] == DBNull.Value ? 1 : Convert.ToInt32(r["FB_WITH_IMAGE"]),
                    FbPostId = S("FB_POST_ID"), LastError = S("LAST_ERROR"),
                    SentDt = r["SENT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["SENT_DT"]), SentBy = S("SENT_BY"),
                    UpdtDt = r["UDT"] == DBNull.Value ? null : Convert.ToDateTime(r["UDT"]), UpdtId = S("UID_")
                };
            }, new OracleParameter("B", bulletinId), new OracleParameter("C", channel));
        var sh = rows.FirstOrDefault();
        if (sh != null && channel == "FB")
        {
            var msg = await ReadClobAsync("HR_BULLETIN_SHARE", "FB_MESSAGE", "ID = :SID", new OracleParameter("SID", sh.RowId));
            sh.FbMessage = string.IsNullOrEmpty(msg) ? null : msg;
        }
        return sh;
    }

    private string DefaultRecipients() => _cfg["BulletinShare:DefaultEmailTo"] ?? "all_vnsamho_expat_staff@esamho.com";

    /// <summary>Giá trị mặc định (chưa có nháp) — tự điền từ bản tin.</summary>
    private Share Defaults(Blt b, string channel)
    {
        var (dVi, dEn) = DeptName(b.Role);
        var s = new Share { Channel = channel, Status = "NEW" };
        if (channel == "EMAIL")
        {
            s.Subject    = "[My Samho] " + b.Title + (b.TitleEn != null ? " | " + b.TitleEn : "");
            s.Recipients = DefaultRecipients();
            s.IntroVi    = $"Thông báo từ My Samho – Phòng {dVi}";
            s.IntroEn    = $"Announcement from My Samho – {dEn} Department";
        }
        else
        {
            s.FbMessage = BuildFbText(b);
        }
        return s;
    }

    private static object ShareDto(Share s) => new
    {
        channel = s.Channel, status = s.Status, subject = s.Subject, recipients = s.Recipients,
        introVi = s.IntroVi, introEn = s.IntroEn, fbMessage = s.FbMessage, fbWithImage = s.FbWithImage == 1,
        fbPostId = s.FbPostId, lastError = s.LastError, sentDt = s.SentDt, sentBy = s.SentBy, updtDt = s.UpdtDt, updtId = s.UpdtId
    };

    // GET apiHR/BulletinShare/{bulletinId}?loginUser=
    [HttpGet("{bulletinId:int}")]
    public async Task<IActionResult> Get(int bulletinId, [FromQuery] string? loginUser)
    {
        try
        {
            if (!await ReadyAsync()) return NotReady();
            var b = await LoadBulletinAsync(bulletinId);
            if (b == null || b.IsActive == 0) return Ok(new { success = false, message = "Không tìm thấy bản tin" });
            if (!await CanAccessAsync(b, loginUser)) return Ok(new { success = false, message = "Bạn không có quyền với bản tin của phòng khác" });

            var email = await LoadShareAsync(bulletinId, "EMAIL") ?? Defaults(b, "EMAIL");
            var fb    = await LoadShareAsync(bulletinId, "FB")    ?? Defaults(b, "FB");
            var cfg   = await LoadCfgAsync();
            var (dVi, dEn) = DeptName(b.Role);
            return Ok(new
            {
                success = true,
                bulletin = new { id = b.Id, title = b.Title, titleEn = b.TitleEn, content = b.Content, contentEn = b.ContentEn,
                                 coverImg = b.Cover, role = b.Role, deptVi = dVi, deptEn = dEn, isPublished = b.IsPublished == 1 },
                email = ShareDto(email), fb = ShareDto(fb),
                fbDefault = BuildFbText(b),
                fbConfigured = !string.IsNullOrEmpty(cfg.GetValueOrDefault("FB_PAGE_ID")) && !string.IsNullOrEmpty(cfg.GetValueOrDefault("FB_PAGE_TOKEN")),
                fbPageName = cfg.GetValueOrDefault("FB_PAGE_NAME")
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    public class SaveReq
    {
        public int BulletinId { get; set; } public string Channel { get; set; } = "";
        public string? Subject { get; set; } public string? Recipients { get; set; }
        public string? IntroVi { get; set; } public string? IntroEn { get; set; }
        public string? FbMessage { get; set; } public bool FbWithImage { get; set; } = true;
        public string? LoginUser { get; set; }
    }

    // POST apiHR/BulletinShare/save — lưu nháp (không đổi trạng thái SENT thành DRAFT: gửi rồi sửa vẫn giữ lịch sử đã gửi)
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveReq req)
    {
        try
        {
            if (!await ReadyAsync()) return NotReady();
            var ch = (req.Channel ?? "").ToUpperInvariant();
            if (ch != "EMAIL" && ch != "FB") return Ok(new { success = false, message = "Kênh không hợp lệ" });
            var b = await LoadBulletinAsync(req.BulletinId);
            if (b == null) return Ok(new { success = false, message = "Không tìm thấy bản tin" });
            if (!await CanAccessAsync(b, req.LoginUser)) return Ok(new { success = false, message = "Bạn không có quyền với bản tin của phòng khác" });
            if (ch == "EMAIL")
            {
                var list = ParseRecipients(req.Recipients);
                var bad = list.Where(a => !IsEmail(a)).ToList();
                if (bad.Any()) return Ok(new { success = false, message = "Email không hợp lệ: " + string.Join(", ", bad) });
                if (string.Join(", ", list).Length > 2000)
                    return Ok(new { success = false, message = "Danh sách người nhận quá dài (tối đa 2000 ký tự) — dùng địa chỉ nhóm thay cho từng người" });
            }
            await UpsertAsync(req.BulletinId, ch, req);
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    private async Task UpsertAsync(int bulletinId, string ch, SaveReq? req)
    {
        bool exists = (await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_BULLETIN_SHARE WHERE BULLETIN_ID = :B AND CHANNEL = :C",
            r => 1, new OracleParameter("B", bulletinId), new OracleParameter("C", ch))).Count > 0;
        if (!exists)
        {
            try
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_BULLETIN_SHARE (ID, BULLETIN_ID, CHANNEL, STATUS, INST_ID, INST_DT)
                    VALUES (HRMS.SEQ_HR_BULLETIN_SHARE.NEXTVAL, :B, :C, 'DRAFT', :U, SYSDATE)",
                    new OracleParameter("B", bulletinId), new OracleParameter("C", ch), new OracleParameter("U", (object?)req?.LoginUser ?? DBNull.Value));
            }
            catch (OracleException ex) when (ex.Number == 1) { /* lưu đồng thời — dòng đã có, cập nhật bên dưới */ }
        }
        if (req == null) return;
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_BULLETIN_SHARE SET
                SUBJECT = :SUBJ, RECIPIENTS = :RCPT, INTRO_VI = :IVI, INTRO_EN = :IEN,
                FB_MESSAGE = :MSG, FB_WITH_IMAGE = :WIMG, UPDT_ID = :U, UPDT_DT = SYSDATE
            WHERE BULLETIN_ID = :B AND CHANNEL = :C",
            new OracleParameter("SUBJ", OracleDbType.NVarchar2) { Value = (object?)Trunc(req.Subject, 500) ?? DBNull.Value },
            new OracleParameter("RCPT", OracleDbType.Varchar2) { Value = (object?)Trunc(string.Join(", ", ParseRecipients(req.Recipients)), 2000) ?? DBNull.Value },
            new OracleParameter("IVI", OracleDbType.NVarchar2) { Value = (object?)Trunc(req.IntroVi, 1000) ?? DBNull.Value },
            new OracleParameter("IEN", OracleDbType.NVarchar2) { Value = (object?)Trunc(req.IntroEn, 1000) ?? DBNull.Value },
            new OracleParameter("MSG", OracleDbType.NClob) { Value = (object?)req.FbMessage ?? DBNull.Value },
            new OracleParameter("WIMG", OracleDbType.Int32) { Value = req.FbWithImage ? 1 : 0 },
            new OracleParameter("U", OracleDbType.Varchar2) { Value = (object?)req.LoginUser ?? DBNull.Value },
            new OracleParameter("B", OracleDbType.Int32) { Value = bulletinId },
            new OracleParameter("C", OracleDbType.Varchar2) { Value = ch });
    }

    private async Task MarkAsync(int bulletinId, string ch, bool ok, string? error, string? fbPostId, string? loginUser)
    {
        await UpsertAsync(bulletinId, ch, null);
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_BULLETIN_SHARE SET
                STATUS = :ST, LAST_ERROR = :ERR,
                SENT_DT = CASE WHEN :OK1 = 1 THEN SYSDATE ELSE SENT_DT END,
                SENT_BY = CASE WHEN :OK2 = 1 THEN :U ELSE SENT_BY END,
                FB_POST_ID = NVL(:PID, FB_POST_ID)
            WHERE BULLETIN_ID = :B AND CHANNEL = :C",
            new OracleParameter("ST", OracleDbType.Varchar2) { Value = ok ? "SENT" : "FAILED" },
            new OracleParameter("ERR", OracleDbType.NVarchar2) { Value = (object?)Trunc(error, 1000) ?? DBNull.Value },
            new OracleParameter("OK1", OracleDbType.Int32) { Value = ok ? 1 : 0 },
            new OracleParameter("OK2", OracleDbType.Int32) { Value = ok ? 1 : 0 },
            new OracleParameter("U", OracleDbType.Varchar2) { Value = (object?)loginUser ?? DBNull.Value },
            new OracleParameter("PID", OracleDbType.Varchar2) { Value = (object?)fbPostId ?? DBNull.Value },
            new OracleParameter("B", OracleDbType.Int32) { Value = bulletinId },
            new OracleParameter("C", OracleDbType.Varchar2) { Value = ch });
    }

    // GET apiHR/BulletinShare/status?ids=1,2,3 — trạng thái cho trang Manage
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? ids)
    {
        try
        {
            if (!await ReadyAsync()) return Ok(new { success = true, available = false, data = Array.Empty<object>() });
            var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x.Trim(), out var v) ? v : 0).Where(v => v > 0).Distinct().Take(500).ToList();
            if (list.Count == 0) return Ok(new { success = true, available = true, data = Array.Empty<object>() });
            var ps = list.Select((v, i) => new OracleParameter("P" + i, v)).ToArray();
            var rows = await _db.ExecuteQueryAsync(
                $@"SELECT S.BULLETIN_ID, S.CHANNEL, S.STATUS, S.SENT_DT{(await LogReadyAsync()
                        ? ", (SELECT COUNT(*) FROM HRMS.HR_BULLETIN_SHARE_LOG L WHERE L.BULLETIN_ID = S.BULLETIN_ID AND L.CHANNEL = S.CHANNEL AND L.IS_TEST = 0 AND L.OK = 1) AS CNT"
                        : ", 0 AS CNT")}
                   FROM HRMS.HR_BULLETIN_SHARE S WHERE S.BULLETIN_ID IN ({string.Join(",", ps.Select(p => ":" + p.ParameterName))})",
                r => new
                {
                    bulletinId = Convert.ToInt32(r["BULLETIN_ID"]), channel = r["CHANNEL"]?.ToString(), status = r["STATUS"]?.ToString(),
                    sentDt = r["SENT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["SENT_DT"]),
                    sentCount = Convert.ToInt32(r["CNT"])
                }, ps);
            return Ok(new { success = true, available = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ═════════════════════════════ EMAIL ═════════════════════════════
    public class SendReq { public int BulletinId { get; set; } public string? LoginUser { get; set; } public string? TestTo { get; set; } }

    // GET apiHR/BulletinShare/{id}/email-preview?loginUser= — HTML đúng như mail sẽ gửi (ảnh để link web để xem được trên trình duyệt)
    [HttpGet("{bulletinId:int}/email-preview")]
    public async Task<IActionResult> EmailPreview(int bulletinId, [FromQuery] string? loginUser)
    {
        try
        {
            if (!await ReadyAsync()) return NotReady();
            var b = await LoadBulletinAsync(bulletinId);
            if (b == null) return Ok(new { success = false, message = "Không tìm thấy bản tin" });
            if (!await CanAccessAsync(b, loginUser)) return Ok(new { success = false, message = "Không có quyền" });
            var s = await LoadShareAsync(bulletinId, "EMAIL") ?? Defaults(b, "EMAIL");
            return Ok(new { success = true, subject = s.Subject, recipients = s.Recipients, html = BuildEmailHtml(b, s, null) });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/BulletinShare/send-email — TestTo có giá trị = gửi thử tới 1 địa chỉ (không ghi trạng thái đã gửi)
    [HttpPost("send-email")]
    public async Task<IActionResult> SendEmail([FromBody] SendReq req)
    {
        try { return await SendEmailCore(req); }
        catch (Exception ex) { return Ok(new { success = false, message = "Chưa gửi được (lỗi đọc dữ liệu): " + ex.Message }); }
    }
    private async Task<IActionResult> SendEmailCore(SendReq req)
    {
        bool isTest = !string.IsNullOrWhiteSpace(req.TestTo);
        if (!await ReadyAsync()) return NotReady();
        var b = await LoadBulletinAsync(req.BulletinId);
        if (b == null || b.IsActive == 0) return Ok(new { success = false, message = "Không tìm thấy bản tin" });
        if (!await CanAccessAsync(b, req.LoginUser)) return Ok(new { success = false, message = "Không có quyền" });
        // Mail ghi "xem chi tiết ở Bản tin My Samho" → bài phải đang hiện trên app (gửi thử thì cho qua)
        if (!isTest && b.IsPublished != 1)
            return Ok(new { success = false, message = "Bản tin chưa đăng trên app — đăng bản tin trước rồi mới gửi email (gửi thử vẫn được)" });
        var s = await LoadShareAsync(req.BulletinId, "EMAIL") ?? Defaults(b, "EMAIL");
        var to = isTest ? new List<string> { req.TestTo!.Trim() } : ParseRecipients(s.Recipients);
        if (to.Count == 0 || to.Any(a => !IsEmail(a))) return Ok(new { success = false, message = "Danh sách người nhận trống hoặc sai định dạng" });

        if (!isTest && !TryLock(req.BulletinId, "EMAIL"))
            return Ok(new { success = false, message = "Email của bản tin này đang được gửi, vui lòng đợi" });
        try
        {
            if (!isTest && await JustSentAsync(req.BulletinId, "EMAIL"))
                return Ok(new { success = false, message = "Email bản tin này vừa được gửi xong (dưới 1 phút) — đợi chút rồi gửi lại nếu cần" });
            var subj = (isTest ? "[GỬI THỬ] " : "") + (s.Subject ?? b.Title);
            try { await SendMailAsync(to, subj, b, s); }
            catch (Exception ex)
            {
                if (!isTest) { try { await MarkAsync(req.BulletinId, "EMAIL", false, ex.Message, null, req.LoginUser); } catch { } }
                await LogSendAsync(req.BulletinId, "EMAIL", isTest, false, to, subj, ex.Message, req.LoginUser);
                return Ok(new { success = false, message = "Gửi email lỗi: " + ex.Message });
            }
            await LogSendAsync(req.BulletinId, "EMAIL", isTest, true, to, subj, null, req.LoginUser);
            if (isTest) return Ok(new { success = true, message = $"Đã gửi thử tới {to[0]}" });
            // Mail ĐÃ đi rồi: lỗi ghi trạng thái KHÔNG được báo "gửi lỗi" (người dùng sẽ gửi lại → cả công ty nhận 2 mail)
            string warn = "";
            try { await MarkAsync(req.BulletinId, "EMAIL", true, null, null, req.LoginUser); }
            catch { warn = " (lưu trạng thái lỗi — ĐỪNG gửi lại, email đã đi)"; }
            return Ok(new { success = true, message = $"Đã gửi email tới {string.Join(", ", to)}{warn}" });
        }
        finally { if (!isTest) Unlock(req.BulletinId, "EMAIL"); }
    }

    private async Task SendMailAsync(List<string> to, string subject, Blt b, Share s)
    {
        // Hộp thư gửi bản tin:
        //  - "BulletinShare:Mail:FromAddress" có giá trị → gửi TỪ địa chỉ đó, đăng nhập SMTP bằng CHÍNH địa chỉ đó
        //    (User riêng nếu có) + Password của mục này — KHÔNG lấy lẫn tài khoản/mật khẩu hộp thư chung.
        //  - Để trống → dùng nguyên hộp thư chung ở mục "Mail" (rtdm@esamho.com) như mail Kaizen.
        // Host/Port/EnableSsl/FromName: mục bản tin để trống thì lấy của mục Mail.
        string? M(string k) { var v = _cfg["BulletinShare:Mail:" + k]; return string.IsNullOrWhiteSpace(v) ? _cfg["Mail:" + k] : v; }
        string? Own(string k) { var v = _cfg["BulletinShare:Mail:" + k]; return string.IsNullOrWhiteSpace(v) ? null : v; }
        var host = M("Host");
        string? from, user, pass;
        if (Own("FromAddress") is string ownFrom)
        {
            from = ownFrom; user = Own("User") ?? ownFrom; pass = Own("Password");
            if (string.IsNullOrEmpty(pass)) throw new InvalidOperationException("Chưa điền mật khẩu hộp thư gửi bản tin (BulletinShare:Mail:Password)");
        }
        else
        {
            user = _cfg["Mail:User"]; pass = _cfg["Mail:Password"]; from = _cfg["Mail:FromAddress"] ?? user;
        }
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Chưa cấu hình SMTP (appsettings.json mục Mail / BulletinShare:Mail)");
        int port = int.TryParse(M("Port"), out var p) ? p : 587;
        bool ssl = bool.TryParse(M("EnableSsl"), out var sl) && sl;

        // Ảnh trong bài (và ảnh bìa) nhúng THẲNG vào mail (cid:) — người nhận ngoài mạng công ty vẫn xem được
        var images = new List<LinkedResource>();
        var pre = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in ImageNames(b)) pre[fn] = await LoadImageAsync(fn);
        var html = BuildEmailHtml(b, s, images, pre);
        using var msg = new MailMessage
        {
            From = new MailAddress(from!, M("FromName") ?? "My Samho"),
            Subject = Regex.Replace(subject, @"[\r\n\t]+", " ").Trim(),   // xuống dòng trong tiêu đề → MailMessage báo lỗi
            SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8, IsBodyHtml = true
        };
        foreach (var a in to) msg.To.Add(a);
        var view = AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html);
        foreach (var lr in images) view.LinkedResources.Add(lr);
        msg.AlternateViews.Add(view);
        using var smtp = new SmtpClient
        {
            Host = host, Port = port, EnableSsl = ssl, DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false, Timeout = 120_000
        };
        if (!string.IsNullOrWhiteSpace(user)) smtp.Credentials = new NetworkCredential(user, pass);
        await smtp.SendMailAsync(msg);
    }

    private string ImageFolder() => _cfg["BulletinShare:ImageFolder"] ?? @"\\192.168.1.5\vserp_picture\BULLETIN\IMG";
    private string WebBase() => (_cfg["BulletinShare:PublicWebBase"] ?? "http://192.168.1.24/HR_Web").TrimEnd('/');
    private static readonly Regex ImgFileRe = new(@"GetBulletinImage\?fileName=([^""'&\s>]+)", RegexOptions.IgnoreCase);

    private byte[]? ReadImage(string fileName)
    {
        try
        {
            if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\')) return null;
            var path = Path.Combine(ImageFolder(), fileName);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        }
        catch { return null; }
    }
    /// <summary>
    /// Đọc ảnh bản tin: thư mục chia sẻ trước; không đọc được (máy không vào được ổ mạng) thì tải qua web
    /// (Image/GetBulletinImage — AllowAnonymous). Để mail luôn NHÚNG được ảnh, không phụ thuộc link.
    /// </summary>
    private async Task<byte[]?> LoadImageAsync(string fileName)
    {
        var bytes = ReadImage(fileName);
        if (bytes != null) return bytes;
        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\')) return null;
        try
        {
            var http = _http.CreateClient(); http.Timeout = TimeSpan.FromSeconds(30);
            using var res = await http.GetAsync(WebBase() + "/Image/GetBulletinImage?fileName=" + Uri.EscapeDataString(fileName));
            if (!res.IsSuccessStatusCode) return null;
            var ct = res.Content.Headers.ContentType?.MediaType ?? "";
            if (!ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            var data = await res.Content.ReadAsByteArrayAsync();
            return data.Length > 0 && data.Length <= 15 * 1024 * 1024 ? data : null;
        }
        catch { return null; }
    }

    /// <summary>Tên file mọi ảnh bản tin dùng trong mail (ảnh bìa + ảnh trong nội dung VI/EN).</summary>
    private static List<string> ImageNames(Blt b)
    {
        var list = new List<string>();
        if (b.Cover != null) list.Add(b.Cover);
        foreach (var html in new[] { b.Content, b.ContentEn })
            if (!string.IsNullOrEmpty(html))
                foreach (Match m in ImgFileRe.Matches(html))
                    list.Add(Uri.UnescapeDataString(m.Groups[1].Value));
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string MimeOf(string f) => Path.GetExtension(f).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp", _ => "image/jpeg"
    };

    /// <summary>
    /// images == null → bản xem trước (ảnh trỏ về web); != null → bản gửi thật (ảnh nhúng cid:, không đọc được
    /// file thì để link web tuyệt đối).
    /// </summary>
    private string BuildEmailHtml(Blt b, Share s, List<LinkedResource>? images, Dictionary<string, byte[]?>? preloaded = null)
    {
        var cidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long embedded = 0;
        string ImgSrc(string fileName)
        {
            var webUrl = WebBase() + "/Image/GetBulletinImage?fileName=" + Uri.EscapeDataString(fileName);
            if (images == null) return webUrl;
            if (cidMap.TryGetValue(fileName, out var cid)) return "cid:" + cid;
            var bytes = preloaded != null && preloaded.TryGetValue(fileName, out var pb) ? pb : ReadImage(fileName);
            // Không đọc được file, hoặc đã nhúng quá ~12MB (máy chủ mail hay giới hạn 20–25MB/thư) → để link web
            if (bytes == null || embedded + bytes.Length > 12 * 1024 * 1024) return webUrl;
            embedded += bytes.Length;
            cid = "img" + (cidMap.Count + 1) + "@mysamho";
            var lr = new LinkedResource(new MemoryStream(bytes), MimeOf(fileName)) { ContentId = cid, TransferEncoding = TransferEncoding.Base64 };
            images.Add(lr); cidMap[fileName] = cid;
            return "cid:" + cid;
        }
        string FixContent(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            // Ảnh bản tin: src="/HR_Web/Image/GetBulletinImage?fileName=x" (đường dẫn tương đối) → nhúng/tuyệt đối
            html = Regex.Replace(html, @"src\s*=\s*([""'])([^""']*GetBulletinImage\?fileName=[^""']+)\1", m =>
            {
                var fm = ImgFileRe.Match(m.Groups[2].Value);
                var fn = fm.Success ? Uri.UnescapeDataString(fm.Groups[1].Value) : "";
                return fn.Length > 0 ? $"src=\"{ImgSrc(fn)}\"" : m.Value;
            }, RegexOptions.IgnoreCase);
            // Đường dẫn tương đối còn lại (ảnh/link khác trong web, vd "/HR_Web/...") → tuyệt đối, mở trong mail mới được
            var origin = new Uri(WebBase()).GetLeftPart(UriPartial.Authority);
            html = Regex.Replace(html, @"\b(src|href)\s*=\s*([""'])/(?!/)", m => $"{m.Groups[1].Value}={m.Groups[2].Value}{origin}/", RegexOptions.IgnoreCase);
            // Nội dung dán từ Word: căn đều làm giãn chữ, tiêu đề con quá to → chuẩn hoá cho mail dễ đọc
            html = Regex.Replace(html, @"text-align\s*:\s*justify", "text-align:left", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<h([1-6])(\s[^>]*)?>", m =>
            {
                var size = m.Groups[1].Value switch { "1" => 19, "2" => 17, _ => 16 };
                var attrs = Regex.Replace(m.Groups[2].Value, @"\sstyle\s*=\s*(""[^""]*""|'[^']*')", "", RegexOptions.IgnoreCase);
                return $"<h{m.Groups[1].Value}{attrs} style=\"font-size:{size}px;line-height:1.4;font-weight:700;color:#0f172a;margin:20px 0 8px;text-align:left;\">";
            }, RegexOptions.IgnoreCase);
            // Cỡ chữ người soạn đặt quá to (vd 28–36px) → tối đa 18px cho mail gọn, dễ đọc
            html = Regex.Replace(html, @"font-size\s*:\s*(\d+(?:\.\d+)?)(px|pt)", m =>
            {
                var v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var px = m.Groups[2].Value.ToLowerInvariant() == "pt" ? v * 4 / 3 : v;
                return px > 18 ? "font-size:18px" : m.Value;
            }, RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"font-size\s*:\s*(x+-large|larger)", "font-size:18px", RegexOptions.IgnoreCase);
            // Ảnh gọn trong khung mail (Outlook bỏ qua CSS ngoài)
            html = Regex.Replace(html, @"<img(?![^>]*style=)", "<img style=\"max-width:100%;height:auto;border-radius:6px;\"", RegexOptions.IgnoreCase);
            return html;
        }
        string E(string? t) => WebUtility.HtmlEncode(t ?? "");

        // Logo: bản gửi thật nhúng cid (đọc Assets/mail-logo.png), bản xem trước lấy từ web
        string logoSrc = WebBase() + "/assets/img/samho-logo.png";
        if (images != null)
        {
            try
            {
                var lp = Path.Combine(AppContext.BaseDirectory, "Assets", "mail-logo.png");
                if (System.IO.File.Exists(lp))
                {
                    images.Add(new LinkedResource(new MemoryStream(System.IO.File.ReadAllBytes(lp)), "image/png") { ContentId = "logo@mysamho", TransferEncoding = TransferEncoding.Base64 });
                    logoSrc = "cid:logo@mysamho";
                }
            }
            catch { /* không có logo vẫn gửi được */ }
        }

        // ── Mẫu email dạng bảng (Outlook/Gmail hiển thị giống nhau), toàn bộ CSS inline ──
        const string FONT = "font-family:'Segoe UI',Arial,Helvetica,sans-serif;";
        const string RED = "#b91c1c";
        var (dVi, dEn) = DeptName(b.Role);
        var dateStr = DateTime.Now.ToString("dd/MM/yyyy");
        // Nội dung bài: căn chữ, khoảng cách đoạn dễ đọc
        string Body(string? html) =>
            $"<div style=\"{FONT}font-size:15px;line-height:1.7;color:#334155;\">{FixContent(html)}</div>";
        string Callout(string text) =>
            $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:24px 0 4px;\"><tr>" +
            $"<td style=\"background:#fff7ed;border-left:4px solid {RED};border-radius:6px;padding:14px 16px;{FONT}font-size:14px;line-height:1.55;color:#7c2d12;\">{text}</td></tr></table>";
        string Section(string label, string? intro, string title, string? html, string callout, bool cover, string anchor = "") =>
            "<tr><td style=\"padding:28px 32px 8px;\">" +
              $"<div style=\"{FONT}font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:{RED};margin:0 0 8px;\">{E(label)}</div>" +
              (string.IsNullOrWhiteSpace(intro) ? "" : $"<div style=\"{FONT}font-size:13px;color:#64748b;margin:0 0 6px;\">{E(intro)}</div>") +
              $"<h1 style=\"{FONT}font-size:20px;line-height:1.4;font-weight:700;color:#0f172a;margin:0 0 16px;\">{E(title)}</h1>" +
              (cover && b.Cover != null ? $"<img src=\"{ImgSrc(b.Cover)}\" alt=\"\" width=\"576\" style=\"display:block;width:100%;max-width:576px;height:auto;border-radius:8px;margin:0 0 20px;border:0;\" />" : "") +
              Body(html) + Callout(callout) +
            "</td></tr>";

        var sb = new StringBuilder();
        // Dòng xem trước trong hộp thư (ẩn trong thân mail)
        sb.Append($"<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;\">{E(b.Title)}{(b.TitleEn != null ? " | " + E(b.TitleEn) : "")}</div>");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f1f5f9;padding:24px 0;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:640px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #e2e8f0;\">");
        // Thanh màu + đầu thư
        sb.Append($"<tr><td style=\"height:5px;background:{RED};font-size:0;line-height:0;\">&nbsp;</td></tr>");
        sb.Append("<tr><td style=\"padding:20px 32px;border-bottom:1px solid #f1f5f9;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
                  $"<td style=\"vertical-align:middle;\"><img src=\"{logoSrc}\" alt=\"Samho\" width=\"40\" height=\"34\" style=\"display:inline-block;vertical-align:middle;border:0;\" />" +
                  $"<span style=\"{FONT}font-size:20px;font-weight:800;vertical-align:middle;margin-left:10px;\"><span style=\"color:#ed1c24;\">My</span> <span style=\"color:#0f172a;\">SAMHO</span></span></td>" +
                  $"<td align=\"right\" style=\"vertical-align:middle;{FONT}font-size:12px;color:#64748b;\">" +
                  $"<span style=\"display:inline-block;background:#fef2f2;color:{RED};font-weight:700;border-radius:999px;padding:4px 12px;\">{E(dVi)}</span><br />" +
                  $"<span style=\"display:inline-block;margin-top:4px;\">{dateStr}</span></td>" +
                  "</tr></table></td></tr>");
        // Báo cho NV nước ngoài: bản tiếng Anh ở phía dưới
        bool hasEn = b.ContentEn != null || b.TitleEn != null;
        if (hasEn)
            sb.Append("<tr><td style=\"padding:12px 32px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
                      $"<td style=\"background:#eff6ff;border-radius:8px;padding:10px 14px;{FONT}font-size:13px;color:#1e40af;\">" +
                      // Không dùng link nhảy (#en): webmail mở link sang tab mới → chỉ ghi chú để kéo xuống
                      "🇬🇧 <b>English below ↓</b> — please scroll down for the English version of this announcement.</td></tr></table></td></tr>");
        // Tiếng Việt
        sb.Append(Section("Thông báo nội bộ", s.IntroVi, b.Title, b.Content,
            "📱 <b>Xem chi tiết tại mục Bản tin trên app My Samho.</b><br />Xin cảm ơn.", true));
        // English
        if (hasEn)
        {
            sb.Append("<tr><td style=\"padding:20px 32px 0;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
                      "<td style=\"border-top:1px solid #e2e8f0;font-size:0;\">&nbsp;</td>" +
                      $"<td width=\"170\" align=\"center\" style=\"{FONT}font-size:11px;font-weight:700;letter-spacing:.08em;color:#94a3b8;white-space:nowrap;padding:0 10px;\">ENGLISH BELOW</td>" +
                      "<td style=\"border-top:1px solid #e2e8f0;font-size:0;\">&nbsp;</td></tr></table></td></tr>");
            sb.Append(Section("Internal announcement", s.IntroEn, b.TitleEn ?? b.Title, b.ContentEn,
                "📱 <b>Please see the full details in the Bulletin section of the My Samho app.</b><br />Thank you.", false, "en"));
        }
        // Chân thư
        sb.Append($"<tr><td style=\"padding:24px 32px 28px;background:#f8fafc;border-top:1px solid #e2e8f0;{FONT}font-size:12px;line-height:1.6;color:#94a3b8;text-align:center;\">" +
                  $"Phòng {E(dVi)} · {E(dEn)} Department<br />" +
                  "Đây là email tự động từ hệ thống My Samho, vui lòng không trả lời email này.<br />" +
                  "This is an automated email from My Samho. Please do not reply.<br />" +
                  "<span style=\"color:#cbd5e1;\">© VietNam Samho Co., LTD</span></td></tr>");
        sb.Append("</table></td></tr></table>");
        return sb.ToString();
    }

    // ═════════════════════════════ FACEBOOK ═════════════════════════════
    /// <summary>Nội dung mặc định cho Facebook: chữ thường (FB không hiển thị HTML).</summary>
    private static string BuildFbText(Blt b)
    {
        var sb = new StringBuilder();
        sb.Append(b.Title).Append("\n\n").Append(HtmlToText(b.Content));
        if (b.ContentEn != null || b.TitleEn != null)
            sb.Append("\n\n— English below —\n\n").Append(b.TitleEn ?? b.Title).Append("\n\n").Append(HtmlToText(b.ContentEn));
        return sb.ToString().Trim();
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var t = Regex.Replace(html, @"<(br|BR)\s*/?>", "\n");
        t = Regex.Replace(t, @"</(p|div|h[1-6]|tr)>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</t[dh]>", "  ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<li[^>]*>", "• ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"</li>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<img[^>]*>", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<[^>]+>", "");
        t = WebUtility.HtmlDecode(t).Replace("\u00a0", " ");
        t = Regex.Replace(t, @"[ \t]+\n", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }

    private async Task<Dictionary<string, string>> LoadCfgAsync()
    {
        var rows = await _db.ExecuteQueryAsync("SELECT CFG_KEY, CFG_VALUE FROM HRMS.HR_BULLETIN_SHARE_CFG",
            r => (K: r["CFG_KEY"]?.ToString() ?? "", V: r["CFG_VALUE"] == DBNull.Value ? "" : r["CFG_VALUE"]?.ToString() ?? ""));
        return rows.ToDictionary(x => x.K, x => x.V);
    }
    private async Task SetCfgAsync(string key, string? value, string? user)
    {
        int n = await _db.ExecuteNonQueryAsync("UPDATE HRMS.HR_BULLETIN_SHARE_CFG SET CFG_VALUE = :V, UPDT_ID = :U, UPDT_DT = SYSDATE WHERE CFG_KEY = :K",
            new OracleParameter("V", (object?)value ?? DBNull.Value), new OracleParameter("U", (object?)user ?? DBNull.Value), new OracleParameter("K", key));
        if (n == 0)
            await _db.ExecuteNonQueryAsync("INSERT INTO HRMS.HR_BULLETIN_SHARE_CFG (CFG_KEY, CFG_VALUE, UPDT_ID, UPDT_DT) VALUES (:K, :V, :U, SYSDATE)",
                new OracleParameter("K", key), new OracleParameter("V", (object?)value ?? DBNull.Value), new OracleParameter("U", (object?)user ?? DBNull.Value));
    }

    // GET apiHR/BulletinShare/fb-config — KHÔNG trả token thật, chỉ 4 ký tự cuối
    [HttpGet("fb-config")]
    public async Task<IActionResult> GetFbConfig()
    {
        try
        {
            if (!await ReadyAsync()) return NotReady();
            var c = await LoadCfgAsync();
            var tok = c.GetValueOrDefault("FB_PAGE_TOKEN") ?? "";
            return Ok(new { success = true, pageId = c.GetValueOrDefault("FB_PAGE_ID"), pageName = c.GetValueOrDefault("FB_PAGE_NAME"),
                            hasToken = tok.Length > 0, tokenTail = tok.Length > 4 ? "…" + tok[^4..] : "" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    public class FbCfgReq { public string? PageId { get; set; } public string? Token { get; set; } public string? LoginUser { get; set; } }

    // POST apiHR/BulletinShare/fb-config — lưu Page ID + token, kiểm tra luôn với Facebook (lấy tên Page)
    [HttpPost("fb-config")]
    public async Task<IActionResult> SaveFbConfig([FromBody] FbCfgReq req)
    {
        try
        {
            if (!await ReadyAsync()) return NotReady();
            var c = await LoadCfgAsync();
            var pageId = string.IsNullOrWhiteSpace(req.PageId) ? c.GetValueOrDefault("FB_PAGE_ID") : req.PageId.Trim();
            var token  = string.IsNullOrWhiteSpace(req.Token) ? c.GetValueOrDefault("FB_PAGE_TOKEN") : req.Token.Trim();
            if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
                return Ok(new { success = false, message = "Cần nhập Page ID và Page Access Token" });
            var (ok, name, err) = await CheckFbAsync(pageId!, token!);
            if (!ok) return Ok(new { success = false, message = "Facebook từ chối: " + err });
            await SetCfgAsync("FB_PAGE_ID", pageId, req.LoginUser);
            await SetCfgAsync("FB_PAGE_TOKEN", token, req.LoginUser);
            await SetCfgAsync("FB_PAGE_NAME", name, req.LoginUser);
            return Ok(new { success = true, pageName = name, message = $"Đã kết nối Fanpage \"{name}\"" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST apiHR/BulletinShare/fb-test — chỉ ĐỌC tên Page, không đăng gì
    [HttpPost("fb-test")]
    public async Task<IActionResult> TestFb()
    {
        try
        {
            if (!await ReadyAsync()) return NotReady();
            var c = await LoadCfgAsync();
            var pageId = c.GetValueOrDefault("FB_PAGE_ID"); var token = c.GetValueOrDefault("FB_PAGE_TOKEN");
            if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
                return Ok(new { success = false, message = "Chưa cấu hình Fanpage" });
            var (ok, name, err) = await CheckFbAsync(pageId!, token!);
            return Ok(new { success = ok, message = ok ? $"Kết nối tốt: Fanpage \"{name}\"" : "Lỗi: " + err });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    private async Task<(bool Ok, string? Name, string? Err)> CheckFbAsync(string pageId, string token)
    {
        try
        {
            var http = _http.CreateClient(); http.Timeout = TimeSpan.FromSeconds(20);
            // /me bằng Page token trả về CHÍNH Fanpage đó; token người dùng trả về người dùng → báo sai loại token
            var res = await http.GetAsync($"{Graph()}/me?fields=id,name&access_token={Uri.EscapeDataString(token)}");
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return (false, null, e.TryGetProperty("message", out var m) ? m.GetString() : body);
            var meId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (meId != pageId)
                return (false, null, "Token này không phải Page Access Token của Fanpage " + pageId + " (có thể đang dán token người dùng — xem bước 4 trong hướng dẫn)");
            return (true, doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : pageId, null);
        }
        catch (Exception ex) { return (false, null, "Không gọi được Facebook (server có ra Internet không?): " + ex.Message); }
    }

    // POST apiHR/BulletinShare/post-fb — đăng lên Fanpage (kèm ảnh bìa nếu chọn)
    [HttpPost("post-fb")]
    public async Task<IActionResult> PostFb([FromBody] SendReq req)
    {
        try { return await PostFbCore(req); }
        catch (Exception ex) { return Ok(new { success = false, message = "Chưa đăng được (lỗi đọc dữ liệu): " + ex.Message }); }
    }
    private async Task<IActionResult> PostFbCore(SendReq req)
    {
        if (!await ReadyAsync()) return NotReady();
        var b = await LoadBulletinAsync(req.BulletinId);
        if (b == null || b.IsActive == 0) return Ok(new { success = false, message = "Không tìm thấy bản tin" });
        if (!await CanAccessAsync(b, req.LoginUser)) return Ok(new { success = false, message = "Không có quyền" });
        var c = await LoadCfgAsync();
        var pageId = c.GetValueOrDefault("FB_PAGE_ID"); var token = c.GetValueOrDefault("FB_PAGE_TOKEN");
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
            return Ok(new { success = false, needConfig = true, message = "Chưa kết nối Fanpage — vào \"Cài đặt Facebook\" để dán Page token" });

        var s = await LoadShareAsync(req.BulletinId, "FB") ?? Defaults(b, "FB");
        var message = string.IsNullOrWhiteSpace(s.FbMessage) ? BuildFbText(b) : s.FbMessage!;
        if (message.Length > 60000) return Ok(new { success = false, message = $"Nội dung quá dài cho Facebook ({message.Length:N0} ký tự, tối đa ~60.000)" });
        byte[]? img = s.FbWithImage == 1 && b.Cover != null ? await LoadImageAsync(b.Cover) : null;

        if (!TryLock(req.BulletinId, "FB")) return Ok(new { success = false, message = "Bài này đang được đăng, vui lòng đợi" });
        try
        {
            if (await JustSentAsync(req.BulletinId, "FB"))
                return Ok(new { success = false, message = "Bài này vừa được đăng (dưới 1 phút) — đợi chút rồi đăng lại nếu cần" });
            string body;
            try
            {
                var http = _http.CreateClient(); http.Timeout = TimeSpan.FromSeconds(120);
                HttpResponseMessage res;
                if (img != null)
                {
                    using var form = new MultipartFormDataContent();
                    var file = new ByteArrayContent(img);
                    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MimeOf(b.Cover!));
                    form.Add(file, "source", b.Cover!);
                    form.Add(new StringContent(message, Encoding.UTF8), "message");
                    form.Add(new StringContent("true"), "published");
                    form.Add(new StringContent(token!), "access_token");
                    res = await http.PostAsync($"{Graph()}/{Uri.EscapeDataString(pageId!)}/photos", form);
                }
                else
                {
                    using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["message"] = message, ["access_token"] = token! });
                    res = await http.PostAsync($"{Graph()}/{Uri.EscapeDataString(pageId!)}/feed", form);
                }
                body = await res.Content.ReadAsStringAsync();
            }
            catch (TaskCanceledException)
            {
                // Hết thời gian chờ: Facebook CÓ THỂ đã đăng rồi → không cho bấm lại ngay
                try { await MarkAsync(req.BulletinId, "FB", false, "Hết thời gian chờ Facebook trả lời — kiểm tra Fanpage xem bài đã lên chưa trước khi đăng lại", null, req.LoginUser); } catch { }
                return Ok(new { success = false, message = "Facebook không trả lời kịp. Mở Fanpage kiểm tra xem bài ĐÃ LÊN chưa trước khi đăng lại." });
            }
            catch (Exception ex)
            {
                try { await MarkAsync(req.BulletinId, "FB", false, ex.Message, null, req.LoginUser); } catch { }
                return Ok(new { success = false, message = "Không gọi được Facebook (server có ra Internet không?): " + ex.Message });
            }

            string? err = null, postId = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var e)) err = e.TryGetProperty("message", out var m) ? m.GetString() : body;
                else postId = doc.RootElement.TryGetProperty("post_id", out var pid) ? pid.GetString()
                            : doc.RootElement.TryGetProperty("id", out var id2) ? id2.GetString() : null;
            }
            catch { err = "Facebook trả về dữ liệu lạ: " + (body.Length > 300 ? body[..300] : body); }

            if (err != null)
            {
                try { await MarkAsync(req.BulletinId, "FB", false, err, null, req.LoginUser); } catch { }
                return Ok(new { success = false, message = "Facebook báo lỗi: " + err });
            }
            // ĐÃ lên Fanpage: lỗi ghi trạng thái không được biến thành "đăng lỗi" (sẽ bị đăng trùng)
            string warn = "";
            try { await MarkAsync(req.BulletinId, "FB", true, null, postId, req.LoginUser); }
            catch { warn = " (lưu trạng thái lỗi — ĐỪNG đăng lại, bài đã lên)"; }
            bool noImg = s.FbWithImage == 1 && b.Cover != null && img == null;
            return Ok(new { success = true, postId, message = "Đã đăng lên Fanpage" + (noImg ? " (không đọc được ảnh bìa nên chỉ đăng chữ)" : "") + warn });
        }
        finally { Unlock(req.BulletinId, "FB"); }
    }

    // ─────────────────────────── tiện ích ───────────────────────────
    private static List<string> ParseRecipients(string? s) =>
        (s ?? "").Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static bool IsEmail(string a) => Regex.IsMatch(a, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    private static string? Trunc(string? s, int max) => s == null ? null : s.Length <= max ? s : s[..max];
}
