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

    // Mã tra cứu hội thoại ẩn danh — KHÔNG dùng INQUIRY_NO (dạng INQ-20260626-0038, đoán được vì
    // tăng dần theo ngày) — sinh ngẫu nhiên thật (RandomNumberGenerator, không phải Random thường).
    // Định dạng (yêu cầu 2026-11-06, công nhân không phân biệt được chữ hoa/chữ thường): 6 ký tự gồm SỐ + CHỮ IN HOA
    // (bỏ 0/O, 1/I/L dễ nhầm) và THÊM 1 ký tự đặc biệt ở vị trí ngẫu nhiên => 7 ký tự, vd "K7#M2P9".
    // Tra cứu KHÔNG phân biệt hoa/thường (xem LookupByCode). Không gian mã = 31^6 x 4 x 7 ≈ 25 tỷ, không thấp hơn
    // định dạng cũ (6 ký tự hoa+thường, ≈ 22 tỷ) nên không làm yếu việc đoán mã.
    private const string AccessCodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string AccessCodeSpecials = "#@*!";

    private static string GenerateAccessCode()
    {
        var chars = new List<char>(7);
        for (int i = 0; i < 6; i++)
            chars.Add(AccessCodeAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(AccessCodeAlphabet.Length)]);
        chars.Insert(System.Security.Cryptography.RandomNumberGenerator.GetInt32(7),
                     AccessCodeSpecials[System.Security.Cryptography.RandomNumberGenerator.GetInt32(AccessCodeSpecials.Length)]);
        return new string(chars.ToArray());
    }

    // Chuẩn hóa mã NV gõ vào: bỏ khoảng trắng, viết hoa (mã mới chỉ có chữ in hoa; mã cũ lẫn hoa/thường vẫn tra được
    // vì so sánh bằng UPPER ở cả hai phía).
    private static string NormalizeAccessCode(string? raw) =>
        new string((raw ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/topics
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("topics")]
    public async Task<IActionResult> GetTopics()
    {
        try
        {
            // Công nhân: có mức độ + số ngày phản hồi để gợi ý hạn; KHÔNG trả PIC (thông tin điều phối nội bộ).
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT T.TOPIC_CD, T.TOPIC_NAME, T.TOPIC_NAME_EN, T.ICON, T.COLOR, T.SORT_ORDER,
                       T.SEVERITY_CD, S.NAME_VI AS SEV_NAME, S.COLOR AS SEV_COLOR, S.RESPONSE_DAYS
                FROM HRMS.HR_INQUIRY_TOPIC T
                LEFT JOIN HRMS.HR_INQUIRY_SEVERITY S ON S.SEVERITY_CD = T.SEVERITY_CD
                WHERE T.IS_ACTIVE = 1
                ORDER BY T.SORT_ORDER",
                r => new InquiryTopicDto
                {
                    TopicCd     = r["TOPIC_CD"]?.ToString()    ?? "",
                    TopicName   = r["TOPIC_NAME"]?.ToString()  ?? "",
                    TopicNameEn = r["TOPIC_NAME_EN"]?.ToString(),
                    Icon        = r["ICON"]?.ToString(),
                    Color       = r["COLOR"]?.ToString(),
                    SortOrder   = r["SORT_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["SORT_ORDER"]),
                    SeverityCd     = r["SEVERITY_CD"]?.ToString() ?? "LOW",
                    SeverityName   = r["SEV_NAME"]?.ToString(),
                    SeverityColor  = r["SEV_COLOR"]?.ToString(),
                    ResponseDays   = r["RESPONSE_DAYS"] == DBNull.Value ? null : Convert.ToInt32(r["RESPONSE_DAYS"])
                });

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/admin/topics  — Admin: lấy tất cả topic (kể cả ẩn)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("admin/topics")]
    public async Task<IActionResult> GetAdminTopics()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT t.TOPIC_CD, t.TOPIC_NAME, t.TOPIC_NAME_EN, t.ICON, t.COLOR,
                       t.SORT_ORDER, t.IS_ACTIVE,
                       t.SEVERITY_CD, s.NAME_VI AS SEV_NAME, s.COLOR AS SEV_COLOR, s.RESPONSE_DAYS,
                       t.PIC_DEPT_ROLE_ID, dr.ROLE_NAME AS PIC_NAME,
                       (SELECT COUNT(*) FROM HRMS.HR_INQUIRY i WHERE i.TOPIC_CD = t.TOPIC_CD) AS USAGE_COUNT
                FROM HRMS.HR_INQUIRY_TOPIC t
                LEFT JOIN HRMS.HR_INQUIRY_SEVERITY s ON s.SEVERITY_CD = t.SEVERITY_CD
                LEFT JOIN HRMS.HR_DEPT_ROLES dr ON dr.ID = t.PIC_DEPT_ROLE_ID
                ORDER BY t.SORT_ORDER, t.TOPIC_CD",
                r => new InquiryTopicDto
                {
                    TopicCd     = r["TOPIC_CD"]?.ToString()    ?? "",
                    TopicName   = r["TOPIC_NAME"]?.ToString()  ?? "",
                    TopicNameEn = r["TOPIC_NAME_EN"]?.ToString(),
                    Icon        = r["ICON"]?.ToString(),
                    Color       = r["COLOR"]?.ToString(),
                    SortOrder   = r["SORT_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["SORT_ORDER"]),
                    IsActive    = r["IS_ACTIVE"]  == DBNull.Value ? true : Convert.ToInt32(r["IS_ACTIVE"]) == 1,
                    UsageCount  = r["USAGE_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["USAGE_COUNT"]),
                    SeverityCd     = r["SEVERITY_CD"]?.ToString() ?? "LOW",
                    SeverityName   = r["SEV_NAME"]?.ToString(),
                    SeverityColor  = r["SEV_COLOR"]?.ToString(),
                    ResponseDays   = r["RESPONSE_DAYS"] == DBNull.Value ? null : Convert.ToInt32(r["RESPONSE_DAYS"]),
                    PicDeptRoleId  = r["PIC_DEPT_ROLE_ID"] == DBNull.Value ? null : Convert.ToInt32(r["PIC_DEPT_ROLE_ID"]),
                    PicDeptName    = r["PIC_NAME"]?.ToString()
                });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/admin/severities  — danh sách mức độ (Low/High/ZT + số ngày) để đổ dropdown
    // GET /apiHR/Inquiry/admin/dept-roles  — danh sách phòng ban (HR_DEPT_ROLES) làm PIC
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("admin/severities")]
    public async Task<IActionResult> GetSeverities()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT SEVERITY_CD, NAME_VI, NAME_EN, RESPONSE_DAYS, COLOR
                FROM HRMS.HR_INQUIRY_SEVERITY ORDER BY SORT_ORDER",
                r => new
                {
                    severityCd   = r["SEVERITY_CD"]?.ToString() ?? "",
                    nameVi       = r["NAME_VI"]?.ToString(),
                    nameEn       = r["NAME_EN"]?.ToString(),
                    responseDays = Convert.ToInt32(r["RESPONSE_DAYS"]),
                    color        = r["COLOR"]?.ToString()
                });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpGet("admin/dept-roles")]
    public async Task<IActionResult> GetPicDeptRoles()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(
                "SELECT ID, ROLE_NAME FROM HRMS.HR_DEPT_ROLES ORDER BY ID",
                r => new { id = Convert.ToInt32(r["ID"]), name = r["ROLE_NAME"]?.ToString() ?? "" });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // Kiểm tra mức độ / PIC hợp lệ trước khi ghi topic hoặc case. Trả null nếu hợp lệ, ngược lại là thông báo lỗi.
    private async Task<string?> ValidateSeverityPicAsync(string? severityCd, int? picDeptRoleId)
    {
        if (!string.IsNullOrWhiteSpace(severityCd))
        {
            var ok = await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_INQUIRY_SEVERITY WHERE SEVERITY_CD = :S AND ROWNUM = 1",
                r => 1, new OracleParameter("S", severityCd.Trim().ToUpperInvariant()));
            if (ok.Count == 0) return "Mức độ không hợp lệ";
        }
        if (picDeptRoleId.HasValue)
        {
            var ok = await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_DEPT_ROLES WHERE ID = :I AND ROWNUM = 1",
                r => 1, new OracleParameter("I", picDeptRoleId.Value));
            if (ok.Count == 0) return "Phòng ban phụ trách (PIC) không hợp lệ";
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/admin/topics  — Admin: tạo topic mới
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("admin/topics")]
    public async Task<IActionResult> CreateTopic([FromBody] InquiryTopicSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.TopicCd))
                return Ok(new { success = false, message = "Thiếu mã chủ đề" });
            if (string.IsNullOrWhiteSpace(req.TopicName))
                return Ok(new { success = false, message = "Thiếu tên chủ đề" });

            string topicCd = req.TopicCd.Trim().ToUpperInvariant();
            if (topicCd.Length > 20)
                return Ok(new { success = false, message = "Mã chủ đề tối đa 20 ký tự" });

            var existing = await _db.ExecuteQueryAsync(
                "SELECT 1 FROM HRMS.HR_INQUIRY_TOPIC WHERE TOPIC_CD = :CD AND ROWNUM = 1",
                r => 1,
                new OracleParameter("CD", topicCd));
            if (existing.Count > 0)
                return Ok(new { success = false, message = "Mã chủ đề đã tồn tại" });

            var sevErr = await ValidateSeverityPicAsync(req.SeverityCd, req.PicDeptRoleId);
            if (sevErr != null) return Ok(new { success = false, message = sevErr });

            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY_TOPIC
                    (TOPIC_CD, TOPIC_NAME, TOPIC_NAME_EN, ICON, COLOR, SORT_ORDER, IS_ACTIVE, SEVERITY_CD, PIC_DEPT_ROLE_ID, INST_ID, INST_DT)
                VALUES
                    (:CD, :NAME, :NAME_EN, :ICON, :COLOR, :SORT, :ACTIVE, :SEVERITY, :PIC, :INST, SYSDATE)",
                new OracleParameter("CD",      topicCd),
                new OracleParameter("NAME",    req.TopicName.Trim()),
                new OracleParameter("NAME_EN", (object?)req.TopicNameEn?.Trim() ?? DBNull.Value),
                new OracleParameter("ICON",    (object?)req.Icon?.Trim()        ?? DBNull.Value),
                new OracleParameter("COLOR",   (object?)req.Color?.Trim()       ?? DBNull.Value),
                new OracleParameter("SORT",    req.SortOrder),
                new OracleParameter("ACTIVE",  req.IsActive ? 1 : 0),
                new OracleParameter("SEVERITY", string.IsNullOrWhiteSpace(req.SeverityCd) ? "LOW" : req.SeverityCd.Trim().ToUpperInvariant()),
                new OracleParameter("PIC",      (object?)req.PicDeptRoleId ?? DBNull.Value),
                new OracleParameter("INST",    (object?)req.UpdtId ?? DBNull.Value));

            return Ok(new { success = true, topicCd });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /apiHR/Inquiry/admin/topics/{topicCd}  — Admin: cập nhật topic
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("admin/topics/{topicCd}")]
    public async Task<IActionResult> UpdateTopic(string topicCd, [FromBody] InquiryTopicSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(topicCd))
                return Ok(new { success = false, message = "Thiếu mã chủ đề" });
            if (string.IsNullOrWhiteSpace(req.TopicName))
                return Ok(new { success = false, message = "Thiếu tên chủ đề" });

            var sevErr = await ValidateSeverityPicAsync(req.SeverityCd, req.PicDeptRoleId);
            if (sevErr != null) return Ok(new { success = false, message = sevErr });

            int rows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_TOPIC
                SET TOPIC_NAME    = :NAME,
                    TOPIC_NAME_EN = :NAME_EN,
                    ICON          = :ICON,
                    COLOR         = :COLOR,
                    SORT_ORDER    = :SORT,
                    IS_ACTIVE     = :ACTIVE,
                    SEVERITY_CD   = NVL(:SEVERITY, SEVERITY_CD),
                    PIC_DEPT_ROLE_ID = CASE WHEN :SEV_SET = 1 THEN :PIC ELSE PIC_DEPT_ROLE_ID END,
                    UPDT_ID       = :UPDT,
                    UPDT_DT       = SYSDATE
                WHERE TOPIC_CD = :CD",
                new OracleParameter("NAME",    req.TopicName.Trim()),
                new OracleParameter("NAME_EN", (object?)req.TopicNameEn?.Trim() ?? DBNull.Value),
                new OracleParameter("ICON",    (object?)req.Icon?.Trim()        ?? DBNull.Value),
                new OracleParameter("COLOR",   (object?)req.Color?.Trim()       ?? DBNull.Value),
                new OracleParameter("SORT",    req.SortOrder),
                new OracleParameter("ACTIVE",  req.IsActive ? 1 : 0),
                // Client CŨ (trang Chủ đề bản trước, không gửi severityCd) -> GIỮ NGUYÊN mức độ + PIC đã cấu hình, không reset về LOW/NULL.
                // Khai báo KIỂU tham số rõ ràng: NVL(:SEVERITY, ...) / CASE ... THEN :PIC với DBNull không tự suy ra được kiểu (ORA-00932)
                new OracleParameter("SEVERITY", OracleDbType.Varchar2) { Value = string.IsNullOrWhiteSpace(req.SeverityCd) ? DBNull.Value : req.SeverityCd.Trim().ToUpperInvariant() },
                new OracleParameter("SEV_SET",  string.IsNullOrWhiteSpace(req.SeverityCd) ? 0 : 1),
                new OracleParameter("PIC",      OracleDbType.Int32) { Value = req.PicDeptRoleId.HasValue ? req.PicDeptRoleId.Value : DBNull.Value },
                new OracleParameter("UPDT",    (object?)req.UpdtId ?? DBNull.Value),
                new OracleParameter("CD",      topicCd.Trim().ToUpperInvariant()));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy chủ đề" });
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/admin/topics/{topicCd}/toggle  — Admin: ẩn/hiện
    //   Ẩn (IS_ACTIVE=0) KHÔNG ảnh hưởng các hội thoại đã có TOPIC_CD này.
    //   Chỉ ngăn người dùng tạo hội thoại mới với topic ẩn.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("admin/topics/{topicCd}/toggle")]
    public async Task<IActionResult> ToggleTopic(string topicCd, [FromBody] InquiryTopicToggleRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(topicCd))
                return Ok(new { success = false, message = "Thiếu mã chủ đề" });

            int rows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_TOPIC
                SET IS_ACTIVE = :ACTIVE,
                    UPDT_ID   = :UPDT,
                    UPDT_DT   = SYSDATE
                WHERE TOPIC_CD = :CD",
                new OracleParameter("ACTIVE", req.IsActive ? 1 : 0),
                new OracleParameter("UPDT",   (object?)req.UpdtId ?? DBNull.Value),
                new OracleParameter("CD",     topicCd.Trim().ToUpperInvariant()));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy chủ đề" });
            return Ok(new { success = true, isActive = req.IsActive });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /apiHR/Inquiry/admin/topics/{topicCd}  — Admin: xóa topic
    //   Chặn xóa nếu còn hội thoại tham chiếu (gợi ý ẩn thay vì xóa)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("admin/topics/{topicCd}")]
    public async Task<IActionResult> DeleteTopic(string topicCd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(topicCd))
                return Ok(new { success = false, message = "Thiếu mã chủ đề" });

            string cd = topicCd.Trim().ToUpperInvariant();

            var inUse = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) AS C FROM HRMS.HR_INQUIRY WHERE TOPIC_CD = :CD",
                r => Convert.ToInt32(r["C"]),
                new OracleParameter("CD", cd));
            int usage = inUse.FirstOrDefault();
            if (usage > 0)
                return Ok(new
                {
                    success  = false,
                    code     = "IN_USE",
                    usage,
                    message  = $"Chủ đề đang được sử dụng trong {usage} hội thoại. Hãy ẩn thay vì xóa."
                });

            int rows = await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_INQUIRY_TOPIC WHERE TOPIC_CD = :CD",
                new OracleParameter("CD", cd));
            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy chủ đề" });
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /apiHR/Inquiry/admin/{id}  — Admin: xoá hẳn 1 hội thoại (kèm tin nhắn,
    // attach, ref). Dùng để dọn dữ liệu test/phá nhằm không lệch KPI HR.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> DeleteConversation(long id)
    {
        try
        {
            if (id <= 0) return Ok(new { success = false, message = "Thiếu ID hội thoại" });

            var exists = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) C FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                r => Convert.ToInt32(r["C"]),
                new OracleParameter("ID", id));
            if (exists.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Không tìm thấy hội thoại" });

            // Xoá theo thứ tự con → cha (REF/ATTACH gắn với MSG, MSG gắn với INQUIRY)
            await _db.ExecuteNonQueryAsync(
                @"DELETE FROM HRMS.HR_INQUIRY_MSG_REF
                  WHERE MSG_ID IN (SELECT ID FROM HRMS.HR_INQUIRY_MSG WHERE INQUIRY_ID = :ID)",
                new OracleParameter("ID", id));

            await _db.ExecuteNonQueryAsync(
                @"DELETE FROM HRMS.HR_INQUIRY_ATTACH
                  WHERE MSG_ID IN (SELECT ID FROM HRMS.HR_INQUIRY_MSG WHERE INQUIRY_ID = :ID)",
                new OracleParameter("ID", id));

            await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_INQUIRY_MSG WHERE INQUIRY_ID = :ID",
                new OracleParameter("ID", id));

            int rows = await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                new OracleParameter("ID", id));

            if (rows == 0)
                return Ok(new { success = false, message = "Không xoá được hội thoại" });

            return Ok(new { success = true });
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

            // Loại báo cáo: GRIEVANCE (Kiến nghị) | OPINION (Ý kiến). Không gửi (web bản cũ lúc đang deploy) -> lưu NULL;
            // đã gửi mà sai giá trị thì từ chối.
            string? reportType = string.IsNullOrWhiteSpace(req.ReportType) ? null : req.ReportType.Trim().ToUpperInvariant();
            if (reportType != null && reportType != "GRIEVANCE" && reportType != "OPINION")
                return Ok(new { success = false, message = "Loại báo cáo không hợp lệ" });

            // Phản ánh sai thông tin cá nhân (link ở trang Hồ sơ → topic COMPLAINT): luôn là Góp ý, PIC = Nhân sự,
            // không có mức độ/hạn; vẫn nhận kể cả khi COMPLAINT đã bị tắt khỏi danh sách chủ đề sau go-live.
            bool isProfileFeedback = string.Equals(req.TopicCd, "COMPLAINT", StringComparison.OrdinalIgnoreCase);
            if (isProfileFeedback) reportType = "OPINION";

            // Mức độ + PIC + số ngày phản hồi lấy từ catalog Topic lúc tạo (CSR sửa được sau ở trang Chi tiết case).
            // Hạn = hết ngày (TRUNC(SYSDATE) + số ngày) — ngày hạn còn hiệu lực hết ngày đó. Topic không có trong catalog -> để trống.
            var topicInfo = (await _db.ExecuteQueryAsync(@"
                SELECT T.SEVERITY_CD, T.PIC_DEPT_ROLE_ID, S.RESPONSE_DAYS, T.IS_ACTIVE
                FROM HRMS.HR_INQUIRY_TOPIC T
                LEFT JOIN HRMS.HR_INQUIRY_SEVERITY S ON S.SEVERITY_CD = T.SEVERITY_CD
                WHERE T.TOPIC_CD = :CD AND ROWNUM = 1",
                r => new
                {
                    Severity = r["SEVERITY_CD"]?.ToString(),
                    Pic      = r["PIC_DEPT_ROLE_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PIC_DEPT_ROLE_ID"]),
                    Days     = r["RESPONSE_DAYS"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["RESPONSE_DAYS"]),
                    Active   = r["IS_ACTIVE"] == DBNull.Value || Convert.ToInt32(r["IS_ACTIVE"]) == 1
                },
                new OracleParameter("CD", req.TopicCd))).FirstOrDefault();

            // Chủ đề phải tồn tại và đang bật: UI công nhân chỉ liệt kê chủ đề đang bật, nhưng gọi thẳng API vẫn tạo được chủ đề đã tắt
            // (đặc biệt sau go-live, khi 11 chủ đề cũ bị tắt) → chặn ở đây.
            // Lối tắt từ trang Phiếu lương (G01) cũng nhận được trước khi go-live bật catalog.
            bool isQuickEntry = isProfileFeedback || string.Equals(req.TopicCd, "G01", StringComparison.OrdinalIgnoreCase);
            if (topicInfo == null || (!isQuickEntry && !topicInfo.Active))
                return Ok(new { success = false, message = "Chủ đề này không tồn tại hoặc đã ngừng sử dụng. Vui lòng chọn chủ đề khác." });

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
            string? accessCode  = null;
            if (req.ChatType == "ANON" && !string.IsNullOrWhiteSpace(req.AnonToken))
            {
                string tok = req.AnonToken.Replace("-", "");
                anonDisplay = "Ẩn danh #" + tok[^Math.Min(4, tok.Length)..].ToUpper();

                // Thử tối đa 5 lần nếu trùng mã (xác suất cực thấp với không gian ~25 tỷ mã,
                // nhưng vẫn kiểm tra UNIQUE thật thay vì tin suông vào xác suất).
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    string candidate = GenerateAccessCode();
                    var dup = await _db.ExecuteQueryAsync(
                        "SELECT 1 AS X FROM HRMS.HR_INQUIRY WHERE UPPER(ACCESS_CODE) = :CODE AND ROWNUM = 1",
                        r => 1, new OracleParameter("CODE", candidate));
                    if (dup.Count == 0) { accessCode = candidate; break; }
                }
                if (accessCode == null)
                    return Ok(new { success = false, message = "Không sinh được mã tra cứu, vui lòng thử lại" });
            }

            // Insert HR_INQUIRY (trigger tự set ID + INQUIRY_NO)
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY
                    (CHAT_TYPE, EMPCD, EMP_NAME, ANON_TOKEN, ANON_DISPLAY, ACCESS_CODE,
                     TOPIC_CD, SUBJECT, REPORT_TYPE, SEVERITY_CD, PIC_DEPT_ROLE_ID, DEADLINE_DT,
                     STATUS, UNREAD_HR, UNREAD_EMP, MSG_COUNT, INST_DT)
                VALUES
                    (:CHAT_TYPE, :EMPCD, :EMP_NAME, :ANON_TOKEN, :ANON_DISPLAY, :ACCESS_CODE,
                     :TOPIC_CD, :SUBJECT, :REPORT_TYPE, :SEVERITY, :PIC, :DEADLINE,
                     'OPEN', 0, 0, 0, SYSDATE)",
                new OracleParameter("CHAT_TYPE",    req.ChatType),
                new OracleParameter("EMPCD",        (object?)req.EmpCd     ?? DBNull.Value),
                new OracleParameter("EMP_NAME",     string.IsNullOrEmpty(empName) ? (object)DBNull.Value : empName),
                new OracleParameter("ANON_TOKEN",   (object?)req.AnonToken ?? DBNull.Value),
                new OracleParameter("ANON_DISPLAY", (object?)anonDisplay   ?? DBNull.Value),
                new OracleParameter("ACCESS_CODE",  (object?)accessCode    ?? DBNull.Value),
                new OracleParameter("TOPIC_CD",     req.TopicCd),
                new OracleParameter("SUBJECT",      (object?)req.Subject   ?? DBNull.Value),
                new OracleParameter("REPORT_TYPE",  (object?)reportType    ?? DBNull.Value),
                new OracleParameter("SEVERITY",     (object?)(isProfileFeedback ? null : topicInfo?.Severity) ?? DBNull.Value),
                new OracleParameter("PIC",          isProfileFeedback ? 1 : (object?)topicInfo?.Pic ?? DBNull.Value),
                new OracleParameter("DEADLINE",     OracleDbType.Date) { Value = !isProfileFeedback && topicInfo?.Days is int d ? DateTime.Today.AddDays(d) : DBNull.Value });

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

            return Ok(new { success = true, inquiryId, inquiryNo, accessCode });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/{id}/case-info — trang "Chi tiết case" của CSR: phân loại hiện tại của 1 hội thoại.
    // Đơn ẨN DANH: chỉ trả nhãn "Ẩn danh #xxxx" (không có EMPCD/tên — vốn không được lưu).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{id:long}/case-info")]
    public async Task<IActionResult> GetCaseInfo(long id)
    {
        try
        {
            var c = (await _db.ExecuteQueryAsync(@"
                SELECT I.ID, I.INQUIRY_NO, I.CHAT_TYPE, I.EMPCD, I.EMP_NAME, I.ANON_DISPLAY, I.TOPIC_CD, T.TOPIC_NAME,
                       I.SUBJECT, I.STATUS, I.REPORT_TYPE, I.SEVERITY_CD, I.PIC_DEPT_ROLE_ID, DR.ROLE_NAME AS PIC_NAME,
                       I.DEADLINE_DT, I.INST_DT, I.CLOSED_DT
                FROM HRMS.HR_INQUIRY I
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC T ON T.TOPIC_CD = I.TOPIC_CD
                LEFT JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = I.PIC_DEPT_ROLE_ID
                WHERE I.ID = :ID",
                r =>
                {
                    bool anon = r["CHAT_TYPE"]?.ToString() == "ANON";
                    return new
                    {
                        id           = Convert.ToInt64(r["ID"]),
                        inquiryNo    = r["INQUIRY_NO"]?.ToString(),
                        chatType     = r["CHAT_TYPE"]?.ToString(),
                        empCd        = anon ? null : r["EMPCD"]?.ToString(),
                        empName      = anon ? null : r["EMP_NAME"]?.ToString(),
                        anonDisplay  = anon ? r["ANON_DISPLAY"]?.ToString() : null,
                        topicCd      = r["TOPIC_CD"]?.ToString(),
                        topicName    = r["TOPIC_NAME"]?.ToString(),
                        subject      = r["SUBJECT"]?.ToString(),
                        status       = r["STATUS"]?.ToString(),
                        reportType   = r["REPORT_TYPE"]?.ToString(),
                        severityCd   = r["SEVERITY_CD"]?.ToString(),
                        picDeptRoleId = r["PIC_DEPT_ROLE_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PIC_DEPT_ROLE_ID"]),
                        picDeptName  = r["PIC_NAME"]?.ToString(),
                        deadlineDt   = r["DEADLINE_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["DEADLINE_DT"]),
                        createdDt    = r["INST_DT"]     == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["INST_DT"]),
                        closedDt     = r["CLOSED_DT"]   == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CLOSED_DT"])
                    };
                },
                new OracleParameter("ID", id))).FirstOrDefault();
            if (c == null) return Ok(new { success = false, message = "Không tìm thấy hội thoại" });
            return Ok(new { success = true, data = c });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /apiHR/Inquiry/{id}/case-info — CSR sửa Loại báo cáo / Topic / Mức độ / PIC / Hạn.
    // KHÔNG tự tính lại hạn khi đổi mức độ (tránh ghi đè hạn CSR đã chỉnh tay); muốn đổi hạn thì sửa ô Hạn.
    // Role-gate nằm ở web layer (HrInquiryController, HR/CSR) theo đúng pattern của file này.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("{id:long}/case-info")]
    public async Task<IActionResult> UpdateCaseInfo(long id, [FromBody] InquiryCaseInfoUpdateRequest req)
    {
        try
        {
            string? reportType = string.IsNullOrWhiteSpace(req.ReportType) ? null : req.ReportType.Trim().ToUpperInvariant();
            if (reportType != null && reportType != "GRIEVANCE" && reportType != "OPINION")
                return Ok(new { success = false, message = "Loại báo cáo không hợp lệ" });

            if (string.IsNullOrWhiteSpace(req.TopicCd))
                return Ok(new { success = false, message = "Vui lòng chọn chủ đề" });
            var topicOk = await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_INQUIRY_TOPIC WHERE TOPIC_CD = :CD AND ROWNUM = 1",
                r => 1, new OracleParameter("CD", req.TopicCd.Trim()));
            if (topicOk.Count == 0) return Ok(new { success = false, message = "Chủ đề không tồn tại" });

            var err = await ValidateSeverityPicAsync(req.SeverityCd, req.PicDeptRoleId);
            if (err != null) return Ok(new { success = false, message = err });

            int rows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET REPORT_TYPE      = :RT,
                    TOPIC_CD         = :TOPIC,
                    SEVERITY_CD      = :SEV,
                    PIC_DEPT_ROLE_ID = :PIC,
                    DEADLINE_DT      = :DL,
                    UPDT_ID          = :UPDT,
                    UPDT_DT          = SYSDATE
                WHERE ID = :ID",
                new OracleParameter("RT",    (object?)reportType ?? DBNull.Value),
                new OracleParameter("TOPIC", req.TopicCd.Trim()),
                new OracleParameter("SEV",   string.IsNullOrWhiteSpace(req.SeverityCd) ? DBNull.Value : req.SeverityCd.Trim().ToUpperInvariant()),
                new OracleParameter("PIC",   (object?)req.PicDeptRoleId ?? DBNull.Value),
                new OracleParameter("DL",    OracleDbType.Date) { Value = req.DeadlineDt.HasValue ? req.DeadlineDt.Value.Date : DBNull.Value },
                new OracleParameter("UPDT",  (object?)req.UpdtId ?? DBNull.Value),
                new OracleParameter("ID",    id));
            // CSR đã phân loại bằng popup = đã xác nhận (rời hàng đợi "Phân loại nhanh")
            if (rows > 0 && await ConfirmColsAsync())
                await _db.ExecuteNonQueryAsync("UPDATE HRMS.HR_INQUIRY SET CSR_CONFIRM_DT = SYSDATE, CSR_CONFIRM_BY = :U WHERE ID = :ID",
                    new OracleParameter("U", (object?)req.UpdtId ?? DBNull.Value), new OracleParameter("ID", id));
            return Ok(new { success = rows > 0, message = rows > 0 ? null : "Không tìm thấy hội thoại" });
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            // UQ_INQUIRY_DIRECT_OPEN / UQ_INQUIRY_ANON_OPEN: mỗi người chỉ có 1 hội thoại ĐANG MỞ cho mỗi chủ đề
            return Ok(new { success = false, message = "Không đổi chủ đề được: người gửi đã có một hội thoại đang mở ở chủ đề này. Hãy đóng hội thoại kia trước." });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Inquiry/lookup-by-code — tra cứu hội thoại ẩn danh bằng mã, trả lại đúng
    // AnonToken gốc để FE lưu vào localStorage máy đang tra cứu. KHÔNG dùng EMPCD/tài khoản đăng
    // nhập của người gọi API này để xác định hay ghi nhận gì cả — chỉ dựa đúng vào ACCESS_CODE
    // khớp hay không (yêu cầu khách hàng 2026-11-04, xem chính sách gốc trong memory
    // project_inquiry_anon_policy — không liên kết thiết bị/tài khoản với hồ sơ ẩn danh).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("lookup-by-code")]
    public async Task<IActionResult> LookupByCode([FromBody] InquiryLookupByCodeRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Code))
                return Ok(new { success = false, message = "Vui lòng nhập mã tra cứu" });

            var rows = await _db.ExecuteQueryAsync(@"
                SELECT ID, ANON_TOKEN FROM HRMS.HR_INQUIRY
                WHERE UPPER(ACCESS_CODE) = :CODE AND ROWNUM = 1",
                r => new { Id = Convert.ToInt64(r["ID"]), AnonToken = r["ANON_TOKEN"]?.ToString() },
                new OracleParameter("CODE", NormalizeAccessCode(req.Code)));

            var hit = rows.FirstOrDefault();
            if (hit == null || string.IsNullOrEmpty(hit.AnonToken))
                return Ok(new { success = false, message = "Mã tra cứu không đúng hoặc không tồn tại" });

            return Ok(new { success = true, inquiryId = hit.Id, anonToken = hit.AnonToken });
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
    // GET /apiHR/Inquiry/search-refs?type=POLICY|GUIDE|BULLETIN&q=...
    // Tìm Policy/Guide/Bulletin để HR/Admin chèn trích dẫn vào tin nhắn
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("search-refs")]
    public async Task<IActionResult> SearchRefs(string type = "POLICY", string? q = null)
    {
        try
        {
            type = (type ?? "").ToUpper();
            if (type != "POLICY" && type != "GUIDE" && type != "BULLETIN")
                return Ok(new { success = false, message = "type phải là POLICY, GUIDE hoặc BULLETIN" });

            string trimmed = (q ?? "").Trim().ToUpper();
            string term    = "%" + trimmed + "%";
            string inner = type switch
            {
                "POLICY" => @"SELECT ID, CATEGORY, TITLE
                    FROM HRMS.HR_COMPANY_POLICY
                    WHERE IS_ACTIVE = 1
                      AND (:Q IS NULL OR UPPER(TITLE) LIKE :TERM OR UPPER(CATEGORY) LIKE :TERM)
                    ORDER BY CATEGORY, DISPLAY_ORDER, ID",
                "GUIDE" => @"SELECT ID, CATEGORY, TITLE
                    FROM HRMS.HR_GUIDE
                    WHERE IS_ACTIVE = 1
                      AND (:Q IS NULL OR UPPER(TITLE) LIKE :TERM OR UPPER(CATEGORY) LIKE :TERM)
                    ORDER BY CATEGORY NULLS LAST, DISPLAY_ORDER, ID",
                // HR_BULLETIN không có cột CATEGORY — trả NULL cho khớp shape 3 cột dùng chung.
                _ => @"SELECT ID, CAST(NULL AS NVARCHAR2(200)) AS CATEGORY, TITLE
                    FROM HRMS.HR_BULLETIN
                    WHERE IS_ACTIVE = 1 AND IS_PUBLISHED = 1
                      AND (:Q IS NULL OR UPPER(TITLE) LIKE :TERM)
                    ORDER BY PUBLISHED_DT DESC NULLS LAST, ID DESC"
            };
            string sql = $"SELECT * FROM ({inner}) WHERE ROWNUM <= 30";

            var rows = await _db.ExecuteQueryAsync(sql,
                r => new
                {
                    id       = Convert.ToInt64(r["ID"]),
                    refType  = type,
                    category = r["CATEGORY"]?.ToString(),
                    title    = r["TITLE"]?.ToString()
                },
                new OracleParameter("Q",    string.IsNullOrEmpty(trimmed) ? (object)DBNull.Value : trimmed),
                new OracleParameter("TERM", term));

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Câu trả lời mẫu (yêu cầu HR 2026-09-12) — kho dùng chung cho Admin/HR/CSR,
    // chỉ Admin được thêm/sửa/xoá; ai chat cũng chọn được để chèn vào ô soạn tin.
    // ─────────────────────────────────────────────────────────────────────────

    // GET /apiHR/Inquiry/canned-replies?q=&role=... — danh sách đang active, dùng cho picker khi chat.
    // role = RoleName của người đang chat (HR/Admin/CSR) — chỉ trả mẫu TARGET_ROLES rỗng (dùng chung)
    // hoặc có chứa role đó, để HR/Admin/CSR không còn thấy lẫn mẫu của nhau (yêu cầu 2026-09-21).
    [HttpGet("canned-replies")]
    public async Task<IActionResult> GetCannedReplies(string? q = null, string? role = null)
    {
        try
        {
            string trimmed = (q ?? "").Trim().ToUpper();
            string? roleTrim = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT * FROM (
                    SELECT ID, TITLE, CONTENT FROM HRMS.HR_INQUIRY_CANNED_REPLY
                    WHERE IS_ACTIVE = 1
                      AND (:Q IS NULL OR UPPER(TITLE) LIKE :TERM)
                      AND (:ROLE IS NULL OR TARGET_ROLES IS NULL
                           OR INSTR(',' || TARGET_ROLES || ',', ',' || :ROLE || ',') > 0)
                    ORDER BY DISPLAY_ORDER, ID
                ) WHERE ROWNUM <= 50",
                r => new
                {
                    id      = Convert.ToInt64(r["ID"]),
                    title   = r["TITLE"]?.ToString(),
                    content = r["CONTENT"]?.ToString()
                },
                new OracleParameter("Q",    string.IsNullOrEmpty(trimmed) ? (object)DBNull.Value : trimmed),
                new OracleParameter("TERM", "%" + trimmed + "%"),
                new OracleParameter("ROLE", (object?)roleTrim ?? DBNull.Value));

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Inquiry/admin/canned-replies?actor_empcd=... — TẤT CẢ (kể cả ẩn), cho trang quản lý
    [HttpGet("admin/canned-replies")]
    public async Task<IActionResult> GetCannedRepliesAdmin(string? actor_empcd = null)
    {
        try
        {
            if (!await IsAdminOrHrAsync(actor_empcd))
                return Ok(new { success = false, message = "Bạn không có quyền thực hiện thao tác này" });

            var rows = await _db.ExecuteQueryAsync(@"
                SELECT ID, TITLE, CONTENT, DISPLAY_ORDER, IS_ACTIVE, TARGET_ROLES, UPDATED_BY, UPDATED_DATE
                FROM HRMS.HR_INQUIRY_CANNED_REPLY ORDER BY DISPLAY_ORDER, ID",
                r => new
                {
                    id           = Convert.ToInt64(r["ID"]),
                    title        = r["TITLE"]?.ToString(),
                    content      = r["CONTENT"]?.ToString(),
                    displayOrder = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"]),
                    isActive     = r["IS_ACTIVE"] != DBNull.Value && Convert.ToInt32(r["IS_ACTIVE"]) == 1,
                    targetRoles  = r["TARGET_ROLES"] == DBNull.Value ? null : r["TARGET_ROLES"]?.ToString(),
                    updatedBy    = r["UPDATED_BY"]?.ToString(),
                    updatedDate  = r["UPDATED_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UPDATED_DATE"])
                });

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/Inquiry/admin/canned-replies/save — upsert theo ID (ID null/0 = tạo mới)
    [HttpPost("admin/canned-replies/save")]
    public async Task<IActionResult> SaveCannedReply([FromBody] SaveCannedReplyRequest req)
    {
        try
        {
            if (!await IsAdminOrHrAsync(req.ActorEmpCd))
                return Ok(new { success = false, message = "Bạn không có quyền thực hiện thao tác này" });
            if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Content))
                return Ok(new { success = false, message = "Thiếu tiêu đề hoặc nội dung" });

            // Lưu ý: bind ":BY" gây ORA-01745 (trùng từ khoá reserved GROUP BY/ORDER BY của Oracle —
            // xem cùng lỗi đã gặp ở OTController) — phải đặt tên khác (":UPDBY").
            if (req.Id is > 0)
            {
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY_CANNED_REPLY
                    SET TITLE = :TITLE, CONTENT = :CONTENT, DISPLAY_ORDER = :ORD,
                        IS_ACTIVE = :ACTIVE, TARGET_ROLES = :TROLES, UPDATED_BY = :UPDBY, UPDATED_DATE = SYSDATE
                    WHERE ID = :ID",
                    new OracleParameter("TITLE",   req.Title),
                    new OracleParameter("CONTENT", req.Content),
                    new OracleParameter("ORD",     req.DisplayOrder),
                    new OracleParameter("ACTIVE",  req.IsActive ? 1 : 0),
                    new OracleParameter("TROLES",  (object?)NormalizeTargetRoles(req.TargetRoles) ?? DBNull.Value),
                    new OracleParameter("UPDBY",   (object?)req.ActorEmpCd ?? DBNull.Value),
                    new OracleParameter("ID",      req.Id));
            }
            else
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_INQUIRY_CANNED_REPLY
                        (TITLE, CONTENT, DISPLAY_ORDER, IS_ACTIVE, TARGET_ROLES, UPDATED_BY, UPDATED_DATE)
                    VALUES
                        (:TITLE, :CONTENT, :ORD, :ACTIVE, :TROLES, :UPDBY, SYSDATE)",
                    new OracleParameter("TITLE",   req.Title),
                    new OracleParameter("CONTENT", req.Content),
                    new OracleParameter("ORD",     req.DisplayOrder),
                    new OracleParameter("ACTIVE",  req.IsActive ? 1 : 0),
                    new OracleParameter("TROLES",  (object?)NormalizeTargetRoles(req.TargetRoles) ?? DBNull.Value),
                    new OracleParameter("UPDBY",   (object?)req.ActorEmpCd ?? DBNull.Value));
            }

            return Ok(new { success = true, message = "Lưu thành công" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/Inquiry/admin/canned-replies/delete
    [HttpPost("admin/canned-replies/delete")]
    public async Task<IActionResult> DeleteCannedReply([FromBody] DeleteCannedReplyRequest req)
    {
        try
        {
            if (!await IsAdminOrHrAsync(req.ActorEmpCd))
                return Ok(new { success = false, message = "Bạn không có quyền thực hiện thao tác này" });

            await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_INQUIRY_CANNED_REPLY WHERE ID = :ID",
                new OracleParameter("ID", req.Id));

            return Ok(new { success = true, message = "Đã xoá" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // Xác thực ACTOR_EMPCD thực sự có role Admin/HR/CSR trong DB — tránh việc chỉ dựa vào
    // check role phía HR_web (ai gọi thẳng apiHR/Inquiry/admin/* sẽ bỏ qua được lớp đó).
    // Câu trả lời mẫu: ban đầu chỉ Admin quản lý, sau đổi ý cho cả HR/CSR quản lý (2026-09-14).
    private async Task<bool> IsAdminOrHrAsync(string? empCd)
    {
        if (string.IsNullOrEmpty(empCd)) return false;
        return await DeptRoleHelper.HasAnyRoleAsync(_db, empCd, "Admin", "HR", "CSR", "GA");
    }

    // CSV role dùng chung mẫu (yêu cầu 2026-09-21) — rỗng/trống = dùng chung cho cả Admin/HR/CSR,
    // giữ theo đúng convention TARGET_ROLES của HR_BULLETIN (HomeAdminService.NormalizeRoles).
    private static string? NormalizeTargetRoles(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var roles = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(r => r is "Admin" or "HR" or "CSR" or "GA")
                        .Distinct()
                        .ToArray();
        return roles.Length == 0 ? null : string.Join(",", roles);
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
                       i.RATING, i.RATING_NOTE,
                       b.DEPTNM AS DEPT_NAME, b.TEAMNM AS LINE_NAME, b.WORKNM AS WORK_NAME
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = i.EMPCD
                LEFT JOIN HRMS.EAM410 b  ON b.DEPTCD = ec.DEPTCD AND b.LINECD = ec.LINECD AND b.WORKCD = ec.WORKCD
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

            // Lấy refs cho các msg đang trả về
            var refsByMsg = new Dictionary<long, List<InquiryRefDto>>();
            var refRows = await _db.ExecuteQueryAsync(@"
                SELECT ID, MSG_ID, REF_TYPE, REF_ID, REF_TITLE
                FROM HRMS.HR_INQUIRY_MSG_REF
                WHERE INQUIRY_ID = :INQ_ID
                  AND (:AFTER = 0 OR MSG_ID > :AFTER2)
                ORDER BY ID",
                r => new
                {
                    MsgId = Convert.ToInt64(r["MSG_ID"]),
                    Dto   = new InquiryRefDto
                    {
                        Id       = Convert.ToInt64(r["ID"]),
                        RefType  = r["REF_TYPE"]?.ToString() ?? "",
                        RefId    = Convert.ToInt64(r["REF_ID"]),
                        RefTitle = r["REF_TITLE"]?.ToString()
                    }
                },
                new OracleParameter("INQ_ID", id),
                new OracleParameter("AFTER",  afterMsgId),
                new OracleParameter("AFTER2", afterMsgId));

            foreach (var rr in refRows)
            {
                if (!refsByMsg.TryGetValue(rr.MsgId, out var list))
                {
                    list = new List<InquiryRefDto>();
                    refsByMsg[rr.MsgId] = list;
                }
                list.Add(rr.Dto);
            }

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
                        Refs       = refsByMsg.TryGetValue(first.MsgId, out var rfs) ? rfs : new List<InquiryRefDto>(),
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
            bool hasRefs    = req.Refs.Count > 0;
            if (!hasContent && !hasFiles && !hasRefs)
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
                    // (SkipLockCheck: CSR duyệt bản nháp của HR/GA — gửi thay người soạn, không đòi lock)
                    bool lockedByOther = !req.SkipLockCheck
                                        && !string.IsNullOrEmpty(conv.AssignedTo)
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
                string lockName    = req.AssignedName ?? req.SenderName ?? "";
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
            else if ((isHr || isAdmin) && conv.AssignedTo != req.EmpCd && !req.SkipLockCheck)
            {
                // Admin gửi vào lock của HR khác → chuyển lock sang Admin
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY
                    SET ASSIGNED_TO = :HR_CD, ASSIGNED_NAME = :HR_NAME, LOCKED_DT = SYSDATE
                    WHERE ID = :ID AND STATUS = 'OPEN'",
                    new OracleParameter("HR_CD",   req.EmpCd   ?? ""),
                    new OracleParameter("HR_NAME", req.AssignedName ?? req.SenderName ?? ""),
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

            // Insert refs nếu có (chỉ HR/Admin được phép — EMP refs bị bỏ qua)
            if ((isHr || isAdmin) && req.Refs.Count > 0)
            {
                foreach (var rf in req.Refs)
                {
                    if (rf.RefId <= 0) continue;
                    if (rf.RefType != "POLICY" && rf.RefType != "GUIDE" && rf.RefType != "BULLETIN") continue;

                    // lookup title từ source table tương ứng
                    string? refTitle = null;
                    string lookupSql = rf.RefType switch
                    {
                        "POLICY"   => "SELECT TITLE FROM HRMS.HR_COMPANY_POLICY WHERE ID = :ID AND IS_ACTIVE = 1",
                        "GUIDE"    => "SELECT TITLE FROM HRMS.HR_GUIDE          WHERE ID = :ID AND IS_ACTIVE = 1",
                        _          => "SELECT TITLE FROM HRMS.HR_BULLETIN       WHERE ID = :ID AND IS_ACTIVE = 1 AND IS_PUBLISHED = 1"
                    };
                    var titleRows = await _db.ExecuteQueryAsync(
                        lookupSql,
                        r => r["TITLE"]?.ToString(),
                        new OracleParameter("ID", rf.RefId));
                    if (titleRows.Count == 0) continue;  // ref không tồn tại / inactive → skip
                    refTitle = titleRows[0];

                    await _db.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_INQUIRY_MSG_REF
                            (MSG_ID, INQUIRY_ID, REF_TYPE, REF_ID, REF_TITLE)
                        VALUES
                            (:MSG_ID, :INQ_ID, :REF_TYPE, :REF_ID, :REF_TITLE)",
                        new OracleParameter("MSG_ID",   msgId),
                        new OracleParameter("INQ_ID",   req.InquiryId),
                        new OracleParameter("REF_TYPE", rf.RefType),
                        new OracleParameter("REF_ID",   rf.RefId),
                        new OracleParameter("REF_TITLE",(object?)refTitle ?? DBNull.Value));
                }
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
                            foreach (var empCd in await GetInquiryNotifyEmpCdsAsync(capInquiryId))
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
                // IS_READ_HR vẫn giữ nguyên — đây là "đã xem bởi HR" nói chung, NV chỉ cần biết có
                // người bên HR xem chưa, không quan tâm cụ thể ai (không phải nguồn gây bug).
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_INQUIRY_MSG
                    SET IS_READ_HR = 1
                    WHERE INQUIRY_ID = :ID AND SENDER_TYPE = 'EMP' AND IS_READ_HR = 0",
                    new OracleParameter("ID", req.InquiryId));

                // Badge "chưa đọc" hiện cho CSR/HR/Admin PHẢI tính riêng theo từng người xem — trước
                // đây dùng chung 1 cờ HR_INQUIRY.UNREAD_HR nên 1 người mở hội thoại là mất badge của
                // TẤT CẢ mọi người khác luôn (bug HR báo 2026-09-10). Giờ ghi mốc "đã đọc tới đâu"
                // riêng cho từng VIEWER_EMPCD, HrList sẽ tự tính lại unread riêng cho người đang xem.
                if (!string.IsNullOrEmpty(req.ViewerEmpcd))
                {
                    await _db.ExecuteNonQueryAsync(@"
                        MERGE INTO HRMS.HR_INQUIRY_READER T
                        USING (SELECT :ID AS INQUIRY_ID, :EMPCD AS VIEWER_EMPCD FROM DUAL) S
                        ON (T.INQUIRY_ID = S.INQUIRY_ID AND T.VIEWER_EMPCD = S.VIEWER_EMPCD)
                        WHEN MATCHED THEN UPDATE SET T.LAST_READ_DT = SYSDATE
                        WHEN NOT MATCHED THEN INSERT (INQUIRY_ID, VIEWER_EMPCD, LAST_READ_DT)
                        VALUES (:ID, :EMPCD, SYSDATE)",
                        new OracleParameter("ID", req.InquiryId),
                        new OracleParameter("EMPCD", req.ViewerEmpcd));
                }
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

            // Kiểm tra quyền đóng — CSR xử lý giống hệt HR (cùng chịu luật khoá phụ trách), chỉ khác
            // CLOSED_BY_TYPE lưu riêng 'CSR' để báo cáo tách được ai đóng (yêu cầu 2026-10-01: "csr
            // đâu" — trước đây CSR gắn cứng lưu là 'HR' nên báo cáo không tách ra được).
            if (req.CloserType == "HR" || req.CloserType == "CSR")
            {
                // HR/CSR chỉ đóng được conversation mà mình đang phụ trách
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
            string dbCloserType = req.CloserType; // DB constraint CHK_INQUIRY_CLOSED_TYPE allows 'HR' | 'EMP' | 'ADMIN' | 'CSR'

            // Đóng conversation — atomic: check STATUS='OPEN' + ownership trong cùng 1 UPDATE
            int closeRows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY
                SET STATUS = 'CLOSED', CLOSED_DT = SYSDATE,
                    CLOSED_BY = :CLOSER_EMP, CLOSED_BY_NAME = :CLOSER_NAME,
                    CLOSED_BY_TYPE = :CLOSER_TYPE, CLOSE_NOTE = :NOTE
                WHERE ID = :ID
                  AND STATUS = 'OPEN'
                  AND (
                       :CLOSER_TYPE = 'ADMIN'
                    OR (:CLOSER_TYPE IN ('HR','CSR') AND (ASSIGNED_TO IS NULL OR ASSIGNED_TO = :EMP_CD))
                    OR (:CLOSER_TYPE = 'EMP' AND (EMPCD = :EMP_CD OR ANON_TOKEN = :ANON_TOKEN))
                  )",
                new OracleParameter("CLOSER_EMP",   req.EmpCd        ?? ""),
                new OracleParameter("CLOSER_NAME",  closerName),
                new OracleParameter("CLOSER_TYPE",  dbCloserType),
                new OracleParameter("NOTE",         (object?)req.CloseNote ?? DBNull.Value),
                new OracleParameter("ID",           req.InquiryId),
                new OracleParameter("EMP_CD",       (object?)req.EmpCd    ?? DBNull.Value),
                new OracleParameter("ANON_TOKEN",   (object?)req.AnonToken ?? DBNull.Value));
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
                new OracleParameter("CONTENT", "Cuộc hội thoại đã kết thúc"));

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
    // POST /apiHR/Inquiry/bulk-close — HR/CSR tự đóng hàng loạt nhiều hội thoại
    // (trước đây làm thủ công qua script ngoài, giờ build thành feature chính thức).
    // Tái dùng nguyên action Send + Close ở trên cho từng ID (gọi trực tiếp trong process,
    // không qua HTTP) để giữ đúng luật nghiệp vụ hiện có (lock/ownership/notification) —
    // không có đường tắt bypass nào ở đây, CSR/HR vẫn chỉ đóng được hội thoại chưa ai nhận
    // hoặc đang do chính mình phụ trách, giống hệt luồng đóng từng cái một.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("bulk-close")]
    public async Task<IActionResult> BulkClose([FromBody] InquiryBulkCloseRequest req)
    {
        try
        {
            if (req.InquiryNos == null || req.InquiryNos.Count == 0)
                return Ok(new { success = false, message = "Thiếu danh sách ID chat" });
            if (string.IsNullOrWhiteSpace(req.EmpCd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên thực hiện" });
            if (string.IsNullOrWhiteSpace(req.Message))
                return Ok(new { success = false, message = "Thiếu nội dung tin nhắn" });

            var noList = req.InquiryNos.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

            // Resolve INQUIRY_NO → ID theo batch 1000 (giới hạn IN-list Oracle)
            var idByNo = new Dictionary<string, long>();
            for (int i = 0; i < noList.Count; i += 1000)
            {
                var batch = noList.Skip(i).Take(1000).ToList();
                var inParams = batch.Select((no, j) => new OracleParameter($"N{j}", no)).ToArray();
                var inList = string.Join(",", inParams.Select(p => $":{p.ParameterName}"));
                var rows = await _db.ExecuteQueryAsync(
                    $"SELECT ID, INQUIRY_NO FROM HRMS.HR_INQUIRY WHERE INQUIRY_NO IN ({inList})",
                    r => new { Id = Convert.ToInt64(r["ID"]), No = r["INQUIRY_NO"]?.ToString() ?? "" },
                    inParams);
                foreach (var r in rows) idByNo[r.No] = r.Id;
            }

            var results = new List<InquiryBulkCloseItemResult>();
            int okCount = 0;

            // SenderType dùng cho bước gửi tin: "ADMIN" bypass lock hoàn toàn (giống Close bên dưới
            // cũng cho ADMIN đóng bất kỳ); HR/CSR giữ "HR" (bảng HR_INQUIRY_MSG không có giá trị
            // SENDER_TYPE riêng cho CSR, đúng như luồng gửi 1 tin đơn lẻ hiện có).
            string sendSenderType = req.CloserType == "ADMIN" ? "ADMIN" : "HR";

            foreach (var no in noList)
            {
                if (!idByNo.TryGetValue(no, out var id))
                {
                    results.Add(new InquiryBulkCloseItemResult { InquiryNo = no, Success = false, Message = "Không tìm thấy ID chat" });
                    continue;
                }

                var sendResult = await Send(new InquirySendRequest
                {
                    InquiryId    = id,
                    EmpCd        = req.EmpCd,
                    SenderType   = sendSenderType,
                    SenderName   = req.CloserName,
                    AssignedName = req.CloserName,
                    Content      = req.Message
                });
                dynamic sendVal = ((OkObjectResult)sendResult).Value!;
                if (sendVal.success != true)
                {
                    results.Add(new InquiryBulkCloseItemResult { InquiryNo = no, Success = false, Message = (string?)sendVal.message ?? "Gửi tin thất bại" });
                    continue;
                }

                // Đóng — CloserType giữ đúng role thật (HR/CSR) để báo cáo tách được.
                var closeResult = await Close(new InquiryCloseRequest
                {
                    InquiryId  = id,
                    EmpCd      = req.EmpCd,
                    CloserType = req.CloserType,
                    CloserName = req.CloserName,
                    CloseNote  = "Đóng hàng loạt - đã phản hồi"
                });
                dynamic closeVal = ((OkObjectResult)closeResult).Value!;
                if (closeVal.success != true)
                {
                    results.Add(new InquiryBulkCloseItemResult { InquiryNo = no, Success = false, Message = (string?)closeVal.message ?? "Đóng thất bại" });
                    continue;
                }

                okCount++;
                results.Add(new InquiryBulkCloseItemResult { InquiryNo = no, Success = true });
            }

            return Ok(new { success = true, total = noList.Count, okCount, failCount = noList.Count - okCount, results });
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
        string? sort       = null,   // newest (default) | oldest
        string? viewerEmpcd = null,  // mã NV người đang xem — tính "chưa đọc" riêng cho người này
        string? draftPending = null, // "1" → chỉ hội thoại có bản nháp trả lời chờ CSR duyệt
        string? picRoles   = null,   // CSV tên phòng (HR,GA...) — chỉ trả hội thoại có PIC thuộc các phòng này (người xem KHÔNG phải CSR)
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
            // Phạm vi theo phòng (2026-11-06): HR/GA... chỉ thấy hội thoại CSR đã phân loại cho phòng mình.
            // picRoles == null → không giới hạn (CSR). Có giá trị → chỉ PIC thuộc các phòng đó; chưa phân loại (PIC NULL) bị ẩn.
            var picNames = picRoles == null ? null
                : picRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Take(10).ToArray();
            string wherePic = picNames == null ? ""
                : picNames.Length == 0 ? "AND 1 = 0"
                : "AND i.PIC_DEPT_ROLE_ID IN (SELECT ID FROM HRMS.HR_DEPT_ROLES WHERE ROLE_NAME IN ("
                  + string.Join(",", picNames.Select((_, k) => ":PICR" + k)) + "))";
            bool draftOk = await DraftTableAvailableAsync();
            string draftSel = draftOk
                ? ", (SELECT COUNT(*) FROM HRMS.HR_INQUIRY_DRAFT dd WHERE dd.INQUIRY_ID = i.ID AND dd.STATUS = 'PENDING') AS DRAFT_PENDING"
                : "";
            string whereDraft = draftOk && draftPending == "1"
                ? "AND EXISTS (SELECT 1 FROM HRMS.HR_INQUIRY_DRAFT dd WHERE dd.INQUIRY_ID = i.ID AND dd.STATUS = 'PENDING')"
                : "";
            string whereSearch   = string.IsNullOrEmpty(search)
                                    ? ""
                                    : "AND (i.INQUIRY_NO LIKE :SEARCH OR i.EMP_NAME LIKE :SEARCH2 OR i.ANON_DISPLAY LIKE :SEARCH3 OR i.SUBJECT LIKE :SEARCH4 OR i.EMPCD LIKE :SEARCH5)";

            string orderClause = sort == "oldest"
                ? "ORDER BY i.LAST_MSG_DT ASC NULLS LAST, i.ID ASC"
                : "ORDER BY UNREAD_HR DESC, i.LAST_MSG_DT DESC NULLS LAST";

            // "Chưa đọc" tính RIÊNG cho từng người xem (HR_INQUIRY_READER.LAST_READ_DT theo
            // VIEWER_EMPCD) — không dùng chung 1 cờ i.UNREAD_HR nữa (bug: 1 người mở là mất badge
            // của mọi CSR/HR/Admin khác). Đếm số tin NV gửi SAU mốc lần cuối người này đọc.
            //
            // KHÔNG tính chưa đọc khi (yêu cầu 2026-09-10):
            //   - hội thoại đã ĐÓNG (CLOSED) — không ai cần đọc nữa;
            //   - hội thoại đã có người PHỤ TRÁCH khác mình — người ta đang lo rồi, người khác
            //     khỏi cần badge; chỉ chính người phụ trách mới thấy chưa đọc.
            const string unreadHrExpr = @"
                CASE
                    WHEN i.STATUS = 'CLOSED' THEN 0
                    WHEN i.ASSIGNED_TO IS NOT NULL AND i.ASSIGNED_TO <> :VIEWER_EMPCD THEN 0
                    ELSE (SELECT COUNT(*) FROM HRMS.HR_INQUIRY_MSG m
                          WHERE m.INQUIRY_ID = i.ID AND m.SENDER_TYPE = 'EMP' AND m.IS_DELETED = 0
                            AND m.SENT_DT > NVL(rd.LAST_READ_DT, DATE '1900-01-01'))
                END";

            string sql = $@"
                SELECT * FROM (
                    SELECT ROWNUM AS RN, q.* FROM (
                        SELECT i.ID, i.INQUIRY_NO, i.CHAT_TYPE, i.TOPIC_CD, t.TOPIC_NAME,
                               t.COLOR AS TOPIC_COLOR, t.ICON AS TOPIC_ICON,
                               i.SUBJECT, i.EMPCD, i.EMP_NAME,
                               b.DEPTNM AS DEPT_NAME, b.TEAMNM AS LINE_NAME, b.WORKNM AS WORK_NAME,
                               i.ANON_DISPLAY, NULL AS ANON_TOKEN, i.STATUS,
                               i.ASSIGNED_TO, i.ASSIGNED_NAME,
                               {unreadHrExpr} UNREAD_HR,
                               i.UNREAD_EMP, i.MSG_COUNT, i.LAST_MSG_DT, i.INST_DT,
                               i.CLOSED_DT, i.CLOSED_BY_NAME, i.CLOSED_BY_TYPE, i.RATING{draftSel}
                        FROM HRMS.HR_INQUIRY i
                        LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                        LEFT JOIN HRMS.HR_INQUIRY_READER rd ON rd.INQUIRY_ID = i.ID AND rd.VIEWER_EMPCD = :VIEWER_EMPCD
                        LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = i.EMPCD
                        LEFT JOIN HRMS.EAM410 b  ON b.DEPTCD = ec.DEPTCD AND b.LINECD = ec.LINECD AND b.WORKCD = ec.WORKCD
                        WHERE 1=1
                        {whereStatus}
                        {whereTopic}
                        {whereChatType}
                        {whereAssign}
                        {whereEmpCd}
                        {wherePic}
                        {whereDraft}
                        {whereSearch}
                        {orderClause}
                    ) q WHERE ROWNUM <= :MAXRN
                ) WHERE RN > :OFFSET";

            var parameters = new List<OracleParameter>
            {
                new("MAXRN",  maxRn),
                new("OFFSET", offset),
                new("VIEWER_EMPCD", (object?)viewerEmpcd ?? DBNull.Value)
            };

            if (!string.IsNullOrEmpty(status))     parameters.Add(new OracleParameter("STATUS",    status));
            if (!string.IsNullOrEmpty(topicCd))    parameters.Add(new OracleParameter("TOPIC",     topicCd));
            if (!string.IsNullOrEmpty(chatType))   parameters.Add(new OracleParameter("CHAT_TYPE", chatType));
            if (!string.IsNullOrEmpty(assignedTo) && assignedTo != "UNASSIGNED")
                                                    parameters.Add(new OracleParameter("ASSIGNED",  assignedTo));
            if (!string.IsNullOrEmpty(empCd))      parameters.Add(new OracleParameter("EMPCD",     "%" + empCd + "%"));
            if (picNames is { Length: > 0 })
                for (int k = 0; k < picNames.Length; k++) parameters.Add(new OracleParameter("PICR" + k, picNames[k]));
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

            // Summary COUNT (same filters, no pagination) — tổng "chưa đọc" cũng phải tính riêng
            // theo người xem, dùng lại đúng biểu thức unreadHrExpr ở trên (cùng JOIN reader table).
            string sqlCount = $@"
                SELECT COUNT(*)                                                       AS TOTAL,
                       NVL(SUM(CASE WHEN i.STATUS = 'OPEN'   THEN 1 ELSE 0 END), 0) AS CNT_OPEN,
                       NVL(SUM(CASE WHEN i.STATUS = 'CLOSED' THEN 1 ELSE 0 END), 0) AS CNT_CLOSED,
                       NVL(SUM(CASE WHEN i.STATUS = 'OPEN' AND i.ASSIGNED_TO IS NULL THEN 1 ELSE 0 END), 0) AS CNT_OPEN_UNASSIGNED,
                       NVL(SUM({unreadHrExpr}), 0)                                   AS TOTAL_UNREAD
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_READER rd ON rd.INQUIRY_ID = i.ID AND rd.VIEWER_EMPCD = :VIEWER_EMPCD
                WHERE 1=1
                {whereStatus}
                {whereTopic}
                {whereChatType}
                {whereAssign}
                {whereEmpCd}
                {wherePic}
                {whereDraft}
                {whereSearch}";

            var countParams = new List<OracleParameter> { new("VIEWER_EMPCD", (object?)viewerEmpcd ?? DBNull.Value) };
            if (!string.IsNullOrEmpty(status))     countParams.Add(new OracleParameter("STATUS",    status));
            if (!string.IsNullOrEmpty(topicCd))    countParams.Add(new OracleParameter("TOPIC",     topicCd));
            if (!string.IsNullOrEmpty(chatType))   countParams.Add(new OracleParameter("CHAT_TYPE", chatType));
            if (!string.IsNullOrEmpty(assignedTo) && assignedTo != "UNASSIGNED")
                                                    countParams.Add(new OracleParameter("ASSIGNED",  assignedTo));
            if (!string.IsNullOrEmpty(empCd))      countParams.Add(new OracleParameter("EMPCD",     "%" + empCd + "%"));
            if (picNames is { Length: > 0 })
                for (int k = 0; k < picNames.Length; k++) countParams.Add(new OracleParameter("PICR" + k, picNames[k]));
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
                total              = Convert.ToInt32(r["TOTAL"]),
                cntOpen            = Convert.ToInt32(r["CNT_OPEN"]),
                cntClosed          = Convert.ToInt32(r["CNT_CLOSED"]),
                cntOpenUnassigned  = Convert.ToInt32(r["CNT_OPEN_UNASSIGNED"]),
                totalUnread        = Convert.ToInt32(r["TOTAL_UNREAD"])
            }, countParams.ToArray());

            var c = counts.FirstOrDefault();
            int total             = c?.total             ?? 0;
            int cntOpen           = c?.cntOpen           ?? 0;
            int cntClosed         = c?.cntClosed         ?? 0;
            int cntOpenUnassigned = c?.cntOpenUnassigned ?? 0;
            int totalUnread       = c?.totalUnread       ?? 0;
            int totalPages  = pageSize > 0 ? (int)Math.Ceiling((double)total / pageSize) : 1;

            return Ok(new { success = true, data = rows, page, pageSize, total, totalPages, cntOpen, cntClosed, cntOpenUnassigned, totalUnread });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/report?from=YYYY-MM-DD&to=YYYY-MM-DD
    // Mặc định: tuần hiện tại (Thứ Hai → Chủ Nhật)
    // Báo cáo thống kê inquiry — chỉ Admin
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("report")]
    public async Task<IActionResult> Report(string? from = null, string? to = null)
    {
        try
        {
            DateTime startDt, endDt;
            if (DateTime.TryParse(from, out var fDt) && DateTime.TryParse(to, out var tDt))
            {
                startDt = fDt.Date;
                endDt   = tDt.Date.AddDays(1); // inclusive end-day
            }
            else
            {
                // Default: current ISO week (Monday → Sunday)
                var today = DateTime.Today;
                int dow   = (int)today.DayOfWeek;
                int diff  = dow == 0 ? -6 : 1 - dow; // back to Monday
                startDt = today.AddDays(diff);
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
                    SUM(CASE WHEN RATING >= 4 THEN 1 ELSE 0 END) AS CNT_SATISFIED,
                       SUM(CASE WHEN RATING = 3 THEN 1 ELSE 0 END) AS CNT_NORMAL,
                       SUM(CASE WHEN RATING IS NOT NULL AND RATING <= 2 THEN 1 ELSE 0 END) AS CNT_NOT_SATISFIED,
                       SUM(CASE WHEN STATUS = 'CLOSED' AND RATING IS NULL THEN 1 ELSE 0 END) AS CNT_UNRATED,
                    ROUND(AVG(RATING), 1)    AS AVG_RATING,
                    ROUND(AVG(MSG_COUNT), 1) AS AVG_MSG,
                    SUM(CASE WHEN CLOSED_BY_TYPE = 'HR'    THEN 1 ELSE 0 END) AS CLOSED_BY_HR,
                    SUM(CASE WHEN CLOSED_BY_TYPE = 'CSR'   THEN 1 ELSE 0 END) AS CLOSED_BY_CSR,
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
                        CntSatisfied    = I(r["CNT_SATISFIED"]),
                        CntNormal       = I(r["CNT_NORMAL"]),
                        CntNotSatisfied = I(r["CNT_NOT_SATISFIED"]),
                        CntUnrated      = I(r["CNT_UNRATED"]),
                        AvgRating     = D(r["AVG_RATING"]),
                        AvgMsg        = D(r["AVG_MSG"]),
                        ClosedByHr    = I(r["CLOSED_BY_HR"]),
                        ClosedByCsr   = I(r["CLOSED_BY_CSR"]),
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
                       SUM(CASE WHEN i.RATING >= 4 THEN 1 ELSE 0 END) AS CNT_SATISFIED,
                       SUM(CASE WHEN i.RATING = 3 THEN 1 ELSE 0 END) AS CNT_NORMAL,
                       SUM(CASE WHEN i.RATING IS NOT NULL AND i.RATING <= 2 THEN 1 ELSE 0 END) AS CNT_NOT_SATISFIED,
                       SUM(CASE WHEN i.STATUS = 'CLOSED' AND i.RATING IS NULL THEN 1 ELSE 0 END) AS CNT_UNRATED,
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
                        CntSatisfied    = Convert.ToInt32(r["CNT_SATISFIED"]),
                        CntNormal       = Convert.ToInt32(r["CNT_NORMAL"]),
                        CntNotSatisfied = Convert.ToInt32(r["CNT_NOT_SATISFIED"]),
                        CntUnrated      = Convert.ToInt32(r["CNT_UNRATED"]),
                        AvgRating = D(r["AVG_RATING"])
                    };
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            // ── 3. Người tham gia xử lý (yêu cầu 2026-11-06: "all người tham gia xử lý", không chỉ người được giao) ──
            // Tham gia = đã trả lời (tin nhắn HR/GA/CSR) | soạn hoặc duyệt bản nháp | là người phụ trách. Mỗi người đếm 1 lần cho mỗi hội thoại.
            bool draftOkRpt = await DraftTableAvailableAsync();
            string draftUnion = draftOkRpt ? @"
                    UNION SELECT INQUIRY_ID, AUTHOR_CD   FROM HRMS.HR_INQUIRY_DRAFT WHERE AUTHOR_CD IS NOT NULL
                    UNION SELECT INQUIRY_ID, REVIEWER_CD FROM HRMS.HR_INQUIRY_DRAFT WHERE REVIEWER_CD IS NOT NULL" : "";
            var byHr = await _db.ExecuteQueryAsync($@"
                SELECT p.CD AS HR_CD, NVL(MAX(u.FULL_NAME), p.CD) AS HR_NAME,
                       COUNT(*) AS HANDLED,
                       SUM(CASE WHEN i.STATUS = 'CLOSED' THEN 1 ELSE 0 END) AS CNT_CLOSED,
                       SUM(CASE WHEN i.RATING >= 4 THEN 1 ELSE 0 END) AS CNT_SATISFIED,
                       SUM(CASE WHEN i.RATING = 3 THEN 1 ELSE 0 END) AS CNT_NORMAL,
                       SUM(CASE WHEN i.RATING IS NOT NULL AND i.RATING <= 2 THEN 1 ELSE 0 END) AS CNT_NOT_SATISFIED,
                       SUM(CASE WHEN i.STATUS = 'CLOSED' AND i.RATING IS NULL THEN 1 ELSE 0 END) AS CNT_UNRATED,
                       ROUND(AVG(i.RATING), 1) AS AVG_RATING,
                       ROUND(AVG(
                           CASE WHEN i.CLOSED_DT IS NOT NULL AND i.LOCKED_DT IS NOT NULL
                                THEN (i.CLOSED_DT - i.LOCKED_DT) * 24 * 60
                           END
                       ), 0) AS AVG_HANDLE_MIN
                FROM (
                    SELECT INQUIRY_ID, SENDER_CD AS CD FROM HRMS.HR_INQUIRY_MSG WHERE SENDER_TYPE = 'HR' AND IS_DELETED = 0 AND SENDER_CD IS NOT NULL
                    UNION SELECT ID, ASSIGNED_TO FROM HRMS.HR_INQUIRY WHERE ASSIGNED_TO IS NOT NULL{draftUnion}
                ) p
                JOIN HRMS.HR_INQUIRY i ON i.ID = p.INQUIRY_ID
                LEFT JOIN HRMS.HR_USERS u ON u.EMPCD = p.CD
                WHERE TRUNC(i.INST_DT) >= :START_DT AND TRUNC(i.INST_DT) < :END_DT
                GROUP BY p.CD
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
                        CntSatisfied    = Convert.ToInt32(r["CNT_SATISFIED"]),
                        CntNormal       = Convert.ToInt32(r["CNT_NORMAL"]),
                        CntNotSatisfied = Convert.ToInt32(r["CNT_NOT_SATISFIED"]),
                        CntUnrated      = Convert.ToInt32(r["CNT_UNRATED"]),
                        AvgRating   = D(r["AVG_RATING"]),
                        AvgHandleMin= D(r["AVG_HANDLE_MIN"])
                    };
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            return Ok(new
            {
                success = true,
                from    = startDt.ToString("yyyy-MM-dd"),
                to      = endDt.AddDays(-1).ToString("yyyy-MM-dd"),
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
    // GET /apiHR/Inquiry/report-raw?from=YYYY-MM-DD&to=YYYY-MM-DD
    // Raw data từng hội thoại — dùng cho sheet "Raw Data" khi xuất Excel báo cáo
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("report-raw")]
    public async Task<IActionResult> ReportRaw(string? from = null, string? to = null)
    {
        try
        {
            DateTime startDt, endDt;
            if (DateTime.TryParse(from, out var fDt) && DateTime.TryParse(to, out var tDt))
            {
                startDt = fDt.Date;
                endDt   = tDt.Date.AddDays(1);
            }
            else
            {
                var today = DateTime.Today;
                int dow   = (int)today.DayOfWeek;
                int diff  = dow == 0 ? -6 : 1 - dow;
                startDt = today.AddDays(diff);
                endDt   = startDt.AddDays(7);
            }

            bool draftOkRaw = await DraftTableAvailableAsync();
            string draftSelRaw = draftOkRaw
                ? ", (SELECT COUNT(*) FROM HRMS.HR_INQUIRY_DRAFT dd WHERE dd.INQUIRY_ID = i.ID AND dd.STATUS = 'PENDING') AS DRAFT_PENDING"
                : ", 0 AS DRAFT_PENDING";
            var rows = await _db.ExecuteQueryAsync($@"
                SELECT i.ID, i.INQUIRY_NO, i.SUBJECT, t.TOPIC_NAME, i.STATUS, i.INST_DT, i.CLOSED_DT,
                       i.CHAT_TYPE, i.EMPCD, i.EMP_NAME,
                       i.CLOSED_BY_NAME, i.CLOSED_BY_TYPE, i.CLOSE_NOTE,
                       i.MSG_COUNT, i.ASSIGNED_TO, i.ASSIGNED_NAME, i.RATING, i.RATING_NOTE,
                       CASE WHEN i.LOCKED_DT IS NOT NULL
                            THEN ROUND((i.LOCKED_DT - i.INST_DT) * 24 * 60, 1)
                       END AS RESPONSE_MIN,
                       (SELECT B.DEPTNM FROM HRMS.ECM100 EC JOIN HRMS.EAM410 B
                          ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.EMPCD = i.EMPCD AND ROWNUM = 1) AS DEPT_NAME,
                       (SELECT B.TEAMNM FROM HRMS.ECM100 EC JOIN HRMS.EAM410 B
                          ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.EMPCD = i.EMPCD AND ROWNUM = 1) AS LINE_NAME,
                       (SELECT B.WORKNM FROM HRMS.ECM100 EC JOIN HRMS.EAM410 B
                          ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                        WHERE EC.EMPCD = i.EMPCD AND ROWNUM = 1) AS WORK_NAME,
                       fm.CONTENT AS FIRST_MSG,
                       lm.CONTENT AS LAST_HANDLER_MSG,
                       (SELECT COUNT(*) FROM HRMS.HR_INQUIRY_MSG hm WHERE hm.INQUIRY_ID = i.ID AND hm.SENDER_TYPE = 'HR' AND hm.IS_DELETED = 0) AS HR_MSGS,
                       i.DEADLINE_DT, pr.ROLE_NAME AS PIC_NAME,
                       (SELECT EC.SEXGB FROM HRMS.ECM100 EC WHERE EC.EMPCD = i.EMPCD AND ROWNUM = 1) AS SEXGB{draftSelRaw}
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_DEPT_ROLES pr ON pr.ID = i.PIC_DEPT_ROLE_ID
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                LEFT JOIN (
                    SELECT INQUIRY_ID, CONTENT FROM (
                        SELECT INQUIRY_ID, CONTENT,
                               ROW_NUMBER() OVER (PARTITION BY INQUIRY_ID ORDER BY SENT_DT ASC) RN
                        FROM HRMS.HR_INQUIRY_MSG WHERE SENDER_TYPE = 'EMP'
                    ) WHERE RN = 1
                ) fm ON fm.INQUIRY_ID = i.ID
                LEFT JOIN (
                    SELECT INQUIRY_ID, CONTENT FROM (
                        SELECT INQUIRY_ID, CONTENT,
                               ROW_NUMBER() OVER (PARTITION BY INQUIRY_ID ORDER BY SENT_DT DESC) RN
                        FROM HRMS.HR_INQUIRY_MSG WHERE SENDER_TYPE IN ('HR','ADMIN')
                    ) WHERE RN = 1
                ) lm ON lm.INQUIRY_ID = i.ID
                WHERE TRUNC(i.INST_DT) >= :START_DT AND TRUNC(i.INST_DT) < :END_DT
                ORDER BY i.INST_DT DESC",
                r =>
                {
                    static string? S(object v) => v == DBNull.Value ? null : v.ToString();
                    static DateTime? Dt(object v) => v == DBNull.Value ? null : Convert.ToDateTime(v);
                    static double? D(object v) => v == DBNull.Value ? null : (double?)Math.Round(Convert.ToDouble(v), 1);
                    static int? N(object v) => v == DBNull.Value ? null : Convert.ToInt32(v);
                    return new InquiryReportRawRowDto
                    {
                        Id             = Convert.ToInt64(r["ID"]),
                        InquiryNo      = r["INQUIRY_NO"]?.ToString() ?? "",
                        Subject        = S(r["SUBJECT"]),
                        TopicName      = S(r["TOPIC_NAME"]),
                        ChatType       = r["CHAT_TYPE"]?.ToString() ?? "",
                        Status         = r["STATUS"]?.ToString() ?? "",
                        InstDt         = Dt(r["INST_DT"]),
                        ClosedDt       = Dt(r["CLOSED_DT"]),
                        ClosedByName   = S(r["CLOSED_BY_NAME"]),
                        ClosedByType   = S(r["CLOSED_BY_TYPE"]),
                        CloseNote      = S(r["CLOSE_NOTE"]),
                        EmpCd          = r["CHAT_TYPE"]?.ToString() == "ANON" ? null : S(r["EMPCD"]),
                        EmpDisplay     = r["CHAT_TYPE"]?.ToString() == "ANON" ? "Ẩn danh" : S(r["EMP_NAME"]),
                        DeptName       = S(r["DEPT_NAME"]),
                        LineName       = S(r["LINE_NAME"]),
                        WorkName       = S(r["WORK_NAME"]),
                        FirstMsg       = S(r["FIRST_MSG"]),
                        MsgCount       = N(r["MSG_COUNT"]) ?? 0,
                        AssignedName   = S(r["ASSIGNED_NAME"]),
                        AssignedTo     = S(r["ASSIGNED_TO"]),
                        LastHandlerMsg = S(r["LAST_HANDLER_MSG"]),
                        ResponseMin    = D(r["RESPONSE_MIN"]),
                        Rating         = N(r["RATING"]),
                        RatingNote     = S(r["RATING_NOTE"]),
                        HrMsgCount     = N(r["HR_MSGS"]) ?? 0,
                        DeadlineDt     = Dt(r["DEADLINE_DT"]),
                        PicName        = S(r["PIC_NAME"]),
                        DraftPending   = N(r["DRAFT_PENDING"]) ?? 0,
                        Sex            = r["CHAT_TYPE"]?.ToString() == "ANON" ? null : S(r["SEXGB"])
                    };
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            return Ok(new
            {
                success = true,
                from    = startDt.ToString("yyyy-MM-dd"),
                to      = endDt.AddDays(-1).ToString("yyyy-MM-dd"),
                data    = rows
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/report-messages?from=YYYY-MM-DD&to=YYYY-MM-DD
    // Toàn bộ tin nhắn của các hội thoại trong khoảng ngày — dùng cho sheet
    // "Nội dung chat" khi xuất Excel báo cáo (yêu cầu 2026-09-21, HR/CSR cần xem
    // lại NV nhắn gì, ai phản hồi lúc mấy giờ, ai bấm kết thúc). Cùng cách lọc
    // ngày với ReportRaw ở trên để 2 sheet luôn khớp đúng 1 tập hội thoại.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("report-messages")]
    public async Task<IActionResult> ReportMessages(string? from = null, string? to = null)
    {
        try
        {
            DateTime startDt, endDt;
            if (DateTime.TryParse(from, out var fDt) && DateTime.TryParse(to, out var tDt))
            {
                startDt = fDt.Date;
                endDt   = tDt.Date.AddDays(1);
            }
            else
            {
                var today = DateTime.Today;
                int dow   = (int)today.DayOfWeek;
                int diff  = dow == 0 ? -6 : 1 - dow;
                startDt = today.AddDays(diff);
                endDt   = startDt.AddDays(7);
            }

            var rows = await _db.ExecuteQueryAsync(@"
                SELECT m.INQUIRY_ID, m.SENDER_TYPE, m.SENDER_NAME, m.MSG_TYPE, m.CONTENT, m.SENT_DT
                FROM HRMS.HR_INQUIRY_MSG m
                JOIN HRMS.HR_INQUIRY i ON i.ID = m.INQUIRY_ID
                WHERE TRUNC(i.INST_DT) >= :START_DT AND TRUNC(i.INST_DT) < :END_DT
                  AND m.IS_DELETED = 0
                ORDER BY m.INQUIRY_ID, m.SENT_DT, m.ID",
                r => new
                {
                    inquiryId  = Convert.ToInt64(r["INQUIRY_ID"]),
                    senderType = r["SENDER_TYPE"]?.ToString() ?? "",
                    senderName = r["SENDER_NAME"]?.ToString(),
                    msgType    = r["MSG_TYPE"]?.ToString() ?? "TEXT",
                    content    = r["CONTENT"]?.ToString(),
                    sentDt     = r["SENT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["SENT_DT"])
                },
                new OracleParameter("START_DT", startDt.Date),
                new OracleParameter("END_DT",   endDt.Date));

            return Ok(new { success = true, data = rows });
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
        static bool HasCol(OracleDataReader rd, string name)
        {
            for (int i = 0; i < rd.FieldCount; i++)
                if (rd.GetName(i).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

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
            DeptName     = HasCol(r, "DEPT_NAME") ? S(r["DEPT_NAME"]) : null,
            LineName     = HasCol(r, "LINE_NAME") ? S(r["LINE_NAME"]) : null,
            WorkName     = HasCol(r, "WORK_NAME") ? S(r["WORK_NAME"]) : null,
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
            Rating       = N(r["RATING"]),
            DraftPending = HasCol(r, "DRAFT_PENDING") ? I(r["DRAFT_PENDING"]) : 0
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




    // ═════════════════════════════════════════════════════════════════════════
    // DASHBOARD hộp thư (Phase 5 — yêu cầu khách #7): người dùng hoạt động, thiết bị, lượt liên hệ,
    // thời gian giải quyết, đúng/quá hạn, case lặp lại.
    // GET /apiHR/Inquiry/dashboard?from=YYYY-MM-DD&to=YYYY-MM-DD   (mặc định 30 ngày gần nhất)
    // CHÍNH SÁCH ẨN DANH: hội thoại ẩn danh chỉ được ĐẾM (số hội thoại / số tin); KHÔNG đếm người, KHÔNG thống kê thiết bị,
    // KHÔNG tính "case lặp lại" — nên các chỉ số về NGƯỜI (người dùng hoạt động, thiết bị, lặp lại) chỉ dựa trên hội thoại công khai.
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(string? from = null, string? to = null)
    {
        try
        {
            DateTime endDay   = DateTime.TryParse(to,   out var e) ? e.Date : DateTime.Today;
            DateTime startDay = DateTime.TryParse(from, out var s) ? s.Date : endDay.AddDays(-29);
            if (startDay > endDay) (startDay, endDay) = (endDay, startDay);
            if ((endDay - startDay).TotalDays > 400) startDay = endDay.AddDays(-400);
            DateTime endEx = endDay.AddDays(1);

            OracleParameter[] P() => new[] { new OracleParameter("START_DT", startDay), new OracleParameter("END_DT", endEx) };
            static int I(object v) => v == DBNull.Value ? 0 : Convert.ToInt32(v);
            static double? D(object v) => v == DBNull.Value ? null : (double?)Math.Round(Convert.ToDouble(v), 1);

            // 1) Tổng quan
            var overview = (await _db.ExecuteQueryAsync(@"
                SELECT
                  (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE INST_DT >= :START_DT AND INST_DT < :END_DT) AS NEW_CONV,
                  (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE INST_DT >= :START_DT AND INST_DT < :END_DT AND CHAT_TYPE = 'DIRECT') AS NEW_DIRECT,
                  (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE INST_DT >= :START_DT AND INST_DT < :END_DT AND CHAT_TYPE = 'ANON') AS NEW_ANON,
                  (SELECT COUNT(*) FROM HRMS.HR_INQUIRY_MSG WHERE SENDER_TYPE = 'EMP' AND IS_DELETED = 0 AND SENT_DT >= :START_DT AND SENT_DT < :END_DT) AS EMP_MSGS,
                  (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE STATUS = 'OPEN') AS OPEN_NOW,
                  (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE STATUS = 'OPEN' AND DEADLINE_DT IS NOT NULL AND DEADLINE_DT < TRUNC(SYSDATE)) AS OVERDUE_NOW
                FROM DUAL",
                r => new
                {
                    newConv = I(r["NEW_CONV"]), newDirect = I(r["NEW_DIRECT"]), newAnon = I(r["NEW_ANON"]),
                    empMsgs = I(r["EMP_MSGS"]), openNow = I(r["OPEN_NOW"]), overdueNow = I(r["OVERDUE_NOW"])
                }, P())).First();

            // 2) Người dùng hoạt động (công khai): tạo hội thoại HOẶC nhắn tin trong kỳ
            const string activeSql = @"
                SELECT i.EMPCD FROM HRMS.HR_INQUIRY i
                 WHERE i.CHAT_TYPE = 'DIRECT' AND i.EMPCD IS NOT NULL AND i.INST_DT >= :START_DT AND i.INST_DT < :END_DT
                UNION
                SELECT i.EMPCD FROM HRMS.HR_INQUIRY_MSG m JOIN HRMS.HR_INQUIRY i ON i.ID = m.INQUIRY_ID
                 WHERE i.CHAT_TYPE = 'DIRECT' AND i.EMPCD IS NOT NULL AND m.SENDER_TYPE = 'EMP' AND m.IS_DELETED = 0
                   AND m.SENT_DT >= :START_DT AND m.SENT_DT < :END_DT";
            int activeUsers = (await _db.ExecuteQueryAsync($"SELECT COUNT(*) AS C FROM ({activeSql})", r => I(r["C"]), P())).First();

            // 3) Loại thiết bị của người dùng hoạt động (lần đăng nhập gần nhất) — chỉ hội thoại công khai
            var devices = await _db.ExecuteQueryAsync($@"
                SELECT NVL(u.LAST_DEVICE_TYPE, 'UNKNOWN') AS DEV, COUNT(*) AS C
                FROM ({activeSql}) a LEFT JOIN HRMS.HR_USERS u ON u.EMPCD = a.EMPCD
                GROUP BY NVL(u.LAST_DEVICE_TYPE, 'UNKNOWN') ORDER BY COUNT(*) DESC",
                r => new { device = r["DEV"]?.ToString(), count = I(r["C"]) }, P());

            // 4) Thời gian giải quyết (đóng trong kỳ) + phân bố
            var resolve = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS C,
                       ROUND(AVG((CLOSED_DT - INST_DT) * 24), 1)    AS AVG_H,
                       ROUND(MEDIAN((CLOSED_DT - INST_DT) * 24), 1) AS MED_H,
                       SUM(CASE WHEN (CLOSED_DT - INST_DT) * 24 <  1 THEN 1 ELSE 0 END) AS B1,
                       SUM(CASE WHEN (CLOSED_DT - INST_DT) * 24 >= 1  AND (CLOSED_DT - INST_DT) * 24 < 4  THEN 1 ELSE 0 END) AS B2,
                       SUM(CASE WHEN (CLOSED_DT - INST_DT) * 24 >= 4  AND (CLOSED_DT - INST_DT) * 24 < 24 THEN 1 ELSE 0 END) AS B3,
                       SUM(CASE WHEN (CLOSED_DT - INST_DT) * 24 >= 24 AND (CLOSED_DT - INST_DT) * 24 < 72 THEN 1 ELSE 0 END) AS B4,
                       SUM(CASE WHEN (CLOSED_DT - INST_DT) * 24 >= 72 THEN 1 ELSE 0 END) AS B5,
                       SUM(CASE WHEN DEADLINE_DT IS NOT NULL THEN 1 ELSE 0 END) AS WITH_DL,
                       SUM(CASE WHEN DEADLINE_DT IS NOT NULL AND TRUNC(CLOSED_DT) <= DEADLINE_DT THEN 1 ELSE 0 END) AS ON_TIME
                FROM HRMS.HR_INQUIRY
                WHERE STATUS = 'CLOSED' AND CLOSED_DT IS NOT NULL AND CLOSED_DT >= :START_DT AND CLOSED_DT < :END_DT",
                r => new
                {
                    closed = I(r["C"]), avgHours = D(r["AVG_H"]), medianHours = D(r["MED_H"]),
                    buckets = new[] { I(r["B1"]), I(r["B2"]), I(r["B3"]), I(r["B4"]), I(r["B5"]) },
                    withDeadline = I(r["WITH_DL"]), onTime = I(r["ON_TIME"])
                }, P())).First();

            // 5) Thời gian phản hồi đầu tiên (hội thoại tạo trong kỳ đã có tin trả lời)
            var firstResp = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS C, ROUND(AVG((f.FIRST_DT - i.INST_DT) * 24), 1) AS AVG_H, ROUND(MEDIAN((f.FIRST_DT - i.INST_DT) * 24), 1) AS MED_H
                FROM HRMS.HR_INQUIRY i
                JOIN (SELECT INQUIRY_ID, MIN(SENT_DT) AS FIRST_DT FROM HRMS.HR_INQUIRY_MSG
                       WHERE SENDER_TYPE = 'HR' AND IS_DELETED = 0 GROUP BY INQUIRY_ID) f ON f.INQUIRY_ID = i.ID
                WHERE i.INST_DT >= :START_DT AND i.INST_DT < :END_DT",
                r => new { answered = I(r["C"]), avgHours = D(r["AVG_H"]), medianHours = D(r["MED_H"]) }, P())).First();

            // 6) Quá hạn đang mở theo PIC
            var overdueByPic = await _db.ExecuteQueryAsync(@"
                SELECT NVL(dr.ROLE_NAME, '(chưa gán)') AS PIC, COUNT(*) AS C
                FROM HRMS.HR_INQUIRY i LEFT JOIN HRMS.HR_DEPT_ROLES dr ON dr.ID = i.PIC_DEPT_ROLE_ID
                WHERE i.STATUS = 'OPEN' AND i.DEADLINE_DT IS NOT NULL AND i.DEADLINE_DT < TRUNC(SYSDATE)
                GROUP BY NVL(dr.ROLE_NAME, '(chưa gán)') ORDER BY COUNT(*) DESC",
                r => new { pic = r["PIC"]?.ToString(), count = I(r["C"]) });

            // 7) Case lặp lại: cùng 1 nhân viên, cùng chủ đề, ≥ 2 hội thoại trong kỳ (CHỈ hội thoại công khai)
            var repeatTopics = await _db.ExecuteQueryAsync(@"
                SELECT x.TOPIC_CD, t.TOPIC_NAME, COUNT(*) AS EMPS, SUM(x.C) AS CASES
                FROM (SELECT EMPCD, TOPIC_CD, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                       WHERE CHAT_TYPE = 'DIRECT' AND EMPCD IS NOT NULL AND INST_DT >= :START_DT AND INST_DT < :END_DT
                       GROUP BY EMPCD, TOPIC_CD HAVING COUNT(*) >= 2) x
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = x.TOPIC_CD
                GROUP BY x.TOPIC_CD, t.TOPIC_NAME ORDER BY SUM(x.C) DESC",
                r => new { topicCd = r["TOPIC_CD"]?.ToString(), topicName = r["TOPIC_NAME"] == DBNull.Value ? null : r["TOPIC_NAME"].ToString(), employees = I(r["EMPS"]), cases = I(r["CASES"]) }, P());

            // 8) Theo chủ đề / loại báo cáo / mức độ
            var byTopic = await _db.ExecuteQueryAsync(@"
                SELECT * FROM (
                    SELECT i.TOPIC_CD, t.TOPIC_NAME, t.COLOR, COUNT(*) AS C
                    FROM HRMS.HR_INQUIRY i LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                    WHERE i.INST_DT >= :START_DT AND i.INST_DT < :END_DT
                    GROUP BY i.TOPIC_CD, t.TOPIC_NAME, t.COLOR ORDER BY COUNT(*) DESC) WHERE ROWNUM <= 10",
                r => new { topicCd = r["TOPIC_CD"]?.ToString(), topicName = r["TOPIC_NAME"] == DBNull.Value ? null : r["TOPIC_NAME"].ToString(),
                           color = r["COLOR"] == DBNull.Value ? null : r["COLOR"].ToString(), count = I(r["C"]) }, P());

            var byType = await _db.ExecuteQueryAsync(@"
                SELECT NVL(REPORT_TYPE, 'NONE') AS K, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                WHERE INST_DT >= :START_DT AND INST_DT < :END_DT GROUP BY NVL(REPORT_TYPE, 'NONE')",
                r => new { key = r["K"]?.ToString(), count = I(r["C"]) }, P());

            var bySeverity = await _db.ExecuteQueryAsync(@"
                SELECT NVL(i.SEVERITY_CD, 'NONE') AS K, MAX(s.NAME_VI) AS N, COUNT(*) AS C
                FROM HRMS.HR_INQUIRY i LEFT JOIN HRMS.HR_INQUIRY_SEVERITY s ON s.SEVERITY_CD = i.SEVERITY_CD
                WHERE i.INST_DT >= :START_DT AND i.INST_DT < :END_DT GROUP BY NVL(i.SEVERITY_CD, 'NONE')",
                r => new { key = r["K"]?.ToString(), name = r["N"] == DBNull.Value ? null : r["N"].ToString(), count = I(r["C"]) }, P());

            // 9) Theo ngày
            var daily = await _db.ExecuteQueryAsync(@"
                SELECT TRUNC(INST_DT) AS D, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                WHERE INST_DT >= :START_DT AND INST_DT < :END_DT GROUP BY TRUNC(INST_DT) ORDER BY 1",
                r => new { date = Convert.ToDateTime(r["D"]).ToString("yyyy-MM-dd"), count = I(r["C"]) }, P());
            var dailyMsgs = await _db.ExecuteQueryAsync(@"
                SELECT TRUNC(SENT_DT) AS D, COUNT(*) AS C FROM HRMS.HR_INQUIRY_MSG
                WHERE SENDER_TYPE = 'EMP' AND IS_DELETED = 0 AND SENT_DT >= :START_DT AND SENT_DT < :END_DT GROUP BY TRUNC(SENT_DT) ORDER BY 1",
                r => new { date = Convert.ToDateTime(r["D"]).ToString("yyyy-MM-dd"), count = I(r["C"]) }, P());

            // 10) Dân số nhân viên + mức độ dùng My Samho (tham chiếu báo cáo WOVO của khách: Contacts / Registered on App / Active users...)
            var population = (await _db.ExecuteQueryAsync(@"
                SELECT
                  (SELECT COUNT(*) FROM HRMS.ECM100 WHERE JEAJIKGB = 'Y') AS HEADCOUNT,
                  (SELECT COUNT(*) FROM HRMS.HR_USERS u JOIN HRMS.ECM100 e ON e.EMPCD = u.EMPCD AND e.JEAJIKGB = 'Y'
                    WHERE u.IS_ACTIVE = 1 AND u.LASTED_LOGIN IS NOT NULL) AS LOGGED_IN,
                  (SELECT COUNT(*) FROM HRMS.HR_USERS u JOIN HRMS.ECM100 e ON e.EMPCD = u.EMPCD AND e.JEAJIKGB = 'Y'
                    WHERE u.IS_ACTIVE = 1 AND u.LAST_DEVICE_TYPE IN ('APP_ANDROID', 'APP_IOS', 'WEB_MOBILE')) AS ON_MOBILE,
                  (SELECT COUNT(DISTINCT t.EMPCD) FROM HRMS.HR_USER_TOKENS t JOIN HRMS.ECM100 e ON e.EMPCD = t.EMPCD AND e.JEAJIKGB = 'Y') AS APP_REGISTERED
                FROM DUAL",
                r => new { headcount = I(r["HEADCOUNT"]), loggedIn = I(r["LOGGED_IN"]), onMobile = I(r["ON_MOBILE"]), appRegistered = I(r["APP_REGISTERED"]) })).First();

            // 10a) Nhân viên MỚI VÀO LÀM trong kỳ (user chốt 2026-11-09: người mới vào làm thì mới tải My Samho) + nhân viên NGHỈ VIỆC trong kỳ.
            //   • Ngày vào làm = ECM100.IGENTDAT (VARCHAR2 yyyyMMdd) — đúng cột AccountController dùng cho "ngày vào làm", KHÔNG dùng IN_DATE (sai).
            //   • Nghỉ việc = ECM100.RETDAT trong kỳ — đúng điều kiện báo cáo nghỉ việc của ERP (user gửi 2026-11-09: A.RETDAT BETWEEN ngày-từ AND ngày-đến,
            //     join EAM410 theo DEPTCD/LINECD/WORKCD). KHÔNG dùng ngày khóa tài khoản My Samho (UPDT_DT) vì đợt đồng bộ khóa hàng loạt 27/10 gom cả
            //     người nghỉ từ trước.  "Đã khóa" = số người trong nhóm đó đã bị IS_ACTIVE = 0 ở My Samho.
            var acct = (await _db.ExecuteQueryAsync(@"
                SELECT
                  (SELECT COUNT(*) FROM HRMS.ECM100 e WHERE e.IGENTDAT >= :S8 AND e.IGENTDAT <= :E8) AS HIRED,
                  (SELECT COUNT(*) FROM HRMS.ECM100 e WHERE e.IGENTDAT >= :S8 AND e.IGENTDAT <= :E8
                      AND EXISTS (SELECT 1 FROM HRMS.HR_USERS u WHERE u.EMPCD = e.EMPCD AND u.LASTED_LOGIN IS NOT NULL)) AS HIRED_USED,
                  (SELECT COUNT(DISTINCT A.EMPCD) FROM HRMS.ECM100 A, HRMS.EAM410 B
                    WHERE A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD AND A.WORKCD = B.WORKCD
                      AND A.RETDAT BETWEEN :S8 AND :E8) AS RESIGNED,
                  (SELECT COUNT(DISTINCT A.EMPCD) FROM HRMS.ECM100 A, HRMS.EAM410 B
                    WHERE A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD AND A.WORKCD = B.WORKCD
                      AND A.RETDAT BETWEEN :S8 AND :E8
                      AND EXISTS (SELECT 1 FROM HRMS.HR_USERS u WHERE u.EMPCD = A.EMPCD AND u.IS_ACTIVE = 0)) AS RESIGNED_LOCKED
                FROM DUAL",
                r => new { hired = I(r["HIRED"]), hiredUsed = I(r["HIRED_USED"]), resigned = I(r["RESIGNED"]), resignedLocked = I(r["RESIGNED_LOCKED"]) },
                new[] { new OracleParameter("S8", startDay.ToString("yyyyMMdd")), new OracleParameter("E8", endDay.ToString("yyyyMMdd")) })).First();

            // 10b) Giới tính người liên hệ (công khai) + toàn bộ nhân viên để so sánh
            var activeBySex = await _db.ExecuteQueryAsync($@"
                SELECT NVL(ec.SEXGB, '?') AS SX, COUNT(*) AS N
                FROM ({activeSql}) a LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = a.EMPCD GROUP BY NVL(ec.SEXGB, '?')",
                r => new { sex = r["SX"]?.ToString(), count = I(r["N"]) }, P());
            var headBySex = await _db.ExecuteQueryAsync(@"
                SELECT NVL(SEXGB, '?') AS SX, COUNT(*) AS N FROM HRMS.ECM100 WHERE JEAJIKGB = 'Y' GROUP BY NVL(SEXGB, '?')",
                r => new { sex = r["SX"]?.ToString(), count = I(r["N"]) });

            // 10c) Theo tháng: nhận / đã giải quyết / còn tồn cuối tháng (yêu cầu #7 của khách: số kiến nghị nhận mỗi tháng, đã xử lý, còn tồn)
            var recvM = await _db.ExecuteQueryAsync(@"
                SELECT TO_CHAR(INST_DT, 'YYYY-MM') AS B, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                WHERE INST_DT >= :START_DT AND INST_DT < :END_DT GROUP BY TO_CHAR(INST_DT, 'YYYY-MM')",
                r => new { bucket = r["B"]?.ToString(), count = I(r["C"]) }, P());
            var doneM = await _db.ExecuteQueryAsync(@"
                SELECT TO_CHAR(CLOSED_DT, 'YYYY-MM') AS B, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                WHERE STATUS = 'CLOSED' AND CLOSED_DT >= :START_DT AND CLOSED_DT < :END_DT GROUP BY TO_CHAR(CLOSED_DT, 'YYYY-MM')",
                r => new { bucket = r["B"]?.ToString(), count = I(r["C"]) }, P());
            int openAtStart = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS C FROM HRMS.HR_INQUIRY WHERE INST_DT < :START_DT AND (CLOSED_DT IS NULL OR CLOSED_DT >= :START_DT)",
                r => I(r["C"]), new OracleParameter("START_DT", startDay))).First();

            // 11) Hội thoại tạo trong kỳ: số tin TB / hội thoại + trạng thái (Mở chưa ai nhận / Đang xử lý / Đã đóng)
            var caseStats = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS C, ROUND(AVG(MSG_COUNT), 1) AS AVG_MSG,
                       SUM(CASE WHEN STATUS = 'OPEN' AND ASSIGNED_TO IS NULL THEN 1 ELSE 0 END) AS ST_OPEN,
                       SUM(CASE WHEN STATUS = 'OPEN' AND ASSIGNED_TO IS NOT NULL THEN 1 ELSE 0 END) AS ST_PROGRESS,
                       SUM(CASE WHEN STATUS = 'CLOSED' THEN 1 ELSE 0 END) AS ST_RESOLVED
                FROM HRMS.HR_INQUIRY WHERE INST_DT >= :START_DT AND INST_DT < :END_DT",
                r => new { cases = I(r["C"]), avgMsgPerCase = D(r["AVG_MSG"]), open = I(r["ST_OPEN"]), inProgress = I(r["ST_PROGRESS"]), resolved = I(r["ST_RESOLVED"]) }, P())).First();

            // 12) Chủ đề nhiều nhất / ít nhất (chỉ chủ đề có phát sinh trong kỳ)
            var topicAll = await _db.ExecuteQueryAsync(@"
                SELECT i.TOPIC_CD, t.TOPIC_NAME, COUNT(*) AS C
                FROM HRMS.HR_INQUIRY i LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE i.INST_DT >= :START_DT AND i.INST_DT < :END_DT
                GROUP BY i.TOPIC_CD, t.TOPIC_NAME ORDER BY COUNT(*) DESC, i.TOPIC_CD",
                r => new { topicCd = r["TOPIC_CD"]?.ToString(), topicName = r["TOPIC_NAME"] == DBNull.Value ? null : r["TOPIC_NAME"].ToString(), count = I(r["C"]) }, P());

            // 13) Chuỗi theo thời gian: kỳ ≤ 31 ngày theo NGÀY, ≤ 150 ngày theo TUẦN, dài hơn theo THÁNG — loại báo cáo, top 5 chủ đề, người liên hệ mới
            // Luôn theo NGÀY để trục thời gian của biểu đồ khớp đúng từ ngày → đến ngày của bộ lọc
            const string gran = "day";
            const string bucketExpr = "TO_CHAR(TRUNC(INST_DT), 'YYYY-MM-DD')";
            var seriesType = await _db.ExecuteQueryAsync($@"
                SELECT {bucketExpr} AS B, NVL(REPORT_TYPE, 'NONE') AS K, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                WHERE INST_DT >= :START_DT AND INST_DT < :END_DT GROUP BY {bucketExpr}, NVL(REPORT_TYPE, 'NONE') ORDER BY 1",
                r => new { bucket = r["B"]?.ToString(), key = r["K"]?.ToString(), count = I(r["C"]) }, P());
            var top5 = topicAll.Take(5).Select(x => x.topicCd).ToList();
            var seriesTopic = top5.Count == 0 ? new List<dynamic>() : (await _db.ExecuteQueryAsync($@"
                SELECT {bucketExpr} AS B, TOPIC_CD AS K, COUNT(*) AS C FROM HRMS.HR_INQUIRY
                WHERE INST_DT >= :START_DT AND INST_DT < :END_DT AND TOPIC_CD IN ({string.Join(",", top5.Select((_, k) => ":T" + k))})
                GROUP BY {bucketExpr}, TOPIC_CD ORDER BY 1",
                r => (dynamic)new { bucket = r["B"]?.ToString(), topicCd = r["K"]?.ToString(), count = I(r["C"]) },
                P().Concat(top5.Select((t, k) => new OracleParameter("T" + k, t))).ToArray()));
            // Người liên hệ MỚI trong kỳ theo bucket (mỗi nhân viên tính ở lần liên hệ đầu tiên trong kỳ) → web cộng dồn thành đường "Số người liên hệ"
            const string firstBucket = "TO_CHAR(TRUNC(F), 'YYYY-MM-DD')";
            var newContacts = await _db.ExecuteQueryAsync($@"
                SELECT {firstBucket} AS B, COUNT(*) AS C FROM (
                    SELECT EMPCD, MIN(INST_DT) AS F FROM HRMS.HR_INQUIRY
                    WHERE CHAT_TYPE = 'DIRECT' AND EMPCD IS NOT NULL AND INST_DT >= :START_DT AND INST_DT < :END_DT GROUP BY EMPCD)
                GROUP BY {firstBucket} ORDER BY 1",
                r => new { bucket = r["B"]?.ToString(), count = I(r["C"]) }, P());

            return Ok(new
            {
                success = true, from = startDay.ToString("yyyy-MM-dd"), to = endDay.ToString("yyyy-MM-dd"),
                population, accounts = acct, caseStats, activeBySex, headBySex,
                monthly = new { openAtStart, received = recvM, resolved = doneM },
                topBottom = new { top = topicAll.Take(3), bottom = topicAll.AsEnumerable().Reverse().Take(3) },
                series = new { granularity = gran, byType = seriesType, byTopic = seriesTopic, topTopics = topicAll.Take(5), newContacts },
                overview, activeUsers, devices, resolve, firstResponse = firstResp, overdueByPic,
                repeat = new { employees = repeatTopics.Sum(x => x.employees), cases = repeatTopics.Sum(x => x.cases), topics = repeatTopics },
                byTopic, byType, bySeverity, daily, dailyMsgs
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }


    // GET /apiHR/Inquiry/dashboard/repeat?from&to — chi tiết "người lặp lại": cùng nhân viên, cùng chủ đề, từ 2 hội thoại trong kỳ.
    // CHỈ hội thoại công khai (ẩn danh không nối với người). Tóm tắt theo giới tính (so với toàn bộ nhân viên) + loại báo cáo, kèm danh sách.
    [HttpGet("dashboard/repeat")]
    public async Task<IActionResult> DashboardRepeat(string? from = null, string? to = null)
    {
        try
        {
            DateTime endDay   = DateTime.TryParse(to,   out var e) ? e.Date : DateTime.Today;
            DateTime startDay = DateTime.TryParse(from, out var s) ? s.Date : endDay.AddDays(-29);
            if (startDay > endDay) (startDay, endDay) = (endDay, startDay);
            if ((endDay - startDay).TotalDays > 400) startDay = endDay.AddDays(-400);
            OracleParameter[] P() => new[] { new OracleParameter("START_DT", startDay), new OracleParameter("END_DT", endDay.AddDays(1)) };
            static int I(object v) => v == DBNull.Value ? 0 : Convert.ToInt32(v);

            const string pairs = @"
                SELECT EMPCD, TOPIC_CD, COUNT(*) AS C, MAX(INST_DT) AS LAST_DT, MAX(EMP_NAME) AS EMP_NAME,
                       SUM(CASE WHEN REPORT_TYPE = 'GRIEVANCE' THEN 1 ELSE 0 END) AS NG,
                       SUM(CASE WHEN REPORT_TYPE = 'OPINION'   THEN 1 ELSE 0 END) AS NO_,
                       SUM(CASE WHEN REPORT_TYPE IS NULL       THEN 1 ELSE 0 END) AS NN
                FROM HRMS.HR_INQUIRY
                WHERE CHAT_TYPE = 'DIRECT' AND EMPCD IS NOT NULL AND INST_DT >= :START_DT AND INST_DT < :END_DT
                GROUP BY EMPCD, TOPIC_CD HAVING COUNT(*) >= 2";

            var types = (await _db.ExecuteQueryAsync($@"
                SELECT COUNT(DISTINCT EMPCD) AS PEOPLE, NVL(SUM(C), 0) AS CASES, NVL(SUM(NG), 0) AS NG, NVL(SUM(NO_), 0) AS NOP, NVL(SUM(NN), 0) AS NN FROM ({pairs})",
                r => new { people = I(r["PEOPLE"]), cases = I(r["CASES"]), grievance = I(r["NG"]), opinion = I(r["NOP"]), unclassified = I(r["NN"]) }, P())).First();

            var bySex = await _db.ExecuteQueryAsync($@"
                SELECT NVL(ec.SEXGB, '?') AS SX, COUNT(DISTINCT x.EMPCD) AS PEOPLE, SUM(x.C) AS CASES
                FROM ({pairs}) x LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = x.EMPCD GROUP BY NVL(ec.SEXGB, '?')",
                r => new { sex = r["SX"]?.ToString(), people = I(r["PEOPLE"]), cases = I(r["CASES"]) }, P());

            var baseline = await _db.ExecuteQueryAsync(@"
                SELECT NVL(SEXGB, '?') AS SX, COUNT(*) AS N FROM HRMS.ECM100 WHERE JEAJIKGB = 'Y' GROUP BY NVL(SEXGB, '?')",
                r => new { sex = r["SX"]?.ToString(), count = I(r["N"]) });

            var rows = await _db.ExecuteQueryAsync($@"
                SELECT y.*, ec.SEXGB,
                       (SELECT B.DEPTNM FROM HRMS.EAM410 B WHERE B.DEPTCD = ec.DEPTCD AND B.LINECD = ec.LINECD AND B.WORKCD = ec.WORKCD AND ROWNUM = 1) AS DEPT_NAME,
                       t.TOPIC_NAME
                FROM (SELECT * FROM ({pairs}) ORDER BY C DESC, EMPCD) y
                LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = y.EMPCD
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = y.TOPIC_CD
                WHERE ROWNUM <= 300",
                r => new
                {
                    empCd = r["EMPCD"]?.ToString(), empName = r["EMP_NAME"] == DBNull.Value ? null : r["EMP_NAME"].ToString(),
                    sex = r["SEXGB"] == DBNull.Value ? null : r["SEXGB"].ToString(),
                    dept = r["DEPT_NAME"] == DBNull.Value ? null : r["DEPT_NAME"].ToString(),
                    topicCd = r["TOPIC_CD"]?.ToString(), topicName = r["TOPIC_NAME"] == DBNull.Value ? null : r["TOPIC_NAME"].ToString(),
                    cases = I(r["C"]), lastDt = Convert.ToDateTime(r["LAST_DT"]),
                    grievance = I(r["NG"]), opinion = I(r["NO_"]), unclassified = I(r["NN"])
                }, P());

            return Ok(new { success = true, from = startDay.ToString("yyyy-MM-dd"), to = endDay.ToString("yyyy-MM-dd"), summary = types, bySex, baseline, rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }


    // ═════════════════════════════════════════════════════════════════════════
    // PHÂN LOẠI NHANH (CSR) — hàng đợi hội thoại chưa được CSR xác nhận + gán hàng loạt. Script: Scripts/alter_inquiry_csr_confirm.sql
    // ═════════════════════════════════════════════════════════════════════════
    private static volatile bool _confirmCols;
    private static DateTime _confirmNextProbeUtc = DateTime.MinValue;
    private async Task<bool> ConfirmColsAsync()
    {
        if (_confirmCols) return true;
        if (DateTime.UtcNow < _confirmNextProbeUtc) return false;
        bool ok;
        try
        {
            var n = await _db.ExecuteQueryAsync("SELECT COUNT(*) AS C FROM ALL_TAB_COLUMNS WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_INQUIRY' AND COLUMN_NAME = 'CSR_CONFIRM_DT'",
                r => Convert.ToInt32(r["C"]));
            ok = n.FirstOrDefault() > 0;
        }
        catch { ok = false; }
        if (ok) _confirmCols = true; else _confirmNextProbeUtc = DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    // GET /apiHR/Inquiry/triage/queue?scope=open|all|done&chatType=&noPic=&topicCd=&page=&pageSize=
    [HttpGet("triage/queue")]
    public async Task<IActionResult> TriageQueue(string scope = "open", string? chatType = null, bool noPic = false, string? topicCd = null, string? from = null, string? to = null, int page = 1, int pageSize = 30)
    {
        try
        {
            if (!await ConfirmColsAsync())
                return Ok(new { success = false, needDdl = true, message = "Chưa chạy script alter_inquiry_csr_confirm.sql (thêm cột CSR_CONFIRM_DT/BY)" });
            if (page < 1) page = 1; if (pageSize < 1 || pageSize > 100) pageSize = 30;

            var where = new List<string>(); var ps = new List<OracleParameter>();
            if (scope == "done") where.Add("i.CSR_CONFIRM_DT IS NOT NULL");
            else if (scope == "all") where.Add("i.CSR_CONFIRM_DT IS NULL");
            else where.Add("i.STATUS = 'OPEN' AND i.CSR_CONFIRM_DT IS NULL");
            if (chatType == "ANON" || chatType == "DIRECT") { where.Add("i.CHAT_TYPE = :CT"); ps.Add(new OracleParameter("CT", chatType)); }
            if (noPic) where.Add("i.PIC_DEPT_ROLE_ID IS NULL");
            if (!string.IsNullOrWhiteSpace(topicCd)) { where.Add("i.TOPIC_CD = :TC"); ps.Add(new OracleParameter("TC", topicCd.Trim())); }
            // Lọc theo NGÀY TẠO hội thoại (từ ngày → đến ngày, gồm cả hai đầu); để trống = không giới hạn
            if (DateTime.TryParse(from, out var fromDt)) { where.Add("i.INST_DT >= :D_FROM"); ps.Add(new OracleParameter("D_FROM", OracleDbType.Date) { Value = fromDt.Date }); }
            if (DateTime.TryParse(to, out var toDt))     { where.Add("i.INST_DT < :D_TO");    ps.Add(new OracleParameter("D_TO", OracleDbType.Date) { Value = toDt.Date.AddDays(1) }); }
            string whereSql = string.Join(" AND ", where);
            string order = scope == "done" ? "i.CSR_CONFIRM_DT DESC, i.ID DESC"
                : "CASE i.SEVERITY_CD WHEN 'ZT' THEN 0 WHEN 'HIGH' THEN 1 ELSE 2 END, i.INST_DT ASC, i.ID ASC";

            int total = (await _db.ExecuteQueryAsync($"SELECT COUNT(*) AS C FROM HRMS.HR_INQUIRY i WHERE {whereSql}", r => Convert.ToInt32(r["C"]), ps.ToArray())).First();

            var qps = ps.Select(x => new OracleParameter(x.ParameterName, x.OracleDbType, x.Value, System.Data.ParameterDirection.Input)).ToList();
            qps.Add(new OracleParameter("MAXN", page * pageSize)); qps.Add(new OracleParameter("OFFSET", (page - 1) * pageSize));
            var rows = await _db.ExecuteQueryAsync($@"
                SELECT * FROM (
                    SELECT q.*, ROWNUM AS RN FROM (
                        SELECT i.ID, i.INQUIRY_NO, i.CHAT_TYPE, i.TOPIC_CD, t.TOPIC_NAME, i.REPORT_TYPE, i.SEVERITY_CD, i.PIC_DEPT_ROLE_ID,
                               dr.ROLE_NAME AS PIC_NAME, i.DEADLINE_DT, i.INST_DT, i.STATUS, i.MSG_COUNT, i.ANON_DISPLAY, i.EMP_NAME,
                               i.CSR_CONFIRM_DT, i.SUBJECT, fm.CONTENT AS FIRST_MSG
                        FROM HRMS.HR_INQUIRY i
                        LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                        LEFT JOIN HRMS.HR_DEPT_ROLES dr ON dr.ID = i.PIC_DEPT_ROLE_ID
                        LEFT JOIN (
                            SELECT INQUIRY_ID, CONTENT FROM (
                                SELECT INQUIRY_ID, CONTENT, ROW_NUMBER() OVER (PARTITION BY INQUIRY_ID ORDER BY SENT_DT ASC) RN
                                FROM HRMS.HR_INQUIRY_MSG WHERE SENDER_TYPE = 'EMP' AND IS_DELETED = 0
                            ) WHERE RN = 1
                        ) fm ON fm.INQUIRY_ID = i.ID
                        WHERE {whereSql}
                        ORDER BY {order}
                    ) q WHERE ROWNUM <= :MAXN
                ) WHERE RN > :OFFSET",
                r => new
                {
                    id = Convert.ToInt64(r["ID"]), inquiryNo = r["INQUIRY_NO"]?.ToString(), chatType = r["CHAT_TYPE"]?.ToString(),
                    topicCd = r["TOPIC_CD"]?.ToString(), topicName = r["TOPIC_NAME"] == DBNull.Value ? null : r["TOPIC_NAME"].ToString(),
                    reportType = r["REPORT_TYPE"] == DBNull.Value ? null : r["REPORT_TYPE"].ToString(),
                    severityCd = r["SEVERITY_CD"] == DBNull.Value ? null : r["SEVERITY_CD"].ToString(),
                    picDeptRoleId = r["PIC_DEPT_ROLE_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PIC_DEPT_ROLE_ID"]),
                    picName = r["PIC_NAME"] == DBNull.Value ? null : r["PIC_NAME"].ToString(),
                    deadlineDt = r["DEADLINE_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["DEADLINE_DT"]),
                    instDt = Convert.ToDateTime(r["INST_DT"]), status = r["STATUS"]?.ToString(), msgCount = r["MSG_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["MSG_COUNT"]),
                    sender = r["CHAT_TYPE"]?.ToString() == "ANON" ? (r["ANON_DISPLAY"] == DBNull.Value ? "Ẩn danh" : r["ANON_DISPLAY"].ToString()) : (r["EMP_NAME"] == DBNull.Value ? null : r["EMP_NAME"].ToString()),
                    confirmDt = r["CSR_CONFIRM_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CSR_CONFIRM_DT"]),
                    subject = r["SUBJECT"] == DBNull.Value ? null : r["SUBJECT"].ToString(),
                    firstMsg = r["FIRST_MSG"] == DBNull.Value ? null : r["FIRST_MSG"].ToString()
                }, qps.ToArray());

            // Chủ đề + mức độ + PIC + số ngày hạn (để dựng gợi ý)
            var topics = (await _db.ExecuteQueryAsync(@"
                SELECT T.TOPIC_CD, T.TOPIC_NAME, T.SEVERITY_CD, T.PIC_DEPT_ROLE_ID, DR.ROLE_NAME AS PIC_NAME, S.RESPONSE_DAYS, S.NAME_VI AS SEV_NAME
                FROM HRMS.HR_INQUIRY_TOPIC T
                LEFT JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = T.PIC_DEPT_ROLE_ID
                LEFT JOIN HRMS.HR_INQUIRY_SEVERITY S ON S.SEVERITY_CD = T.SEVERITY_CD",
                r => new
                {
                    cd = r["TOPIC_CD"]?.ToString() ?? "", name = r["TOPIC_NAME"]?.ToString(), sev = r["SEVERITY_CD"]?.ToString(), sevName = r["SEV_NAME"] == DBNull.Value ? null : r["SEV_NAME"].ToString(),
                    pic = r["PIC_DEPT_ROLE_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PIC_DEPT_ROLE_ID"]), picName = r["PIC_NAME"] == DBNull.Value ? null : r["PIC_NAME"].ToString(),
                    days = r["RESPONSE_DAYS"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["RESPONSE_DAYS"])
                })).ToDictionary(x => x.cd, StringComparer.OrdinalIgnoreCase);

            static string? Snip(string? html) { if (html == null) return null; var t = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "); t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim(); return t.Length > 320 ? t[..320] + "…" : t; }
            var data = rows.Select(x =>
            {
                var sg = HR_api.Helpers.InquiryTopicSuggester.Suggest((x.subject ?? "") + " " + (x.firstMsg ?? ""), x.topicCd);
                topics.TryGetValue(sg.TopicCd, out var ti);
                return new
                {
                    x.id, x.inquiryNo, x.chatType, x.topicCd, x.topicName, x.reportType, x.severityCd, x.picDeptRoleId, x.picName, x.deadlineDt, x.instDt, x.status, x.msgCount, x.sender, x.confirmDt, x.subject,
                    firstMsg = Snip(x.firstMsg),
                    suggest = new
                    {
                        topicCd = sg.TopicCd, topicName = ti?.name, severityCd = ti?.sev, severityName = ti?.sevName, picDeptRoleId = ti?.pic, picName = ti?.picName, responseDays = ti?.days,
                        confidence = sg.Confidence, reason = sg.Reason
                    }
                };
            }).ToList();

            var counts = (await _db.ExecuteQueryAsync(@"
                SELECT (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE STATUS = 'OPEN' AND CSR_CONFIRM_DT IS NULL) AS PENDING_OPEN,
                       (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE CSR_CONFIRM_DT IS NULL) AS PENDING_ALL,
                       (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE CSR_CONFIRM_DT >= TRUNC(SYSDATE)) AS DONE_TODAY
                FROM DUAL",
                r => new { pendingOpen = Convert.ToInt32(r["PENDING_OPEN"]), pendingAll = Convert.ToInt32(r["PENDING_ALL"]), doneToday = Convert.ToInt32(r["DONE_TODAY"]) })).First();

            return Ok(new { success = true, total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize), counts, data });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Inquiry/triage/confirm — xác nhận phân loại cho 1 hoặc nhiều hội thoại (null = giữ nguyên field đó)
    [HttpPost("triage/confirm")]
    public async Task<IActionResult> TriageConfirm([FromBody] TriageConfirmRequest req)
    {
        try
        {
            if (!await ConfirmColsAsync()) return Ok(new { success = false, needDdl = true, message = "Chưa chạy script alter_inquiry_csr_confirm.sql" });
            var ids = (req.Ids ?? new()).Where(x => x > 0).Distinct().ToList();
            if (ids.Count == 0) return Ok(new { success = false, message = "Chưa chọn hội thoại" });
            if (ids.Count > 200) return Ok(new { success = false, message = "Tối đa 200 hội thoại mỗi lần" });

            string? rt = string.IsNullOrWhiteSpace(req.ReportType) ? null : req.ReportType.Trim().ToUpperInvariant();
            if (rt != null && rt != "GRIEVANCE" && rt != "OPINION") return Ok(new { success = false, message = "Loại báo cáo không hợp lệ" });
            string? tc = string.IsNullOrWhiteSpace(req.TopicCd) ? null : req.TopicCd.Trim();
            if (tc != null && (await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_INQUIRY_TOPIC WHERE TOPIC_CD = :C AND ROWNUM = 1", r => 1, new OracleParameter("C", tc))).Count == 0)
                return Ok(new { success = false, message = "Chủ đề không tồn tại" });
            string? sev = string.IsNullOrWhiteSpace(req.SeverityCd) ? null : req.SeverityCd.Trim().ToUpperInvariant();
            var err = await ValidateSeverityPicAsync(sev, req.PicDeptRoleId);
            if (err != null) return Ok(new { success = false, message = err });
            string mode = req.DeadlineMode is "created" or "today" ? req.DeadlineMode : "keep";
            bool setPic = req.SetPic && req.PicDeptRoleId.HasValue;

            int ok = 0; var failed = new List<object>();
            foreach (var id in ids)
            {
                try
                {
                    int n = await _db.ExecuteNonQueryAsync(@"
                        UPDATE HRMS.HR_INQUIRY SET
                            REPORT_TYPE      = NVL(:RT, REPORT_TYPE),
                            TOPIC_CD         = NVL(:TC, TOPIC_CD),
                            SEVERITY_CD      = NVL(:SEV, SEVERITY_CD),
                            PIC_DEPT_ROLE_ID = CASE WHEN :PIC_SET = 1 THEN :PIC ELSE PIC_DEPT_ROLE_ID END,
                            DEADLINE_DT      = CASE :DL_MODE
                                                 WHEN 'created' THEN NVL(TRUNC(INST_DT) + (SELECT S.RESPONSE_DAYS FROM HRMS.HR_INQUIRY_SEVERITY S WHERE S.SEVERITY_CD = NVL(:SEV, SEVERITY_CD)), DEADLINE_DT)
                                                 WHEN 'today'   THEN NVL(TRUNC(SYSDATE) + (SELECT S.RESPONSE_DAYS FROM HRMS.HR_INQUIRY_SEVERITY S WHERE S.SEVERITY_CD = NVL(:SEV, SEVERITY_CD)), DEADLINE_DT)
                                                 ELSE DEADLINE_DT END,
                            CSR_CONFIRM_DT = SYSDATE, CSR_CONFIRM_BY = :UPDT, UPDT_ID = :UPDT, UPDT_DT = SYSDATE
                        WHERE ID = :ID",
                        new OracleParameter("RT",  OracleDbType.Varchar2) { Value = (object?)rt ?? DBNull.Value },
                        new OracleParameter("TC",  OracleDbType.Varchar2) { Value = (object?)tc ?? DBNull.Value },
                        new OracleParameter("SEV", OracleDbType.Varchar2) { Value = (object?)sev ?? DBNull.Value },
                        new OracleParameter("PIC_SET", OracleDbType.Int32) { Value = setPic ? 1 : 0 },
                        new OracleParameter("PIC", OracleDbType.Int32) { Value = setPic ? req.PicDeptRoleId!.Value : DBNull.Value },
                        new OracleParameter("DL_MODE", OracleDbType.Varchar2) { Value = mode },
                        new OracleParameter("UPDT", OracleDbType.Varchar2) { Value = (object?)req.UpdtId ?? DBNull.Value },
                        new OracleParameter("ID", OracleDbType.Int64) { Value = id });
                    if (n > 0) ok++; else failed.Add(new { id, message = "Không tìm thấy hội thoại" });
                }
                catch (OracleException ex) when (ex.Number == 1)
                {
                    failed.Add(new { id, message = "Người gửi đã có hội thoại đang mở ở chủ đề này — đóng hội thoại kia trước" });
                }
            }
            return Ok(new { success = ok > 0 || failed.Count == 0, confirmed = ok, failed });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ── Email nhắc hạn phản hồi (Phase 2) ─────────────────────────────────────────────────────
    // GET /apiHR/Inquiry/admin/reminder/preview — ai sẽ nhận email nào (KHÔNG gửi, email che bớt)
    [HttpGet("admin/reminder/preview")]
    public async Task<IActionResult> ReminderPreview()
    {
        try
        {
            var svc = HttpContext.RequestServices.GetRequiredService<HR_api.Services.InquiryReminderService>();
            return Ok(new { success = true, data = await svc.PreviewAsync() });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Inquiry/admin/reminder/send-test { email } — gửi thử 1 email tổng hợp tới đúng địa chỉ đó (không ghi nhật ký)
    [HttpPost("admin/reminder/send-test")]
    public async Task<IActionResult> ReminderSendTest([FromBody] ReminderTestRequest req)
    {
        try
        {
            var mail = (req?.Email ?? "").Trim();
            if (!mail.Contains('@') || mail.Contains(' ') || mail.Length > 200) return Ok(new { success = false, message = "Email không hợp lệ" });
            var svc = HttpContext.RequestServices.GetRequiredService<HR_api.Services.InquiryReminderService>();
            int n = await svc.SendTestAsync(mail);
            return Ok(new { success = true, cases = n });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.InnerException?.Message ?? ex.Message }); }
    }


    // GET /apiHR/Inquiry/admin/remind-to — danh sách người nhận email nhắc hạn (theo chủ đề / theo phòng PIC)
    [HttpGet("admin/remind-to")]
    public async Task<IActionResult> GetRemindTo()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync("SELECT SCOPE_TYPE, SCOPE_KEY, EMAILS FROM HRMS.HR_INQUIRY_REMIND_TO ORDER BY SCOPE_TYPE, SCOPE_KEY",
                r => new { scopeType = r["SCOPE_TYPE"]?.ToString(), scopeKey = r["SCOPE_KEY"]?.ToString(), emails = r["EMAILS"] == DBNull.Value ? "" : r["EMAILS"].ToString() });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = "Chưa có bảng người nhận email nhắc hạn (chạy create_inquiry_remind_to.sql): " + ex.Message }); }
    }

    // PUT /apiHR/Inquiry/admin/remind-to { scopeType: TOPIC|PIC, scopeKey, emails, updtId } — emails trống = xoá cấu hình
    [HttpPut("admin/remind-to")]
    public async Task<IActionResult> SaveRemindTo([FromBody] RemindToSaveRequest req)
    {
        try
        {
            var type = (req.ScopeType ?? "").Trim().ToUpperInvariant();
            var key  = (req.ScopeKey ?? "").Trim();
            if ((type != "TOPIC" && type != "PIC") || key.Length == 0 || key.Length > 40)
                return Ok(new { success = false, message = "Thiếu hoặc sai phạm vi (chủ đề/phòng)" });

            bool exists = type == "TOPIC"
                ? (await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_INQUIRY_TOPIC WHERE TOPIC_CD = :K AND ROWNUM = 1", r => 1, new OracleParameter("K", key))).Any()
                : (await _db.ExecuteQueryAsync("SELECT 1 FROM HRMS.HR_DEPT_ROLES WHERE ROLE_NAME = :K AND ROWNUM = 1", r => 1, new OracleParameter("K", key))).Any();
            if (!exists) return Ok(new { success = false, message = type == "TOPIC" ? "Chủ đề không tồn tại" : "Phòng không tồn tại" });

            var tokens = (req.Emails ?? "").Split(new[] { ';', ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var valid  = HR_api.Services.InquiryReminderService.ParseEmails(req.Emails);
            if (tokens.Select(t => t.Trim().ToLowerInvariant()).Distinct().Count() != valid.Count)
                return Ok(new { success = false, message = "Có email không hợp lệ, vui lòng kiểm tra lại" });
            if (valid.Count > 20) return Ok(new { success = false, message = "Tối đa 20 email cho mỗi mục" });

            if (valid.Count == 0)
            {
                await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_INQUIRY_REMIND_TO WHERE SCOPE_TYPE = :T AND SCOPE_KEY = :K",
                    new OracleParameter("T", type), new OracleParameter("K", key));
                return Ok(new { success = true });
            }
            var joined = string.Join(";", valid);
            int n = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_REMIND_TO SET EMAILS = :E, UPDT_ID = :U, UPDT_DT = SYSDATE WHERE SCOPE_TYPE = :T AND SCOPE_KEY = :K",
                new OracleParameter("E", OracleDbType.NVarchar2) { Value = joined }, new OracleParameter("U", (object?)req.UpdtId ?? DBNull.Value),
                new OracleParameter("T", type), new OracleParameter("K", key));
            if (n == 0)
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_INQUIRY_REMIND_TO (SCOPE_TYPE, SCOPE_KEY, EMAILS, UPDT_ID, UPDT_DT) VALUES (:T, :K, :E, :U, SYSDATE)",
                    new OracleParameter("T", type), new OracleParameter("K", key),
                    new OracleParameter("E", OracleDbType.NVarchar2) { Value = joined }, new OracleParameter("U", (object?)req.UpdtId ?? DBNull.Value));
            return Ok(new { success = true, emails = joined });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BẢN NHÁP TRẢ LỜI (HR/GA soạn → CSR duyệt/sửa → gửi cho công nhân) — bảng HR_INQUIRY_DRAFT
    // Script: Scripts/create_inquiry_draft.sql. Web điều phối: tạo nháp → claim → gửi tin thật (api send) → finalize.
    // ═════════════════════════════════════════════════════════════════════════
    private static volatile bool _draftTable;
    private static DateTime _draftNextProbeUtc = DateTime.MinValue;

    // Bảng có thể chưa tạo khi HR_api deploy trước DDL → danh sách hội thoại KHÔNG được gãy.
    private async Task<bool> DraftTableAvailableAsync()
    {
        if (_draftTable) return true;
        if (DateTime.UtcNow < _draftNextProbeUtc) return false;
        bool ok;
        try
        {
            var n = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_INQUIRY_DRAFT'",
                r => Convert.ToInt32(r["C"]));
            ok = n.FirstOrDefault() > 0;
        }
        catch { ok = false; }
        if (ok) _draftTable = true; else _draftNextProbeUtc = DateTime.UtcNow.AddSeconds(60);
        return ok;
    }

    private const string DraftCols = @"ID, INQUIRY_ID, AUTHOR_CD, AUTHOR_NAME, AUTHOR_ROLE, CONTENT, FILES_JSON, REFS_JSON, STATUS,
                       FINAL_CONTENT, REVIEWER_CD, REVIEWER_NAME, REVIEW_NOTE, FINAL_MSG_ID, CREATED_DT, REVIEWED_DT";

    private static object MapDraft(OracleDataReader r) => new
    {
        id           = Convert.ToInt64(r["ID"]),
        inquiryId    = Convert.ToInt64(r["INQUIRY_ID"]),
        authorCd     = r["AUTHOR_CD"]?.ToString(),
        authorName   = r["AUTHOR_NAME"] == DBNull.Value ? null : r["AUTHOR_NAME"].ToString(),
        authorRole   = r["AUTHOR_ROLE"] == DBNull.Value ? null : r["AUTHOR_ROLE"].ToString(),
        content      = r["CONTENT"] == DBNull.Value ? null : r["CONTENT"].ToString(),
        filesJson    = r["FILES_JSON"] == DBNull.Value ? null : r["FILES_JSON"].ToString(),
        refsJson     = r["REFS_JSON"] == DBNull.Value ? null : r["REFS_JSON"].ToString(),
        status       = r["STATUS"]?.ToString(),
        finalContent = r["FINAL_CONTENT"] == DBNull.Value ? null : r["FINAL_CONTENT"].ToString(),
        reviewerCd   = r["REVIEWER_CD"] == DBNull.Value ? null : r["REVIEWER_CD"].ToString(),
        reviewerName = r["REVIEWER_NAME"] == DBNull.Value ? null : r["REVIEWER_NAME"].ToString(),
        reviewNote   = r["REVIEW_NOTE"] == DBNull.Value ? null : r["REVIEW_NOTE"].ToString(),
        createdDt    = Convert.ToDateTime(r["CREATED_DT"]),
        reviewedDt   = r["REVIEWED_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["REVIEWED_DT"])
    };

    private async Task<object?> GetDraftByIdAsync(long draftId)
        => (await _db.ExecuteQueryAsync($"SELECT {DraftCols} FROM HRMS.HR_INQUIRY_DRAFT WHERE ID = :ID",
            r => MapDraft(r), new OracleParameter("ID", draftId))).FirstOrDefault();

    // POST /apiHR/Inquiry/draft/create
    [HttpPost("draft/create")]
    public async Task<IActionResult> CreateDraft([FromBody] InquiryDraftCreateRequest req)
    {
        try
        {
            if (!await DraftTableAvailableAsync()) return Ok(new { success = false, message = "Chức năng duyệt trả lời chưa sẵn sàng (thiếu bảng)" });
            if (req.InquiryId <= 0 || string.IsNullOrWhiteSpace(req.AuthorCd))
                return Ok(new { success = false, message = "Thiếu thông tin" });
            bool hasContent = !string.IsNullOrWhiteSpace(req.Content);
            if (!hasContent && string.IsNullOrWhiteSpace(req.FilesJson) && string.IsNullOrWhiteSpace(req.RefsJson))
                return Ok(new { success = false, message = "Nội dung không được để trống" });
            if ((req.Content?.Length ?? 0) > 4000 || (req.FilesJson?.Length ?? 0) > 4000 || (req.RefsJson?.Length ?? 0) > 2000)
                return Ok(new { success = false, message = "Nội dung hoặc đính kèm quá dài" });

            var inq = (await _db.ExecuteQueryAsync("SELECT STATUS, INQUIRY_NO FROM HRMS.HR_INQUIRY WHERE ID = :ID",
                r => new { Status = r["STATUS"]?.ToString(), No = r["INQUIRY_NO"]?.ToString() ?? "" },
                new OracleParameter("ID", req.InquiryId))).FirstOrDefault();
            if (inq == null) return Ok(new { success = false, message = "Không tìm thấy hội thoại" });
            if (inq.Status != "OPEN") return Ok(new { success = false, message = "Hội thoại đã đóng, không thể gửi tin" });

            long id = (await _db.ExecuteQueryAsync("SELECT HRMS.SEQ_HR_INQUIRY_DRAFT.NEXTVAL AS ID FROM DUAL",
                r => Convert.ToInt64(r["ID"]))).First();
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_INQUIRY_DRAFT
                    (ID, INQUIRY_ID, AUTHOR_CD, AUTHOR_NAME, AUTHOR_ROLE, CONTENT, FILES_JSON, REFS_JSON, STATUS, CREATED_DT)
                VALUES (:ID, :INQ, :CD, :NM, :RL, :CT, :FJ, :RJ, 'PENDING', SYSDATE)",
                new OracleParameter("ID",  id),
                new OracleParameter("INQ", req.InquiryId),
                new OracleParameter("CD",  req.AuthorCd.Trim()),
                new OracleParameter("NM",  OracleDbType.NVarchar2) { Value = (object?)req.AuthorName ?? DBNull.Value },
                new OracleParameter("RL",  (object?)req.AuthorRole ?? DBNull.Value),
                new OracleParameter("CT",  OracleDbType.NVarchar2) { Value = (object?)req.Content ?? DBNull.Value },
                new OracleParameter("FJ",  OracleDbType.NVarchar2) { Value = string.IsNullOrWhiteSpace(req.FilesJson) ? DBNull.Value : req.FilesJson },
                new OracleParameter("RJ",  OracleDbType.NVarchar2) { Value = string.IsNullOrWhiteSpace(req.RefsJson)  ? DBNull.Value : req.RefsJson });

            // Báo CSR bằng EMAIL gom đợt (InquiryReminderService.TryRunDraftDigestAsync) — push trong app không dùng được vì
            // Admin/CSR/HR bị chặn thông báo cá nhân (NotificationHelper.IsNoNotiRoleAsync, quy tắc 2026-09-12).

            return Ok(new { success = true, draftId = id });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/Inquiry/{id}/drafts — bản nháp còn hiệu lực (PENDING + RETURNED) của 1 hội thoại
    [HttpGet("{id:long}/drafts")]
    public async Task<IActionResult> GetDrafts(long id)
    {
        try
        {
            if (!await DraftTableAvailableAsync()) return Ok(new { success = true, data = Array.Empty<object>() });
            var rows = await _db.ExecuteQueryAsync($@"
                SELECT {DraftCols} FROM HRMS.HR_INQUIRY_DRAFT
                WHERE INQUIRY_ID = :ID AND STATUS IN ('PENDING','RETURNED')
                ORDER BY CREATED_DT, ID", r => MapDraft(r), new OracleParameter("ID", id));
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/Inquiry/draft/{draftId}
    [HttpGet("draft/{draftId:long}")]
    public async Task<IActionResult> GetDraft(long draftId)
    {
        try
        {
            if (!await DraftTableAvailableAsync()) return Ok(new { success = false, message = "Chưa có bảng bản nháp" });
            var d = await GetDraftByIdAsync(draftId);
            return d == null ? Ok(new { success = false, message = "Không tìm thấy bản nháp" }) : Ok(new { success = true, data = d });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST draft/{id}/claim — CSR "chốt" duyệt: PENDING → APPROVED nguyên tử (chống 2 CSR bấm cùng lúc), lưu bản đã sửa.
    // Web gửi tin thật rồi gọi finalize; gửi lỗi thì gọi revert.
    [HttpPost("draft/{draftId:long}/claim")]
    public async Task<IActionResult> ClaimDraft(long draftId, [FromBody] InquiryDraftReviewRequest req)
    {
        try
        {
            if ((req.Content?.Length ?? 0) > 4000) return Ok(new { success = false, message = "Nội dung vượt quá 4000 ký tự" });
            int n = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_DRAFT
                   SET STATUS = 'APPROVED', REVIEWER_CD = :RCD, REVIEWER_NAME = :RNM, REVIEWED_DT = SYSDATE,
                       FINAL_CONTENT = NVL(:FC, CONTENT)
                 WHERE ID = :ID AND STATUS = 'PENDING'",
                new OracleParameter("RCD", (object?)req.ReviewerCd ?? DBNull.Value),
                new OracleParameter("RNM", OracleDbType.NVarchar2) { Value = (object?)req.ReviewerName ?? DBNull.Value },
                new OracleParameter("FC",  OracleDbType.NVarchar2) { Value = string.IsNullOrWhiteSpace(req.Content) ? DBNull.Value : req.Content },
                new OracleParameter("ID",  draftId));
            if (n == 0) return Ok(new { success = false, message = "Bản nháp đã được xử lý (duyệt/trả lại/thu hồi) bởi người khác" });
            return Ok(new { success = true, data = await GetDraftByIdAsync(draftId) });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST draft/{id}/revert — gửi tin thật thất bại → trả về PENDING để CSR thử lại
    [HttpPost("draft/{draftId:long}/revert")]
    public async Task<IActionResult> RevertDraft(long draftId)
    {
        try
        {
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_DRAFT SET STATUS = 'PENDING', REVIEWER_CD = NULL, REVIEWER_NAME = NULL, REVIEWED_DT = NULL, FINAL_CONTENT = NULL
                 WHERE ID = :ID AND STATUS = 'APPROVED' AND FINAL_MSG_ID IS NULL", new OracleParameter("ID", draftId));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST draft/{id}/finalize { msgId }
    [HttpPost("draft/{draftId:long}/finalize")]
    public async Task<IActionResult> FinalizeDraft(long draftId, [FromBody] InquiryDraftReviewRequest req)
    {
        try
        {
            await _db.ExecuteNonQueryAsync("UPDATE HRMS.HR_INQUIRY_DRAFT SET FINAL_MSG_ID = :M WHERE ID = :ID AND STATUS = 'APPROVED'",
                new OracleParameter("M", (object?)req.MsgId ?? DBNull.Value), new OracleParameter("ID", draftId));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST draft/{id}/return { reviewerCd, reviewerName, note } — CSR trả lại kèm góp ý (bắt buộc có góp ý)
    [HttpPost("draft/{draftId:long}/return")]
    public async Task<IActionResult> ReturnDraft(long draftId, [FromBody] InquiryDraftReviewRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Note)) return Ok(new { success = false, message = "Vui lòng nhập góp ý để người soạn chỉnh lại" });
            if (req.Note.Length > 1000) return Ok(new { success = false, message = "Góp ý tối đa 1000 ký tự" });
            int n = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_DRAFT
                   SET STATUS = 'RETURNED', REVIEWER_CD = :RCD, REVIEWER_NAME = :RNM, REVIEW_NOTE = :NT, REVIEWED_DT = SYSDATE
                 WHERE ID = :ID AND STATUS = 'PENDING'",
                new OracleParameter("RCD", (object?)req.ReviewerCd ?? DBNull.Value),
                new OracleParameter("RNM", OracleDbType.NVarchar2) { Value = (object?)req.ReviewerName ?? DBNull.Value },
                new OracleParameter("NT",  OracleDbType.NVarchar2) { Value = req.Note.Trim() },
                new OracleParameter("ID",  draftId));
            if (n == 0) return Ok(new { success = false, message = "Bản nháp đã được xử lý bởi người khác" });

            var d = await _db.ExecuteQueryAsync(@"
                SELECT D.AUTHOR_CD, I.INQUIRY_NO, D.INQUIRY_ID FROM HRMS.HR_INQUIRY_DRAFT D
                JOIN HRMS.HR_INQUIRY I ON I.ID = D.INQUIRY_ID WHERE D.ID = :ID",
                r => new { Cd = r["AUTHOR_CD"]?.ToString(), No = r["INQUIRY_NO"]?.ToString() ?? "", Inq = Convert.ToInt64(r["INQUIRY_ID"]) },
                new OracleParameter("ID", draftId));
            var a = d.FirstOrDefault();
            if (a != null && !string.IsNullOrEmpty(a.Cd))
            {
                string capBy = req.ReviewerCd ?? ""; var capA = a;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var noti = scope.ServiceProvider.GetRequiredService<NotificationHelper>();
                        await noti.SendNotificationAsync(new SendNotificationRequest
                        {
                            TITLE = "Bản nháp bị trả lại", BODY = $"CSR đã trả lại bản nháp trả lời ở hội thoại {capA.No}, vui lòng xem góp ý.",
                            TITLE_EN = "Reply draft returned", BODY_EN = $"CSR returned your reply draft in conversation {capA.No}. Please check the feedback.",
                            NOTI_TYPE = "EMPCD", TARGET_VAL = capA.Cd, LINK_ACTION = $"inquiry/{capA.Inq}", CREATED_BY = capBy
                        });
                    }
                    catch { }
                });
            }
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST draft/{id}/dismiss { actorCd } — người soạn thu hồi bản nháp đang chờ / bỏ bản đã bị trả lại
    [HttpPost("draft/{draftId:long}/dismiss")]
    public async Task<IActionResult> DismissDraft(long draftId, [FromBody] InquiryDraftReviewRequest req)
    {
        try
        {
            int n = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_INQUIRY_DRAFT SET STATUS = 'DISMISSED'
                 WHERE ID = :ID AND STATUS IN ('PENDING','RETURNED') AND AUTHOR_CD = :CD",
                new OracleParameter("ID", draftId), new OracleParameter("CD", (object?)req.ActorCd ?? DBNull.Value));
            return n == 0 ? Ok(new { success = false, message = "Không thể thu hồi (đã được xử lý hoặc không phải bản nháp của bạn)" }) : Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // Người nhận thông báo khi NV nhắn vào hội thoại chưa có người phụ trách (2026-11-06): Admin + CSR (người phân loại)
    // + phòng PIC của hội thoại (HR/GA...). Phòng khác KHÔNG nhận vì không được xem hội thoại đó.
    private async Task<List<string>> GetInquiryNotifyEmpCdsAsync(long inquiryId)
    {
        try
        {
            var pic = (await _db.ExecuteQueryAsync(@"
                SELECT DR.ROLE_NAME FROM HRMS.HR_INQUIRY I
                JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = I.PIC_DEPT_ROLE_ID
                WHERE I.ID = :ID", r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("ID", inquiryId))).FirstOrDefault();

            var roles = new List<string> { "Admin", "CSR" };
            if (!string.IsNullOrEmpty(pic) && !roles.Contains(pic)) roles.Add(pic);

            var inRoles = await DeptRoleHelper.EmpcdInRolesSqlAsync(_db, roles.ToArray());
            return await _db.ExecuteQueryAsync($@"
                SELECT U.EMPCD FROM HRMS.HR_USERS U
                WHERE U.IS_ACTIVE = 1 AND U.EMPCD IN ({inRoles})",
                r => r["EMPCD"]?.ToString() ?? "");
        }
        catch { return new(); }
    }
}

// ─── Extra request models (chỉ dùng nội bộ controller này) ──────────────────

public class RemindToSaveRequest { public string? ScopeType { get; set; } public string? ScopeKey { get; set; } public string? Emails { get; set; } public string? UpdtId { get; set; } }

public class ReminderTestRequest { public string? Email { get; set; } }

public class RecallRequest
{
    public long    MsgId      { get; set; }
    public string  SenderType { get; set; } = "EMP";
    public string? EmpCd      { get; set; }
    public string? AnonToken  { get; set; }
}

public class SaveCannedReplyRequest
{
    public long?   Id            { get; set; }
    public string  Title         { get; set; } = "";
    public string  Content       { get; set; } = "";
    public int     DisplayOrder  { get; set; }
    public bool    IsActive      { get; set; } = true;
    public string? TargetRoles   { get; set; }   // CSV: "HR,Admin,CSR" — null/rỗng = dùng chung cả 3
    public string? ActorEmpCd    { get; set; }
}

public class DeleteCannedReplyRequest
{
    public long    Id         { get; set; }
    public string? ActorEmpCd { get; set; }
}
