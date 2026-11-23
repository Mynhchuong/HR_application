using HR_api.Services;

namespace HR_api.HostedServices;

// Cron 1 phút — tới giờ chốt HR đặt (mặc định 21:00) gửi email số suất ăn trưa ngày làm việc kế tiếp cho căn tin
// (yêu cầu 2026-11-23). Điều kiện (đã bật, đã qua giờ chốt, ngày đó chưa gửi OK, có người nhận) nằm trong
// CanteenDailyMailService.TryRunDailyAsync, dựa vào giờ DB + bảng HR_CANTEEN_MAIL_LOG để không gửi trùng khi IIS khởi động lại.
// Chạy trong tiến trình HR_api nên Application Pool IIS cần Start Mode = AlwaysRunning và Idle Time-out = 0.
public class CanteenDailyMailHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CanteenDailyMailHostedService> _log;

    public CanteenDailyMailHostedService(IServiceScopeFactory scopeFactory, ILogger<CanteenDailyMailHostedService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("CanteenDailyMailHostedService started");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CanteenDailyMailService>().TryRunDailyAsync();
            }
            catch (Exception ex) { _log.LogError(ex, "CanteenDailyMail tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }
}
