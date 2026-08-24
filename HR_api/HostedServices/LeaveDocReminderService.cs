using HR_api.Data;
using HR_api.Services;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 1 phút — nhắc nhở nhân viên nghỉ Đám tang/Đám cưới/Vợ sanh/Khám thai (DT/DC/VS/KT)
// đã qua TO_DATE + 3 ngày mà chưa nộp giấy tờ cho phòng Nhân sự. Chỉ quét sau 9h sáng
// (gate theo giờ, tránh chạy nhiều lần vô ích khi tick mỗi phút). Nhắc lặp lại mỗi 3 ngày
// (dedupe qua DOC_REMINDED_DATE) cho tới khi HR cập nhật DOC_STATUS='SUBMITTED'.
public class LeaveDocReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveDocReminderService> _log;

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

        var rows = await db.ExecuteQueryAsync(@"
            SELECT L.REQUEST_ID, L.EMPCD, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
            WHERE L.LEAVE_TYPE IN ('DT','DC','VS','KT')
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
