using HR_api.Data;
using HR_api.Services;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 1 phút — 2 việc:
//   1) ĐỐI CHIẾU ERP: với 5 loại có quy tắc rõ ràng từ HR (SI/DT/VS/KT/DC), kiểm tra HR đã đổi
//      Absent Code / remark trên ERP (EFM410) đúng như bảng quy ước chưa — nếu khớp thì tự đánh dấu
//      DOC_STATUS='SUBMITTED', không cần HR bấm tay trên MySamho nữa. Áp dụng luôn cho các đơn CŨ
//      đã có sẵn từ trước (backfill) lẫn đơn mới phát sinh sau này.
//   2) NHẮC NHỞ: nhân viên nghỉ Đám tang/Đám cưới/Vợ sanh/Khám thai/Bệnh có giấy/Dưỡng sức
//      (DT/DC/VS/KT/SI/DS) đã qua TO_DATE + 3 ngày mà chưa nộp giấy tờ (sau bước 1 vẫn chưa
//      SUBMITTED — DS không detect được qua ERP nên luôn rơi vào nhánh này cho tới khi HR bấm tay).
// Chỉ quét sau 9h sáng (gate theo giờ, tránh chạy nhiều lần vô ích khi tick mỗi phút). Nhắc lặp lại
// mỗi 3 ngày (dedupe qua DOC_REMINDED_DATE) cho tới khi DOC_STATUS='SUBMITTED'.
public class LeaveDocReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveDocReminderService> _log;

    // Loại nghỉ đối chiếu tự động được (HR đã xác nhận bảng quy ước Absent Code/remark 2026-09-10).
    // Dưỡng sức (DS) chưa có quy ước rõ ràng từ HR nên KHÔNG đưa vào đây — vẫn chờ HR bấm tay.
    private static readonly string[] ErpAutoDetectTypes = { "SI", "DT", "VS", "KT", "DC" };

    public LeaveDocReminderService(IServiceScopeFactory scopeFactory, ILogger<LeaveDocReminderService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("LeaveDocReminderService started");
        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(stop); }
            catch (Exception ex) { _log.LogError(ex, "LeaveDocReminder tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken stop)
    {
        if (DateTime.Now.Hour < 9) return;   // gate: chỉ quét từ 9h sáng trở đi

        using var scope = _scopeFactory.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<OracleService>();
        var noti = scope.ServiceProvider.GetRequiredService<NotificationService>();

        await AutoMarkFromErpAsync(db, stop);
        await SendOverdueRemindersAsync(db, noti, stop);
    }

    // ── 1) Đối chiếu ERP, tự đánh dấu SUBMITTED ─────────────────────────────
    private async Task AutoMarkFromErpAsync(OracleService db, CancellationToken stop)
    {
        var candidates = await db.ExecuteQueryAsync(@"
            SELECT L.REQUEST_ID, L.EMPCD, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
            WHERE L.LEAVE_TYPE IN ('SI','DT','VS','KT','DC')
              AND R.STATUS IN ('APPROVED','ASSIGNED')
              AND (L.SOURCE = 'SELF' OR NVL(L.CONFIRM_STATUS,'X') != 'WORKER_REJECTED')
              AND NVL(L.DOC_STATUS,'X') != 'SUBMITTED'",
            r => new
            {
                RequestId = r["REQUEST_ID"]?.ToString() ?? "",
                Empcd     = r["EMPCD"]?.ToString() ?? "",
                LeaveType = r["LEAVE_TYPE"]?.ToString() ?? "",
                FromDate  = Convert.ToDateTime(r["FROM_DATE"]),
                ToDate    = Convert.ToDateTime(r["TO_DATE"])
            });

        foreach (var c in candidates)
        {
            if (stop.IsCancellationRequested) return;
            try { await TryAutoMarkSubmittedAsync(db, c.RequestId, c.Empcd, c.LeaveType, c.FromDate, c.ToDate); }
            catch (Exception ex) { _log.LogError(ex, "AutoMarkFromErp failed for {ReqId}", c.RequestId); }
        }
    }

    private async Task<bool> TryAutoMarkSubmittedAsync(OracleService db, string requestId, string empcd, string leaveType, DateTime fromDate, DateTime toDate)
    {
        var efmRows = await db.ExecuteQueryAsync(
            "SELECT LEAVECD, REMAR FROM HRMS.EFM410 WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FROM_D AND :TO_D",
            r => new { leavecd = r["LEAVECD"]?.ToString() ?? "", remar = r["REMAR"]?.ToString() },
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("FROM_D", OracleDbType.Date) { Value = fromDate },
            new OracleParameter("TO_D",   OracleDbType.Date) { Value = toDate });

        // Chưa có dòng ERP nào cho khoảng ngày này (chưa insert, hoặc đang chờ duyệt) → chưa kết luận được.
        if (efmRows.Count == 0) return false;

        // Chỉ kết luận "đã nộp" khi TẤT CẢ các ngày trong khoảng đều đã được HR đổi đúng mã/remark xác
        // nhận — tránh dương tính giả khi ERP mới cập nhật 1 phần (đang xử lý dở).
        if (!efmRows.All(x => IsDocConfirmedInErp(leaveType, x.leavecd, x.remar))) return false;

        await db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_LEAVE_REQUEST
            SET DOC_STATUS = 'SUBMITTED', DOC_SUBMITTED_DATE = SYSDATE, DOC_SUBMITTED_BY = 'ERP_AUTO'
            WHERE REQUEST_ID = :ID",
            new OracleParameter("ID", requestId));
        return true;
    }

    // Bảng quy ước Absent Code / remark do HR xác nhận (Excel 2026-09-10), đã đối chiếu khớp data thật:
    //   SI  → LEAVECD vẫn 'CP', remark đổi thành chứa "BHXH"
    //   DT  → LEAVECD đổi thành TC/TCR/TKH/TMR/TVC (theo quan hệ với người mất)
    //   VS  → LEAVECD đổi thành VS, hoặc CT kèm remark chứa "SANH" (ví dụ "VO SANH")
    //   KT  → LEAVECD đổi thành KT, hoặc CT kèm remark chứa "THAI" (ví dụ "KHAÙM THAI lần N")
    //   DC  → LEAVECD đổi thành CC hoặc DC
    private static bool IsDocConfirmedInErp(string leaveType, string leavecd, string? remar)
    {
        var r = (remar ?? "").ToUpperInvariant();
        return leaveType switch
        {
            "SI" => leavecd == "CP" && r.Contains("BHXH"),
            "DT" => leavecd is "TC" or "TCR" or "TKH" or "TMR" or "TVC",
            "VS" => leavecd == "VS" || (leavecd == "CT" && r.Contains("SANH")),
            "KT" => leavecd == "KT" || (leavecd == "CT" && r.Contains("THAI")),
            "DC" => leavecd is "CC" or "DC",
            _    => false
        };
    }

    // ── 2) Nhắc nhở đơn quá hạn vẫn chưa nộp ────────────────────────────────
    private async Task SendOverdueRemindersAsync(OracleService db, NotificationService noti, CancellationToken stop)
    {
        var rows = await db.ExecuteQueryAsync(@"
            SELECT L.REQUEST_ID, L.EMPCD, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
            WHERE L.LEAVE_TYPE IN ('DT','DC','VS','KT','SI','DS')
              AND R.STATUS IN ('APPROVED','ASSIGNED')
              AND (L.SOURCE = 'SELF' OR NVL(L.CONFIRM_STATUS,'X') != 'WORKER_REJECTED')
              AND L.TO_DATE < TRUNC(SYSDATE) - 3
              AND NVL(L.DOC_STATUS,'X') != 'SUBMITTED'
              AND (L.DOC_REMINDED_DATE IS NULL OR L.DOC_REMINDED_DATE < TRUNC(SYSDATE) - 3)",
            r => new
            {
                RequestId = r["REQUEST_ID"]?.ToString() ?? "",
                Empcd     = r["EMPCD"]?.ToString() ?? "",
                LeaveType = r["LEAVE_TYPE"]?.ToString() ?? "",
                FromDate  = Convert.ToDateTime(r["FROM_DATE"]),
                ToDate    = Convert.ToDateTime(r["TO_DATE"])
            });

        foreach (var row in rows)
        {
            if (stop.IsCancellationRequested) return;

            noti.LeaveDocReminder(row.Empcd, row.LeaveType, row.FromDate, row.ToDate);

            await db.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_LEAVE_REQUEST SET DOC_REMINDED_DATE = TRUNC(SYSDATE) WHERE REQUEST_ID = :ID",
                new OracleParameter("ID", row.RequestId));
        }
    }
}
