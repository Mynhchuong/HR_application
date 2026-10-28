using HR_api.Services;

namespace HR_api.HostedServices;

// Cron 1 phút — gửi Excel đăng ký Kaizen qua email cho bộ phận Lean mỗi THỨ SÁU từ 08:00 (yêu cầu 2026-10-28).
// Toàn bộ điều kiện (đúng thứ Sáu, đã qua 8h, hôm nay chưa gửi OK, có người nhận) nằm trong
// KaizenMailService.TryRunWeeklyAsync, dựa vào giờ DB + bảng HR_KAIZEN_MAIL_LOG để không gửi trùng khi IIS khởi động lại.
// Chạy trong tiến trình HR_api nên Application Pool IIS cần Start Mode = AlwaysRunning và Idle Time-out = 0.
public class KaizenWeeklyMailService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KaizenWeeklyMailService> _log;

    public KaizenWeeklyMailService(IServiceScopeFactory scopeFactory, ILogger<KaizenWeeklyMailService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("KaizenWeeklyMailService started");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<KaizenMailService>().TryRunWeeklyAsync();
            }
            catch (Exception ex) { _log.LogError(ex, "KaizenWeeklyMail tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }
}
