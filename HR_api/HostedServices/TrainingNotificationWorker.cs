using System.Text.Json;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Notification;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 30s — dequeue HR_TRAINING_NOTI_QUEUE + send FCM + retry.
// Oracle 10: dùng UPDATE claim pattern (không có FOR UPDATE SKIP LOCKED).
// Backoff: retry=0 → +1min, 1 → +5min, 2 → +30min, 3 → +2h, 4 → +6h. retry>=5 → FAILED.
public class TrainingNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrainingNotificationWorker> _log;
    private readonly string _workerId;

    public TrainingNotificationWorker(IServiceScopeFactory scopeFactory, ILogger<TrainingNotificationWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
        _workerId = Environment.MachineName + "/" + Guid.NewGuid().ToString("N")[..8];
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("TrainingNotificationWorker started (workerId={W})", _workerId);
        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(stop); }
            catch (Exception ex) { _log.LogError(ex, "TrainingNotificationWorker tick failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken stop)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp     = scope.ServiceProvider;
        var db     = sp.GetRequiredService<OracleService>();
        var helper = sp.GetRequiredService<NotificationHelper>();
        var config = sp.GetRequiredService<IConfiguration>();

        // Kill switch — nếu tắt noti, worker skip toàn bộ (không recover CLAIMED, không claim mới).
        // Queue rows tồn tại từ trước cũng KHÔNG bị process → không burst khi bật lại.
        if (!config.GetValue<bool>("Training:NotificationsEnabled", defaultValue: false))
            return;

        // Recover stuck CLAIMED rows (>5 phút) — worker crash / timeout
        await db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_NOTI_QUEUE
               SET STATUS = 'PENDING', CLAIMED_BY = NULL, CLAIMED_DT = NULL
             WHERE STATUS = 'CLAIMED'
               AND CLAIMED_DT < SYSDATE - (5/1440)");

        // Claim 50 rows PENDING due for retry
        var claimed = await db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_NOTI_QUEUE
               SET STATUS = 'CLAIMED', CLAIMED_BY = :W, CLAIMED_DT = SYSDATE
             WHERE ID IN (
                 SELECT ID FROM (
                     SELECT ID FROM HRMS.HR_TRAINING_NOTI_QUEUE
                      WHERE STATUS = 'PENDING'
                        AND NEXT_ATTEMPT_DT <= SYSDATE
                      ORDER BY NEXT_ATTEMPT_DT
                 ) WHERE ROWNUM <= 50
             )
               AND STATUS = 'PENDING'",
            new OracleParameter("W", _workerId));

        if (claimed == 0) return;

        var rows = await db.ExecuteQueryAsync(@"
            SELECT ID, TEMPLATE_KEY, TARGET_EMPCD, PLACEHOLDERS,
                   RELATED_CLASS_ID, RETRY_COUNT
              FROM HRMS.HR_TRAINING_NOTI_QUEUE
             WHERE CLAIMED_BY = :W AND STATUS = 'CLAIMED'",
            r => new
            {
                ID           = Convert.ToInt32(r["ID"]),
                TEMPLATE_KEY = r["TEMPLATE_KEY"]?.ToString() ?? "",
                TARGET_EMPCD = r["TARGET_EMPCD"]?.ToString() ?? "",
                PLACEHOLDERS = r["PLACEHOLDERS"] as string,
                CLASS_ID     = r["RELATED_CLASS_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["RELATED_CLASS_ID"]),
                RETRY_COUNT  = Convert.ToInt32(r["RETRY_COUNT"]),
            },
            new OracleParameter("W", _workerId));

        foreach (var row in rows)
        {
            if (stop.IsCancellationRequested) return;
            try
            {
                var placeholders = ParsePlaceholders(row.PLACEHOLDERS);
                var (titleVi, bodyVi, titleEn, bodyEn) = await helper.GetTemplateAsync(row.TEMPLATE_KEY, placeholders);

                // SendNotificationAsync catch exception internal, return -1 khi INSERT HR_NOTIFICATIONS fail.
                // Phải check return value — không throw.
                var notiId = await helper.SendNotificationAsync(new SendNotificationRequest
                {
                    TITLE       = titleVi,
                    BODY        = bodyVi,
                    TITLE_EN    = titleEn,
                    BODY_EN     = bodyEn,
                    NOTI_TYPE   = "EMPCD",
                    TARGET_VAL  = row.TARGET_EMPCD,
                    LINK_ACTION = LinkFor(row.TEMPLATE_KEY, row.CLASS_ID),
                    CREATED_BY  = "SYSTEM",
                });
                if (notiId < 0)
                    throw new Exception("SendNotificationAsync returned -1 (DB insert failed)");

                await db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_TRAINING_NOTI_QUEUE
                       SET STATUS = 'SENT', SENT_DT = SYSDATE,
                           CLAIMED_BY = NULL, CLAIMED_DT = NULL
                     WHERE ID = :ID",
                    new OracleParameter("ID", row.ID));
            }
            catch (Exception ex)
            {
                var newRetry = row.RETRY_COUNT + 1;
                if (newRetry >= 5)
                {
                    await db.ExecuteNonQueryAsync(@"
                        UPDATE HRMS.HR_TRAINING_NOTI_QUEUE
                           SET STATUS = 'FAILED', RETRY_COUNT = :RT,
                               LAST_ERROR = :ER,
                               CLAIMED_BY = NULL, CLAIMED_DT = NULL
                         WHERE ID = :ID",
                        new OracleParameter("RT", newRetry),
                        new OracleParameter("ER", Truncate(ex.Message, 500)),
                        new OracleParameter("ID", row.ID));
                    _log.LogError(ex, "Noti {Id} FAILED sau {N} retries", row.ID, newRetry);
                }
                else
                {
                    // Backoff: 1, 5, 30, 120, 360 phút
                    int minutes = newRetry switch { 1 => 1, 2 => 5, 3 => 30, 4 => 120, _ => 360 };
                    await db.ExecuteNonQueryAsync(@"
                        UPDATE HRMS.HR_TRAINING_NOTI_QUEUE
                           SET STATUS = 'PENDING', RETRY_COUNT = :RT,
                               LAST_ERROR = :ER,
                               NEXT_ATTEMPT_DT = SYSDATE + (:MIN / 1440),
                               CLAIMED_BY = NULL, CLAIMED_DT = NULL
                         WHERE ID = :ID",
                        new OracleParameter("RT",  newRetry),
                        new OracleParameter("ER",  Truncate(ex.Message, 500)),
                        new OracleParameter("MIN", minutes),
                        new OracleParameter("ID",  row.ID));
                    _log.LogWarning("Noti {Id} retry {N} sau {M} phút — {E}", row.ID, newRetry, minutes, ex.Message);
                }
            }
        }
    }

    private static Dictionary<string, string>? ParsePlaceholders(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    // LINK_ACTION mapping — front-end deep-link tương ứng.
    private static string LinkFor(string templateKey, int? classId) => templateKey switch
    {
        "TRAINING_ASSIGNED"           => classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_REGISTRATION_OPEN"  => classId.HasValue ? $"TRAINING_CLASS_REGISTER/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_REGISTER_APPROVED"  => classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_REGISTER_REJECTED"  => "TRAINING_MY_CLASSES",
        "TRAINING_SESSION_TODAY"      => "TRAINING_MY_CLASSES",
        "TRAINING_SESSION_SOON"       => "TRAINING_MY_CLASSES",
        "TRAINING_SESSION_RESCHEDULED"=> classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_SESSION_CANCELLED"  => classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_ABSENT"             => classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_TEST_OPEN"          => "TRAINING_MY_CLASSES",
        "TRAINING_TEST_GRADED"        => "TRAINING_MY_CLASSES",
        "TRAINING_CLASS_COMPLETED"    => classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        "TRAINING_DROPPED"            => "TRAINING_MY_CLASSES",
        "TRAINING_TEACHER_CONFIRM_REMINDER" => classId.HasValue ? $"TRAINING_CLASS/{classId}" : "TRAINING_MY_CLASSES",
        _                             => "TRAINING_MY_CLASSES",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
