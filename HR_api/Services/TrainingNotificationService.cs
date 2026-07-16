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

    // Enqueue TRAINING_ASSIGNED cho học viên ENROLLED chưa có noti — 1 câu INSERT-SELECT set-based
    // thay vì loop N lần INSERT (lớp 800+ học viên bấm "Lên lịch học" bị treo hàng chục giây vì
    // mỗi INSERT 1 round-trip). Placeholders giống nhau cho cả lớp nên serialize 1 lần là đủ.
    // Trả về số row đã enqueue.
    public async Task<int> EnqueueAssignedForMissingAsync(int classId, Dictionary<string, string> placeholders)
    {
        if (!_enabled) return 0;   // kill switch

        var json = JsonSerializer.Serialize(placeholders);
        return await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_NOTI_QUEUE
                (TEMPLATE_KEY, TARGET_EMPCD, PLACEHOLDERS, RELATED_CLASS_ID, STATUS, NEXT_ATTEMPT_DT)
            SELECT 'TRAINING_ASSIGNED', E.EMPCD, :PH, E.CLASS_ID, 'PENDING', SYSDATE
              FROM HRMS.HR_TRAINING_ENROLLMENT E
             WHERE E.CLASS_ID = :CID
               AND E.STATUS = 'ENROLLED'
               AND NOT EXISTS (
                   SELECT 1 FROM HRMS.HR_TRAINING_NOTI_QUEUE Q
                    WHERE Q.RELATED_CLASS_ID = E.CLASS_ID
                      AND Q.TARGET_EMPCD     = E.EMPCD
                      AND Q.TEMPLATE_KEY     = 'TRAINING_ASSIGNED'
                      AND Q.STATUS IN ('PENDING','CLAIMED','SENT'))",
            new OracleParameter("PH",  json),
            new OracleParameter("CID", classId));
    }

    // Enqueue cho tất cả học viên ENROLLED của Class (filter group nếu event là session-specific §5b.3).
    // Lớp CHƯA công bố (DRAFT/OPEN_FOR_REGISTRATION) → im lặng: HR còn soạn nháp (sửa/xóa buổi,
    // đổi DS...), học viên chưa được báo assign thì không được nhận noti sự kiện của lớp.
    public async Task EnqueueForClassEnrollmentsAsync(
        int classId,
        string templateKey,
        Dictionary<string, string>? placeholders = null,
        int? sessionGroupId = null,
        int? sessionId = null,
        int? testId = null)
    {
        var classStatus = (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_CLASS WHERE ID = :CID",
            r => r["STATUS"]?.ToString() ?? "",
            new OracleParameter("CID", classId))).FirstOrDefault();
        if (classStatus is "DRAFT" or "OPEN_FOR_REGISTRATION") return;

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
