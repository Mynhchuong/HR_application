using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Home;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Check birthday + anniversary cho 1 user, cùng team birthday cho manager.
// Column: ECM100.BIRTHDAT (DATE), ECM100.IGENTDAT (DATE).
// Expat KHÔNG có anniversary.
public class HomeBirthdayService
{
    private readonly OracleService _oracleService;
    private readonly IMemoryCache  _cache;

    // Mốc năm tròn cho anniversary — đổi ở đây → SQL string tự update
    private static readonly int[] AnniversaryMilestones = { 1, 5, 10, 15, 20, 25, 30 };
    private static readonly string MilestonesInClause  = string.Join(",", AnniversaryMilestones);

    public HomeBirthdayService(OracleService oracleService, IMemoryCache cache)
    {
        _oracleService = oracleService;
        _cache         = cache;
    }

    // ============================================================
    // 1. SELF birthday/anniversary — Home banner
    // ============================================================
    public async Task<HomeBirthdayModel?> CheckSelfBirthdayAsync(HomeUserContext user)
    {
        if (string.IsNullOrEmpty(user.EMPCD)) return null;

        string cacheKey = $"home:birthday:{user.EMPCD}:{DateTime.Today:yyyyMMdd}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            return await LoadSelfBirthdayAsync(user);
        });
    }

    private async Task<HomeBirthdayModel?> LoadSelfBirthdayAsync(HomeUserContext user)
    {
        // ECM100.BIRTHDAT / IGENTDAT / RETDAT là VARCHAR2 dạng 'YYYYMMDD'
        // SUBSTR(x,5,4) = MMDD, SUBSTR(x,1,4) = YYYY
        string sql = $@"
            SELECT EC.CNAME EMP_NAME,
                   NVL(R.ROLE_NAME, 'Employee') ROLE_NAME,
                   CASE
                     WHEN SUBSTR(EC.BIRTHDAT,5,4) = TO_CHAR(SYSDATE,'MMDD')
                       OR (SUBSTR(EC.BIRTHDAT,5,4) = '0229'
                           AND TO_CHAR(SYSDATE,'MMDD') = '0228'
                           AND TO_CHAR(LAST_DAY(TO_DATE(TO_CHAR(SYSDATE,'YYYY')||'0201','YYYYMMDD')),'DD') = '28')
                     THEN 1 ELSE 0
                   END IS_BIRTHDAY,
                   CASE
                     WHEN NVL(R.ROLE_NAME, '') = 'Expat' THEN 0
                     WHEN EC.IGENTDAT IS NULL            THEN 0
                     WHEN (SUBSTR(EC.IGENTDAT,5,4) = TO_CHAR(SYSDATE,'MMDD')
                           OR (SUBSTR(EC.IGENTDAT,5,4) = '0229'
                               AND TO_CHAR(SYSDATE,'MMDD') = '0228'
                               AND TO_CHAR(LAST_DAY(TO_DATE(TO_CHAR(SYSDATE,'YYYY')||'0201','YYYYMMDD')),'DD') = '28'))
                          AND (TO_NUMBER(TO_CHAR(SYSDATE,'YYYY')) - TO_NUMBER(SUBSTR(EC.IGENTDAT,1,4)))
                               IN ({MilestonesInClause})
                     THEN TO_NUMBER(TO_CHAR(SYSDATE,'YYYY')) - TO_NUMBER(SUBSTR(EC.IGENTDAT,1,4))
                     ELSE 0
                   END ANNIVERSARY_YEARS
            FROM HRMS.ECM100 EC
            LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = EC.EMPCD
            LEFT JOIN HRMS.HR_ROLES R ON R.ID    = U.ROLE_ID
            WHERE EC.EMPCD = :EMPCD
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              AND ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            EmpName          = r["EMP_NAME"]?.ToString() ?? "",
            IsBirthday       = Convert.ToInt32(r["IS_BIRTHDAY"]) == 1,
            AnniversaryYears = Convert.ToInt32(r["ANNIVERSARY_YEARS"])
        }, new OracleParameter("EMPCD", user.EMPCD));

        var row = rows.FirstOrDefault();
        if (row == null || (!row.IsBirthday && row.AnniversaryYears == 0))
            return null;

        // Build model
        string type = (row.IsBirthday, row.AnniversaryYears > 0) switch
        {
            (true, true)  => "BOTH",
            (true, false) => "BIRTHDAY",
            (false, true) => "ANNIVERSARY",
            _             => "BIRTHDAY"
        };

        // Lấy câu chúc từ pool (theo type ưu tiên) — random
        string slot   = type == "ANNIVERSARY" ? "ANNIVERSARY" : "BIRTHDAY";
        var greeting  = await GetGreetingFromPoolAsync(slot);

        var placeholders = new Dictionary<string, string>
        {
            ["empName"] = row.EmpName,
            ["years"]   = row.AnniversaryYears.ToString()
        };

        string message = ReplacePlaceholders(
            user.LANG == "en" && !string.IsNullOrEmpty(greeting.MessageEn)
                ? greeting.MessageEn
                : greeting.MessageVi,
            placeholders);

        return new HomeBirthdayModel
        {
            TYPE     = type,
            EMP_NAME = row.EmpName,
            MESSAGE  = message,
            EMOJI    = greeting.Emoji,
            YEARS    = row.AnniversaryYears > 0 ? row.AnniversaryYears : null
        };
    }

    // ============================================================
    // 2. TEAM birthday — cho manager scope hoặc HR/Admin/Expat (toàn cty)
    // ============================================================
    public async Task<List<TeamBirthdayItem>> GetTeamBirthdayAsync(HomeUserContext user)
    {
        if (string.IsNullOrEmpty(user.EMPCD)) return new();

        string cacheKey = $"home:birthday:team:{user.EMPCD}:{DateTime.Today:yyyyMMdd}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            return await LoadTeamBirthdayAsync(user);
        }) ?? new();
    }

    private async Task<List<TeamBirthdayItem>> LoadTeamBirthdayAsync(HomeUserContext user)
    {
        // HR/Admin thấy toàn công ty. Expat + các role khác dùng scope HR_USERS_DEPT.
        bool isCompanyWide = user.ROLENAME is "HR" or "Admin";

        string sql;
        var    parameters = new List<OracleParameter>();

        if (isCompanyWide)
        {
            sql = @"
                SELECT * FROM (
                    SELECT EC.EMPCD, EC.CNAME, EC.DEPTCD, EC.LINECD, EC.WORKCD,
                           SUBSTR(EC.BIRTHDAT,5,2) || '-' || SUBSTR(EC.BIRTHDAT,7,2) BIRTHDAT_MMDD
                    FROM HRMS.ECM100 EC
                    WHERE SUBSTR(EC.BIRTHDAT,5,4) = TO_CHAR(SYSDATE,'MMDD')
                      AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                    ORDER BY EC.DEPTCD, EC.CNAME
                ) WHERE ROWNUM <= 50";
        }
        else
        {
            var scope = OTScopeFilterHelper.ForScopeByTuple(user.EMPCD, empAlias: "EC", prefix: "ME");
            sql = $@"
                SELECT * FROM (
                    SELECT EC.EMPCD, EC.CNAME, EC.DEPTCD, EC.LINECD, EC.WORKCD,
                           SUBSTR(EC.BIRTHDAT,5,2) || '-' || SUBSTR(EC.BIRTHDAT,7,2) BIRTHDAT_MMDD
                    FROM HRMS.ECM100 EC
                    WHERE SUBSTR(EC.BIRTHDAT,5,4) = TO_CHAR(SYSDATE,'MMDD')
                      AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                      {scope.SqlClause}
                    ORDER BY EC.CNAME
                ) WHERE ROWNUM <= 50";
            parameters.AddRange(scope.Params);
        }

        return await _oracleService.ExecuteQueryAsync(sql, r => new TeamBirthdayItem
        {
            EMPCD    = r["EMPCD"]?.ToString() ?? "",
            CNAME    = r["CNAME"]?.ToString() ?? "",
            DEPTCD   = r["DEPTCD"]?.ToString(),
            LINECD   = r["LINECD"]?.ToString(),
            WORKCD   = r["WORKCD"]?.ToString(),
            BIRTHDAT = r["BIRTHDAT_MMDD"]?.ToString()
        }, parameters.ToArray());
    }

    // ============================================================
    // Helpers
    // ============================================================
    private async Task<GreetingRow> GetGreetingFromPoolAsync(string slot)
    {
        string cacheKey = $"home:greeting:{slot}";
        var pool = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            const string sql = @"
                SELECT MESSAGE_VI, MESSAGE_EN, EMOJI
                FROM HRMS.HR_HOME_GREETING
                WHERE TIME_SLOT = :SLOT AND IS_ACTIVE = 1";

            return await _oracleService.ExecuteQueryAsync(sql, r => new GreetingRow
            {
                MessageVi = r["MESSAGE_VI"]?.ToString() ?? "",
                MessageEn = r["MESSAGE_EN"]?.ToString(),
                Emoji     = r["EMOJI"]?.ToString()
            }, new OracleParameter("SLOT", slot));
        }) ?? new();

        if (pool.Count == 0)
            return new GreetingRow
            {
                MessageVi = slot == "BIRTHDAY"
                            ? "Chúc mừng sinh nhật {empName}! 🎂"
                            : "Kỷ niệm {years} năm gắn bó cùng Samho, {empName}! 🎖️",
                MessageEn = slot == "BIRTHDAY"
                            ? "Happy Birthday {empName}! 🎂"
                            : "Celebrating {years} years, {empName}! 🎖️",
                Emoji     = slot == "BIRTHDAY" ? "🎂" : "🎖️"
            };

        return pool[Random.Shared.Next(pool.Count)];
    }

    private static string ReplacePlaceholders(string template, Dictionary<string, string> placeholders)
    {
        var result = template;
        foreach (var (key, val) in placeholders)
            result = result.Replace($"{{{key}}}", val);
        return result;
    }

    private class GreetingRow
    {
        public string  MessageVi { get; set; } = "";
        public string? MessageEn { get; set; }
        public string? Emoji     { get; set; }
    }
}
