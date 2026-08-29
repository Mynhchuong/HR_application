using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Home;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Service quản lý cấu hình Home: banner, greeting, rule, audit.
// Validate overlap banner, whitelist slot greeting, invalidate cache khi save.
public class HomeAdminService
{
    private readonly OracleService     _oracleService;
    private readonly HomeAuditHelper   _audit;
    private readonly IMemoryCache      _cache;

    // Whitelist slot cho Greeting tab — reject BIRTHDAY/ANNIVERSARY/PAYDAY (rule §10.1)
    public static readonly string[] AllowedGreetingSlots =
        { "MORNING", "NOON", "AFTERNOON", "EVENING", "NIGHT" };

    public static readonly string[] AllowedRuleSlots = AllowedGreetingSlots;

    public HomeAdminService(OracleService oracleService, HomeAuditHelper audit, IMemoryCache cache)
    {
        _oracleService = oracleService;
        _audit         = audit;
        _cache         = cache;
    }

    // ============================================================
    // BANNER
    // ============================================================
    public async Task<List<HomeAdminBannerListItem>> ListBannersAsync()
    {
        const string sql = @"
            SELECT ID, IMAGE_FILE,
                   OVERLAY_TEXT_VI, OVERLAY_TEXT_EN, OVERLAY_POS,
                   LINK_URL, LINK_TARGET, TARGET_ROLES, IS_DISMISSIBLE,
                   PUBLISH_FROM, PUBLISH_TO, RECUR_TYPE, RECUR_MONTH, RECUR_DAY, IS_ACTIVE,
                   INST_ID, INST_DT, UPDT_ID, UPDT_DT
            FROM HRMS.HR_HOME_BANNER
            ORDER BY IS_ACTIVE DESC, PUBLISH_FROM DESC, ID DESC";

        return await _oracleService.ExecuteQueryAsync(sql, r => new HomeAdminBannerListItem
        {
            ID              = Convert.ToInt32(r["ID"]),
            IMAGE_FILE      = r["IMAGE_FILE"]?.ToString() ?? "",
            OVERLAY_TEXT_VI = r["OVERLAY_TEXT_VI"]?.ToString(),
            OVERLAY_TEXT_EN = r["OVERLAY_TEXT_EN"]?.ToString(),
            OVERLAY_POS     = r["OVERLAY_POS"]?.ToString() ?? "BL",
            LINK_URL        = r["LINK_URL"]?.ToString(),
            LINK_TARGET     = r["LINK_TARGET"]?.ToString() ?? "_self",
            TARGET_ROLES    = r["TARGET_ROLES"]?.ToString(),
            IS_DISMISSIBLE  = Convert.ToInt32(r["IS_DISMISSIBLE"]) == 1,
            PUBLISH_FROM    = Convert.ToDateTime(r["PUBLISH_FROM"]),
            PUBLISH_TO      = r["PUBLISH_TO"]  == DBNull.Value ? null : Convert.ToDateTime(r["PUBLISH_TO"]),
            RECUR_TYPE      = r["RECUR_TYPE"]?.ToString(),
            RECUR_MONTH     = r["RECUR_MONTH"] == DBNull.Value ? null : Convert.ToInt32(r["RECUR_MONTH"]),
            RECUR_DAY       = r["RECUR_DAY"]   == DBNull.Value ? null : Convert.ToInt32(r["RECUR_DAY"]),
            IS_ACTIVE       = Convert.ToInt32(r["IS_ACTIVE"]) == 1,
            INST_ID         = r["INST_ID"]?.ToString(),
            INST_DT         = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
            UPDT_ID         = r["UPDT_ID"]?.ToString(),
            UPDT_DT         = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"])
        });
    }

    public async Task<(bool ok, string message, int? id)> SaveBannerAsync(SaveBannerRequest req)
    {
        // Validate cơ bản
        if (string.IsNullOrEmpty(req.IMAGE_FILE))
            return (false, "Thiếu ảnh banner", null);
        // Fix B25: chống DateTime.MinValue khi JSON thiếu ngày
        if (req.PUBLISH_FROM == default || req.PUBLISH_FROM.Year < 2000)
            return (false, "Vui lòng chọn ngày bắt đầu áp dụng", null);

        // ── Quy tắc lặp ─────────────────────────────────────────────────────
        // null/"" = banner MỘT LẦN (cần PUBLISH_TO); "MONTHLY"/"YEARLY"/"BIRTHDAY" = lặp (PUBLISH_TO optional).
        string? recurType = string.IsNullOrWhiteSpace(req.RECUR_TYPE) ? null : req.RECUR_TYPE.Trim().ToUpperInvariant();
        bool isRecurring  = recurType != null;
        int? recurMonth = null, recurDay = null;

        if (!isRecurring)
        {
            if (req.PUBLISH_TO is not { } pto || pto == default || pto.Year < 2000)
                return (false, "Vui lòng chọn ngày kết thúc", null);
            if (pto < req.PUBLISH_FROM)
                return (false, "Ngày kết thúc phải sau ngày bắt đầu", null);
            // Banner tạo mới không được đặt đã hết hạn sẵn (sửa banner cũ vẫn cho rút ngắn để dừng sớm)
            if ((req.ID == null || req.ID == 0) && pto <= DateTime.Now)
                return (false, "Ngày kết thúc phải ở tương lai", null);
        }
        else
        {
            if (recurType is not ("MONTHLY" or "YEARLY" or "BIRTHDAY"))
                return (false, "Kiểu lặp không hợp lệ", null);
            // PUBLISH_TO ở banner lặp = ngày NGỪNG lặp (optional, trống = mãi mãi)
            if (req.PUBLISH_TO is { } ptoR && ptoR != default && ptoR.Year >= 2000 && ptoR < req.PUBLISH_FROM)
                return (false, "Ngày ngừng lặp phải sau ngày bắt đầu", null);

            if (recurType == "MONTHLY")
            {
                if (req.RECUR_DAY is not (>= 1 and <= 31))
                    return (false, "Ngày trong tháng phải từ 1 đến 31", null);
                recurDay = req.RECUR_DAY;
            }
            else if (recurType == "YEARLY")
            {
                if (req.RECUR_MONTH is not (>= 1 and <= 12) || req.RECUR_DAY is not (>= 1 and <= 31))
                    return (false, "Ngày/tháng lặp không hợp lệ", null);
                recurMonth = req.RECUR_MONTH;
                recurDay   = req.RECUR_DAY;
            }
            // BIRTHDAY: không cần month/day
        }

        if (!new[] { "TL", "TR", "BL", "BR", "CENTER" }.Contains(req.OVERLAY_POS))
            return (false, "OVERLAY_POS không hợp lệ", null);
        if (!new[] { "_self", "_blank" }.Contains(req.LINK_TARGET))
            return (false, "LINK_TARGET không hợp lệ", null);
        // Length validation (khớp schema HR_HOME_BANNER)
        if ((req.OVERLAY_TEXT_VI?.Length ?? 0) > 300 || (req.OVERLAY_TEXT_EN?.Length ?? 0) > 300)
            return (false, "Overlay text tối đa 300 ký tự", null);
        if ((req.LINK_URL?.Length ?? 0) > 500)
            return (false, "LINK_URL tối đa 500 ký tự", null);
        if ((req.TARGET_ROLES?.Length ?? 0) > 200)
            return (false, "TARGET_ROLES tối đa 200 ký tự", null);
        if ((req.IMAGE_FILE?.Length ?? 0) > 200)
            return (false, "IMAGE_FILE tối đa 200 ký tự", null);

        // Sanitize LINK_URL — chỉ chấp nhận http(s):// hoặc /...
        var linkUrl = SanitizeLinkUrl(req.LINK_URL);

        // HR VÀ CSR đều bị giới hạn field (chỉ image/overlay/publish) — chỉ Admin được set
        // link/target/roles/dismissible. Trước đây chỉ chặn "HR" nên CSR lọt qua nhánh full-field.
        bool isRestricted = !string.Equals(req.LOGIN_ROLE, "Admin", StringComparison.OrdinalIgnoreCase);

        // Nạp bản ghi cũ TRƯỚC overlap check (cần TARGET_ROLES cũ khi restricted-update)
        HomeAdminBannerListItem? oldRow = null;
        if (req.ID is int reqId && reqId > 0)
        {
            oldRow = (await ListBannersAsync()).FirstOrDefault(b => b.ID == reqId);
            if (oldRow == null) return (false, "Không tìm thấy banner", null);
        }

        // Overlap check CHỈ áp dụng cho banner MỘT LẦN (so với các banner một lần khác).
        // Banner lặp được phép cùng tồn tại — thứ tự ưu tiên ở consumer quyết định banner nào hiện.
        if (!isRecurring)
        {
            // Dùng TARGET_ROLES SẼ THỰC SỰ được ghi, không phải giá trị thô từ request:
            //  - Admin              → theo request (đã normalize)
            //  - restricted + sửa   → giữ nguyên giá trị cũ trong DB
            //  - restricted + tạo mới → luôn NULL (= mọi role)
            // TARGET_ROLES trống = hiển thị cho ALL → coi như overlap với mọi role.
            string? effectiveRoles = !isRestricted
                ? NormalizeRoles(req.TARGET_ROLES)
                : (oldRow?.TARGET_ROLES);
            var overlap = await CountOverlappingActiveBannersAsync(
                req.PUBLISH_FROM, req.PUBLISH_TO!.Value, req.ID, effectiveRoles);
            if (overlap > 0)
                return (false, "Đã có banner cùng khoảng thời gian và cùng role. Đổi role hoặc ngày.", null);
        }

        // Param dùng chung cho INSERT/UPDATE — PUBLISH_TO có thể NULL (banner lặp mãi mãi)
        object publishToParam = (req.PUBLISH_TO is { } _pto && _pto != default && _pto.Year >= 2000)
            ? _pto : DBNull.Value;
        object recurTypeParam  = (object?)recurType  ?? DBNull.Value;
        object recurMonthParam = (object?)recurMonth ?? DBNull.Value;
        object recurDayParam   = (object?)recurDay   ?? DBNull.Value;

        if (req.ID == null || req.ID == 0)
        {
            // INSERT
            const string sqlIns = @"
                INSERT INTO HRMS.HR_HOME_BANNER
                    (IMAGE_FILE, OVERLAY_TEXT_VI, OVERLAY_TEXT_EN, OVERLAY_POS,
                     LINK_URL, LINK_TARGET, TARGET_ROLES, IS_DISMISSIBLE,
                     PUBLISH_FROM, PUBLISH_TO, RECUR_TYPE, RECUR_MONTH, RECUR_DAY,
                     IS_ACTIVE, INST_ID, INST_DT)
                VALUES
                    (:IMAGE_FILE, :OVERLAY_TEXT_VI, :OVERLAY_TEXT_EN, :OVERLAY_POS,
                     :LINK_URL, :LINK_TARGET, :TARGET_ROLES, :IS_DISMISSIBLE,
                     :PUBLISH_FROM, :PUBLISH_TO, :RECUR_TYPE, :RECUR_MONTH, :RECUR_DAY,
                     1, :INST_ID, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);

            await _oracleService.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("IMAGE_FILE",      req.IMAGE_FILE),
                new OracleParameter("OVERLAY_TEXT_VI", (object?)req.OVERLAY_TEXT_VI ?? DBNull.Value),
                new OracleParameter("OVERLAY_TEXT_EN", (object?)req.OVERLAY_TEXT_EN ?? DBNull.Value),
                new OracleParameter("OVERLAY_POS",     req.OVERLAY_POS),
                new OracleParameter("LINK_URL",        (object?)(isRestricted ? null : linkUrl) ?? DBNull.Value),
                new OracleParameter("LINK_TARGET",     isRestricted ? "_self" : req.LINK_TARGET),
                new OracleParameter("TARGET_ROLES",    (object?)(isRestricted ? null : NormalizeRoles(req.TARGET_ROLES)) ?? DBNull.Value),
                new OracleParameter("IS_DISMISSIBLE",  (isRestricted ? true : req.IS_DISMISSIBLE) ? 1 : 0),
                new OracleParameter("PUBLISH_FROM",    req.PUBLISH_FROM),
                new OracleParameter("PUBLISH_TO",      publishToParam),
                new OracleParameter("RECUR_TYPE",      recurTypeParam),
                new OracleParameter("RECUR_MONTH",     recurMonthParam),
                new OracleParameter("RECUR_DAY",       recurDayParam),
                new OracleParameter("INST_ID",         (object?)req.LOGIN_USER ?? DBNull.Value),
                outIdParam);

            int newId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                ? (int)od.Value : 0;

            _audit.Log(HomeAuditHelper.AuditAction.INS, HomeAuditHelper.AuditEntity.BANNER,
                newId.ToString(), null, req,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: req.LOGIN_ROLE);

            InvalidateBannerCache();
            return (true, "Tạo banner thành công", newId);
        }
        else
        {
            // UPDATE — HR/CSR: chỉ được sửa image/overlay/publish. Giữ nguyên link/target/roles/dismissible
            string sqlUpd;
            if (isRestricted)
            {
                sqlUpd = @"
                    UPDATE HRMS.HR_HOME_BANNER
                    SET IMAGE_FILE      = :IMAGE_FILE,
                        OVERLAY_TEXT_VI = :OVERLAY_TEXT_VI,
                        OVERLAY_TEXT_EN = :OVERLAY_TEXT_EN,
                        OVERLAY_POS     = :OVERLAY_POS,
                        PUBLISH_FROM    = :PUBLISH_FROM,
                        PUBLISH_TO      = :PUBLISH_TO,
                        RECUR_TYPE      = :RECUR_TYPE,
                        RECUR_MONTH     = :RECUR_MONTH,
                        RECUR_DAY       = :RECUR_DAY,
                        UPDT_ID         = :UPDT_ID,
                        UPDT_DT         = SYSDATE
                    WHERE ID = :ID";

                await _oracleService.ExecuteNonQueryAsync(sqlUpd,
                    new OracleParameter("IMAGE_FILE",      req.IMAGE_FILE),
                    new OracleParameter("OVERLAY_TEXT_VI", (object?)req.OVERLAY_TEXT_VI ?? DBNull.Value),
                    new OracleParameter("OVERLAY_TEXT_EN", (object?)req.OVERLAY_TEXT_EN ?? DBNull.Value),
                    new OracleParameter("OVERLAY_POS",     req.OVERLAY_POS),
                    new OracleParameter("PUBLISH_FROM",    req.PUBLISH_FROM),
                    new OracleParameter("PUBLISH_TO",      publishToParam),
                    new OracleParameter("RECUR_TYPE",      recurTypeParam),
                    new OracleParameter("RECUR_MONTH",     recurMonthParam),
                    new OracleParameter("RECUR_DAY",       recurDayParam),
                    new OracleParameter("UPDT_ID",         (object?)req.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("ID",              req.ID.Value));
            }
            else
            {
                sqlUpd = @"
                    UPDATE HRMS.HR_HOME_BANNER
                    SET IMAGE_FILE      = :IMAGE_FILE,
                        OVERLAY_TEXT_VI = :OVERLAY_TEXT_VI,
                        OVERLAY_TEXT_EN = :OVERLAY_TEXT_EN,
                        OVERLAY_POS     = :OVERLAY_POS,
                        LINK_URL        = :LINK_URL,
                        LINK_TARGET     = :LINK_TARGET,
                        TARGET_ROLES    = :TARGET_ROLES,
                        IS_DISMISSIBLE  = :IS_DISMISSIBLE,
                        PUBLISH_FROM    = :PUBLISH_FROM,
                        PUBLISH_TO      = :PUBLISH_TO,
                        RECUR_TYPE      = :RECUR_TYPE,
                        RECUR_MONTH     = :RECUR_MONTH,
                        RECUR_DAY       = :RECUR_DAY,
                        UPDT_ID         = :UPDT_ID,
                        UPDT_DT         = SYSDATE
                    WHERE ID = :ID";

                await _oracleService.ExecuteNonQueryAsync(sqlUpd,
                    new OracleParameter("IMAGE_FILE",      req.IMAGE_FILE),
                    new OracleParameter("OVERLAY_TEXT_VI", (object?)req.OVERLAY_TEXT_VI ?? DBNull.Value),
                    new OracleParameter("OVERLAY_TEXT_EN", (object?)req.OVERLAY_TEXT_EN ?? DBNull.Value),
                    new OracleParameter("OVERLAY_POS",     req.OVERLAY_POS),
                    new OracleParameter("LINK_URL",        (object?)linkUrl ?? DBNull.Value),
                    new OracleParameter("LINK_TARGET",     req.LINK_TARGET),
                    new OracleParameter("TARGET_ROLES",    (object?)NormalizeRoles(req.TARGET_ROLES) ?? DBNull.Value),
                    new OracleParameter("IS_DISMISSIBLE",  req.IS_DISMISSIBLE ? 1 : 0),
                    new OracleParameter("PUBLISH_FROM",    req.PUBLISH_FROM),
                    new OracleParameter("PUBLISH_TO",      publishToParam),
                    new OracleParameter("RECUR_TYPE",      recurTypeParam),
                    new OracleParameter("RECUR_MONTH",     recurMonthParam),
                    new OracleParameter("RECUR_DAY",       recurDayParam),
                    new OracleParameter("UPDT_ID",         (object?)req.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("ID",              req.ID.Value));
            }

            _audit.Log(HomeAuditHelper.AuditAction.UPD, HomeAuditHelper.AuditEntity.BANNER,
                req.ID.Value.ToString(), oldRow, req,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: req.LOGIN_ROLE);

            InvalidateBannerCache();
            return (true, "Cập nhật banner thành công", req.ID.Value);
        }
    }

    public async Task<(bool ok, string message)> DeleteBannerAsync(BannerActionRequest req)
    {
        // Chỉ Admin mới xoá (soft delete IS_ACTIVE=0)
        if (!string.Equals(req.LOGIN_ROLE, "Admin", StringComparison.OrdinalIgnoreCase))
            return (false, "Chỉ Admin được xoá banner");

        var oldRow = (await ListBannersAsync()).FirstOrDefault(b => b.ID == req.ID);
        if (oldRow == null) return (false, "Không tìm thấy banner");

        const string sql = @"
            UPDATE HRMS.HR_HOME_BANNER
            SET IS_ACTIVE = 0, UPDT_ID = :UPDT_ID, UPDT_DT = SYSDATE
            WHERE ID = :ID";

        int n = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("UPDT_ID", (object?)req.LOGIN_USER ?? DBNull.Value),
            new OracleParameter("ID",      req.ID));

        if (n > 0)
        {
            _audit.Log(HomeAuditHelper.AuditAction.DEL, HomeAuditHelper.AuditEntity.BANNER,
                req.ID.ToString(), oldRow, null,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: req.LOGIN_ROLE);
            InvalidateBannerCache();
            return (true, "Đã xoá banner");
        }
        return (false, "Không tìm thấy banner");
    }

    private async Task<int> CountOverlappingActiveBannersAsync(
        DateTime from, DateTime to, int? excludeId, string? newTargetRoles)
    {
        // Bước 1: lấy tất cả banner MỘT LẦN CHỒNG THỜI GIAN (chưa cần biết role)
        // LOẠI banner đã hết hạn (PUBLISH_TO < SYSDATE) — khớp đúng thời điểm consumer ngừng hiện.
        // Banner lặp (RECUR_TYPE IS NOT NULL) không tính vào overlap.
        const string sql = @"
            SELECT ID, TARGET_ROLES
            FROM HRMS.HR_HOME_BANNER
            WHERE IS_ACTIVE = 1
              AND RECUR_TYPE IS NULL
              AND PUBLISH_TO >= SYSDATE
              AND ID <> NVL(:EXCLUDE_ID, -1)
              AND PUBLISH_FROM <= :NEW_TO
              AND PUBLISH_TO   >= :NEW_FROM";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            ID           = Convert.ToInt32(r["ID"]),
            TARGET_ROLES = r["TARGET_ROLES"]?.ToString()
        },
        new OracleParameter("EXCLUDE_ID", (object?)excludeId ?? DBNull.Value),
        new OracleParameter("NEW_TO",     to),
        new OracleParameter("NEW_FROM",   from));

        // Bước 2: đếm những cái còn CHỒNG ROLE
        var newRoles = ParseRolesCsv(newTargetRoles);
        int count = 0;
        foreach (var r in rows)
        {
            var oldRoles = ParseRolesCsv(r.TARGET_ROLES);
            if (RolesIntersect(newRoles, oldRoles)) count++;
        }
        return count;
    }

    // null/empty CSV = "tất cả role" → coi như overlap với mọi role.
    private static HashSet<string>? ParseRolesCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in csv.Split(','))
        {
            var t = part.Trim();
            if (!string.IsNullOrEmpty(t)) set.Add(t);
        }
        return set.Count == 0 ? null : set;
    }

    private static bool RolesIntersect(HashSet<string>? a, HashSet<string>? b)
    {
        // null = ALL → luôn overlap
        if (a == null || b == null) return true;
        return a.Overlaps(b);
    }

    private static string? SanitizeLinkUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var t = url.Trim();
        // Chặn open-redirect qua URL protocol-relative / backslash-trick / slash mã hoá:
        //   //evil.com , /\evil.com , /%2Fevil.com , /%5Cevil.com
        if (t.StartsWith("//") || t.StartsWith("/\\")
            || t.StartsWith("/%2f", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("/%5c", StringComparison.OrdinalIgnoreCase))
            return null;
        // Đường dẫn nội bộ: bắt buộc "/" + ký tự thường (không phải "/" hay "\")
        if (t.Length >= 2 && t[0] == '/' && t[1] != '/' && t[1] != '\\') return t;
        if (t == "/") return t;
        if (t.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return t;
        return null;
    }

    private static string? NormalizeRoles(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : string.Join(",", parts);
    }

    // Banner cache key ở HomeService gồm cả EMPCD (vì banner sinh nhật là per-user) → không
    // enumerate xoá từng key được. Dùng "epoch": bump 1 con số dùng chung, mọi key cũ thành
    // vô hiệu ngay, TTL 5 phút chỉ còn là backstop dọn rác.
    public const string BannerEpochKey = "home:banner:epoch";

    private void InvalidateBannerCache()
    {
        long cur = _cache.TryGetValue(BannerEpochKey, out long v) ? v : 0L;
        _cache.Set(BannerEpochKey, cur + 1,
            new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
    }

    // ============================================================
    // GREETING
    // ============================================================
    public async Task<List<HomeAdminGreetingItem>> ListGreetingsAsync(string? slot)
    {
        // Chỉ trả 5 slot khung giờ — reject BIRTHDAY/ANNIVERSARY/PAYDAY (rule §10.1)
        string filter = "";
        var parameters = new List<OracleParameter>();
        if (!string.IsNullOrEmpty(slot))
        {
            if (!AllowedGreetingSlots.Contains(slot)) return new();
            filter = "AND TIME_SLOT = :SLOT";
            parameters.Add(new OracleParameter("SLOT", slot));
        }
        else
        {
            // Whitelist ở SQL để bảo vệ khỏi lộ event greeting
            filter = "AND TIME_SLOT IN ('MORNING','NOON','AFTERNOON','EVENING','NIGHT')";
        }

        string sql = $@"
            SELECT ID, TIME_SLOT, MESSAGE_VI, MESSAGE_EN, EMOJI,
                   IS_ACTIVE, UPDT_DT, UPDT_ID
            FROM HRMS.HR_HOME_GREETING
            WHERE 1=1 {filter}
            ORDER BY TIME_SLOT,
                     CASE WHEN IS_ACTIVE = 1 THEN 0 ELSE 1 END,
                     ID DESC";

        return await _oracleService.ExecuteQueryAsync(sql, r => new HomeAdminGreetingItem
        {
            ID         = Convert.ToInt32(r["ID"]),
            TIME_SLOT  = r["TIME_SLOT"]?.ToString() ?? "",
            MESSAGE_VI = r["MESSAGE_VI"]?.ToString() ?? "",
            MESSAGE_EN = r["MESSAGE_EN"]?.ToString(),
            EMOJI      = r["EMOJI"]?.ToString(),
            IS_ACTIVE  = Convert.ToInt32(r["IS_ACTIVE"]) == 1,
            UPDT_DT    = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
            UPDT_ID    = r["UPDT_ID"]?.ToString()
        }, parameters.ToArray());
    }

    public async Task<(bool ok, string message, int? id)> SaveGreetingAsync(SaveGreetingRequest req)
    {
        // Whitelist slot — không cho save BIRTHDAY/ANNIVERSARY/PAYDAY qua admin panel
        if (!AllowedGreetingSlots.Contains(req.TIME_SLOT))
            return (false, "TIME_SLOT không hợp lệ", null);
        if (string.IsNullOrWhiteSpace(req.MESSAGE_VI))
            return (false, "Câu chào tiếng Việt bắt buộc", null);
        if (req.MESSAGE_VI.Length > 300 || (req.MESSAGE_EN?.Length ?? 0) > 300)
            return (false, "Câu chào tối đa 300 ký tự", null);
        if ((req.EMOJI?.Length ?? 0) > 20)
            return (false, "Emoji tối đa 20 ký tự", null);

        HomeAdminGreetingItem? oldRow = null;
        if (req.ID.HasValue)
            oldRow = (await ListGreetingsAsync(null)).FirstOrDefault(g => g.ID == req.ID.Value);

        // Nếu đang UPDATE và turn OFF câu active cuối cùng của slot → reject (rule §3.3)
        if (oldRow != null && oldRow.IS_ACTIVE && !req.IS_ACTIVE && req.ID.HasValue)
        {
            int editId = req.ID.Value;
            int activeSiblings = (await ListGreetingsAsync(oldRow.TIME_SLOT))
                .Count(g => g.IS_ACTIVE && g.ID != editId);
            if (activeSiblings == 0)
                return (false, $"Không được tắt câu chào active cuối cùng của slot {oldRow.TIME_SLOT}", null);
        }

        if (req.ID == null || req.ID == 0)
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_HOME_GREETING (TIME_SLOT, MESSAGE_VI, MESSAGE_EN, EMOJI, IS_ACTIVE, INST_ID, INST_DT)
                VALUES (:TIME_SLOT, :MESSAGE_VI, :MESSAGE_EN, :EMOJI, :IS_ACTIVE, :INST_ID, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("TIME_SLOT",  req.TIME_SLOT),
                new OracleParameter("MESSAGE_VI", req.MESSAGE_VI),
                new OracleParameter("MESSAGE_EN", (object?)req.MESSAGE_EN ?? DBNull.Value),
                new OracleParameter("EMOJI",      (object?)req.EMOJI ?? DBNull.Value),
                new OracleParameter("IS_ACTIVE",  req.IS_ACTIVE ? 1 : 0),
                new OracleParameter("INST_ID",    (object?)req.LOGIN_USER ?? DBNull.Value),
                outIdParam);

            int newId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                ? (int)od.Value : 0;

            _audit.Log(HomeAuditHelper.AuditAction.INS, HomeAuditHelper.AuditEntity.GREETING,
                newId.ToString(), null, req,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: req.LOGIN_ROLE);

            _cache.Remove($"home:greeting:{req.TIME_SLOT}");
            return (true, "Tạo câu chào thành công", newId);
        }
        else
        {
            const string sqlUpd = @"
                UPDATE HRMS.HR_HOME_GREETING
                SET TIME_SLOT  = :TIME_SLOT,
                    MESSAGE_VI = :MESSAGE_VI,
                    MESSAGE_EN = :MESSAGE_EN,
                    EMOJI      = :EMOJI,
                    IS_ACTIVE  = :IS_ACTIVE,
                    UPDT_ID    = :UPDT_ID,
                    UPDT_DT    = SYSDATE
                WHERE ID = :ID";

            await _oracleService.ExecuteNonQueryAsync(sqlUpd,
                new OracleParameter("TIME_SLOT",  req.TIME_SLOT),
                new OracleParameter("MESSAGE_VI", req.MESSAGE_VI),
                new OracleParameter("MESSAGE_EN", (object?)req.MESSAGE_EN ?? DBNull.Value),
                new OracleParameter("EMOJI",      (object?)req.EMOJI ?? DBNull.Value),
                new OracleParameter("IS_ACTIVE",  req.IS_ACTIVE ? 1 : 0),
                new OracleParameter("UPDT_ID",    (object?)req.LOGIN_USER ?? DBNull.Value),
                new OracleParameter("ID",         req.ID.Value));

            _audit.Log(HomeAuditHelper.AuditAction.UPD, HomeAuditHelper.AuditEntity.GREETING,
                req.ID.Value.ToString(), oldRow, req,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: req.LOGIN_ROLE);

            // Invalidate cả slot cũ + slot mới (nếu đổi slot)
            _cache.Remove($"home:greeting:{req.TIME_SLOT}");
            if (oldRow != null && oldRow.TIME_SLOT != req.TIME_SLOT)
                _cache.Remove($"home:greeting:{oldRow.TIME_SLOT}");

            return (true, "Cập nhật câu chào thành công", req.ID.Value);
        }
    }

    public async Task<(bool ok, string message)> DeleteGreetingAsync(GreetingActionRequest req)
    {
        // Bảo vệ: không cho xoá câu cuối cùng của 1 slot (rule §3.3)
        var oldRow = (await ListGreetingsAsync(null)).FirstOrDefault(g => g.ID == req.ID);
        if (oldRow == null) return (false, "Không tìm thấy câu chào");
        if (!AllowedGreetingSlots.Contains(oldRow.TIME_SLOT))
            return (false, "Không thể xoá slot sự kiện");

        // Chỉ block nếu đang xoá câu ACTIVE cuối cùng — xoá câu inactive không làm state tệ hơn
        if (oldRow.IS_ACTIVE)
        {
            var activeSiblings = (await ListGreetingsAsync(oldRow.TIME_SLOT))
                .Where(g => g.IS_ACTIVE && g.ID != req.ID).Count();
            if (activeSiblings == 0)
                return (false, $"Mỗi khung giờ phải có tối thiểu 1 câu chào active (slot {oldRow.TIME_SLOT} hiện chỉ còn 1)");
        }

        int n = await _oracleService.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_HOME_GREETING WHERE ID = :ID",
            new OracleParameter("ID", req.ID));

        if (n > 0)
        {
            _audit.Log(HomeAuditHelper.AuditAction.DEL, HomeAuditHelper.AuditEntity.GREETING,
                req.ID.ToString(), oldRow, null,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: req.LOGIN_ROLE);
            _cache.Remove($"home:greeting:{oldRow.TIME_SLOT}");
            return (true, "Đã xoá câu chào");
        }
        return (false, "Không xoá được");
    }

    // ============================================================
    // TIME SLOT RULE
    // ============================================================
    public async Task<List<HomeAdminRuleItem>> ListRulesAsync()
    {
        const string sql = @"
            SELECT TIME_SLOT, FROM_HOUR, TO_HOUR, IS_ACTIVE, UPDT_DT, UPDT_ID
            FROM HRMS.HR_HOME_GREETING_RULE
            ORDER BY CASE TIME_SLOT
                       WHEN 'MORNING'   THEN 1
                       WHEN 'NOON'      THEN 2
                       WHEN 'AFTERNOON' THEN 3
                       WHEN 'EVENING'   THEN 4
                       WHEN 'NIGHT'     THEN 5
                       ELSE 99 END";

        return await _oracleService.ExecuteQueryAsync(sql, r => new HomeAdminRuleItem
        {
            TIME_SLOT = r["TIME_SLOT"]?.ToString() ?? "",
            FROM_HOUR = Convert.ToInt32(r["FROM_HOUR"]),
            TO_HOUR   = Convert.ToInt32(r["TO_HOUR"]),
            IS_ACTIVE = Convert.ToInt32(r["IS_ACTIVE"]) == 1,
            UPDT_DT   = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
            UPDT_ID   = r["UPDT_ID"]?.ToString()
        });
    }

    public async Task<(bool ok, string message)> SaveRuleAsync(SaveRuleRequest req)
    {
        if (!AllowedRuleSlots.Contains(req.TIME_SLOT))
            return (false, "TIME_SLOT không hợp lệ");
        if (req.FROM_HOUR < 0 || req.FROM_HOUR > 23 || req.TO_HOUR < 0 || req.TO_HOUR > 23)
            return (false, "Giờ phải trong khoảng 0-23");

        // Rule §3.4: validate 24h coverage — không được để lỗ hổng giờ
        var currentRules = await ListRulesAsync();
        var oldRow = currentRules.FirstOrDefault(r => r.TIME_SLOT == req.TIME_SLOT);
        var updatedRules = currentRules
            .Where(r => r.IS_ACTIVE)
            .Select(r => r.TIME_SLOT == req.TIME_SLOT
                ? new HomeAdminRuleItem { TIME_SLOT = r.TIME_SLOT, FROM_HOUR = req.FROM_HOUR, TO_HOUR = req.TO_HOUR, IS_ACTIVE = true }
                : r)
            .ToList();

        var coverage = Enumerable.Range(0, 24).Select(_ => new List<string>()).ToArray(); // list slot cover mỗi giờ
        foreach (var r in updatedRules)
        {
            // Ca wrap qua nửa đêm (FROM > TO)
            if (r.FROM_HOUR <= r.TO_HOUR)
                for (int h = r.FROM_HOUR; h < r.TO_HOUR; h++) coverage[h].Add(r.TIME_SLOT);
            else
            {
                for (int h = r.FROM_HOUR; h < 24; h++)     coverage[h].Add(r.TIME_SLOT);
                for (int h = 0; h < r.TO_HOUR; h++)         coverage[h].Add(r.TIME_SLOT);
            }
        }
        var missingHours = Enumerable.Range(0, 24).Where(h => coverage[h].Count == 0).ToList();
        if (missingHours.Count > 0)
            return (false, $"Khung giờ chưa phủ kín 24h (thiếu giờ: {string.Join(",", missingHours)})");
        var overlappingHours = Enumerable.Range(0, 24).Where(h => coverage[h].Count > 1).ToList();
        if (overlappingHours.Count > 0)
            return (false, $"Khung giờ chồng lấp tại giờ {overlappingHours[0]}: {string.Join(",", coverage[overlappingHours[0]])}");

        const string sql = @"
            UPDATE HRMS.HR_HOME_GREETING_RULE
            SET FROM_HOUR = :FROM_HOUR,
                TO_HOUR   = :TO_HOUR,
                UPDT_ID   = :UPDT_ID,
                UPDT_DT   = SYSDATE
            WHERE TIME_SLOT = :TIME_SLOT";

        int n = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("FROM_HOUR", req.FROM_HOUR),
            new OracleParameter("TO_HOUR",   req.TO_HOUR),
            new OracleParameter("UPDT_ID",   (object?)req.LOGIN_USER ?? DBNull.Value),
            new OracleParameter("TIME_SLOT", req.TIME_SLOT));

        if (n > 0)
        {
            _audit.Log(HomeAuditHelper.AuditAction.UPD, HomeAuditHelper.AuditEntity.RULE,
                req.TIME_SLOT, oldRow, req,
                req.LOGIN_USER ?? "", actorName: req.LOGIN_NAME, roleName: "Admin");
            _cache.Remove("home:greeting:rules");
            return (true, "Cập nhật khung giờ thành công");
        }
        return (false, "Không tìm thấy TIME_SLOT");
    }

    // ============================================================
    // AUDIT
    // ============================================================
    public async Task<HomeAdminAuditPage> ListAuditAsync(int page, int pageSize,
        string? entity = null, string? actorEmpcd = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        page     = page     <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 30 : Math.Min(pageSize, 200);
        int offset = (page - 1) * pageSize;
        int maxRn  = offset + pageSize;

        string where = "WHERE 1=1";
        var ps = new List<OracleParameter>();
        if (!string.IsNullOrEmpty(entity))
        {
            where += " AND ENTITY = :ENTITY";
            ps.Add(new OracleParameter("ENTITY", entity));
        }
        if (!string.IsNullOrEmpty(actorEmpcd))
        {
            where += " AND ACTOR_EMPCD = :ACTOR_EMPCD";
            ps.Add(new OracleParameter("ACTOR_EMPCD", actorEmpcd));
        }
        if (dateFrom.HasValue)
        {
            where += " AND INST_DT >= :DATE_FROM";
            ps.Add(new OracleParameter("DATE_FROM", dateFrom.Value));
        }
        if (dateTo.HasValue)
        {
            where += " AND INST_DT < :DATE_TO + 1";
            ps.Add(new OracleParameter("DATE_TO", dateTo.Value));
        }

        // Total count
        string sqlCount = $"SELECT COUNT(*) CNT FROM HRMS.HR_HOME_AUDIT {where}";
        var totalRows = await _oracleService.ExecuteQueryAsync(sqlCount,
            r => Convert.ToInt32(r["CNT"]),
            ps.Select(p => (OracleParameter)p.Clone()).ToArray());
        int total = totalRows.FirstOrDefault();

        if (total == 0)
            return new HomeAdminAuditPage
            {
                PAGE = page, PAGE_SIZE = pageSize, TOTAL = 0, TOTAL_PAGES = 0
            };

        // Data (Oracle 10g ROW_NUMBER)
        string sqlData = $@"
            SELECT * FROM (
                SELECT T.*, ROW_NUMBER() OVER (ORDER BY INST_DT DESC, ID DESC) RN
                FROM (
                    SELECT ID, ACTOR_EMPCD, ACTOR_NAME, ROLE_NAME,
                           ACTION, ENTITY, ENTITY_ID,
                           DBMS_LOB.SUBSTR(OLD_VAL, 4000, 1) OLD_VAL,
                           DBMS_LOB.SUBSTR(NEW_VAL, 4000, 1) NEW_VAL,
                           IP_ADDRESS, USER_AGENT, INST_DT
                    FROM HRMS.HR_HOME_AUDIT
                    {where}
                ) T
            ) WHERE RN > :OFFSET AND RN <= :MAX_RN";

        var dataPs = ps.Select(p => (OracleParameter)p.Clone()).ToList();
        dataPs.Add(new OracleParameter("OFFSET", offset));
        dataPs.Add(new OracleParameter("MAX_RN", maxRn));

        var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new HomeAdminAuditItem
        {
            ID          = Convert.ToInt32(r["ID"]),
            ACTOR_EMPCD = r["ACTOR_EMPCD"]?.ToString() ?? "",
            ACTOR_NAME  = r["ACTOR_NAME"]?.ToString(),
            ROLE_NAME   = r["ROLE_NAME"]?.ToString(),
            ACTION      = r["ACTION"]?.ToString() ?? "",
            ENTITY      = r["ENTITY"]?.ToString() ?? "",
            ENTITY_ID   = r["ENTITY_ID"]?.ToString(),
            OLD_VAL     = r["OLD_VAL"]?.ToString(),
            NEW_VAL     = r["NEW_VAL"]?.ToString(),
            IP_ADDRESS  = r["IP_ADDRESS"]?.ToString(),
            USER_AGENT  = r["USER_AGENT"]?.ToString(),
            INST_DT     = Convert.ToDateTime(r["INST_DT"])
        }, dataPs.ToArray());

        return new HomeAdminAuditPage
        {
            DATA        = list,
            TOTAL       = total,
            PAGE        = page,
            PAGE_SIZE   = pageSize,
            TOTAL_PAGES = (int)Math.Ceiling(total / (double)pageSize)
        };
    }
}
