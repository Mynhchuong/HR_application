using HR_api.Data;
using HR_api.Services;

namespace HR_api.HostedServices;

// Cron 1 phút: bản tin IS_PUBLISHED=1 nhưng PUBLISH_TO đã qua → tự động rút bài (IS_PUBLISHED=0).
// Trước đây bản tin hết hạn chỉ hiện badge "Hết hạn" ở Manage nhưng IS_PUBLISHED không đổi, nên
// Delete (yêu cầu IS_PUBLISHED=0) không xoá được bản tin hết hạn nếu admin quên bấm "Rút bài" tay.
// Cùng convention "active tới 23:59 PUBLISH_TO" như SurveyLifecycleService (so END_DATE).
public class BulletinLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BulletinLifecycleService> _log;

    public BulletinLifecycleService(IServiceScopeFactory scopeFactory, ILogger<BulletinLifecycleService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("BulletinLifecycleService started");

        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { _log.LogError(ex, "BulletinLifecycle tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var sp  = scope.ServiceProvider;
        var db  = sp.GetRequiredService<OracleService>();
        var svc = sp.GetRequiredService<BulletinService>();

        var toExpire = await db.ExecuteQueryAsync(@"
            SELECT ID FROM HRMS.HR_BULLETIN
             WHERE IS_PUBLISHED = 1
               AND IS_ACTIVE    = 1
               AND PUBLISH_TO   < TRUNC(SYSDATE)",
            r => Convert.ToInt32(r["ID"]));

        foreach (var id in toExpire)
        {
            var (ok, msg) = await svc.UnpublishAsync(id, "SYSTEM");
            if (ok) _log.LogInformation("Bulletin {Id} → auto unpublished (expired)", id);
            else    _log.LogWarning("Bulletin {Id} auto unpublish failed: {Msg}", id, msg);
        }
    }
}
