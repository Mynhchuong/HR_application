using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Bulletin;

namespace HR_api.Services;

// Tách từ BulletinController — cho phép các feature khác (VD Training self-registration) tạo +
// publish bản tin mà không viết trùng SQL. BulletinController tiếp tục là caller chính (HR thao
// tác qua BulletinAdmin), các action save/publish/unpublish/toggle-pin giờ chỉ là thin wrapper.
public class BulletinService
{
    private readonly OracleService _db;
    private readonly NotificationService _notificationService;
    private readonly NotificationHelper _notificationHelper;
    private readonly IMemoryCache _cache;

    private const int MAX_PINNED = 2;

    public BulletinService(
        OracleService db,
        NotificationService notificationService,
        NotificationHelper notificationHelper,
        IMemoryCache cache)
    {
        _db                   = db;
        _notificationService  = notificationService;
        _notificationHelper   = notificationHelper;
        _cache                = cache;
    }

    private void InvalidateHomePinnedCache() => _cache.Remove("home:bulletin:pinned");

    public async Task<int> CountPinnedAsync(int excludeId)
    {
        const string sql = @"
            SELECT COUNT(*) AS CNT
            FROM HRMS.HR_BULLETIN
            WHERE IS_PINNED = 1 AND IS_ACTIVE = 1 AND ID <> :EX_ID";
        var rows = await _db.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("EX_ID", excludeId));
        return rows.Count > 0 ? rows[0] : 0;
    }

    public async Task<(bool success, string message, int id)> SaveAsync(SaveBulletinRequest model)
    {
        if (string.IsNullOrWhiteSpace(model.TITLE) || string.IsNullOrWhiteSpace(model.CONTENT))
            return (false, "Vui lòng điền đầy đủ tiêu đề và nội dung", 0);

        // Fix B25: chống DateTime.MinValue khi JSON bỏ sót ngày
        if (model.PUBLISH_FROM == default || model.PUBLISH_TO == default ||
            model.PUBLISH_FROM.Year < 2000 || model.PUBLISH_TO.Year < 2000)
            return (false, "Vui lòng chọn ngày đăng và ngày kết thúc", 0);

        if (model.PUBLISH_TO < model.PUBLISH_FROM)
            return (false, "Ngày kết thúc phải lớn hơn ngày bắt đầu", 0);

        // Fix B17: COVER_IMG empty string → null
        string? coverImg = string.IsNullOrWhiteSpace(model.COVER_IMG) ? null : model.COVER_IMG.Trim();

        // Validate pin nếu IS_PINNED=1
        if (model.IS_PINNED == 1)
        {
            int currentPinned = await CountPinnedAsync(excludeId: model.ID ?? 0);
            if (currentPinned >= MAX_PINNED)
                return (false, $"Đã đạt giới hạn {MAX_PINNED} bài ghim. Vui lòng bỏ ghim bài khác trước.", 0);
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

            await _db.ExecuteNonQueryAsync(sqlInsert,
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

            return (true, "Tạo bản tin thành công", newId);
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

            int rows = await _db.ExecuteNonQueryAsync(sqlUpdate,
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
                return (false, "Không tìm thấy bản tin để cập nhật", 0);

            // Save có thể đổi IS_PINNED / PIN_ORDER / PUBLISH_FROM/TO → ảnh hưởng Home
            InvalidateHomePinnedCache();
            return (true, "Cập nhật thành công", model.ID.Value);
        }
    }

    public async Task<(bool success, string message, bool fcmSent)> PublishAsync(int id, string? loginUser)
    {
        const string getSql = @"
            SELECT IS_PUBLISHED, PUBLISHED_DT, TITLE, PUBLISH_FROM, PUBLISH_TO
            FROM HRMS.HR_BULLETIN
            WHERE ID = :ID AND IS_ACTIVE = 1";

        var rows = await _db.ExecuteQueryAsync(getSql,
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
            return (false, "Không tìm thấy bản tin", false);

        var row = rows[0];
        if (row.IsPublished == 1)
            return (false, "Bản tin đã được đăng", false);

        // Fix E1: cấm publish nếu PUBLISH_FROM > SYSDATE (nhân viên click noti sẽ lạc)
        if (row.PublishFrom.Date > DateTime.Now.Date)
            return (false, $"Chưa đến ngày đăng ({row.PublishFrom:dd/MM/yyyy}). Vui lòng đợi hoặc đổi PUBLISH_FROM về hôm nay.", false);

        if (row.PublishTo.Date < DateTime.Now.Date)
            return (false, "Bản tin đã hết hạn, không thể đăng", false);

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
            await _db.ExecuteNonQueryAsync(sqlFirst,
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
            await _db.ExecuteNonQueryAsync(sqlRe,
                new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
                new OracleParameter("ID",      id));
        }

        InvalidateHomePinnedCache();
        return (true, isFirstPublish ? "Đăng bản tin thành công, đã gửi thông báo" : "Đã đăng lại bản tin", isFirstPublish);
    }

    public async Task<(bool success, string message)> UnpublishAsync(int id, string? loginUser)
    {
        const string sql = @"
            UPDATE HRMS.HR_BULLETIN
            SET IS_PUBLISHED = 0,
                UPDT_ID      = :UPDT_ID,
                UPDT_DT      = SYSDATE
            WHERE ID = :ID";

        int rows = await _db.ExecuteNonQueryAsync(sql,
            new OracleParameter("UPDT_ID", (object?)loginUser ?? DBNull.Value),
            new OracleParameter("ID",      id));

        if (rows > 0) InvalidateHomePinnedCache();
        return (rows > 0, rows > 0 ? "Đã rút bài" : "Không tìm thấy");
    }
}
