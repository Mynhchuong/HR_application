using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Session CRUD + Attendance flow.
// Session status: UPCOMING → ONGOING → COMPLETED / CANCELLED (batch job phase 5 auto)
// Attendance:
//   - Student check-in trong khung [START_TIME-15min .. END_TIME+15min] → PRESENT hoặc LATE (>START+15)
//   - Teacher confirm: set TEACHER_CONFIRMED=1
//   - Batch auto-mark absent §5.4 với group filter §5b.3
public class TrainingSessionService
{
    private readonly OracleService _db;
    private readonly TrainingNotificationService _noti;

    public TrainingSessionService(OracleService db, TrainingNotificationService noti)
    {
        _db = db;
        _noti = noti;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SESSION CRUD
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<SessionModel>> ListByClassAsync(int classId)
    {
        const string sql = @"
            SELECT S.ID, S.CLASS_ID, S.SESSION_NO, S.SESSION_DATE, S.START_TIME, S.END_TIME,
                   S.TOPIC, S.LOCATION, S.STATUS, S.GROUP_ID,
                   G.GROUP_NAME,
                   C.CLASS_NAME,
                   S.INST_ID, S.INST_DT, S.UPDT_ID, S.UPDT_DT
              FROM HRMS.HR_TRAINING_SESSION S
              LEFT JOIN HRMS.HR_TRAINING_CLASS C ON C.ID = S.CLASS_ID
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = S.GROUP_ID
             WHERE S.CLASS_ID = :CID
             ORDER BY S.SESSION_DATE, S.START_TIME";
        return await _db.ExecuteQueryAsync(sql, MapSession,
            new OracleParameter("CID", classId));
    }

    public async Task<SessionModel?> GetDetailAsync(int sessionId, string? empcd = null)
    {
        const string sql = @"
            SELECT S.ID, S.CLASS_ID, S.SESSION_NO, S.SESSION_DATE, S.START_TIME, S.END_TIME,
                   S.TOPIC, S.LOCATION, S.STATUS, S.GROUP_ID,
                   G.GROUP_NAME,
                   C.CLASS_NAME,
                   S.INST_ID, S.INST_DT, S.UPDT_ID, S.UPDT_DT
              FROM HRMS.HR_TRAINING_SESSION S
              LEFT JOIN HRMS.HR_TRAINING_CLASS C ON C.ID = S.CLASS_ID
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = S.GROUP_ID
             WHERE S.ID = :ID";
        var list = await _db.ExecuteQueryAsync(sql, MapSession, new OracleParameter("ID", sessionId));
        var s = list.FirstOrDefault();
        if (s == null) return null;

        if (!string.IsNullOrWhiteSpace(empcd))
        {
            s.ATTENDANCE_STATUS = (await _db.ExecuteQueryAsync(@"
                SELECT STATUS FROM HRMS.HR_TRAINING_ATTENDANCE
                 WHERE SESSION_ID = :SID AND EMPCD = :EMP",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("SID", sessionId),
                new OracleParameter("EMP", empcd))).FirstOrDefault();
        }

        return s;
    }

    public async Task<int> SaveAsync(SaveSessionRequest req)
    {
        if (req.START_TIME?.Length != 4 || req.END_TIME?.Length != 4)
            throw new InvalidOperationException("START_TIME/END_TIME phải 4 ký tự HHMM");
        if (string.Compare(req.END_TIME, req.START_TIME, StringComparison.Ordinal) <= 0)
            throw new InvalidOperationException("END_TIME phải sau START_TIME");

        // Verify GROUP_ID (nếu có) thuộc Class (§16)
        if (req.GROUP_ID.HasValue)
        {
            var ok = (await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE ID = :GID AND CLASS_ID = :CID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("GID", req.GROUP_ID.Value),
                new OracleParameter("CID", req.CLASS_ID))).First();
            if (ok == 0) throw new InvalidOperationException("Group không thuộc Class này");
        }

        if (req.ID == null)
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_SESSION
                    (CLASS_ID, SESSION_NO, SESSION_DATE, START_TIME, END_TIME,
                     TOPIC, LOCATION, STATUS, GROUP_ID, INST_ID)
                VALUES
                    (:CID, :SN, :DT, :ST, :ET, :TP, :LC, 'UPCOMING', :GID, :USR)
                RETURNING ID INTO :NEW_ID";
            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            try
            {
                await _db.ExecuteNonQueryAsync(sqlIns,
                    new OracleParameter("CID", req.CLASS_ID),
                    new OracleParameter("SN",  req.SESSION_NO),
                    new OracleParameter("DT",  req.SESSION_DATE.Date),
                    new OracleParameter("ST",  req.START_TIME),
                    new OracleParameter("ET",  req.END_TIME),
                    new OracleParameter("TP",  (object?)req.TOPIC ?? DBNull.Value),
                    new OracleParameter("LC",  (object?)req.LOCATION ?? DBNull.Value),
                    new OracleParameter("GID", (object?)req.GROUP_ID ?? DBNull.Value),
                    new OracleParameter("USR", req.LOGIN_USER),
                    idParam);
            }
            catch (OracleException ex) when (ex.Number == 1)   // ORA-00001 unique (CLASS_ID, SESSION_NO)
            {
                throw new InvalidOperationException($"SESSION_NO={req.SESSION_NO} đã tồn tại trong Class");
            }
            return OracleService.ConvertToInt(idParam.Value);
        }
        else
        {
            // Chỉ update khi UPCOMING (§5 rules — không sửa khi đã ONGOING/COMPLETED)
            var status = await GetStatusAsync(req.ID.Value)
                ?? throw new InvalidOperationException("Không tìm thấy session");
            if (status != "UPCOMING")
                throw new InvalidOperationException($"Session đang {status}, chỉ sửa được khi UPCOMING");

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_SESSION
                   SET SESSION_NO   = :SN,
                       SESSION_DATE = :DT,
                       START_TIME   = :ST,
                       END_TIME     = :ET,
                       TOPIC        = :TP,
                       LOCATION     = :LC,
                       GROUP_ID     = :GID,
                       UPDT_ID      = :USR
                 WHERE ID = :ID",
                new OracleParameter("SN",  req.SESSION_NO),
                new OracleParameter("DT",  req.SESSION_DATE.Date),
                new OracleParameter("ST",  req.START_TIME),
                new OracleParameter("ET",  req.END_TIME),
                new OracleParameter("TP",  (object?)req.TOPIC ?? DBNull.Value),
                new OracleParameter("LC",  (object?)req.LOCATION ?? DBNull.Value),
                new OracleParameter("GID", (object?)req.GROUP_ID ?? DBNull.Value),
                new OracleParameter("USR", req.LOGIN_USER),
                new OracleParameter("ID",  req.ID.Value));
            return req.ID.Value;
        }
    }

    public async Task RescheduleAsync(RescheduleSessionRequest req)
    {
        var session = await GetDetailAsync(req.ID)
            ?? throw new InvalidOperationException("Không tìm thấy session");
        if (session.STATUS != "UPCOMING")
            throw new InvalidOperationException($"Chỉ reschedule được khi UPCOMING, hiện {session.STATUS}");
        if (req.NEW_START_TIME?.Length != 4 || req.NEW_END_TIME?.Length != 4)
            throw new InvalidOperationException("START_TIME/END_TIME phải HHMM");

        var oldDate = session.SESSION_DATE.ToString("dd/MM/yyyy");
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_SESSION
               SET SESSION_DATE = :DT, START_TIME = :ST, END_TIME = :ET, UPDT_ID = :USR
             WHERE ID = :ID",
            new OracleParameter("DT",  req.NEW_DATE.Date),
            new OracleParameter("ST",  req.NEW_START_TIME),
            new OracleParameter("ET",  req.NEW_END_TIME),
            new OracleParameter("USR", req.LOGIN_USER),
            new OracleParameter("ID",  req.ID));

        var className = await GetClassNameAsync(session.CLASS_ID);
        await _noti.EnqueueForClassEnrollmentsAsync(session.CLASS_ID, "TRAINING_SESSION_RESCHEDULED",
            new Dictionary<string, string>
            {
                ["className"]    = className,
                ["sessionTopic"] = session.TOPIC ?? "",
                ["oldDate"]      = oldDate,
                ["newDate"]      = req.NEW_DATE.ToString("dd/MM/yyyy"),
            }, sessionGroupId: session.GROUP_ID, sessionId: req.ID);
    }

    public async Task CancelAsync(CancelSessionRequest req)
    {
        var session = await GetDetailAsync(req.ID)
            ?? throw new InvalidOperationException("Không tìm thấy session");
        if (session.STATUS == "COMPLETED" || session.STATUS == "CANCELLED")
            throw new InvalidOperationException($"Session đang {session.STATUS}, không cancel được");

        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_SESSION
               SET STATUS = 'CANCELLED', UPDT_ID = :USR
             WHERE ID = :ID",
            new OracleParameter("USR", req.LOGIN_USER),
            new OracleParameter("ID",  req.ID));

        var className = await GetClassNameAsync(session.CLASS_ID);
        await _noti.EnqueueForClassEnrollmentsAsync(session.CLASS_ID, "TRAINING_SESSION_CANCELLED",
            new Dictionary<string, string>
            {
                ["className"]    = className,
                ["sessionTopic"] = session.TOPIC ?? "",
                ["sessionDate"]  = session.SESSION_DATE.ToString("dd/MM/yyyy"),
            }, sessionGroupId: session.GROUP_ID, sessionId: req.ID);
    }

    private async Task<string> GetClassNameAsync(int classId)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT CLASS_NAME FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => r["CLASS_NAME"]?.ToString() ?? "",
            new OracleParameter("ID", classId))).FirstOrDefault() ?? "";
    }

    // ═══════════════════════════════════════════════════════════════
    //  ATTENDANCE
    // ═══════════════════════════════════════════════════════════════

    // Cho teacher / HR: session + danh sách học viên (filter group nếu session có GROUP_ID §5b.3)
    public async Task<SessionAttendanceView> GetAttendanceViewAsync(int sessionId)
    {
        var session = await GetDetailAsync(sessionId);
        if (session == null) return new SessionAttendanceView();

        // List học viên thuộc scope session (group filter). LEFT JOIN attendance để show row cả khi chưa có.
        // Bind session.GROUP_ID trực tiếp — tránh subquery redundant (§5b.3).
        const string sql = @"
            SELECT E.EMPCD, EC.CNAME AS EMP_NAME,
                   E.GROUP_ID, G.GROUP_NAME,
                   A.SESSION_ID, A.CHECKIN_TIME, A.STATUS, A.TEACHER_CONFIRMED,
                   A.CONFIRMED_BY, A.CONFIRMED_DT, A.NOTE
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = E.GROUP_ID
              LEFT JOIN HRMS.HR_TRAINING_ATTENDANCE A
                     ON A.SESSION_ID = :SID AND A.EMPCD = E.EMPCD
             WHERE E.CLASS_ID = :CID
               AND E.STATUS = 'ENROLLED'
               AND (:SGID IS NULL OR E.GROUP_ID = :SGID)
             ORDER BY E.EMPCD";

        var list = await _db.ExecuteQueryAsync(sql, r => new AttendanceModel
        {
            SESSION_ID        = sessionId,
            EMPCD             = r["EMPCD"]?.ToString() ?? "",
            EMP_NAME          = r["EMP_NAME"] as string,
            CHECKIN_TIME      = r["CHECKIN_TIME"] as DateTime?,
            STATUS            = r["STATUS"]?.ToString() ?? "ABSENT",
            TEACHER_CONFIRMED = r["TEACHER_CONFIRMED"] is DBNull ? 0 : Convert.ToInt32(r["TEACHER_CONFIRMED"]),
            CONFIRMED_BY      = r["CONFIRMED_BY"] as string,
            CONFIRMED_DT      = r["CONFIRMED_DT"] as DateTime?,
            NOTE              = r["NOTE"] as string,
            GROUP_ID          = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
            GROUP_NAME        = r["GROUP_NAME"] as string,
        },
        new OracleParameter("SID",  sessionId),
        new OracleParameter("CID",  session.CLASS_ID),
        new OracleParameter("SGID", (object?)session.GROUP_ID ?? DBNull.Value));

        return new SessionAttendanceView { SESSION = session, ATTENDANCE = list };
    }

    // Student check-in — cửa sổ [START_TIME-15min .. END_TIME+15min]. LATE nếu > START_TIME+15min.
    public async Task<(bool ok, string? error)> CheckInAsync(CheckInRequest req)
    {
        var session = await GetDetailAsync(req.SESSION_ID);
        if (session == null) return (false, "Không tìm thấy session");
        if (session.STATUS is "COMPLETED" or "CANCELLED")
            return (false, $"Session đang {session.STATUS}, không check-in được");

        // Verify student là ENROLLED và nằm trong scope group của session
        var okEnroll = (await _db.ExecuteQueryAsync(@"
            SELECT E.EMPCD FROM HRMS.HR_TRAINING_ENROLLMENT E
             WHERE E.CLASS_ID = :CID AND E.EMPCD = :EMP AND E.STATUS = 'ENROLLED'
               AND (:SGID IS NULL OR E.GROUP_ID = :SGID)",
            r => r["EMPCD"]?.ToString() ?? "",
            new OracleParameter("CID",  session.CLASS_ID),
            new OracleParameter("EMP",  req.EMPCD),
            new OracleParameter("SGID", (object?)session.GROUP_ID ?? DBNull.Value))).FirstOrDefault();
        if (okEnroll == null)
            return (false, "Bạn không thuộc scope buổi học này");

        // Compute check-in window
        var (startAt, endAt) = BuildSessionWindow(session);
        var now = DateTime.Now;
        var openFrom = startAt.AddMinutes(-15);
        var openTo   = endAt.AddMinutes(15);
        if (now < openFrom) return (false, "Chưa đến giờ điểm danh");
        if (now > openTo)   return (false, "Đã hết giờ điểm danh");

        var status = now > startAt.AddMinutes(15) ? "LATE" : "PRESENT";

        // MERGE upsert — check-in lần 2 update CHECKIN_TIME chỉ nếu chưa TEACHER_CONFIRMED
        await _db.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_TRAINING_ATTENDANCE T
            USING (SELECT :SID AS SID, :EMP AS EMP FROM DUAL) S
               ON (T.SESSION_ID = S.SID AND T.EMPCD = S.EMP)
            WHEN MATCHED THEN
              UPDATE SET CHECKIN_TIME = :NOW, STATUS = :ST
              WHERE T.TEACHER_CONFIRMED = 0
            WHEN NOT MATCHED THEN
              INSERT (SESSION_ID, EMPCD, CHECKIN_TIME, STATUS, TEACHER_CONFIRMED)
              VALUES (S.SID, S.EMP, :NOW, :ST, 0)",
            new OracleParameter("SID", req.SESSION_ID),
            new OracleParameter("EMP", req.EMPCD),
            new OracleParameter("NOW", now),
            new OracleParameter("ST",  status));
        return (true, null);
    }

    // Teacher confirm 1 học viên (§5)
    public async Task ConfirmAttendanceAsync(ConfirmAttendanceRequest req)
    {
        if (!IsValidAttendanceStatus(req.STATUS))
            throw new InvalidOperationException("STATUS phải PRESENT | LATE | ABSENT | EXCUSED");

        var sessionStatus = (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_SESSION WHERE ID = :SID",
            r => r["STATUS"]?.ToString() ?? "",
            new OracleParameter("SID", req.SESSION_ID))).FirstOrDefault();
        if (sessionStatus == "FINISHED")
        {
            throw new InvalidOperationException("Buổi học đã kết thúc, không thể cập nhật điểm danh");
        }

        await _db.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_TRAINING_ATTENDANCE T
            USING (SELECT :SID AS SID, :EMP AS EMP FROM DUAL) S
               ON (T.SESSION_ID = S.SID AND T.EMPCD = S.EMP)
            WHEN MATCHED THEN
              UPDATE SET STATUS = :ST, NOTE = :NT,
                         TEACHER_CONFIRMED = 1, CONFIRMED_BY = :USR, CONFIRMED_DT = SYSDATE
            WHEN NOT MATCHED THEN
              INSERT (SESSION_ID, EMPCD, STATUS, NOTE, TEACHER_CONFIRMED, CONFIRMED_BY, CONFIRMED_DT)
              VALUES (S.SID, S.EMP, :ST, :NT, 1, :USR, SYSDATE)",
            new OracleParameter("SID", req.SESSION_ID),
            new OracleParameter("EMP", req.EMPCD),
            new OracleParameter("ST",  req.STATUS),
            new OracleParameter("NT",  (object?)req.NOTE ?? DBNull.Value),
            new OracleParameter("USR", req.LOGIN_USER));
    }

    public async Task<int> ConfirmAttendanceBatchAsync(ConfirmAttendanceBatchRequest req)
    {
        if (req.ITEMS == null || req.ITEMS.Count == 0) return 0;
        int n = 0;
        foreach (var i in req.ITEMS)
        {
            if (!IsValidAttendanceStatus(i.STATUS))
                throw new InvalidOperationException($"EMPCD {i.EMPCD}: STATUS invalid");
            await ConfirmAttendanceAsync(new ConfirmAttendanceRequest
            {
                SESSION_ID = req.SESSION_ID,
                EMPCD      = i.EMPCD,
                STATUS     = i.STATUS,
                NOTE       = i.NOTE,
                LOGIN_USER = req.LOGIN_USER,
            });
            n++;
        }
        return n;
    }

    // Batch auto-mark absent (§5.4 với group filter §5b.3 + leave-aware).
    // Idempotent — dùng MERGE, batch chạy 2 lần không duplicate.
    // Chỉ đụng session COMPLETED (batch job phase 5 sẽ set STATUS='COMPLETED' sau END_TIME+30min).
    //
    // Leave-aware (2026-07-06): NV có leave APPROVED cover SESSION_DATE → mark 'EXCUSED' (nghỉ có phép)
    // thay vì 'ABSENT'. Leave source SELF cần STATUS='APPROVED', source ASSIGNED cần thêm CONFIRM_STATUS='CONFIRMED'
    // (khớp pattern LeaveController scheduling).
    public async Task<int> AutoMarkAbsentAsync(int sessionId)
    {
        return await _db.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_TRAINING_ATTENDANCE T
            USING (
                SELECT S.ID SESSION_ID, E.EMPCD, S.SESSION_DATE
                  FROM HRMS.HR_TRAINING_SESSION S
                  JOIN HRMS.HR_TRAINING_ENROLLMENT E ON E.CLASS_ID = S.CLASS_ID
                 WHERE S.ID = :SID
                   AND S.STATUS = 'COMPLETED'
                   AND E.STATUS = 'ENROLLED'
                   AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)
            ) src ON (T.SESSION_ID = src.SESSION_ID AND T.EMPCD = src.EMPCD)
            WHEN NOT MATCHED THEN
              INSERT (SESSION_ID, EMPCD, STATUS, TEACHER_CONFIRMED)
              VALUES (
                src.SESSION_ID,
                src.EMPCD,
                CASE WHEN EXISTS (
                    SELECT 1 FROM HRMS.HR_LEAVE_REQUEST L
                      JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                     WHERE L.EMPCD = src.EMPCD
                       AND R.REQUEST_TYPE = 'LEAVE'
                       AND R.STATUS = 'APPROVED'
                       AND L.FROM_DATE <= src.SESSION_DATE
                       AND L.TO_DATE   >= src.SESSION_DATE
                       AND (
                            L.SOURCE = 'SELF'
                         OR (L.SOURCE = 'ASSIGNED' AND L.CONFIRM_STATUS = 'CONFIRMED')
                       )
                ) THEN 'EXCUSED' ELSE 'ABSENT' END,
                1
              )",
            new OracleParameter("SID", sessionId));
    }

    // ═══════════════════════════════════════════════════════════════
    //  ABSENT STAT + DROP > 25%
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<AbsentStatModel>> GetAbsentStatsAsync(int classId)
    {
        // Với group filter §5b.4: mỗi student count sessions_relevant của group hiện tại.
        const string sql = @"
            SELECT E.EMPCD, EC.CNAME EMP_NAME,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_SESSION S
                     WHERE S.CLASS_ID = E.CLASS_ID
                       AND S.STATUS = 'COMPLETED'
                       AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)) AS COMPLETED_SESSIONS,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_SESSION S
                     JOIN HRMS.HR_TRAINING_ATTENDANCE A
                       ON A.SESSION_ID = S.ID AND A.EMPCD = E.EMPCD
                     WHERE S.CLASS_ID = E.CLASS_ID
                       AND S.STATUS = 'COMPLETED'
                       AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)
                       AND A.STATUS = 'ABSENT') AS ABSENT_COUNT
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
             WHERE E.CLASS_ID = :CID AND E.STATUS = 'ENROLLED'
             ORDER BY E.EMPCD";
        var raw = await _db.ExecuteQueryAsync(sql, r => new
        {
            EMPCD    = r["EMPCD"]?.ToString() ?? "",
            NAME     = r["EMP_NAME"] as string,
            COMPLETED= Convert.ToInt32(r["COMPLETED_SESSIONS"]),
            ABSENT   = Convert.ToInt32(r["ABSENT_COUNT"]),
        }, new OracleParameter("CID", classId));

        return raw.Select(r => new AbsentStatModel
        {
            EMPCD              = r.EMPCD,
            EMP_NAME           = r.NAME,
            COMPLETED_SESSIONS = r.COMPLETED,
            ABSENT_COUNT       = r.ABSENT,
            ABSENT_PERCENT     = r.COMPLETED == 0 ? 0m : Math.Round(100m * r.ABSENT / r.COMPLETED, 2),
        }).ToList();
    }

    // Teacher/HR đá học viên khỏi lớp (§5 absent>25%)
    public async Task DropStudentAsync(DropStudentRequest req)
    {
        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET STATUS = 'DROPPED', DROP_REASON = :REASON, UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND EMPCD = :EMP AND STATUS = 'ENROLLED'",
            new OracleParameter("REASON", req.DROP_REASON),
            new OracleParameter("USR",    req.LOGIN_USER),
            new OracleParameter("CID",    req.CLASS_ID),
            new OracleParameter("EMP",    req.EMPCD));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy enrollment ENROLLED");

        var className = await GetClassNameAsync(req.CLASS_ID);
        var reasonText = req.DROP_REASON switch
        {
            "ABSENCE_EXCEEDED" => "Vắng quá 25%",
            "HR_REMOVED"       => "HR xoá khỏi lớp",
            _                  => req.DROP_REASON,
        };
        await _noti.EnqueueAsync("TRAINING_DROPPED", req.EMPCD,
            new Dictionary<string, string>
            {
                ["className"] = className,
                ["reason"]    = reasonText,
            }, classId: req.CLASS_ID);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<string?> GetStatusAsync(int sessionId)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_SESSION WHERE ID = :ID",
            r => r["STATUS"]?.ToString(),
            new OracleParameter("ID", sessionId))).FirstOrDefault();
    }

    // HHMM → DateTime absolute cho SESSION_DATE
    private static (DateTime start, DateTime end) BuildSessionWindow(SessionModel s)
    {
        var cleanStart = (s.START_TIME ?? "").Replace(":", "").Trim();
        var cleanEnd = (s.END_TIME ?? "").Replace(":", "").Trim();
        
        if (cleanStart.Length < 4) cleanStart = cleanStart.PadLeft(4, '0');
        if (cleanEnd.Length < 4) cleanEnd = cleanEnd.PadLeft(4, '0');

        var startH = int.Parse(cleanStart.Substring(0, 2));
        var startM = int.Parse(cleanStart.Substring(2, 2));
        var endH   = int.Parse(cleanEnd.Substring(0, 2));
        var endM   = int.Parse(cleanEnd.Substring(2, 2));
        
        var d = s.SESSION_DATE.Date;
        return (d.AddHours(startH).AddMinutes(startM), d.AddHours(endH).AddMinutes(endM));
    }

    private static bool IsValidAttendanceStatus(string s)
        => s == "PRESENT" || s == "LATE" || s == "ABSENT" || s == "EXCUSED";

    private static SessionModel MapSession(OracleDataReader r) => new()
    {
        ID           = Convert.ToInt32(r["ID"]),
        CLASS_ID     = Convert.ToInt32(r["CLASS_ID"]),
        SESSION_NO   = Convert.ToInt32(r["SESSION_NO"]),
        SESSION_DATE = Convert.ToDateTime(r["SESSION_DATE"]),
        START_TIME   = r["START_TIME"]?.ToString() ?? "",
        END_TIME     = r["END_TIME"]?.ToString() ?? "",
        TOPIC        = r["TOPIC"] as string,
        LOCATION     = r["LOCATION"] as string,
        STATUS       = r["STATUS"]?.ToString() ?? "UPCOMING",
        GROUP_ID     = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
        GROUP_NAME   = r["GROUP_NAME"] as string,
        CLASS_NAME   = r["CLASS_NAME"] as string,
        INST_ID      = r["INST_ID"] as string,
        INST_DT      = r["INST_DT"] as DateTime?,
        UPDT_ID      = r["UPDT_ID"] as string,
        UPDT_DT      = r["UPDT_DT"] as DateTime?,
    };
}
