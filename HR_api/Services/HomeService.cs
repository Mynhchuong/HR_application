using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Home;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Service chính cho trang Home — gộp mọi dữ liệu cần cho /Home/init:
// greeting + birthday/anniversary + payday + shift + banner + pinned bulletin.
// Cache aggressive vì đầu ngày 8000 NV cùng load.
public class HomeService
{
    private readonly OracleService         _oracleService;
    private readonly ShiftLookupService    _shiftLookup;
    private readonly HomeBirthdayService   _birthdayService;
    private readonly IMemoryCache          _cache;

    public HomeService(
        OracleService         oracleService,
        ShiftLookupService    shiftLookup,
        HomeBirthdayService   birthdayService,
        IMemoryCache          cache)
    {
        _oracleService   = oracleService;
        _shiftLookup     = shiftLookup;
        _birthdayService = birthdayService;
        _cache           = cache;
    }

    // ============================================================
    // ENTRY POINT — build toàn bộ VM cho Home
    // ============================================================
    public async Task<HomeInitResponse> BuildIndexAsync(HomeUserContext user)
    {
        // Chạy song song 6 subtask — nếu 1 fail thì phần đó null, không sập cả trang
        var greetingTask = SafeAsync(() => GetGreetingAsync(user.LANG),                       "greeting");
        var birthdayTask = SafeAsync(() => _birthdayService.CheckSelfBirthdayAsync(user),      "birthday");
        var shiftTask    = SafeAsync(() => GetShiftIfApplicableAsync(user),                    "shift");
        var bannerTask   = SafeAsync(() => GetActiveBannerAsync(user.EMPCD, user.ROLENAME, user.LANG), "banner");
        var pinnedTask   = SafeAsync(() => GetPinnedBulletinsAsync(),                          "pinned");
        var paydayTask   = SafeAsync(() => BuildPaydayIfTodayAsync(user),                      "payday");

        await Task.WhenAll(greetingTask, birthdayTask, shiftTask, bannerTask, pinnedTask, paydayTask);

        return new HomeInitResponse
        {
            Greeting = greetingTask.Result,
            Birthday = birthdayTask.Result,
            Shift    = shiftTask.Result,
            Banner   = bannerTask.Result,
            Pinned   = pinnedTask.Result ?? new(),
            Payday   = paydayTask.Result
        };
    }

    // Wrap subtask trong try/catch → không cho 1 lỗi làm sập cả /Home/init
    private static async Task<T?> SafeAsync<T>(Func<Task<T?>> fn, string name) where T : class
    {
        try   { return await fn(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeService] {name} error: {ex.Message}");
            return null;
        }
    }

    // ============================================================
    // 1. GREETING — random 1 câu theo TIME_SLOT hiện tại
    // ============================================================
    public async Task<HomeGreetingModel?> GetGreetingAsync(string lang)
    {
        string slot = await GetCurrentTimeSlotAsync();
        string cacheKey = $"home:greeting:{slot}";

        var pool = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await LoadGreetingPoolAsync(slot);
        }) ?? new List<HomeGreetingRow>();

        if (pool.Count == 0)
            return new HomeGreetingModel
            {
                MESSAGE = lang == "en" ? "Hello from My Samho" : "My Samho, xin chào",
                EMOJI   = "👋",
                LANG    = lang
            };

        var pick = pool[Random.Shared.Next(pool.Count)];
        return new HomeGreetingModel
        {
            MESSAGE = (lang == "en" && !string.IsNullOrEmpty(pick.MESSAGE_EN))
                      ? pick.MESSAGE_EN
                      : pick.MESSAGE_VI,
            EMOJI   = pick.EMOJI,
            LANG    = lang
        };
    }

    private async Task<List<HomeGreetingRow>> LoadGreetingPoolAsync(string slot)
    {
        const string sql = @"
            SELECT MESSAGE_VI, MESSAGE_EN, EMOJI
            FROM HRMS.HR_HOME_GREETING
            WHERE TIME_SLOT = :SLOT AND IS_ACTIVE = 1";

        return await _oracleService.ExecuteQueryAsync(sql, r => new HomeGreetingRow
        {
            MESSAGE_VI = r["MESSAGE_VI"]?.ToString() ?? "",
            MESSAGE_EN = r["MESSAGE_EN"]?.ToString(),
            EMOJI      = r["EMOJI"]?.ToString()
        }, new OracleParameter("SLOT", slot));
    }

    // Detect TIME_SLOT theo SYSDATE Oracle — dùng HR_HOME_GREETING_RULE
    private async Task<string> GetCurrentTimeSlotAsync()
    {
        const string cacheKey = "home:greeting:rules";
        var rules = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            const string sql = @"
                SELECT TIME_SLOT, FROM_HOUR, TO_HOUR
                FROM HRMS.HR_HOME_GREETING_RULE
                WHERE IS_ACTIVE = 1";

            return await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                Slot = r["TIME_SLOT"]?.ToString() ?? "",
                From = Convert.ToInt32(r["FROM_HOUR"]),
                To   = Convert.ToInt32(r["TO_HOUR"])
            });
        }) ?? new();

        int hour = DateTime.Now.Hour;
        foreach (var rule in rules)
        {
            // Ca đêm wrap qua nửa đêm: FROM > TO (vd NIGHT 22→5)
            bool match = rule.From <= rule.To
                ? (hour >= rule.From && hour < rule.To)
                : (hour >= rule.From || hour < rule.To);
            if (match) return rule.Slot;
        }
        return "MORNING"; // fallback
    }

    // ============================================================
    // 2. SHIFT — chỉ query nếu role có ca (không phải HR/Admin)
    // ============================================================
    private async Task<Models.GatePass.GpShiftInfoModel?> GetShiftIfApplicableAsync(HomeUserContext user)
    {
        if (string.Equals(user.ROLENAME, "HR",    StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.ROLENAME, "Admin", StringComparison.OrdinalIgnoreCase))
            return null;

        string cacheKey = $"home:shift:{user.EMPCD}:{DateTime.Today:yyyyMMdd}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _shiftLookup.GetTodayShiftAsync(user.EMPCD);
        });
    }

    // ============================================================
    // 3. PAYDAY — Expat skip, load random câu chúc từ pool
    // ============================================================
    private async Task<HomePaydayModel?> BuildPaydayIfTodayAsync(HomeUserContext user)
    {
        if (!PaydayHelper.IsPaydayToday(user.ROLENAME)) return null;

        var actual = PaydayHelper.GetPaydayOfMonth(DateTime.Today.Year, DateTime.Today.Month);
        var pool   = await _cache.GetOrCreateAsync("home:greeting:PAYDAY", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await LoadGreetingPoolAsync("PAYDAY");
        }) ?? new List<HomeGreetingRow>();

        string message;
        string emoji;
        if (pool.Count > 0)
        {
            var pick = pool[Random.Shared.Next(pool.Count)];
            message  = user.LANG == "en" && !string.IsNullOrEmpty(pick.MESSAGE_EN)
                       ? pick.MESSAGE_EN
                       : pick.MESSAGE_VI;
            emoji    = pick.EMOJI ?? "💰";
        }
        else
        {
            // Fallback nếu pool empty (seed chưa chạy)
            message = user.LANG == "en"
                     ? "Payday today! Check your payslip on the app 📱"
                     : "Hôm nay là ngày nhận lương! Chúc bạn quản lý tài chính khôn ngoan ✨";
            emoji   = "💰";
        }

        return new HomePaydayModel
        {
            IS_PAYDAY   = true,
            ACTUAL_DATE = actual.ToString("yyyy-MM-dd"),
            MESSAGE     = message,
            EMOJI       = emoji,
            CTA_URL     = "/Payslip/Index"
        };
    }

    // ============================================================
    // 4. BANNER — banner MỘT LẦN (khoảng ngày) + banner LẶP theo lịch (tháng/năm/sinh nhật).
    //    Banner lặp ĐÈ banner thường: ưu tiên BIRTHDAY > YEARLY > MONTHLY > MỘT LẦN.
    //    Cache key gồm EMPCD vì banner sinh nhật là per-user.
    // ============================================================
    private async Task<HomeBannerModel?> GetActiveBannerAsync(string empCd, string roleName, string lang)
    {
        string safeRole = string.IsNullOrEmpty(roleName) ? "Employee" : roleName;
        string safeEmp  = string.IsNullOrEmpty(empCd) ? "-" : empCd;
        long epoch = _cache.TryGetValue(HomeAdminService.BannerEpochKey, out long v) ? v : 0L;
        string cacheKey = $"home:banner:active:{epoch}:{safeRole}:{safeEmp}:{lang}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await LoadActiveBannerAsync(safeEmp, safeRole, lang);
        });
    }

    private async Task<HomeBannerModel?> LoadActiveBannerAsync(string empCd, string roleName, string lang)
    {
        // MMDD hôm nay so ngày sinh ECM100.BIRTHDAT ('YYYYMMDD'); 29/2 -> 28/2 ở năm thường.
        const string sql = @"
            SELECT * FROM (
                SELECT B.ID, B.IMAGE_FILE,
                       B.OVERLAY_TEXT_VI, B.OVERLAY_TEXT_EN, B.OVERLAY_POS,
                       B.LINK_URL, B.LINK_TARGET, B.IS_DISMISSIBLE
                FROM HRMS.HR_HOME_BANNER B
                WHERE B.IS_ACTIVE = 1
                  AND B.PUBLISH_FROM <= SYSDATE
                  AND (B.TARGET_ROLES IS NULL
                       OR INSTR(',' || B.TARGET_ROLES || ',', ',' || :ROLE_NAME || ',') > 0)
                  AND (
                        (B.RECUR_TYPE IS NULL
                             AND B.PUBLISH_TO IS NOT NULL
                             AND SYSDATE <= B.PUBLISH_TO)
                     OR (B.RECUR_TYPE IS NOT NULL
                             AND (B.PUBLISH_TO IS NULL OR TRUNC(SYSDATE) <= B.PUBLISH_TO)
                             AND (
                                   (B.RECUR_TYPE = 'MONTHLY'
                                        AND TO_NUMBER(TO_CHAR(SYSDATE,'DD'))
                                            = LEAST(B.RECUR_DAY, TO_NUMBER(TO_CHAR(LAST_DAY(SYSDATE),'DD'))))
                                OR (B.RECUR_TYPE = 'YEARLY'
                                        AND TO_NUMBER(TO_CHAR(SYSDATE,'MM')) = B.RECUR_MONTH
                                        AND TO_NUMBER(TO_CHAR(SYSDATE,'DD'))
                                            = LEAST(B.RECUR_DAY, TO_NUMBER(TO_CHAR(LAST_DAY(SYSDATE),'DD'))))
                                OR (B.RECUR_TYPE = 'BIRTHDAY'
                                        AND EXISTS (
                                            SELECT 1 FROM HRMS.ECM100 EC
                                            WHERE EC.EMPCD = :EMP_CD
                                              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                                              AND (SUBSTR(EC.BIRTHDAT,5,4) = TO_CHAR(SYSDATE,'MMDD')
                                                   OR (SUBSTR(EC.BIRTHDAT,5,4) = '0229'
                                                       AND TO_CHAR(SYSDATE,'MMDD') = '0228'
                                                       AND TO_CHAR(LAST_DAY(TO_DATE(TO_CHAR(SYSDATE,'YYYY')||'0201','YYYYMMDD')),'DD') = '28')))))
                     ))
                ORDER BY CASE B.RECUR_TYPE
                           WHEN 'BIRTHDAY' THEN 1
                           WHEN 'YEARLY'   THEN 2
                           WHEN 'MONTHLY'  THEN 3
                           ELSE 4
                         END,
                         B.INST_DT DESC
            ) WHERE ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new HomeBannerModel
        {
            ID              = Convert.ToInt32(r["ID"]),
            IMAGE_FILE      = r["IMAGE_FILE"]?.ToString() ?? "",
            OVERLAY_TEXT    = lang == "en"
                              ? (r["OVERLAY_TEXT_EN"]?.ToString() ?? r["OVERLAY_TEXT_VI"]?.ToString())
                              : r["OVERLAY_TEXT_VI"]?.ToString(),
            OVERLAY_POS     = r["OVERLAY_POS"]?.ToString() ?? "BL",
            LINK_URL        = r["LINK_URL"]?.ToString(),
            LINK_TARGET     = r["LINK_TARGET"]?.ToString() ?? "_self",
            IS_DISMISSIBLE  = Convert.ToInt32(r["IS_DISMISSIBLE"]) == 1
        },
        new OracleParameter("ROLE_NAME", roleName ?? "Employee"),
        new OracleParameter("EMP_CD",    (object?)empCd ?? DBNull.Value));

        return rows.FirstOrDefault();
    }

    // ============================================================
    // 5. PINNED BULLETIN — max 2 bài
    // ============================================================
    private async Task<List<HomePinnedBulletinModel>> GetPinnedBulletinsAsync()
    {
        const string cacheKey = "home:bulletin:pinned";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            const string sql = @"
                SELECT * FROM (
                    SELECT B.ID, B.TITLE, B.COVER_IMG, B.PIN_ORDER
                    FROM HRMS.HR_BULLETIN B
                    WHERE B.IS_PUBLISHED = 1
                      AND B.IS_ACTIVE    = 1
                      AND B.IS_PINNED    = 1
                      AND SYSDATE BETWEEN B.PUBLISH_FROM AND B.PUBLISH_TO
                    ORDER BY B.PIN_ORDER ASC, B.PUBLISH_FROM DESC
                ) WHERE ROWNUM <= 2";

            return await _oracleService.ExecuteQueryAsync(sql, r => new HomePinnedBulletinModel
            {
                ID        = Convert.ToInt32(r["ID"]),
                TITLE     = r["TITLE"]?.ToString() ?? "",
                COVER_IMG = r["COVER_IMG"]?.ToString(),
                PIN_ORDER = r["PIN_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["PIN_ORDER"])
            });
        }) ?? new();
    }

    // ============================================================
    // INTERNAL DTO
    // ============================================================
    private class HomeGreetingRow
    {
        public string  MESSAGE_VI { get; set; } = "";
        public string? MESSAGE_EN { get; set; }
        public string? EMOJI      { get; set; }
    }
}
