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
    private readonly TrainingTeamService _trainingTeam;
    private readonly IMemoryCache        _cache;

    public HomeSummaryService(
        OracleService       oracleService,
        HomeBirthdayService birthdayService,
        TrainingTeamService trainingTeam,
        IMemoryCache        cache)
    {
        _oracleService   = oracleService;
        _birthdayService = birthdayService;
        _trainingTeam    = trainingTeam;
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

        // Check user có được set scope không — nếu không có → COUNT = 0.
        // Admin không có dòng nào trong HR_USERS_DEPT (không gắn phòng ban cụ thể) nhưng vẫn cần
        // thấy KPI toàn công ty — coi Admin như luôn "có scope", các query bên dưới sẽ tự bỏ điều
        // kiện lọc phòng ban khi role là Admin (xem CountLeaveDocMissingAsync/GetLeaveDocMissingListAsync).
        bool isAdmin  = string.Equals(user.ROLENAME, "Admin", StringComparison.OrdinalIgnoreCase);
        bool isHrCsr  = user.ROLENAME is "HR" or "Admin" or "CSR";
        var hasScope = isAdmin || await HasScopeAsync(user.EMPCD);
        if (!hasScope)
        {
            // Vẫn count team birthday (không cần scope) + dashboard HR/CSR (không phụ thuộc scope phòng ban)
            var teamBd = await _birthdayService.GetTeamBirthdayAsync(user);
            result.TEAM_BIRTHDAY_COUNT = teamBd.Count;
            if (isHrCsr)
            {
                var hr = await CountHrDashboardAsync(user.EMPCD);
                result.HR_UNREAD_MSG       = hr.UnreadMsg;
                result.HR_OPEN_INQ         = hr.OpenInq;
                result.HR_NEW_BULLETIN_CMT = hr.NewBulletinCmt;
            }
            return result;
        }

        // Chạy song song các query độc lập
        var leaveTask       = CountLeavePendingAsync(user.EMPCD);
        var gpTask          = CountGpPendingAsync(user.EMPCD);
        var otTask          = CountOtAsync(user.EMPCD);
        var bdTask          = _birthdayService.GetTeamBirthdayAsync(user);
        var leaveTodayTask  = CountLeaveTodayAsync(user.EMPCD);
        var gpTodayTask     = CountGpTodayAsync(user.EMPCD);
        var trainingTodayTask = _trainingTeam.CountTodayInScopeAsync(user.EMPCD);
        var docMissingTask  = CountLeaveDocMissingAsync(user);
        var hrDashTask      = isHrCsr ? CountHrDashboardAsync(user.EMPCD) : Task.FromResult(new HrDashCounts());

        await Task.WhenAll(leaveTask, gpTask, otTask, bdTask, leaveTodayTask, gpTodayTask, trainingTodayTask, docMissingTask, hrDashTask);

        result.LEAVE_PENDING        = leaveTask.Result;
        result.GP_PENDING           = gpTask.Result;
        result.OT_NEED_SIGN         = otTask.Result.NeedSign;
        result.OT_SIGNED            = otTask.Result.Signed;
        result.OT_TOTAL             = otTask.Result.Total;
        result.TEAM_BIRTHDAY_COUNT  = bdTask.Result.Count;
        result.LEAVE_TODAY_TOTAL    = leaveTodayTask.Result;
        result.GP_TODAY_TOTAL       = gpTodayTask.Result;
        result.TRAINING_TODAY_TOTAL = trainingTodayTask.Result;
        result.LEAVE_DOC_MISSING_COUNT = docMissingTask.Result;
        result.HR_UNREAD_MSG        = hrDashTask.Result.UnreadMsg;
        result.HR_OPEN_INQ          = hrDashTask.Result.OpenInq;
        result.HR_NEW_BULLETIN_CMT  = hrDashTask.Result.NewBulletinCmt;

        return result;
    }

    // "Chưa đọc" tính RIÊNG cho từng người xem (HR_INQUIRY_READER.LAST_READ_DT theo VIEWER_EMPCD) —
    // KHÔNG dùng chung 1 cờ HR_INQUIRY.UNREAD_HR (bug cũ: cờ dùng chung cho mọi HR/CSR/Admin nên
    // cộng dồn qua cả hội thoại đã đóng/đã có người khác phụ trách, báo số ảo dù đã đọc hết —
    // xem đúng logic unreadHrExpr đã sửa ở InquiryController.HrList). KHÔNG tính khi hội thoại
    // đã CLOSED hoặc đã có người phụ trách KHÁC mình.
    private async Task<HrDashCounts> CountHrDashboardAsync(string empcd)
    {
        const string sql = @"
            SELECT
                (SELECT NVL(SUM(
                    CASE
                        WHEN i.STATUS = 'CLOSED' THEN 0
                        WHEN i.ASSIGNED_TO IS NOT NULL AND i.ASSIGNED_TO <> :E1 THEN 0
                        ELSE (SELECT COUNT(*) FROM HRMS.HR_INQUIRY_MSG m
                              WHERE m.INQUIRY_ID = i.ID AND m.SENDER_TYPE = 'EMP' AND m.IS_DELETED = 0
                                AND m.SENT_DT > NVL(rd.LAST_READ_DT, DATE '1900-01-01'))
                    END), 0)
                 FROM HRMS.HR_INQUIRY i
                 LEFT JOIN HRMS.HR_INQUIRY_READER rd ON rd.INQUIRY_ID = i.ID AND rd.VIEWER_EMPCD = :E2)     AS UNREAD_MSG,
                (SELECT COUNT(*) FROM HRMS.HR_INQUIRY WHERE STATUS = 'OPEN' AND ASSIGNED_TO IS NULL)        AS OPEN_INQ,
                (SELECT COUNT(DISTINCT BULLETIN_ID) FROM HRMS.HR_BULLETIN_COMMENT
                  WHERE IS_DELETED = 0 AND INST_DT >= SYSDATE - 1)                                          AS NEW_CMT_BULL
            FROM DUAL";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new HrDashCounts
        {
            UnreadMsg      = Convert.ToInt32(r["UNREAD_MSG"]),
            OpenInq        = Convert.ToInt32(r["OPEN_INQ"]),
            NewBulletinCmt = Convert.ToInt32(r["NEW_CMT_BULL"]),
        }, new OracleParameter("E1", empcd), new OracleParameter("E2", empcd));

        return rows.FirstOrDefault() ?? new HrDashCounts();
    }

    private class HrDashCounts
    {
        public int UnreadMsg      { get; set; }
        public int OpenInq        { get; set; }
        public int NewBulletinCmt { get; set; }
    }

    // Đếm số đơn nghỉ Đám tang/Đám cưới/Vợ sanh/Khám thai (tháng hiện tại) đã duyệt mà chưa nộp giấy tờ
    private async Task<int> CountLeaveDocMissingAsync(HomeUserContext user)
    {
        // Admin không có dòng trong HR_USERS_DEPT → bỏ điều kiện lọc phòng ban, xem toàn công ty
        bool isAdmin = string.Equals(user.ROLENAME, "Admin", StringComparison.OrdinalIgnoreCase);
        var scope = isAdmin
            ? new OTScopeFilterHelper.FilterResult("", new List<OracleParameter>())
            : OTScopeFilterHelper.ForScopeByTuple(user.EMPCD, empAlias: "EC", prefix: "DM");
        string sql = $@"
            SELECT COUNT(*) AS CNT
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID = L.REQUEST_ID
            JOIN HRMS.ECM100    EC ON EC.EMPCD     = L.EMPCD
            WHERE R.REQUEST_TYPE = 'LEAVE'
              AND L.LEAVE_TYPE IN ('DT','DC','VS','KT','SI')
              AND R.STATUS IN ('APPROVED','ASSIGNED')
              AND (L.SOURCE = 'SELF' OR NVL(L.CONFIRM_STATUS,'X') != 'WORKER_REJECTED')
              AND TRUNC(L.TO_DATE,'MM') = TRUNC(SYSDATE,'MM')
              AND NVL(L.DOC_STATUS,'X') != 'SUBMITTED'
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              {scope.SqlClause}";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    // Danh sách chi tiết cho popup khi user click KPI "chưa nộp giấy tờ"
    public async Task<List<LeaveDocMissingItem>> GetLeaveDocMissingListAsync(HomeUserContext user)
    {
        bool isAdmin = string.Equals(user.ROLENAME, "Admin", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && !await HasScopeAsync(user.EMPCD)) return new List<LeaveDocMissingItem>();

        var scope = isAdmin
            ? new OTScopeFilterHelper.FilterResult("", new List<OracleParameter>())
            : OTScopeFilterHelper.ForScopeByTuple(user.EMPCD, empAlias: "EC", prefix: "DL");
        string sql = $@"
            SELECT L.EMPCD, EC.CNAME, B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                   L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.DOC_STATUS
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R  ON R.REQUEST_ID = L.REQUEST_ID
            JOIN HRMS.ECM100    EC ON EC.EMPCD     = L.EMPCD
            LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
            WHERE R.REQUEST_TYPE = 'LEAVE'
              AND L.LEAVE_TYPE IN ('DT','DC','VS','KT','SI')
              AND R.STATUS IN ('APPROVED','ASSIGNED')
              AND (L.SOURCE = 'SELF' OR NVL(L.CONFIRM_STATUS,'X') != 'WORKER_REJECTED')
              AND TRUNC(L.TO_DATE,'MM') = TRUNC(SYSDATE,'MM')
              AND NVL(L.DOC_STATUS,'X') != 'SUBMITTED'
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              {scope.SqlClause}
            ORDER BY L.TO_DATE DESC";

        return await _oracleService.ExecuteQueryAsync(sql, r => new LeaveDocMissingItem
        {
            EMPCD      = r["EMPCD"]?.ToString() ?? "",
            CNAME      = r["CNAME"]?.ToString(),
            DEPT_NAME  = r["DEPT_NAME"]?.ToString(),
            LINE_NAME  = r["LINE_NAME"]?.ToString(),
            WORK_NAME  = r["WORK_NAME"]?.ToString(),
            LEAVE_TYPE = r["LEAVE_TYPE"]?.ToString(),
            FROM_DATE  = r["FROM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
            TO_DATE    = r["TO_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
            DOC_STATUS = r["DOC_STATUS"] == DBNull.Value ? null : r["DOC_STATUS"].ToString()
        }, scope.Params.ToArray());
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

    // Đếm khớp với mặc định của trang duyệt GpListForSupervisor (date_from=date_to=hôm nay)
    // để số trên Home KPI luôn bằng số NV thấy khi bấm vào duyệt — trước đây dùng khoảng
    // ±7 ngày nên số bị lệch (VD Home báo 9 nhưng trang duyệt hôm nay chỉ có 2).
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
              AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) = TRUNC(SYSDATE)
              {scope.SqlClause}";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["CNT"]),
            scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    private async Task<OtCounts> CountOtAsync(string empcd)
    {
        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "OT");

        // Count DISTINCT EMPCD cho OT hôm nay (WORK_DATE = TRUNC(SYSDATE))
        string sql = $@"
            SELECT
                COUNT(DISTINCT CASE WHEN OT.CONFIRM_STATUS = 'CONFIRMED' THEN OT.EMPCD END) SIGNED,
                COUNT(DISTINCT CASE WHEN OT.CONFIRM_STATUS IS NULL OR OT.CONFIRM_STATUS = 'PENDING' THEN OT.EMPCD END) NEED_SIGN,
                COUNT(DISTINCT OT.EMPCD) TOTAL
            FROM HRMS.HR_OT_REQUEST OT
            JOIN HRMS.ECM100 EC ON EC.EMPCD = OT.EMPCD
            WHERE OT.WORK_DATE = TRUNC(SYSDATE)
              {scope.SqlClause}";

        var allParams = scope.Params.ToArray();

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
