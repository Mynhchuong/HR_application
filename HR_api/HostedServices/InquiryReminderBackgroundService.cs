using HR_api.Services;

namespace HR_api.HostedServices;

// Kiểm tra mỗi 5 phút — gửi email nhắc PIC/CSR các hội thoại sắp đến hạn phản hồi (Phase 2). Mọi điều kiện (giờ gửi 08–18h,
// chưa nhắc, có người nhận, bảng nhật ký đã tạo) nằm trong InquiryReminderService.TryRunAsync; không gửi trùng nhờ HR_INQUIRY_REMIND_LOG.
// Chạy trong tiến trình HR_api → Application Pool IIS cần Start Mode = AlwaysRunning và Idle Time-out = 0 (giống Kaizen mail).
public class InquiryReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InquiryReminderBackgroundService> _log;

    public InquiryReminderBackgroundService(IServiceScopeFactory scopeFactory, ILogger<InquiryReminderBackgroundService> log)
    { _scopeFactory = scopeFactory; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("InquiryReminderBackgroundService started");
        try { await Task.Delay(TimeSpan.FromSeconds(30), stop); } catch (TaskCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<InquiryReminderService>().TryRunAsync();
            }
            catch (Exception ex) { _log.LogError(ex, "InquiryReminder tick failed"); }

            try
            {
                using var scope2 = _scopeFactory.CreateScope();
                await scope2.ServiceProvider.GetRequiredService<InquiryReminderService>().TryRunDraftDigestAsync();
            }
            catch (Exception ex) { _log.LogError(ex, "InquiryReminder draft digest failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(5), stop); }
            catch (TaskCanceledException) { break; }
        }
    }
}
