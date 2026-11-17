using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 15 phút: xoá hội thoại hộp thư không có tin nhắn người thật nào (chỉ có tin SYS, vd tin đóng
// hội thoại) sau 1 tiếng — NV bấm tạo rồi bỏ đi, không gõ gì, làm rác danh sách/dashboard/báo cáo CSR.
// Chỉ xoá tin SYS; nếu NV gửi tin đúng lúc đang xoá thì DELETE cha không khớp NOT EXISTS → giữ nguyên.
public class InquiryEmptyCleanupService : BackgroundService
{
    private const double EmptyAgeHours = 1;

    private static readonly string[] ChildTables =
    {
        "HR_INQUIRY_READER", "HR_INQUIRY_DRAFT", "HR_INQUIRY_REMIND_LOG", "HR_INQUIRY_ATTACH", "HR_INQUIRY_MSG_REF",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InquiryEmptyCleanupService> _log;

    public InquiryEmptyCleanupService(IServiceScopeFactory scopeFactory, ILogger<InquiryEmptyCleanupService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { _log.LogError(ex, "InquiryEmptyCleanup tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OracleService>();

        var ids = await db.ExecuteQueryAsync(@"
            SELECT i.ID FROM HRMS.HR_INQUIRY i
            WHERE i.INST_DT < SYSDATE - :HRS / 24
              AND NOT EXISTS (SELECT 1 FROM HRMS.HR_INQUIRY_MSG m WHERE m.INQUIRY_ID = i.ID AND m.SENDER_TYPE <> 'SYS')",
            r => Convert.ToInt64(r["ID"]),
            new OracleParameter("HRS", EmptyAgeHours));

        foreach (var id in ids)
        {
            try
            {
                foreach (var t in ChildTables)
                    await db.ExecuteNonQueryAsync($"DELETE FROM HRMS.{t} WHERE INQUIRY_ID = :ID", new OracleParameter("ID", id));
                await db.ExecuteNonQueryAsync(
                    "DELETE FROM HRMS.HR_INQUIRY_MSG WHERE INQUIRY_ID = :ID AND SENDER_TYPE = 'SYS'",
                    new OracleParameter("ID", id));

                int rows = await db.ExecuteNonQueryAsync(@"
                    DELETE FROM HRMS.HR_INQUIRY i WHERE i.ID = :ID
                      AND NOT EXISTS (SELECT 1 FROM HRMS.HR_INQUIRY_MSG m WHERE m.INQUIRY_ID = i.ID)",
                    new OracleParameter("ID", id));
                if (rows > 0) _log.LogInformation("Inquiry {Id} → auto deleted (no messages)", id);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Inquiry {Id} empty cleanup failed", id);
            }
        }
    }
}
