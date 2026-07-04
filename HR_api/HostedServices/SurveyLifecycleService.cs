using HR_api.Data;
using HR_api.Services;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 1 phút:
//   • SCHEDULED + START_DATE <= SYSDATE → snapshot recipient + set ACTIVE + FCM SurveyPublished
//   • ACTIVE/PAUSED + END_DATE  <  SYSDATE → auto-submit IN_PROGRESS + compute score + set EXPIRED
//
// OracleService là scoped nên phải resolve trong scope mỗi tick.
public class SurveyLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SurveyLifecycleService> _log;

    public SurveyLifecycleService(IServiceScopeFactory scopeFactory, ILogger<SurveyLifecycleService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("SurveyLifecycleService started");

        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(stop); }
            catch (Exception ex) { _log.LogError(ex, "SurveyLifecycle tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken stop)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp        = scope.ServiceProvider;
        var db        = sp.GetRequiredService<OracleService>();
        var recipient = sp.GetRequiredService<SurveyRecipientService>();
        var score     = sp.GetRequiredService<SurveyScoreService>();
        var noti      = sp.GetRequiredService<NotificationService>();

        // ── 1. SCHEDULED → ACTIVE ─────────────────────────────────────
        var toActivate = await db.ExecuteQueryAsync(@"
            SELECT ID, TITLE, PUBLISHED_BY
              FROM HRMS.HR_SURVEY
             WHERE STATUS = 'SCHEDULED'
               AND START_DATE IS NOT NULL
               AND START_DATE <= SYSDATE",
            r => new
            {
                Id       = Convert.ToInt32(r["ID"]),
                Title    = r["TITLE"]?.ToString() ?? "",
                PubBy    = r["PUBLISHED_BY"]?.ToString() ?? "SYSTEM",
            });

        foreach (var s in toActivate)
        {
            if (stop.IsCancellationRequested) return;
            try
            {
                // Claim survey đầu tiên bằng UPDATE có ràng buộc STATUS='SCHEDULED'.
                // Nếu worker khác đã UPDATE thành ACTIVE trước → row count = 0 → skip.
                int claimed = await db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_SURVEY
                       SET STATUS = 'ACTIVE', UPDT_ID = 'SYSTEM'
                     WHERE ID = :ID AND STATUS = 'SCHEDULED'",
                    new OracleParameter("ID", s.Id));

                if (claimed == 0)
                {
                    _log.LogDebug("Survey {Id} đã bị worker khác activate — skip", s.Id);
                    continue;
                }

                var snapCount = await recipient.SnapshotAsync(s.Id);
                _log.LogInformation("Survey {Id} → ACTIVE (snapshot {Count} recipients)", s.Id, snapCount);
                noti.SurveyPublished(s.Id, s.Title, s.PubBy);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Activate survey {Id} failed", s.Id);
            }
        }

        // ── 2. ACTIVE/PAUSED → EXPIRED ───────────────────────────────
        // Rules §13: active tới 23:59 END_DATE.
        // END_DATE lưu 00:00 của ngày cuối → chỉ expire khi TRUNC(SYSDATE) đã > END_DATE.
        var toExpire = await db.ExecuteQueryAsync(@"
            SELECT ID, SURVEY_TYPE
              FROM HRMS.HR_SURVEY
             WHERE STATUS IN ('ACTIVE','PAUSED')
               AND END_DATE IS NOT NULL
               AND END_DATE < TRUNC(SYSDATE)",
            r => new
            {
                Id   = Convert.ToInt32(r["ID"]),
                Type = r["SURVEY_TYPE"]?.ToString() ?? "POLL",
            });

        foreach (var s in toExpire)
        {
            if (stop.IsCancellationRequested) return;
            try
            {
                // Claim trước để tránh 2 worker cùng expire
                int claimed = await db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_SURVEY
                       SET STATUS = 'EXPIRED', UPDT_ID = 'SYSTEM'
                     WHERE ID = :ID AND STATUS IN ('ACTIVE','PAUSED')",
                    new OracleParameter("ID", s.Id));

                if (claimed == 0)
                {
                    _log.LogDebug("Survey {Id} đã bị worker khác expire — skip", s.Id);
                    continue;
                }

                // Auto-submit tất cả IN_PROGRESS
                await db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_SURVEY_RESPONSE
                       SET STATUS = 'AUTO_SUBMITTED', SUBMIT_DT = SYSDATE
                     WHERE SURVEY_ID = :SID AND STATUS = 'IN_PROGRESS'",
                    new OracleParameter("SID", s.Id));

                // Compute score cho QUIZ (POLL không cần)
                if (s.Type == "QUIZ")
                {
                    var responseIds = await db.ExecuteQueryAsync(@"
                        SELECT ID FROM HRMS.HR_SURVEY_RESPONSE
                         WHERE SURVEY_ID = :SID AND STATUS = 'AUTO_SUBMITTED' AND SCORE IS NULL",
                        r => Convert.ToInt32(r["ID"]),
                        new OracleParameter("SID", s.Id));

                    foreach (var rid in responseIds)
                    {
                        var (sc, mx, pass) = await score.ComputeAsync(rid);
                        await db.ExecuteNonQueryAsync(@"
                            UPDATE HRMS.HR_SURVEY_RESPONSE
                               SET SCORE = :SC, MAX_SCORE = :MX, IS_PASS = :PS
                             WHERE ID = :ID",
                            new OracleParameter("SC", (object?)sc   ?? DBNull.Value),
                            new OracleParameter("MX", (object?)mx   ?? DBNull.Value),
                            new OracleParameter("PS", (object?)pass ?? DBNull.Value),
                            new OracleParameter("ID", rid));
                    }
                }

                _log.LogInformation("Survey {Id} → EXPIRED", s.Id);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Expire survey {Id} failed", s.Id);
            }
        }
    }
}
