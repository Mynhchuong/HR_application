using HR_api.Data;
using HR_api.Helpers;
using HR_api.Services;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.HostedServices;

// Cron 1 phút — auto-transition state machine:
//   • Session UPCOMING → ONGOING (SESSION_DATE = today AND SYSDATE >= START_TIME)
//   • Session ONGOING → COMPLETED (SYSDATE > END_TIME + 30min) → MERGE auto-absent §5.4
//   • Class SCHEDULED → IN_PROGRESS (START_DATE <= today)
//   • Class IN_PROGRESS → COMPLETED (END_DATE < today)
//   • Test PUBLISHED → OPEN (AVAILABLE_FROM <= SYSDATE)
//   • Test OPEN → CLOSED (AVAILABLE_TO < SYSDATE)
//   • Test attempt IN_PROGRESS → AUTO_SUBMITTED (SYSDATE > EFFECTIVE_DEADLINE)
//   • Auto-open recurring (mùng N mỗi tháng)
//   • Session reminders: 8h sáng + T-1h enqueue noti
//   • Teacher confirm reminder: 18h nhắc GV còn điểm danh chưa confirm trong ngày
public class TrainingLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrainingLifecycleService> _log;

    public TrainingLifecycleService(IServiceScopeFactory scopeFactory, ILogger<TrainingLifecycleService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("TrainingLifecycleService started");
        while (!stop.IsCancellationRequested)
        {
            try { await TickAsync(stop); }
            catch (Exception ex) { _log.LogError(ex, "TrainingLifecycle tick failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stop); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken stop)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp   = scope.ServiceProvider;
        var db   = sp.GetRequiredService<OracleService>();
        var noti = sp.GetRequiredService<TrainingNotificationService>();
        var att  = sp.GetRequiredService<TrainingSessionService>();
        var test = sp.GetRequiredService<TrainingAttemptService>();
        var auth = sp.GetRequiredService<TrainingAuthHelper>();

        await TransitionSessionsAsync(db, noti, att, stop);
        await TransitionClassesAsync(db, auth, stop);
        await TransitionTestsAsync(db, stop);
        await test.AutoSubmitExpiredAsync();
        await SessionRemindersAsync(db, noti, stop);
        await TeacherConfirmRemindersAsync(db, noti, stop);
        await AutoOpenRecurringAsync(db, noti, stop);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SESSION
    // ═══════════════════════════════════════════════════════════════

    private async Task TransitionSessionsAsync(
        OracleService db, TrainingNotificationService noti, TrainingSessionService att, CancellationToken stop)
    {
        // UPCOMING → ONGOING: SESSION_DATE = today AND TO_CHAR(SYSDATE,'HH24MI') >= START_TIME.
        // CHỈ lớp đã công bố (SCHEDULED/IN_PROGRESS) — lớp DRAFT/OPEN_FOR_REGISTRATION còn là
        // nháp, buổi không được tự "diễn ra" (sẽ khóa mất nút sửa/xóa của HR).
        var toOngoing = await db.ExecuteQueryAsync(@"
            SELECT S.ID FROM HRMS.HR_TRAINING_SESSION S
              JOIN HRMS.HR_TRAINING_CLASS C ON C.ID = S.CLASS_ID
             WHERE S.STATUS = 'UPCOMING'
               AND S.SESSION_DATE = TRUNC(SYSDATE)
               AND TO_CHAR(SYSDATE,'HH24MI') >= S.START_TIME
               AND C.STATUS IN ('SCHEDULED','IN_PROGRESS')",
            r => Convert.ToInt32(r["ID"]));

        foreach (var id in toOngoing)
        {
            if (stop.IsCancellationRequested) return;
            var claimed = await db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_SESSION
                   SET STATUS = 'ONGOING', UPDT_ID = 'SYSTEM'
                 WHERE ID = :ID AND STATUS = 'UPCOMING'",
                new OracleParameter("ID", id));
            if (claimed > 0) _log.LogInformation("Session {Id} → ONGOING", id);
        }

        // ONGOING → COMPLETED: SYSDATE > END_TIME + 30 min
        // END_TIME lưu HHMM string. Convert bằng TO_DATE(SESSION_DATE || END_TIME, 'YYYYMMDDHH24MI') + 30/1440.
        // Cùng filter lớp đã công bố — buổi lỡ ONGOING trong lớp nháp sẽ được HR sửa lại
        // (SaveAsync tự reset về UPCOMING), không complete + mark absent nhầm.
        var toCompleted = await db.ExecuteQueryAsync(@"
            SELECT S.ID FROM HRMS.HR_TRAINING_SESSION S
              JOIN HRMS.HR_TRAINING_CLASS C ON C.ID = S.CLASS_ID
             WHERE S.STATUS = 'ONGOING'
               AND SYSDATE > TO_DATE(TO_CHAR(S.SESSION_DATE,'YYYYMMDD') || S.END_TIME, 'YYYYMMDDHH24MI')
                             + (30/1440)
               AND C.STATUS IN ('SCHEDULED','IN_PROGRESS')",
            r => Convert.ToInt32(r["ID"]));

        foreach (var id in toCompleted)
        {
            if (stop.IsCancellationRequested) return;
            var claimed = await db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_SESSION
                   SET STATUS = 'COMPLETED', UPDT_ID = 'SYSTEM'
                 WHERE ID = :ID AND STATUS = 'ONGOING'",
                new OracleParameter("ID", id));
            if (claimed == 0) continue;

            _log.LogInformation("Session {Id} → COMPLETED", id);

            // Auto-mark absent + EXCUSED (§5.4 leave-aware). Idempotent MERGE.
            await att.AutoMarkAbsentAsync(id);

            // Enqueue TRAINING_ABSENT cho students bị ABSENT (không EXCUSED)
            var absentEmpcds = await db.ExecuteQueryAsync(@"
                SELECT A.EMPCD, TO_CHAR(S.SESSION_DATE,'DD/MM/YYYY') SD, CL.CLASS_NAME
                  FROM HRMS.HR_TRAINING_ATTENDANCE A
                  JOIN HRMS.HR_TRAINING_SESSION S ON S.ID = A.SESSION_ID
                  JOIN HRMS.HR_TRAINING_CLASS   CL ON CL.ID = S.CLASS_ID
                 WHERE A.SESSION_ID = :SID AND A.STATUS = 'ABSENT'",
                r => new
                {
                    EMPCD      = r["EMPCD"]?.ToString() ?? "",
                    SD         = r["SD"]?.ToString() ?? "",
                    CLASS_NAME = r["CLASS_NAME"]?.ToString() ?? "",
                }, new OracleParameter("SID", id));

            foreach (var row in absentEmpcds)
            {
                await noti.EnqueueAsync("TRAINING_ABSENT", row.EMPCD,
                    new Dictionary<string, string>
                    {
                        ["className"]   = row.CLASS_NAME,
                        ["sessionDate"] = row.SD,
                    }, sessionId: id);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLASS
    // ═══════════════════════════════════════════════════════════════

    private async Task TransitionClassesAsync(OracleService db, TrainingAuthHelper auth, CancellationToken stop)
    {
        // SCHEDULED → IN_PROGRESS
        var toRun = await db.ExecuteQueryAsync(@"
            SELECT ID FROM HRMS.HR_TRAINING_CLASS
             WHERE STATUS = 'SCHEDULED'
               AND START_DATE IS NOT NULL
               AND START_DATE <= TRUNC(SYSDATE)",
            r => Convert.ToInt32(r["ID"]));

        foreach (var id in toRun)
        {
            if (stop.IsCancellationRequested) return;
            var claimed = await db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_CLASS
                   SET STATUS = 'IN_PROGRESS', UPDT_ID = 'SYSTEM'
                 WHERE ID = :ID AND STATUS = 'SCHEDULED'",
                new OracleParameter("ID", id));
            if (claimed > 0) _log.LogInformation("Class {Id} → IN_PROGRESS", id);
        }

        // IN_PROGRESS → COMPLETED
        var toDone = await db.ExecuteQueryAsync(@"
            SELECT ID FROM HRMS.HR_TRAINING_CLASS
             WHERE STATUS = 'IN_PROGRESS'
               AND END_DATE IS NOT NULL
               AND END_DATE < TRUNC(SYSDATE)",
            r => Convert.ToInt32(r["ID"]));

        foreach (var id in toDone)
        {
            if (stop.IsCancellationRequested) return;
            var claimed = await db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_CLASS
                   SET STATUS = 'COMPLETED', UPDT_ID = 'SYSTEM'
                 WHERE ID = :ID AND STATUS = 'IN_PROGRESS'",
                new OracleParameter("ID", id));
            if (claimed > 0)
            {
                _log.LogInformation("Class {Id} → COMPLETED (HR bấm finalize để chốt criteria)", id);

                // §7.4 training_plan: Class → COMPLETED → invalidate cache IsActiveTeacherAsync cho
                // tất cả teacher của class (trước đây chỉ invalidate lúc assign/remove teacher —
                // cache tự hết hạn sau 5 phút nên tác động thấp, nhưng bổ sung cho đúng thiết kế).
                var teacherEmpcds = await db.ExecuteQueryAsync(
                    "SELECT EMPCD FROM HRMS.HR_TRAINING_CLASS_TEACHER WHERE CLASS_ID = :ID",
                    r => r["EMPCD"]?.ToString() ?? "",
                    new OracleParameter("ID", id));
                foreach (var empcd in teacherEmpcds)
                    auth.InvalidateActiveTeacher(empcd);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEST
    // ═══════════════════════════════════════════════════════════════

    private async Task TransitionTestsAsync(OracleService db, CancellationToken stop)
    {
        // PUBLISHED → OPEN
        var toOpen = await db.ExecuteQueryAsync(@"
            SELECT ID FROM HRMS.HR_TRAINING_TEST
             WHERE STATUS = 'PUBLISHED'
               AND AVAILABLE_FROM IS NOT NULL
               AND AVAILABLE_FROM <= SYSDATE",
            r => Convert.ToInt32(r["ID"]));
        foreach (var id in toOpen)
        {
            if (stop.IsCancellationRequested) return;
            var claimed = await db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_TEST
                   SET STATUS = 'OPEN', UPDT_ID = 'SYSTEM'
                 WHERE ID = :ID AND STATUS = 'PUBLISHED'",
                new OracleParameter("ID", id));
            if (claimed > 0) _log.LogInformation("Test {Id} → OPEN", id);
        }

        // OPEN → CLOSED
        var toClose = await db.ExecuteQueryAsync(@"
            SELECT ID FROM HRMS.HR_TRAINING_TEST
             WHERE STATUS = 'OPEN'
               AND AVAILABLE_TO IS NOT NULL
               AND AVAILABLE_TO < SYSDATE",
            r => Convert.ToInt32(r["ID"]));
        foreach (var id in toClose)
        {
            if (stop.IsCancellationRequested) return;
            var claimed = await db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_TEST
                   SET STATUS = 'CLOSED', UPDT_ID = 'SYSTEM'
                 WHERE ID = :ID AND STATUS = 'OPEN'",
                new OracleParameter("ID", id));
            if (claimed > 0) _log.LogInformation("Test {Id} → CLOSED", id);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SESSION REMINDERS (§13)
    //  - 8h sáng: gửi cho sessions hôm nay (STATUS UPCOMING/ONGOING)
    //  - T-1h: gửi trước khi session bắt đầu 60 phút
    //  Dedup bằng cách check đã có row trong queue chưa (same TEMPLATE_KEY + SESSION_ID + EMPCD).
    // ═══════════════════════════════════════════════════════════════

    private async Task SessionRemindersAsync(
        OracleService db, TrainingNotificationService noti, CancellationToken stop)
    {
        // TODAY reminder — chạy từ 8h sáng trở đi trong ngày. Idempotent qua AlreadyEnqueuedAsync
        // (dedup theo TEMPLATE_KEY + SESSION_ID). Server down lúc 8h vẫn catch up khi lên lại.
        var now = DateTime.Now;
        if (now.Hour >= 8)
        {
            var todaySessions = await db.ExecuteQueryAsync(@"
                SELECT S.ID, S.CLASS_ID, S.GROUP_ID, S.TOPIC,
                       TO_CHAR(TO_DATE(S.START_TIME,'HH24MI'),'HH24:MI') START_HHMM,
                       CL.CLASS_NAME
                  FROM HRMS.HR_TRAINING_SESSION S
                  JOIN HRMS.HR_TRAINING_CLASS   CL ON CL.ID = S.CLASS_ID
                 WHERE S.SESSION_DATE = TRUNC(SYSDATE)
                   AND S.STATUS IN ('UPCOMING','ONGOING')",
                r => new
                {
                    ID         = Convert.ToInt32(r["ID"]),
                    CLASS_ID   = Convert.ToInt32(r["CLASS_ID"]),
                    GROUP_ID   = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                    TOPIC      = r["TOPIC"]?.ToString() ?? "",
                    START_HHMM = r["START_HHMM"]?.ToString() ?? "",
                    CLASS_NAME = r["CLASS_NAME"]?.ToString() ?? "",
                });

            foreach (var s in todaySessions)
            {
                if (stop.IsCancellationRequested) return;
                if (await AlreadyEnqueuedAsync(db, "TRAINING_SESSION_TODAY", s.ID)) continue;

                await noti.EnqueueForClassEnrollmentsAsync(s.CLASS_ID, "TRAINING_SESSION_TODAY",
                    new Dictionary<string, string>
                    {
                        ["className"]    = s.CLASS_NAME,
                        ["sessionTopic"] = s.TOPIC,
                        ["startTime"]    = s.START_HHMM,
                    }, sessionGroupId: s.GROUP_ID, sessionId: s.ID);
            }
        }

        // T-1h reminder — session bắt đầu trong 55..65 phút tới
        var soonSessions = await db.ExecuteQueryAsync(@"
            SELECT S.ID, S.CLASS_ID, S.GROUP_ID, S.TOPIC,
                   TO_CHAR(TO_DATE(S.START_TIME,'HH24MI'),'HH24:MI') START_HHMM,
                   CL.CLASS_NAME
              FROM HRMS.HR_TRAINING_SESSION S
              JOIN HRMS.HR_TRAINING_CLASS   CL ON CL.ID = S.CLASS_ID
             WHERE S.STATUS = 'UPCOMING'
               AND TO_DATE(TO_CHAR(S.SESSION_DATE,'YYYYMMDD') || S.START_TIME, 'YYYYMMDDHH24MI')
                   BETWEEN SYSDATE + (55/1440) AND SYSDATE + (65/1440)",
            r => new
            {
                ID         = Convert.ToInt32(r["ID"]),
                CLASS_ID   = Convert.ToInt32(r["CLASS_ID"]),
                GROUP_ID   = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                TOPIC      = r["TOPIC"]?.ToString() ?? "",
                START_HHMM = r["START_HHMM"]?.ToString() ?? "",
                CLASS_NAME = r["CLASS_NAME"]?.ToString() ?? "",
            });

        foreach (var s in soonSessions)
        {
            if (stop.IsCancellationRequested) return;
            if (await AlreadyEnqueuedAsync(db, "TRAINING_SESSION_SOON", s.ID)) continue;

            await noti.EnqueueForClassEnrollmentsAsync(s.CLASS_ID, "TRAINING_SESSION_SOON",
                new Dictionary<string, string>
                {
                    ["className"]    = s.CLASS_NAME,
                    ["sessionTopic"] = s.TOPIC,
                    ["startTime"]    = s.START_HHMM,
                }, sessionGroupId: s.GROUP_ID, sessionId: s.ID);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER CONFIRM REMINDER
    //  Buổi học COMPLETED trong ngày mà còn điểm danh TEACHER_CONFIRMED=0 sẽ bị
    //  TrainingCompletionService tính attendance = 0% cho những dòng đó → học viên rớt oan
    //  dù có đi học, chỉ vì GV quên bấm xác nhận (xem vụ Class 32/Session 60).
    //  Chạy từ 18h — dedup theo TEMPLATE_KEY + SESSION_ID (giống SessionRemindersAsync),
    //  nên mỗi session chỉ nhắc 1 lần/ngày dù tick chạy mỗi phút.
    // ═══════════════════════════════════════════════════════════════

    private async Task TeacherConfirmRemindersAsync(
        OracleService db, TrainingNotificationService noti, CancellationToken stop)
    {
        var now = DateTime.Now;
        if (now.Hour < 18) return;

        var pendingSessions = await db.ExecuteQueryAsync(@"
            SELECT S.ID, S.CLASS_ID, S.GROUP_ID, S.TOPIC,
                   TO_CHAR(S.SESSION_DATE,'DD/MM/YYYY') SD,
                   CL.CLASS_NAME,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_ATTENDANCE A
                     WHERE A.SESSION_ID = S.ID AND A.TEACHER_CONFIRMED = 0) PENDING_CNT
              FROM HRMS.HR_TRAINING_SESSION S
              JOIN HRMS.HR_TRAINING_CLASS   CL ON CL.ID = S.CLASS_ID
             WHERE S.SESSION_DATE = TRUNC(SYSDATE)
               AND S.STATUS = 'COMPLETED'
               AND EXISTS (
                   SELECT 1 FROM HRMS.HR_TRAINING_ATTENDANCE A
                    WHERE A.SESSION_ID = S.ID AND A.TEACHER_CONFIRMED = 0)",
            r => new
            {
                ID          = Convert.ToInt32(r["ID"]),
                CLASS_ID    = Convert.ToInt32(r["CLASS_ID"]),
                GROUP_ID    = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                TOPIC       = r["TOPIC"]?.ToString() ?? "",
                SD          = r["SD"]?.ToString() ?? "",
                CLASS_NAME  = r["CLASS_NAME"]?.ToString() ?? "",
                PENDING_CNT = Convert.ToInt32(r["PENDING_CNT"]),
            });

        foreach (var s in pendingSessions)
        {
            if (stop.IsCancellationRequested) return;
            if (await AlreadyEnqueuedAsync(db, "TRAINING_TEACHER_CONFIRM_REMINDER", s.ID)) continue;

            // GV phụ trách session: GROUP_ID IS NULL trên HR_TRAINING_CLASS_TEACHER = dạy cả lớp
            // (luôn nhắc); nếu session có GROUP_ID thì thêm GV được gán đúng group đó. Nếu session
            // không có GROUP_ID (áp dụng cả lớp) thì nhắc luôn mọi GV của lớp.
            var teacherEmpcds = await db.ExecuteQueryAsync(@"
                SELECT DISTINCT EMPCD FROM HRMS.HR_TRAINING_CLASS_TEACHER
                 WHERE CLASS_ID = :CID
                   AND (:GID IS NULL OR GROUP_ID IS NULL OR GROUP_ID = :GID)",
                r => r["EMPCD"]?.ToString() ?? "",
                new OracleParameter("CID", s.CLASS_ID),
                new OracleParameter("GID", (object?)s.GROUP_ID ?? DBNull.Value));

            if (teacherEmpcds.Count == 0) continue;

            await noti.EnqueueBulkAsync("TRAINING_TEACHER_CONFIRM_REMINDER", teacherEmpcds,
                new Dictionary<string, string>
                {
                    ["className"]    = s.CLASS_NAME,
                    ["sessionTopic"] = s.TOPIC,
                    ["sessionDate"]  = s.SD,
                    ["pendingCount"] = s.PENDING_CNT.ToString(),
                }, classId: s.CLASS_ID, sessionId: s.ID);

            _log.LogInformation("Nhắc {N} GV xác nhận điểm danh Session {Id} (còn {P} chưa confirm)",
                teacherEmpcds.Count, s.ID, s.PENDING_CNT);
        }
    }

    private static async Task<bool> AlreadyEnqueuedAsync(OracleService db, string templateKey, int sessionId)
    {
        var cnt = (await db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_NOTI_QUEUE
             WHERE TEMPLATE_KEY = :TK AND RELATED_SESSION_ID = :SID",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("TK",  templateKey),
            new OracleParameter("SID", sessionId))).First();
        return cnt > 0;
    }

    // ═══════════════════════════════════════════════════════════════
    //  AUTO-OPEN RECURRING (§15b Cách 3)
    //  Mùng N mỗi tháng, tạo Class DRAFT + noti HR duyệt DS.
    //  Chỉ chạy đúng 1 lần / ngày (idempotent nhờ NOT EXISTS trong month).
    // ═══════════════════════════════════════════════════════════════

    private async Task AutoOpenRecurringAsync(
        OracleService db, TrainingNotificationService noti, CancellationToken stop)
    {
        // Chạy từ 6h sáng trở đi. Idempotent qua NOT EXISTS trong query — chỉ tạo Class 1 lần / tháng
        // cho mỗi Course. Server down lúc 6h vẫn catch up khi lên lại (trước 23:59 hôm đó).
        var now = DateTime.Now;
        if (now.Hour < 6) return;

        var courses = await db.ExecuteQueryAsync(@"
            SELECT C.ID, C.TITLE, C.COURSE_MODE, C.DEFAULT_MIN_ATTEND_PCT
              FROM HRMS.HR_TRAINING_COURSE C
             WHERE C.IS_ACTIVE = 1
               AND C.AUTO_OPEN_MONTHLY = 1
               AND C.AUTO_OPEN_DAY = TO_NUMBER(TO_CHAR(SYSDATE,'DD'))
               AND NOT EXISTS (
                   SELECT 1 FROM HRMS.HR_TRAINING_CLASS CL
                    WHERE CL.COURSE_ID = C.ID
                      AND TRUNC(CL.START_DATE,'MM') = TRUNC(SYSDATE,'MM')
               )",
            r => new
            {
                ID      = Convert.ToInt32(r["ID"]),
                TITLE   = r["TITLE"]?.ToString() ?? "",
                MODE    = r["COURSE_MODE"]?.ToString() ?? "STANDARD",
                MIN_ATT = r["DEFAULT_MIN_ATTEND_PCT"] is DBNull ? 75m : Convert.ToDecimal(r["DEFAULT_MIN_ATTEND_PCT"]),
            });

        foreach (var c in courses)
        {
            if (stop.IsCancellationRequested) return;

            // Tạo Class DRAFT — HR duyệt DS + Publish sau
            var startDate = DateTime.Today.AddDays(3);   // buffer 3 ngày HR duyệt DS
            var className = $"{Truncate(c.TITLE, 40)} K{now:MM/yyyy}";

            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_CLASS
                    (COURSE_ID, CLASS_NAME, STATUS, REGISTRATION_MODE,
                     START_DATE, MIN_ATTENDANCE_PERCENT,
                     CLONED_FROM_TYPE, IS_EXPRESS, INST_ID)
                VALUES
                    (:COID, :NAME, 'DRAFT', 'ASSIGNED',
                     :SD, :MIN,
                     'COURSE_TEMPLATE', :EXP, 'SYSTEM')
                RETURNING ID INTO :NEW_ID",
                new OracleParameter("COID", c.ID),
                new OracleParameter("NAME", className),
                new OracleParameter("SD",   startDate),
                new OracleParameter("MIN",  c.MIN_ATT),
                new OracleParameter("EXP",  c.MODE == "EXPRESS" ? 1 : 0),
                idParam);
            var newClassId = Convert.ToInt32(idParam.Value);
            _log.LogInformation("Auto-opened Class {Id} '{Name}' từ Course {CID}", newClassId, className, c.ID);

            // Noti tất cả HR/Admin đang active (HR_USERS.IS_ACTIVE=1 + ECM100.RETDAT chưa retire)
            var hrs = await db.ExecuteQueryAsync(@"
                SELECT U.EMPCD FROM HRMS.HR_USERS U
                  LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = U.EMPCD
                 WHERE U.ROLE_ID IN (5,6)
                   AND U.IS_ACTIVE = 1
                   AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))",
                r => r["EMPCD"]?.ToString() ?? "");
            foreach (var hr in hrs)
            {
                await noti.EnqueueAsync("TRAINING_REGISTRATION_OPEN", hr,
                    new Dictionary<string, string>
                    {
                        ["className"]         = className,
                        ["registerDeadline"]  = "(chưa set)",
                    }, classId: newClassId);
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
}
