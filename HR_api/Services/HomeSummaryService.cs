using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Home;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Realtime summary card cho approver (Sup/DM/Mgr/Asst/Expat/HR/Admin).
// Approval logic dùng HR_USERS_DEPT scope tuple — KHÔNG dùng HR_ROUTE_APPROVE.
// Cache 30s/EMPCD để hạn chế query khi user polling.
public class HomeSummaryService
{
    private readonly OracleService       _oracleService;
    private readonly HomeBirthdayService _birthdayService;
    private readonly IMemoryCache        _cache;

    public HomeSummaryService(
        OracleService       oracleService,
        HomeBirthdayService birthdayService,
        IMemoryCache        cache)
    {
        _oracleService   = oracleService;
        _birthdayService = birthdayService;
        _cache           = cache;
    }

    public async Task<HomeSummaryModel> GetAsync(HomeUserContext user, bool force = false)
    {
        if (string.IsNullOrEmpty(user.EMPCD))
            return new HomeSummaryModel();

        string cacheKey = $"home:summary:{user.EMPCD}";

        if (!force && _cache.TryGetValue<HomeSummaryModel>(cacheKey, out var cached) && cached != null)
            return cached;

        var summary = await BuildSummaryAsync(user);

        _cache.Set(cacheKey, summary, TimeSpan.FromSeconds(30));
        return summary;
    }

    // Invalidate cache khi user vừa approve/reject (gọi từ Leave/GP controllers)
    public void InvalidateFor(string empcd)
    {
        _cache.Remove($"home:summary:{empcd}");
    }

    private async Task<HomeSummaryModel> BuildSummaryAsync(HomeUserContext user)
    {
        var result = new HomeSummaryModel { AS_OF = DateTime.Now };

        // Check user có được set scope không — nếu không có → COUNT = 0
        var hasScope = await HasScopeAsync(user.EMPCD);
        if (!hasScope)
        {
            // Vẫn count team birthday (không cần scope)
            var teamBd = await _birthdayService.GetTeamBirthdayAsync(user);
            result.TEAM_BIRTHDAY_COUNT = teamBd.Count;
            return result;
        }

        // Chạy song song 6 query độc lập
        var leaveTask       = CountLeavePendingAsync(user.EMPCD);
        var gpTask          = CountGpPendingAsync(user.EMPCD);
        var otTask          = CountOtAsync(user.EMPCD);
        var bdTask          = _birthdayService.GetTeamBirthdayAsync(user);
        var leaveTodayTask  = CountLeaveTodayAsync(user.EMPCD);
        var gpTodayTask     = CountGpTodayAsync(user.EMPCD);

        await Task.WhenAll(leaveTask, gpTask, otTask, bdTask, leaveTodayTask, gpTodayTask);

        result.LEAVE_PENDING       = leaveTask.Result;
        result.GP_PENDING          = gpTask.Result;
        result.OT_NEED_SIGN        = otTask.Result.NeedSign;
        result.OT_SIGNED           = otTask.Result.Signed;
        result.OT_TOTAL            = otTask.Result.Total;
        result.TEAM_BIRTHDAY_COUNT = bdTask.Result.Count;
        result.LEAVE_TODAY_TOTAL   = leaveTodayTask.Result;
        result.GP_TODAY_TOTAL      = gpTodayTask.Result;

        return result;
    }

    // Tổng số NV nghỉ phép hôm nay trong scope (approved, không tính pending/rejected)
    private async Task<int> CountLeaveTodayAsync(string empcd)
    {
        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "LT");
        string sql = $@"
            SELECT COUNT(DISTINCT L.EMPCD) AS CNT
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID = L.REQUEST_ID
            JOIN HRMS.ECM100    EC ON EC.EMPCD     = L.EMPCD
            WHERE R.REQUEST_TYPE = 'LEAVE'
              AND R.STATUS       = 'APPROVED'
              AND TRUNC(SYSDATE) BETWEEN TRUNC(L.FROM_DATE) AND TRUNC(L.TO_DATE)
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              {scope.SqlClause}";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    // Tổng số NV ra cổng hôm nay trong scope (approved, có OUT_TIME hoặc IN_TIME rơi vào today)
    private async Task<int> CountGpTodayAsync(string empcd)
    {
        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "GT");
        string sql = $@"
            SELECT COUNT(DISTINCT GP.EMPCD) AS CNT
            FROM HRMS.HR_GATEPASS_REQUEST GP
            JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID = GP.REQUEST_ID
            JOIN HRMS.ECM100    EC ON EC.EMPCD     = GP.EMPCD
            WHERE R.STATUS = 'APPROVED'
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) = TRUNC(SYSDATE)
              {scope.SqlClause}";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    private async Task<bool> HasScopeAsync(string empcd)
    {
        var rows = await _oracleService.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :E AND ROWNUM = 1",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("E", empcd));
        return rows.FirstOrDefault() > 0;
    }

    private async Task<int> CountLeavePendingAsync(string empcd)
    {
        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "ME");
        string sql = $@"
            SELECT COUNT(*) AS CNT
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID = L.REQUEST_ID
            JOIN HRMS.ECM100    EC ON EC.EMPCD     = L.EMPCD
            WHERE R.REQUEST_TYPE = 'LEAVE'
              AND L.SOURCE       = 'SELF'
              AND R.STATUS       = 'PENDING'
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              {scope.SqlClause}";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    private async Task<int> CountGpPendingAsync(string empcd)
    {
        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "ME");
        string sql = $@"
            SELECT COUNT(*) AS CNT
            FROM HRMS.HR_GATEPASS_REQUEST GP
            JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID = GP.REQUEST_ID
            JOIN HRMS.ECM100    EC ON EC.EMPCD     = GP.EMPCD
            WHERE R.STATUS = 'PENDING'
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN TRUNC(SYSDATE)-7 AND TRUNC(SYSDATE)+7
              {scope.SqlClause}";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    private async Task<OtCounts> CountOtAsync(string empcd)
    {
        // Dùng 2 prefix khác nhau vì scope xuất hiện 2 lần trong SQL
        // (WITH LATEST_OT + outer WHERE) → tránh duplicate param name
        var scopeInner = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "IN");
        var scopeOuter = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "OU");

        // Count DISTINCT EMPCD vì "số NV" chứ không phải số row OT
        // (1 NV có thể có nhiều slot OT cùng ngày)
        string sql = $@"
            WITH LATEST_OT AS (
                SELECT MAX(OT.WORK_DATE) WD
                FROM HRMS.HR_OT_REQUEST OT
                JOIN HRMS.ECM100 EC ON EC.EMPCD = OT.EMPCD
                WHERE OT.WORK_DATE BETWEEN TRUNC(SYSDATE)-7 AND TRUNC(SYSDATE)+1
                  {scopeInner.SqlClause}
            )
            SELECT
                COUNT(DISTINCT CASE WHEN OT.CONFIRM_STATUS = 'CONFIRMED' THEN OT.EMPCD END) SIGNED,
                COUNT(DISTINCT CASE WHEN OT.CONFIRM_STATUS IS NULL OR OT.CONFIRM_STATUS = 'PENDING' THEN OT.EMPCD END) NEED_SIGN,
                COUNT(DISTINCT OT.EMPCD) TOTAL
            FROM HRMS.HR_OT_REQUEST OT
            JOIN HRMS.ECM100 EC ON EC.EMPCD = OT.EMPCD
            WHERE OT.WORK_DATE = (SELECT WD FROM LATEST_OT)
              {scopeOuter.SqlClause}";

        var allParams = scopeInner.Params.Concat(scopeOuter.Params).ToArray();

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new OtCounts
        {
            Signed   = Convert.ToInt32(r["SIGNED"]),
            NeedSign = Convert.ToInt32(r["NEED_SIGN"]),
            Total    = Convert.ToInt32(r["TOTAL"])
        }, allParams);

        return rows.FirstOrDefault() ?? new OtCounts();
    }

    private class OtCounts
    {
        public int Signed   { get; set; }
        public int NeedSign { get; set; }
        public int Total    { get; set; }
    }
}
