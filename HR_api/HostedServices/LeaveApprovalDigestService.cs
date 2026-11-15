using HR_api.Data;
using HR_api.Services;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

/// <summary>
/// Gộp thông báo "đơn nghỉ phép chờ duyệt" cho người duyệt — 2 lần/ngày, canh theo CA LÀM VIỆC của
/// chính người đó (yêu cầu 2026-11-14).
///
/// Trước đây mỗi đơn gửi ngay 1 thông báo cho từng người duyệt (NotificationService.LeaveSubmitted).
/// Số liệu 30 ngày: 24.024 thông báo cho 379 người duyệt = 63 cái/người/tháng; cá biệt 1 người nhận
/// 119 cái TRONG 1 NGÀY. Đây cũng là loại ít được mở nhất — chỉ 22,7%. Dội nhiều tới mức bị bỏ qua.
///
/// Giờ gửi: VÀO CA + 1 GIỜ  và  HẾT CA − 2 GIỜ.
///   vd ca HC 07:30–16:30 -> 08:30 và 14:30 | ca N1 06:00–14:00 -> 07:00 và 12:00
/// Ca đêm (STIME > ETIME, vd D5 22:00–06:00) vẫn đúng vì cộng/trừ trên trục thời gian rồi lấy dư 24h.
///
/// Chống gửi trùng: dò lại HR_NOTIFICATIONS xem trong 3 giờ gần đây đã có bản tóm tắt nào gửi cho
/// người đó chưa — không cần thêm bảng/cột mới.
/// </summary>
public class LeaveApprovalDigestService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveApprovalDigestService> _log;

    // Chạy mỗi 10 phút; mốc giờ khớp nếu lệch trong khoảng này (tick thưa hơn sẽ bỏ lỡ mốc).
    private const int TickMinutes   = 10;
    private const int WindowMinutes = 10;

    // Danh sách giới hạn để CHẠY THỬ trước khi bật cho toàn công ty (appsettings:
    //   "LeaveApprovalDigest": { "OnlyEmpCds": [ "10041206", "96030199" ] }
    // ). Để trống / bỏ hẳn mục này = gửi cho tất cả người duyệt. Có cái này thì test được trên
    // dữ liệu thật mà không dội thông báo cho 209 quản lý.
    private readonly HashSet<string> _onlyEmpCds;

    public LeaveApprovalDigestService(IServiceScopeFactory scopeFactory,
                                      ILogger<LeaveApprovalDigestService> log,
                                      IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _onlyEmpCds = new HashSet<string>(
            config.GetSection("LeaveApprovalDigest:OnlyEmpCds").Get<string[]>() ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("LeaveApprovalDigestService started");
        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { _log.LogError(ex, "LeaveApprovalDigest tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(TickMinutes), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<OracleService>();
        var noti = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // 1 câu lấy đủ: người duyệt + ca hôm nay + số đơn đang chờ trong phạm vi của họ.
        // Phạm vi duyệt dùng ĐÚNG bộ (DEPTCD, LINECD, WORKCD) như NotificationHelper.GetApproverEmpCdsAsync
        // để con số khớp với danh sách họ thấy trong app.
        // Đơn Công tác (CT) chỉ Manager/Expat/Admin được duyệt nên không tính cho Supervisor/DeputyManager.
        const string sql = @"
            SELECT U.EMPCD, NVL(S.STIME, '0730') AS STIME, NVL(S.ETIME, '1630') AS ETIME,
                   (SELECT COUNT(*)
                      FROM HRMS.HR_REQUEST RQ
                      JOIN HRMS.HR_LEAVE_REQUEST LR ON LR.REQUEST_ID = RQ.REQUEST_ID
                      JOIN HRMS.ECM100 EC2         ON EC2.EMPCD      = LR.EMPCD
                     WHERE RQ.STATUS = 'PENDING'
                       AND RQ.REQUEST_TYPE = 'LEAVE'
                       AND (LR.LEAVE_TYPE <> 'CT' OR R.ROLE_NAME IN ('Manager','Expat','Admin'))
                       AND EXISTS (SELECT 1 FROM HRMS.HR_USERS_DEPT D2
                                    WHERE D2.EMPCD = U.EMPCD
                                      AND D2.DEPTCD = EC2.DEPTCD
                                      AND D2.LINECD = EC2.LINECD
                                      AND D2.WORKCD = EC2.WORKCD)) AS PENDING_CNT
              FROM HRMS.HR_USERS U
              JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID
              -- LEFT JOIN chứ KHÔNG phải JOIN: 18 người duyệt (17 Expat + 1 Supervisor) không có bản ghi
              -- ca trong EBM300. Dùng JOIN thì họ bị loại hẳn, không bao giờ nhận tóm tắt — trong khi
              -- trước đây vẫn nhận từng đơn. Expat lại chính là nhóm duyệt đơn Công tác.
              -- Không có ca -> mặc định giờ hành chính 07:30-16:30 (ca HC, phổ biến nhất ở khối văn phòng).
              LEFT JOIN (
                    SELECT A.EMPCD, SH.STIME, SH.ETIME
                      FROM HRMS.EBM300 A
                      JOIN HRMS.EBM100 SH ON SH.SHIFTCD = A.SHIFTCD
                     WHERE A.DAT IN (TRUNC(SYSDATE), TRUNC(SYSDATE) - 1)
                       AND REGEXP_LIKE(SH.STIME, '^[0-9]{4}$') AND REGEXP_LIKE(SH.ETIME, '^[0-9]{4}$')
                       -- CA ĐÊM: ca 22:00-06:00 vắt qua nửa đêm nên bản ghi EBM300 nằm ở NGÀY HÔM TRƯỚC.
                       -- Lấy cứng TRUNC(SYSDATE) thì lúc 04:00 (mốc 2 của ca đêm) tra nhầm ca hôm nay
                       -- hoặc không có dòng nào -> 12 quản lý trực đêm bị bỏ sót.
                       -- Cùng cách xử lý wrap đã dùng ở màn hình Trang chủ (Scripts/home_plan.md 6.10).
                       AND A.DAT = CASE WHEN TO_NUMBER(TO_CHAR(SYSDATE,'HH24MI')) < TO_NUMBER(SH.STIME)
                                         AND TO_NUMBER(SH.STIME) > TO_NUMBER(SH.ETIME)
                                        THEN TRUNC(SYSDATE) - 1 ELSE TRUNC(SYSDATE) END
              ) S ON S.EMPCD = U.EMPCD
             WHERE U.IS_ACTIVE = 1
               AND R.ROLE_NAME IN ('Supervisor','DeputyManager','Manager','Expat')
               AND EXISTS (SELECT 1 FROM HRMS.HR_USERS_DEPT D WHERE D.EMPCD = U.EMPCD)";

        var rows = await db.ExecuteQueryAsync(sql, r => new
        {
            EmpCd   = r["EMPCD"]?.ToString() ?? "",
            STime   = r["STIME"]?.ToString() ?? "",
            ETime   = r["ETIME"]?.ToString() ?? "",
            Pending = r["PENDING_CNT"] == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING_CNT"])
        });

        var now = DateTime.Now;
        int sent = 0;

        foreach (var row in rows)
        {
            if (row.Pending <= 0 || string.IsNullOrEmpty(row.EmpCd)) continue;
            if (_onlyEmpCds.Count > 0 && !_onlyEmpCds.Contains(row.EmpCd)) continue;   // đang chạy thử
            if (!TryHhmm(row.STime, out int st) || !TryHhmm(row.ETime, out int et)) continue;

            // Hai mốc, tính bằng phút trong ngày rồi lấy dư 1440 để ca đêm không bị âm/tràn.
            int slot1 = ((st + 60) % 1440);
            int slot2 = ((et - 120) + 1440) % 1440;
            int nowM  = now.Hour * 60 + now.Minute;

            if (!NearSlot(nowM, slot1) && !NearSlot(nowM, slot2)) continue;
            if (await AlreadySentRecentlyAsync(db, row.EmpCd)) continue;

            noti.LeaveApprovalDigest(row.EmpCd, row.Pending);
            sent++;
        }

        if (sent > 0) _log.LogInformation("LeaveApprovalDigest: đã gửi {Sent} bản tóm tắt{Mode}",
            sent, _onlyEmpCds.Count > 0 ? " (CHẠY THỬ, chỉ danh sách giới hạn)" : "");
    }

    private static bool NearSlot(int nowMinutes, int slotMinutes)
    {
        int diff = Math.Abs(nowMinutes - slotMinutes);
        diff = Math.Min(diff, 1440 - diff);     // vòng qua nửa đêm
        return diff < WindowMinutes;
    }

    // STIME/ETIME trong EBM100 là chuỗi 'HHMM' (vd '0730'), không phải DATE.
    private static bool TryHhmm(string v, out int minutes)
    {
        minutes = 0;
        if (v.Length != 4 || !int.TryParse(v, out int n)) return false;
        int h = n / 100, m = n % 100;
        if (h > 23 || m > 59) return false;
        minutes = h * 60 + m;
        return true;
    }

    // Tránh gửi 2 lần trong cùng 1 mốc khi service khởi động lại hoặc tick trùng.
    // So theo ĐÚNG TIÊU ĐỀ bản tóm tắt, không phải mọi thông báo LINK_ACTION='LEAVE_MANAGE' —
    // nếu không sẽ bị chặn nhầm bởi các thông báo cũ (vd đợt bắn từng đơn trước khi đổi).
    private static async Task<bool> AlreadySentRecentlyAsync(OracleService db, string empCd)
    {
        var n = await db.ExecuteQueryAsync(@"
            SELECT COUNT(*) AS C FROM HRMS.HR_NOTIFICATIONS
             WHERE NOTI_TYPE = 'EMPCD' AND TARGET_VAL = :AP
               AND LINK_ACTION = 'LEAVE_MANAGE'
               AND TITLE = (SELECT TITLE_VI FROM HRMS.HR_NOTI_TEMPLATES
                             WHERE TEMPLATE_KEY = 'LEAVE_APPROVAL_DIGEST' AND ROWNUM = 1)
               AND CREATED_DATE >= SYSDATE - 3/24",
            r => Convert.ToInt32(r["C"]),
            new OracleParameter("AP", empCd));
        return n.FirstOrDefault() > 0;
    }
}
