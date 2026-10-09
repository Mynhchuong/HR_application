using HR_api.Data;
using HR_api.Services;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 1 phút: HR_PAYROLL_PERIOD có IS_AUTO_PUBLISH=1 (HR tick "Hẹn giờ tự động công bố" lúc tạo kỳ
// lương) + PUBLISH_DATE <= SYSDATE + chưa IS_PUBLISHED → tự công bố, đúng y hệt hành động HR bấm nút
// "Công bố" tay (PayslipService.ReleasePeriodAsync — dùng chung, kể cả gửi thông báo).
//
// Trước đây tính năng "Hẹn giờ công bố" chỉ LƯU PUBLISH_DATE/IS_AUTO_PUBLISH vào DB lúc tạo kỳ lương
// nhưng KHÔNG CÓ GÌ đọc lại 2 cột này — HR tick hẹn giờ xong, tới giờ đó vĩnh viễn không có gì xảy
// ra, phải tự vào bấm "Công bố" tay như không hẹn giờ gì cả (audit 2026-10-09). Thêm job này để
// tính năng thật sự hoạt động như tên gọi.
public class PayslipLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PayslipLifecycleService> _log;

    public PayslipLifecycleService(IServiceScopeFactory scopeFactory, ILogger<PayslipLifecycleService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("PayslipLifecycleService started");

        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(stop); }
            catch (Exception ex) { _log.LogError(ex, "PayslipLifecycle tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken stop)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp      = scope.ServiceProvider;
        var db      = sp.GetRequiredService<OracleService>();
        var payslip = sp.GetRequiredService<PayslipService>();

        var toPublish = await db.ExecuteQueryAsync(@"
            SELECT ID, UPDT_ID
              FROM HRMS.HR_PAYROLL_PERIOD
             WHERE IS_AUTO_PUBLISH = 1
               AND IS_PUBLISHED = 0
               AND PUBLISH_DATE IS NOT NULL
               AND PUBLISH_DATE <= SYSDATE",
            r => new
            {
                Id     = Convert.ToDecimal(r["ID"]),
                UpdtId = r["UPDT_ID"]?.ToString(),
            });

        foreach (var p in toPublish)
        {
            if (stop.IsCancellationRequested) return;
            try
            {
                // ReleasePeriodAsync tự claim bằng UPDATE ... WHERE IS_PUBLISHED = 0 — an toàn nếu
                // HR lỡ bấm công bố tay đúng lúc tick này chạy, không gửi thông báo trùng.
                bool released = await payslip.ReleasePeriodAsync(p.Id, p.UpdtId ?? "SYSTEM");
                if (released)
                    _log.LogInformation("Payroll period {Id} tự động công bố theo giờ hẹn", p.Id);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auto-publish payroll period {Id} failed", p.Id);
            }
        }
    }
}
