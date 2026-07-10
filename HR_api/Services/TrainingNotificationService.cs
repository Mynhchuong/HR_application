using System.Text.Json;
using HR_api.Data;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Enqueue helper cho HR_TRAINING_NOTI_QUEUE (§13).
// KHÔNG gọi FCM trực tiếp — chỉ INSERT PENDING row, TrainingNotificationWorker sẽ send.
// Design này chịu được FCM down: noti stack lên queue, retry với backoff.
//
// KILL SWITCH: appsettings `Training:NotificationsEnabled = false` → EnqueueAsync no-op.
// Tránh spam user khi dev/test. Bật lại khi go-live.
public class TrainingNotificationService
{
    private readonly OracleService _db;
    private readonly bool _enabled;

    public TrainingNotificationService(OracleService db, IConfiguration config)
    {
        _db = db;
        _enabled = config.GetValue<bool>("Training:NotificationsEnabled", defaultValue: false);
    }

    // Enqueue 1 noti cho 1 EMPCD với placeholders. Idempotent: caller kiểm soát dedup (VD dùng
    // combo TEMPLATE_KEY + TARGET_EMPCD + RELATED_*_ID trước khi enqueue nếu cần).
    public async Task EnqueueAsync(
        string templateKey,
        string targetEmpcd,
        Dictionary<string, string>? placeholders = null,
        int? classId = null,
        int? sessionId = null,
        int? testId = null)
    {
        if (!_enabled) return;   // kill switch — không tốn INSERT

        var json = placeholders == null || placeholders.Count == 0 ? null
                 : JsonSerializer.Serialize(placeholders);

        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_NOTI_QUEUE
                (TEMPLATE_KEY, TARGET_EMPCD, PLACEHOLDERS,
                 RELATED_CLASS_ID, RELATED_SESSION_ID, RELATED_TEST_ID,
                 STATUS, NEXT_ATTEMPT_DT)
            VALUES (:TK, :EMP, :PH, :CID, :SID, :TID, 'PENDING', SYSDATE)",
            new OracleParameter("TK",  templateKey),
            new OracleParameter("EMP", targetEmpcd),
            new OracleParameter("PH",  (object?)json ?? DBNull.Value),
            new OracleParameter("CID", (object?)classId   ?? DBNull.Value),
            new OracleParameter("SID", (object?)sessionId ?? DBNull.Value),
            new OracleParameter("TID", (object?)testId    ?? DBNull.Value));
    }

    // Bulk enqueue cho N EMPCDs — dùng cho các event class-wide (assign, publish class, class completed).
    public async Task EnqueueBulkAsync(
        string templateKey,
        IEnumerable<string> targetEmpcds,
        Dictionary<string, string>? placeholders = null,
        int? classId = null,
        int? sessionId = null,
        int? testId = null)
    {
        foreach (var emp in targetEmpcds.Distinct())
            await EnqueueAsync(templateKey, emp, placeholders, classId, sessionId, testId);
    }

    // Enqueue cho tất cả học viên ENROLLED của Class (filter group nếu event là session-specific §5b.3)
    public async Task EnqueueForClassEnrollmentsAsync(
        int classId,
        string templateKey,
        Dictionary<string, string>? placeholders = null,
        int? sessionGroupId = null,
        int? sessionId = null,
        int? testId = null)
    {
        var empcds = await _db.ExecuteQueryAsync(@"
            SELECT EMPCD FROM HRMS.HR_TRAINING_ENROLLMENT
             WHERE CLASS_ID = :CID
               AND STATUS = 'ENROLLED'
               AND (:GID IS NULL OR GROUP_ID = :GID)",
            r => r["EMPCD"]?.ToString() ?? "",
            new OracleParameter("CID", classId),
            new OracleParameter("GID", (object?)sessionGroupId ?? DBNull.Value));

        await EnqueueBulkAsync(templateKey, empcds, placeholders, classId, sessionId, testId);
    }
}
