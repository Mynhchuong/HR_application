using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Bulletin;
using HR_api.Services;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class BulletinController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly NotificationService _notificationService;
    private readonly NotificationHelper _notificationHelper;
    private readonly IMemoryCache _cache;

    private const int MAX_PINNED = 2;
    private const int COMMENT_RATE_LIMIT_PER_MIN = 5;
    private const int COMMENT_MAX_LENGTH = 1000;

    private static readonly HashSet<string> ValidReactions = new()
    { "LIKE", "LOVE", "HAHA", "WOW", "SAD", "ANGRY", "PACMAN" };

    public BulletinController(
        OracleService oracleService,
        NotificationService notificationService,
        NotificationHelper notificationHelper,
        IMemoryCache cache)
    {
        _oracleService       = oracleService;
        _notificationService = notificationService;
        _notificationHelper  = notificationHelper;
        _cache               = cache;
    }

    // Invalidate cache pinned bulletin của Home page (rule §12)
    private void InvalidateHomePinnedCache() => _cache.Remove("home:bulletin:pinned");

    // ═════════════════════════════════════════════════════════════════════════
    // Unread count — cho badge ở bottom nav "Bản tin"
    // Đếm bài đang publish + user chưa mở
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd)) return Ok(new { count = 0 });

            const string sql = @"
                SELECT COUNT(*) CNT
                FROM HRMS.HR_BULLETIN B
                WHERE B.IS_PUBLISHED = 1
                  AND B.IS_ACTIVE    = 1
                  AND SYSDATE BETWEEN B.PUBLISH_FROM AND B.PUBLISH_TO
                  AND NOT EXISTS (
                      SELECT 1 FROM HRMS.HR_BULLETIN_VIEW V
                      WHERE V.BULLETIN_ID = B.ID AND V.EMPCD = :EMPCD
                  )";

            var rows = await _oracleService.ExecuteQueryAsync(sql,
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("EMPCD", empcd));

            return Ok(new { count = rows.FirstOrDefault() });
        }
        catch (Exception ex)
        {
            return Ok(new { count = 0, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 1. LIST  — nhân viên: chỉ bài đang hiển thị
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        try
        {
            const string sql = @"
                SELECT B.ID, B.TITLE, B.COVER_IMG,
                       B.PUBLISH_FROM, B.PUBLISH_TO,
                       B.IS_PINNED, B.PIN_ORDER, B.IS_PUBLISHED, B.PUBLISHED_DT,
                       B.IS_ACTIVE, B.VIEW_COUNT,
                       B.INST_ID, B.INST_DT, B.UPDT_ID, B.UPDT_DT, B.UPDT_FULL_NAME,
                       (SELECT COUNT(*) FROM HRMS.HR_BULLETIN_COMMENT C
                        WHERE C.BULLETIN_ID = B.ID AND C.IS_DELETED = 0) AS COMMENT_COUNT
                FROM HRMS.HR_BULLETIN B
                WHERE B.IS_PUBLISHED = 1
                  AND B.IS_ACTIVE    = 1
                  AND SYSDATE BETWEEN B.PUBLISH_FROM AND B.PUBLISH_TO
                ORDER BY B.IS_PINNED DESC, B.PIN_ORDER ASC,
                         B.PUBLISH_FROM DESC, B.ID DESC";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapLight);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 2. ADMIN LIST  — HR: tất cả (draft + hết hạn + inactive)
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminList()
    {
        try
        {
            const string sql = @"
                SELECT B.ID, B.TITLE, B.COVER_IMG,
                       B.PUBLISH_FROM, B.PUBLISH_TO,
                       B.IS_PINNED, B.PIN_ORDER, B.IS_PUBLISHED, B.PUBLISHED_DT,
                       B.IS_ACTIVE, B.VIEW_COUNT,
                       B.INST_ID, B.INST_DT, B.UPDT_ID, B.UPDT_DT, B.UPDT_FULL_NAME,
                       (SELECT COUNT(*) FROM HRMS.HR_BULLETIN_COMMENT C
                        WHERE C.BULLETIN_ID = B.ID AND C.IS_DELETED = 0) AS COMMENT_COUNT
                FROM HRMS.HR_BULLETIN B
                ORDER BY B.IS_PINNED DESC, B.PIN_ORDER ASC,
                         B.PUBLISH_FROM DESC, B.ID DESC";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapLight);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 3. DETAIL  — kèm CONTENT (NCLOB chunk) + media; tăng VIEW_COUNT (unique)
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] string? empcd)
    {
        try
        {
            const string sql = @"
                SELECT B.ID, B.TITLE,
                       DBMS_LOB.SUBSTR(B.CONTENT, 2000, 1)    AS C1,
                       DBMS_LOB.SUBSTR(B.CONTENT, 2000, 2001) AS C2,
                       DBMS_LOB.SUBSTR(B.CONTENT, 2000, 4001) AS C3,
                       DBMS_LOB.SUBSTR(B.CONTENT, 2000, 6001) AS C4,
                       DBMS_LOB.SUBSTR(B.CONTENT, 2000, 8001) AS C5,
                       B.COVER_IMG,
                       B.PUBLISH_FROM, B.PUBLISH_TO,
                       B.IS_PINNED, B.PIN_ORDER, B.IS_PUBLISHED, B.PUBLISHED_DT,
                       B.IS_ACTIVE, B.VIEW_COUNT,
                       B.INST_ID, B.INST_DT, B.UPDT_ID, B.UPDT_DT, B.UPDT_FULL_NAME,
                       (SELECT COUNT(*) FROM HRMS.HR_BULLETIN_COMMENT C
                        WHERE C.BULLETIN_ID = B.ID AND C.IS_DELETED = 0) AS COMMENT_COUNT
                FROM HRMS.HR_BULLETIN B
                WHERE B.ID = :ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapFull,
                new OracleParameter("ID", id));

            if (result.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bản tin" });

            var bulletin = result[0];

            if (bulletin.IS_ACTIVE == 0)
                return Ok(new { success = false, message = "Bản tin đã bị ẩn" });

            // Load media
            const string mediaSql = @"
                SELECT ID, BULLETIN_ID, MEDIA_TYPE, FILE_NAME, DISPLAY_ORDER
                FROM HRMS.HR_BULLETIN_MEDIA
                WHERE BULLETIN_ID = :ID
                ORDER BY DISPLAY_ORDER ASC, ID ASC";
            bulletin.MEDIA = await _oracleService.ExecuteQueryAsync(mediaSql, MapMedia,
                new OracleParameter("ID", id));

            // Load reaction breakdown
            const string rxnSql = @"
                SELECT REACTION, COUNT(*) AS CNT
                FROM HRMS.HR_BULLETIN_REACTION
                WHERE BULLETIN_ID = :ID
                GROUP BY REACTION
                ORDER BY CNT DESC";
            bulletin.REACTIONS = await _oracleService.ExecuteQueryAsync(rxnSql,
                r => new BulletinReactionSummary
                {
                    REACTION = r["REACTION"]?.ToString() ?? "",
                    CNT      = Convert.ToInt32(r["CNT"])
                },
                new OracleParameter("ID", id));

            // Load reaction của user hiện tại + tăng VIEW_COUNT
            if (!string.IsNullOrWhiteSpace(empcd))
            {
                const string myRxnSql = @"
                    SELECT REACTION FROM HRMS.HR_BULLETIN_REACTION
                    WHERE BULLETIN_ID = :BID AND EMPCD = :EMPCD";
                var myRxn = await _oracleService.ExecuteQueryAsync(myRxnSql,
                    r => r["REACTION"]?.ToString() ?? "",
                    new OracleParameter("BID",   id),
                    new OracleParameter("EMPCD", empcd));
                bulletin.MY_REACTION = myRxn.Count > 0 ? myRxn[0] : null;

                await IncrementViewIfFirstAsync(id, empcd);
            }

            return Ok(new { success = true, data = bulletin });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 4. SAVE  — INSERT (trả ID) hoặc UPDATE
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveBulletinRequest model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.TITLE) ||
                string.IsNullOrWhiteSpace(model.CONTENT))
                return Ok(new { success = false, message = "Vui lòng điền đầy đủ tiêu đề và nội dung" });

            // Fix B25: chống DateTime.MinValue khi JSON bỏ sót ngày
            if (model.PUBLISH_FROM == default || model.PUBLISH_TO == default ||
                model.PUBLISH_FROM.Year < 2000 || model.PUBLISH_TO.Year < 2000)
                return Ok(new { success = false, message = "Vui lòng chọn ngày đăng và ngày kết thúc" });

            if (model.PUBLISH_TO < model.PUBLISH_FROM)
                return Ok(new { success = false, message = "Ngày kết thúc phải lớn hơn ngày bắt đầu" });

            // Fix B17: COVER_IMG empty string → null
            string? coverImg = string.IsNullOrWhiteSpace(model.COVER_IMG) ? null : model.COVER_IMG.Trim();

            // Validate pin nếu IS_PINNED=1
            if (model.IS_PINNED == 1)
            {
                int currentPinned = await CountPinnedAsync(excludeId: model.ID ?? 0);
                if (currentPinned >= MAX_PINNED)
                    return Ok(new { success = false, message = $"Đã đạt giới hạn {MAX_PINNED} bài ghim. Vui lòng bỏ ghim bài khác trước." });
            }

            // Oracle 10g không chấp nhận null byte trong NCLOB
            var content = model.CONTENT.Replace("\0", "");

            if (model.ID == null || model.ID == 0)
            {
                // INSERT — return ID để web upload media
                const string sqlInsert = @"
                    INSERT INTO HRMS.HR_BULLETIN
                        (TITLE, CONTENT, COVER_IMG, PUBLISH_FROM, PUBLISH_TO,
                         IS_PINNED, PIN_ORDER, IS_PUBLISHED, IS_ACTIVE,
                         INST_ID, INST_DT)
                    VALUES
                        (:TITLE, :CONTENT, :COVER_IMG, :PUBLISH_FROM, :PUBLISH_TO,
                         :IS_PINNED, :PIN_ORDER, 0, 1,
                         :INST_ID, SYSDATE)
                    RETURNING ID INTO :OUT_ID";

                var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal,
                    System.Data.ParameterDirection.Output);

                await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                    new OracleParameter("TITLE",        model.TITLE),
                    new OracleParameter("CONTENT",      OracleDbType.NClob) { Value = content },
                    new OracleParameter("COVER_IMG",    (object?)coverImg ?? DBNull.Value),
                    new OracleParameter("PUBLISH_FROM", model.PUBLISH_FROM),
                    new OracleParameter("PUBLISH_TO",   model.PUBLISH_TO),
                    new OracleParameter("IS_PINNED",    model.IS_PINNED),
                    new OracleParameter("PIN_ORDER",    model.PIN_ORDER),
                    new OracleParameter("INST_ID",      (object?)model.LOGIN_USER ?? DBNull.Value),
                    outIdParam);

                int newId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                    ? (int)od.Value : 0;

                return Ok(new { success = true, message = "Tạo bản tin thành công", id = newId });
            }
            else
            {
                // UPDATE
                const string sqlUpdate = @"
                    UPDATE HRMS.HR_BULLETIN
                    SET TITLE         = :TITLE,
                        CONTENT       = :CONTENT,
                        COVER_IMG     = :COVER_IMG,
                        PUBLISH_FROM  = :PUBLISH_FROM,
                        PUBLISH_TO    = :PUBLISH_TO,
                        IS_PINNED     = :IS_PINNED,
                        PIN_ORDER     = :PIN_ORDER,
                        UPDT_ID       = :UPDT_ID,
                        UPDT_DT       = SYSDATE,
                        UPDT_FULL_NAME= :UPDT_FULL_NAME
                    WHERE ID = :ID";

                int rows = await _oracleService.ExecuteNonQueryAsync(sqlUpdate,
                    new OracleParameter("TITLE",        model.TITLE),
                    new OracleParameter("CONTENT",      OracleDbType.NClob) { Value = content },
                    new OracleParameter("COVER_IMG",    (object?)coverImg ?? DBNull.Value),
                    new OracleParameter("PUBLISH_FROM", model.PUBLISH_FROM),
                    new OracleParameter("PUBLISH_TO",   model.PUBLISH_TO),
                    new OracleParameter("IS_PINNED",    model.IS_PINNED),
                    new OracleParameter("PIN_ORDER",    model.PIN_ORDER),
                    new OracleParameter("UPDT_ID",      (object?)model.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("UPDT_FULL_NAME", (object?)model.LOGIN_NAME ?? DBNull.Value),
                    new OracleParameter("ID",           model.ID));

                if (rows == 0)
                    return Ok(new { success = false, message = "Không tìm thấy bản tin để cập nhật" });

                // Save có thể đổi IS_PINNED / PIN_ORDER / PUBLISH_FROM/TO → ảnh hưởng Home
                InvalidateHomePinnedCache();
                return Ok(new { success = true, message = "Cập nhật thành công", id = model.ID });
            }
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 5. PUBLISH  — set IS_PUBLISHED=1; gửi FCM nếu PUBLISHED_DT IS NULL
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(int id, string? loginUser)
    {
        try
        {
            const string getSql = @"
                SELECT IS_PUBLISHED, PUBLISHED_DT, TITLE, PUBLISH_FROM, PUBLISH_TO
                FROM HRMS.HR_BULLETIN
                WHERE ID = :ID AND IS_ACTIVE = 1";

            var rows = await _oracleService.ExecuteQueryAsync(getSql,
                r => new
                {
                    IsPublished  = Convert.ToInt32(r["IS_PUBLISHED"]),
                    PublishedDt  = r["PUBLISHED_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["PUBLISHED_DT"]),
                    Title        = r["TITLE"]?.ToString() ?? "",
                    PublishFrom  = Convert.ToDateTime(r["PUBLISH_FROM"]),
                    PublishTo    = Convert.ToDateTime(r["PUBLISH_TO"])
                },
                new OracleParameter("ID", id));

            if (rows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bản tin" });

            var row = rows[0];
            if (row.IsPublished == 1)
                return Ok(new { success = false, message = "Bản tin đã được đăng" });

            // Fix E1: cấm publish nếu PUBLISH_FROM > SYSDATE (nhân viên click noti sẽ lạc)
            if (row.PublishFrom.Date > DateTime.Now.Date)
                return Ok(new { success = false, message = $"Chưa đến ngày đăng ({row.PublishFrom:dd/MM/yyyy}). Vui lòng đợi hoặc đổi PUBLISH_FROM về hôm nay." });

            if (row.PublishTo.Date < DateTime.Now.Date)
                return Ok(new { success = false, message = "Bản tin đã hết hạn, không thể đăng" });

            bool isFirstPublish = row.PublishedDt == null;

            if (isFirstPublish)
            {
                const string sqlFirst = @"
                    UPDATE HRMS.HR_BULLETIN
                    SET IS_PUBLISHED = 1,
                        PUBLISHED_DT = SYSDATE,
                        UPDT_ID      = :UPDT_ID,
                        UPDT_DT      = SYSDATE
                    WHERE ID = :ID";
                await _oracleService.ExecuteNonQueryAsync(sqlFirst,
                    new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
                    new OracleParameter("ID",      id));

                // Fix E2: nếu template chưa load (lần đầu sau khi seed SQL), force reload
                var (titleVi, _, _, _) = await _notificationHelper.GetTemplateAsync("BULLETIN_NEW");
                if (titleVi == "BULLETIN_NEW")
                    NotificationHelper.InvalidateTemplateCache();

                // Gửi FCM broadcast (fire-and-forget)
                _notificationService.BulletinPublished(id, row.Title, loginUser ?? "");
            }
            else
            {
                const string sqlRe = @"
                    UPDATE HRMS.HR_BULLETIN
                    SET IS_PUBLISHED = 1,
                        UPDT_ID      = :UPDT_ID,
                        UPDT_DT      = SYSDATE
                    WHERE ID = :ID";
                await _oracleService.ExecuteNonQueryAsync(sqlRe,
                    new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
                    new OracleParameter("ID",      id));
            }

            InvalidateHomePinnedCache();
            return Ok(new
            {
                success = true,
                message = isFirstPublish ? "Đăng bản tin thành công, đã gửi thông báo" : "Đã đăng lại bản tin",
                fcmSent = isFirstPublish
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 6. UNPUBLISH  — set IS_PUBLISHED=0, giữ nguyên PUBLISHED_DT
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("unpublish")]
    public async Task<IActionResult> Unpublish(int id, string? loginUser)
    {
        try
        {
            const string sql = @"
                UPDATE HRMS.HR_BULLETIN
                SET IS_PUBLISHED = 0,
                    UPDT_ID      = :UPDT_ID,
                    UPDT_DT      = SYSDATE
                WHERE ID = :ID";

            int rows = await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
                new OracleParameter("ID",      id));

            if (rows > 0) InvalidateHomePinnedCache();
            return Ok(new { success = rows > 0, message = rows > 0 ? "Đã rút bài" : "Không tìm thấy" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 7. TOGGLE PIN  — đảo IS_PINNED; validate max 2 bài
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("toggle-pin")]
    public async Task<IActionResult> TogglePin(int id, string? loginUser)
    {
        try
        {
            // Fix B8: chỉ cho pin bài đang IS_ACTIVE=1
            const string getSql = "SELECT IS_PINNED, IS_ACTIVE FROM HRMS.HR_BULLETIN WHERE ID = :ID";
            var rows = await _oracleService.ExecuteQueryAsync(getSql,
                r => new
                {
                    Pinned = Convert.ToInt32(r["IS_PINNED"]),
                    Active = Convert.ToInt32(r["IS_ACTIVE"])
                },
                new OracleParameter("ID", id));

            if (rows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bản tin" });

            if (rows[0].Active == 0)
                return Ok(new { success = false, message = "Bản tin đã bị xóa, không thể ghim" });

            int currentPin = rows[0].Pinned;
            int newPin     = currentPin == 1 ? 0 : 1;

            if (newPin == 1)
            {
                int currentPinned = await CountPinnedAsync(excludeId: id);
                if (currentPinned >= MAX_PINNED)
                    return Ok(new { success = false, message = $"Đã đạt giới hạn {MAX_PINNED} bài ghim. Vui lòng bỏ ghim bài khác trước." });
            }

            const string updateSql = @"
                UPDATE HRMS.HR_BULLETIN
                SET IS_PINNED = :NEW_PIN,
                    UPDT_ID   = :UPDT_ID,
                    UPDT_DT   = SYSDATE
                WHERE ID = :ID";

            await _oracleService.ExecuteNonQueryAsync(updateSql,
                new OracleParameter("NEW_PIN", newPin),
                new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
                new OracleParameter("ID",      id));

            InvalidateHomePinnedCache();
            return Ok(new { success = true, isPinned = newPin });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 7b. SET PIN ORDER  — chỉ áp dụng khi bài đang IS_PINNED=1
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("set-pin-order")]
    public async Task<IActionResult> SetPinOrder(int id, int pinOrder, string? loginUser)
    {
        try
        {
            // Fix B8: cũng chặn nếu bài đã ẩn
            const string sql = @"
                UPDATE HRMS.HR_BULLETIN
                SET PIN_ORDER = :PIN_ORDER,
                    UPDT_ID   = :UPDT_ID,
                    UPDT_DT   = SYSDATE
                WHERE ID = :ID AND IS_PINNED = 1 AND IS_ACTIVE = 1";

            int n = await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("PIN_ORDER", pinOrder),
                new OracleParameter("UPDT_ID",   (object?)loginUser ?? DBNull.Value),
                new OracleParameter("ID",        id));

            if (n > 0) InvalidateHomePinnedCache();
            return Ok(new
            {
                success = n > 0,
                message = n > 0 ? "Đã cập nhật thứ tự ghim" : "Bài chưa ghim hoặc không tồn tại"
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 8. DELETE  — soft delete (IS_ACTIVE=0); chỉ khi IS_PUBLISHED=0
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(int id, string? loginUser)
    {
        try
        {
            const string checkSql = "SELECT IS_PUBLISHED, IS_ACTIVE FROM HRMS.HR_BULLETIN WHERE ID = :ID";
            var rows = await _oracleService.ExecuteQueryAsync(checkSql,
                r => new
                {
                    IsPublished = Convert.ToInt32(r["IS_PUBLISHED"]),
                    IsActive    = Convert.ToInt32(r["IS_ACTIVE"])
                },
                new OracleParameter("ID", id));

            if (rows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bản tin" });

            // Fix B7: check IS_ACTIVE trước (đã xóa thì không cần báo "rút bài trước")
            if (rows[0].IsActive == 0)
                return Ok(new { success = false, message = "Bản tin đã bị xóa" });

            if (rows[0].IsPublished == 1)
                return Ok(new { success = false, message = "Vui lòng rút bài trước khi xóa" });

            const string sql = @"
                UPDATE HRMS.HR_BULLETIN
                SET IS_ACTIVE = 0,
                    UPDT_ID   = :UPDT_ID,
                    UPDT_DT   = SYSDATE
                WHERE ID = :ID";

            int n = await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
                new OracleParameter("ID",      id));

            return Ok(new { success = n > 0, message = n > 0 ? "Đã xóa bản tin" : "Không tìm thấy" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 9. MEDIA ADD  — web đã upload file lên share, gọi để insert row metadata
    //    Nếu FILE_NAME chỉ là ext (".mp4"), API tự tính FILE_NAME = {ID}{ext}
    //    Trả về ID + finalFileName để client biết tên file thực
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("media/add")]
    public async Task<IActionResult> AddMedia([FromBody] AddMediaRequest model)
    {
        try
        {
            if (model.BULLETIN_ID <= 0 ||
                string.IsNullOrWhiteSpace(model.FILE_NAME) ||
                (model.MEDIA_TYPE != "IMG" && model.MEDIA_TYPE != "VIDEO"))
                return Ok(new { success = false, message = "Tham số không hợp lệ" });

            const string sql = @"
                INSERT INTO HRMS.HR_BULLETIN_MEDIA
                    (BULLETIN_ID, MEDIA_TYPE, FILE_NAME, DISPLAY_ORDER, INST_DT)
                VALUES
                    (:BULLETIN_ID, :MEDIA_TYPE, :FILE_NAME, :DISPLAY_ORDER, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal,
                System.Data.ParameterDirection.Output);

            await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("BULLETIN_ID",   model.BULLETIN_ID),
                new OracleParameter("MEDIA_TYPE",    model.MEDIA_TYPE),
                new OracleParameter("FILE_NAME",     model.FILE_NAME),
                new OracleParameter("DISPLAY_ORDER", model.DISPLAY_ORDER),
                outIdParam);

            int newId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                ? (int)od.Value : 0;

            // Fix B26: nếu client chỉ gửi ext (".mp4"), tính FILE_NAME = {ID}{ext}
            string finalFileName = model.FILE_NAME;
            if (model.FILE_NAME.StartsWith(".") && newId > 0)
            {
                finalFileName = $"{newId}{model.FILE_NAME}";
                const string updSql = "UPDATE HRMS.HR_BULLETIN_MEDIA SET FILE_NAME = :FN WHERE ID = :ID";
                await _oracleService.ExecuteNonQueryAsync(updSql,
                    new OracleParameter("FN", finalFileName),
                    new OracleParameter("ID", newId));
            }

            return Ok(new { success = true, id = newId, fileName = finalFileName });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 10. MEDIA DELETE
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("media/delete")]
    public async Task<IActionResult> DeleteMedia(int id)
    {
        try
        {
            const string sql = "DELETE FROM HRMS.HR_BULLETIN_MEDIA WHERE ID = :ID";
            int n = await _oracleService.ExecuteNonQueryAsync(sql, new OracleParameter("ID", id));
            return Ok(new { success = n > 0 });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 11. COMMENTS  — JOIN HR_USERS lấy FULL_NAME, JOIN ECM100 lấy DEPT/LINE/WORK
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("{id}/comments")]
    public async Task<IActionResult> GetComments(int id)
    {
        try
        {
            const string sql = @"
                SELECT C.ID, C.BULLETIN_ID, C.PARENT_ID, C.EMPCD, C.CONTENT, C.INST_DT,
                       U.FULL_NAME,
                       EC.CNAME EMP_CNAME,
                       EC.DEPTCD, EC.LINECD, EC.WORKCD,
                       B.DEPTNM, B.TEAMNM LINENM, B.WORKNM
                FROM HRMS.HR_BULLETIN_COMMENT C
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = C.EMPCD
                LEFT JOIN HRMS.ECM100   EC ON EC.EMPCD = C.EMPCD
                LEFT JOIN HRMS.EAM410   B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                WHERE C.BULLETIN_ID = :ID
                  AND C.IS_DELETED = 0
                ORDER BY C.INST_DT ASC, C.ID ASC";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapComment,
                new OracleParameter("ID", id));
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 12. COMMENT ADD  — validate length, rate limit 5/phút/empcd
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("comment/add")]
    public async Task<IActionResult> AddComment([FromBody] AddCommentRequest model)
    {
        try
        {
            if (model.BULLETIN_ID <= 0 ||
                string.IsNullOrWhiteSpace(model.EMPCD) ||
                string.IsNullOrWhiteSpace(model.CONTENT))
                return Ok(new { success = false, message = "Thiếu thông tin" });

            string content = model.CONTENT.Trim();
            if (content.Length > COMMENT_MAX_LENGTH)
                return Ok(new { success = false, message = $"Bình luận tối đa {COMMENT_MAX_LENGTH} ký tự" });

            // Check bài còn cho comment không (không hết hạn, không bị ẩn)
            const string checkSql = @"
                SELECT IS_PUBLISHED, IS_ACTIVE, PUBLISH_FROM, PUBLISH_TO
                FROM HRMS.HR_BULLETIN
                WHERE ID = :ID";
            var rows = await _oracleService.ExecuteQueryAsync(checkSql,
                r => new
                {
                    IsPublished = Convert.ToInt32(r["IS_PUBLISHED"]),
                    IsActive    = Convert.ToInt32(r["IS_ACTIVE"]),
                    PublishFrom = Convert.ToDateTime(r["PUBLISH_FROM"]),
                    PublishTo   = Convert.ToDateTime(r["PUBLISH_TO"])
                },
                new OracleParameter("ID", model.BULLETIN_ID));

            if (rows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bản tin" });

            var b = rows[0];
            if (b.IsActive == 0 || b.IsPublished == 0)
                return Ok(new { success = false, message = "Bản tin chưa được đăng" });

            var now = DateTime.Now;
            if (now < b.PublishFrom || now > b.PublishTo)
                return Ok(new { success = false, message = "Bản tin đã hết hạn, không thể bình luận" });

            // Rate limit: 5 comment/phút/empcd
            const string rateSql = @"
                SELECT COUNT(*) AS CNT
                FROM HRMS.HR_BULLETIN_COMMENT
                WHERE EMPCD = :EMPCD
                  AND INST_DT > SYSDATE - INTERVAL '1' MINUTE";
            var cnt = await _oracleService.ExecuteQueryAsync(rateSql,
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("EMPCD", model.EMPCD));

            // Fix B76: trả 200 + success=false (consistent với mọi endpoint khác)
            // ApiService.Parse ở HR_web chỉ deserialize 2xx, 429 sẽ bị nuốt message
            if (cnt.Count > 0 && cnt[0] >= COMMENT_RATE_LIMIT_PER_MIN)
                return Ok(new { success = false, message = "Bạn gửi quá nhanh, vui lòng chờ 1 phút" });

            int? parentId = null;
            if (model.PARENT_ID.HasValue && model.PARENT_ID.Value > 0)
            {
                var parentRows = await _oracleService.ExecuteQueryAsync(
                    "SELECT BULLETIN_ID, NVL(PARENT_ID, 0) ROOT FROM HRMS.HR_BULLETIN_COMMENT WHERE ID = :ID AND IS_DELETED = 0",
                    r => new
                    {
                        BulletinId = Convert.ToInt32(r["BULLETIN_ID"]),
                        Root       = Convert.ToInt32(r["ROOT"])
                    },
                    new OracleParameter("ID", model.PARENT_ID.Value));
                if (parentRows.Count == 0 || parentRows[0].BulletinId != model.BULLETIN_ID)
                    return Ok(new { success = false, message = "Bình luận cha không tồn tại" });
                // Flatten: nếu parent đã là reply, gán PARENT_ID = root
                parentId = parentRows[0].Root > 0 ? parentRows[0].Root : model.PARENT_ID.Value;
            }

            const string sqlInsert = @"
                INSERT INTO HRMS.HR_BULLETIN_COMMENT
                    (BULLETIN_ID, PARENT_ID, EMPCD, CONTENT, IS_DELETED, INST_DT)
                VALUES
                    (:BULLETIN_ID, :PARENT_ID, :EMPCD, :CONTENT, 0, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal,
                System.Data.ParameterDirection.Output);

            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("BULLETIN_ID", model.BULLETIN_ID),
                new OracleParameter("PARENT_ID",   (object?)parentId ?? DBNull.Value),
                new OracleParameter("EMPCD",       model.EMPCD),
                new OracleParameter("CONTENT",     content),
                outIdParam);

            int newId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                ? (int)od.Value : 0;

            return Ok(new { success = true, id = newId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 13. COMMENT DELETE  — chính chủ HOẶC HR/Admin
    //     Client truyền isHrAdmin=true nếu role là HR/Admin (đã check ở web)
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("comment/delete")]
    public async Task<IActionResult> DeleteComment(int id, string empcd, bool isHrAdmin = false)
    {
        try
        {
            const string getSql = @"SELECT EMPCD,
                                           (SYSDATE - INST_DT) * 24 * 60 AS AGE_MIN
                                    FROM HRMS.HR_BULLETIN_COMMENT
                                    WHERE ID = :ID AND IS_DELETED = 0";
            var rows = await _oracleService.ExecuteQueryAsync(getSql,
                r => new
                {
                    Empcd  = r["EMPCD"]?.ToString() ?? "",
                    AgeMin = r["AGE_MIN"] == DBNull.Value ? 0m : Convert.ToDecimal(r["AGE_MIN"])
                },
                new OracleParameter("ID", id));

            if (rows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bình luận" });

            string owner = rows[0].Empcd;
            decimal ageMin = rows[0].AgeMin;
            if (!isHrAdmin && owner != empcd)
                return Ok(new { success = false, message = "Không có quyền xóa bình luận này" });
            if (!isHrAdmin && ageMin > 5)
                return Ok(new { success = false, message = "Chỉ được xóa trong 5 phút sau khi đăng" });

            const string sql = @"
                UPDATE HRMS.HR_BULLETIN_COMMENT
                SET IS_DELETED = 1
                WHERE ID = :ID";
            int n = await _oracleService.ExecuteNonQueryAsync(sql, new OracleParameter("ID", id));
            return Ok(new { success = n > 0 });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 14. REACT  — toggle 1 emp = 1 reaction / 1 bài
    //     Gửi cùng loại đang chọn → bỏ. Loại khác → đổi. Chưa có → thêm.
    // ═════════════════════════════════════════════════════════════════════════
    [HttpPost("react")]
    public async Task<IActionResult> React([FromBody] ReactRequest model)
    {
        try
        {
            if (model.BULLETIN_ID <= 0 ||
                string.IsNullOrWhiteSpace(model.EMPCD) ||
                !ValidReactions.Contains(model.REACTION))
                return Ok(new { success = false, message = "Tham số không hợp lệ" });

            // Fix B57: chặn react khi bài chưa đăng / đã ẩn / hết hạn
            const string checkSql = @"
                SELECT IS_PUBLISHED, IS_ACTIVE, PUBLISH_FROM, PUBLISH_TO
                FROM HRMS.HR_BULLETIN
                WHERE ID = :ID";
            var blt = await _oracleService.ExecuteQueryAsync(checkSql,
                r => new
                {
                    IsPublished = Convert.ToInt32(r["IS_PUBLISHED"]),
                    IsActive    = Convert.ToInt32(r["IS_ACTIVE"]),
                    PublishFrom = Convert.ToDateTime(r["PUBLISH_FROM"]),
                    PublishTo   = Convert.ToDateTime(r["PUBLISH_TO"])
                },
                new OracleParameter("ID", model.BULLETIN_ID));

            if (blt.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy bản tin" });
            if (blt[0].IsActive == 0 || blt[0].IsPublished == 0)
                return Ok(new { success = false, message = "Bản tin chưa hiển thị" });
            var now = DateTime.Now;
            if (now < blt[0].PublishFrom || now > blt[0].PublishTo)
                return Ok(new { success = false, message = "Bản tin đã hết hạn" });

            const string getSql = @"
                SELECT REACTION FROM HRMS.HR_BULLETIN_REACTION
                WHERE BULLETIN_ID = :BID AND EMPCD = :EMPCD";
            var rows = await _oracleService.ExecuteQueryAsync(getSql,
                r => r["REACTION"]?.ToString() ?? "",
                new OracleParameter("BID",   model.BULLETIN_ID),
                new OracleParameter("EMPCD", model.EMPCD));

            string? action;
            if (rows.Count == 0)
            {
                // INSERT
                const string sqlIns = @"
                    INSERT INTO HRMS.HR_BULLETIN_REACTION (BULLETIN_ID, EMPCD, REACTION, INST_DT)
                    VALUES (:BID, :EMPCD, :REACTION, SYSDATE)";
                await _oracleService.ExecuteNonQueryAsync(sqlIns,
                    new OracleParameter("BID",      model.BULLETIN_ID),
                    new OracleParameter("EMPCD",    model.EMPCD),
                    new OracleParameter("REACTION", model.REACTION));
                action = "added";
            }
            else if (rows[0] == model.REACTION)
            {
                // Cùng loại → bỏ
                const string sqlDel = @"
                    DELETE FROM HRMS.HR_BULLETIN_REACTION
                    WHERE BULLETIN_ID = :BID AND EMPCD = :EMPCD";
                await _oracleService.ExecuteNonQueryAsync(sqlDel,
                    new OracleParameter("BID",   model.BULLETIN_ID),
                    new OracleParameter("EMPCD", model.EMPCD));
                action = "removed";
            }
            else
            {
                // Đổi loại
                const string sqlUpd = @"
                    UPDATE HRMS.HR_BULLETIN_REACTION
                    SET REACTION = :REACTION, INST_DT = SYSDATE
                    WHERE BULLETIN_ID = :BID AND EMPCD = :EMPCD";
                await _oracleService.ExecuteNonQueryAsync(sqlUpd,
                    new OracleParameter("REACTION", model.REACTION),
                    new OracleParameter("BID",      model.BULLETIN_ID),
                    new OracleParameter("EMPCD",    model.EMPCD));
                action = "changed";
            }

            return Ok(new { success = true, action });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 15. STATS  — view + comment + reaction breakdown
    // ═════════════════════════════════════════════════════════════════════════
    [HttpGet("{id}/stats")]
    public async Task<IActionResult> GetStats(int id)
    {
        try
        {
            const string sql = @"
                SELECT
                  (SELECT VIEW_COUNT FROM HRMS.HR_BULLETIN WHERE ID = :ID) AS VIEW_COUNT,
                  (SELECT COUNT(*) FROM HRMS.HR_BULLETIN_COMMENT
                   WHERE BULLETIN_ID = :ID AND IS_DELETED = 0)            AS CMT_COUNT
                FROM DUAL";

            var head = await _oracleService.ExecuteQueryAsync(sql,
                r => new BulletinStatsModel
                {
                    VIEW_COUNT    = r["VIEW_COUNT"]  == DBNull.Value ? 0 : Convert.ToInt32(r["VIEW_COUNT"]),
                    COMMENT_COUNT = r["CMT_COUNT"]   == DBNull.Value ? 0 : Convert.ToInt32(r["CMT_COUNT"])
                },
                new OracleParameter("ID", id));

            if (head.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy" });

            const string rxnSql = @"
                SELECT REACTION, COUNT(*) AS CNT
                FROM HRMS.HR_BULLETIN_REACTION
                WHERE BULLETIN_ID = :ID
                GROUP BY REACTION
                ORDER BY CNT DESC";

            var rxns = await _oracleService.ExecuteQueryAsync(rxnSql,
                r => new BulletinReactionSummary
                {
                    REACTION = r["REACTION"]?.ToString() ?? "",
                    CNT      = Convert.ToInt32(r["CNT"])
                },
                new OracleParameter("ID", id));

            var stats = head[0];
            stats.REACTIONS = rxns;
            return Ok(new { success = true, data = stats });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // INTERNAL HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<int> CountPinnedAsync(int excludeId)
    {
        const string sql = @"
            SELECT COUNT(*) AS CNT
            FROM HRMS.HR_BULLETIN
            WHERE IS_PINNED = 1 AND IS_ACTIVE = 1 AND ID <> :EX_ID";
        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("EX_ID", excludeId));
        return rows.Count > 0 ? rows[0] : 0;
    }

    private async Task IncrementViewIfFirstAsync(int bulletinId, string empcd)
    {
        // MERGE: insert nếu chưa có. Nếu insert thành công → cộng VIEW_COUNT.
        const string mergeSql = @"
            MERGE INTO HRMS.HR_BULLETIN_VIEW V
            USING (SELECT :BID AS BULLETIN_ID, :EMPCD AS EMPCD FROM DUAL) S
            ON (V.BULLETIN_ID = S.BULLETIN_ID AND V.EMPCD = S.EMPCD)
            WHEN NOT MATCHED THEN
              INSERT (BULLETIN_ID, EMPCD, FIRST_VIEW)
              VALUES (S.BULLETIN_ID, S.EMPCD, SYSDATE)";

        int rows = await _oracleService.ExecuteNonQueryAsync(mergeSql,
            new OracleParameter("BID",   bulletinId),
            new OracleParameter("EMPCD", empcd));

        if (rows > 0)
        {
            const string updSql = @"
                UPDATE HRMS.HR_BULLETIN SET VIEW_COUNT = VIEW_COUNT + 1
                WHERE ID = :ID";
            await _oracleService.ExecuteNonQueryAsync(updSql,
                new OracleParameter("ID", bulletinId));
        }
    }

    // Map nhẹ (không có CONTENT)
    private static BulletinModel MapLight(OracleDataReader r) => new()
    {
        ID             = Convert.ToInt32(r["ID"]),
        TITLE          = r["TITLE"]?.ToString() ?? "",
        COVER_IMG      = r["COVER_IMG"]?.ToString(),
        PUBLISH_FROM   = Convert.ToDateTime(r["PUBLISH_FROM"]),
        PUBLISH_TO     = Convert.ToDateTime(r["PUBLISH_TO"]),
        IS_PINNED      = Convert.ToInt32(r["IS_PINNED"]),
        PIN_ORDER      = Convert.ToInt32(r["PIN_ORDER"]),
        IS_PUBLISHED   = Convert.ToInt32(r["IS_PUBLISHED"]),
        PUBLISHED_DT   = r["PUBLISHED_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["PUBLISHED_DT"]),
        IS_ACTIVE      = Convert.ToInt32(r["IS_ACTIVE"]),
        VIEW_COUNT     = r["VIEW_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["VIEW_COUNT"]),
        COMMENT_COUNT  = HasColumn(r, "COMMENT_COUNT") && r["COMMENT_COUNT"] != DBNull.Value ? Convert.ToInt32(r["COMMENT_COUNT"]) : 0,
        INST_ID        = r["INST_ID"]?.ToString(),
        INST_DT        = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID        = r["UPDT_ID"]?.ToString(),
        UPDT_DT        = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
        UPDT_FULL_NAME = r["UPDT_FULL_NAME"]?.ToString()
    };

    // Map full (có CONTENT chunk)
    private static BulletinModel MapFull(OracleDataReader r) => new()
    {
        ID             = Convert.ToInt32(r["ID"]),
        TITLE          = r["TITLE"]?.ToString() ?? "",
        CONTENT        = (r["C1"]?.ToString() ?? "") + (r["C2"]?.ToString() ?? "") +
                         (r["C3"]?.ToString() ?? "") + (r["C4"]?.ToString() ?? "") +
                         (r["C5"]?.ToString() ?? ""),
        COVER_IMG      = r["COVER_IMG"]?.ToString(),
        PUBLISH_FROM   = Convert.ToDateTime(r["PUBLISH_FROM"]),
        PUBLISH_TO     = Convert.ToDateTime(r["PUBLISH_TO"]),
        IS_PINNED      = Convert.ToInt32(r["IS_PINNED"]),
        PIN_ORDER      = Convert.ToInt32(r["PIN_ORDER"]),
        IS_PUBLISHED   = Convert.ToInt32(r["IS_PUBLISHED"]),
        PUBLISHED_DT   = r["PUBLISHED_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["PUBLISHED_DT"]),
        IS_ACTIVE      = Convert.ToInt32(r["IS_ACTIVE"]),
        VIEW_COUNT     = r["VIEW_COUNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["VIEW_COUNT"]),
        COMMENT_COUNT  = HasColumn(r, "COMMENT_COUNT") && r["COMMENT_COUNT"] != DBNull.Value ? Convert.ToInt32(r["COMMENT_COUNT"]) : 0,
        INST_ID        = r["INST_ID"]?.ToString(),
        INST_DT        = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID        = r["UPDT_ID"]?.ToString(),
        UPDT_DT        = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
        UPDT_FULL_NAME = r["UPDT_FULL_NAME"]?.ToString()
    };

    private static BulletinMediaModel MapMedia(OracleDataReader r) => new()
    {
        ID            = Convert.ToInt32(r["ID"]),
        BULLETIN_ID   = Convert.ToInt32(r["BULLETIN_ID"]),
        MEDIA_TYPE    = r["MEDIA_TYPE"]?.ToString() ?? "IMG",
        FILE_NAME     = r["FILE_NAME"]?.ToString() ?? "",
        DISPLAY_ORDER = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"])
    };

    private static BulletinCommentDto MapComment(OracleDataReader r) => new()
    {
        ID          = Convert.ToInt32(r["ID"]),
        BULLETIN_ID = Convert.ToInt32(r["BULLETIN_ID"]),
        PARENT_ID   = HasColumn(r, "PARENT_ID") && r["PARENT_ID"] != DBNull.Value ? Convert.ToInt32(r["PARENT_ID"]) : (int?)null,
        EMPCD       = r["EMPCD"]?.ToString() ?? "",
        FULL_NAME   = r["FULL_NAME"]?.ToString(),
        EMP_CNAME   = HasColumn(r, "EMP_CNAME") ? r["EMP_CNAME"]?.ToString() : null,
        DEPTCD      = r["DEPTCD"]?.ToString(),
        LINECD      = r["LINECD"]?.ToString(),
        WORKCD      = r["WORKCD"]?.ToString(),
        DEPT_NAME   = HasColumn(r, "DEPTNM") ? r["DEPTNM"]?.ToString() : null,
        LINE_NAME   = HasColumn(r, "LINENM") ? r["LINENM"]?.ToString() : null,
        WORK_NAME   = HasColumn(r, "WORKNM") ? r["WORKNM"]?.ToString() : null,
        CONTENT     = r["CONTENT"]?.ToString() ?? "",
        INST_DT     = Convert.ToDateTime(r["INST_DT"])
    };

    private static bool HasColumn(OracleDataReader r, string name)
    {
        for (int i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
