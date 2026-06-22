using HR_web.Controllers;
using HR_web.Helpers;

namespace HR_web.Services;

public class InquiryTempCleanupService : BackgroundService
{
    private readonly ILogger<InquiryTempCleanupService> _logger;

    public InquiryTempCleanupService(ILogger<InquiryTempCleanupService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Delay 1 giờ khi app khởi động để tránh tranh chấp tài nguyên lúc startup
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    CleanupTempFiles();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[InquiryCleanup] Lỗi khi dọn file temp");
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // App đang tắt — thoát bình thường
        }
    }

    private void CleanupTempFiles()
    {
        string tempRoot = Path.Combine(ImageController.ShareRoot, "INQUIRY", "temp");

        try
        {
            using (new NetworkShareHelper(ImageController.ShareRoot, ImageController.ShareCred))
            {
                if (!Directory.Exists(tempRoot)) return;

                var cutoff = DateTime.Now.AddHours(-24);
                int deleted = 0;

                foreach (var sessionDir in Directory.GetDirectories(tempRoot))
                {
                    try
                    {
                        var info = new DirectoryInfo(sessionDir);
                        if (info.LastWriteTime < cutoff)
                        {
                            Directory.Delete(sessionDir, recursive: true);
                            deleted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[InquiryCleanup] Bỏ qua folder: {Dir}", sessionDir);
                    }
                }

                if (deleted > 0)
                    _logger.LogInformation("[InquiryCleanup] Đã xóa {Count} session temp folder", deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InquiryCleanup] Không kết nối được share để dọn file");
        }
    }
}
