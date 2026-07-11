using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// CRUD Class + state machine + teacher + group management.
// State machine: DRAFT → OPEN_FOR_REGISTRATION → SCHEDULED → IN_PROGRESS → COMPLETED → CLOSED
//                                              ↘ CANCELLED
// IN_PROGRESS ← auto (batch job, phase 2). Ở đây chỉ expose transition HR chủ động.
public class TrainingClassService
{
    private readonly OracleService _db;

    public TrainingClassService(OracleService db) { _db = db; }

    // ═══════════════════════════════════════════════════════════════
    //  LIST + DETAIL
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<ClassModel>> ListAsync(string? status, int? courseId, string? search)
    {
        const string sql = @"
            SELECT CL.ID, CL.COURSE_ID, CL.CLASS_NAME, CL.DESCRIPTION, CL.STATUS,
                   CL.REGISTRATION_MODE, CL.MAX_STUDENTS, CL.REGISTRATION_DEADLINE,
                   CL.START_DATE, CL.END_DATE,
                   CL.MIN_ATTENDANCE_PERCENT, CL.FINAL_TEST_ID, CL.REQUIRE_POST_REVIEW,
                   CL.IS_EXPRESS, CL.CLONED_FROM_CLASS_ID, CL.CLONED_FROM_TYPE,
                   CL.INST_ID, CL.INST_DT, CL.UPDT_ID, CL.UPDT_DT,
                   CO.TITLE AS COURSE_TITLE, CO.COURSE_MODE,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_ENROLLMENT E
                     WHERE E.CLASS_ID = CL.ID
                       AND E.STATUS IN ('ENROLLED','PENDING_APPROVAL')) AS ENROLLMENT_COUNT,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_SESSION S
                     WHERE S.CLASS_ID = CL.ID) AS SESSION_COUNT
              FROM HRMS.HR_TRAINING_CLASS CL
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
             WHERE (:P_STATUS IS NULL OR CL.STATUS    = :P_STATUS)
               AND (:P_COURSE IS NULL OR CL.COURSE_ID = :P_COURSE)
               AND (:P_SEARCH IS NULL OR UPPER(CL.CLASS_NAME) LIKE '%' || UPPER(:P_SEARCH) || '%')
             ORDER BY CL.ID DESC";

        return await _db.ExecuteQueryAsync(sql, MapClassLight,
            new OracleParameter("P_STATUS", (object?)status   ?? DBNull.Value),
            new OracleParameter("P_COURSE", (object?)courseId ?? DBNull.Value),
            new OracleParameter("P_SEARCH", (object?)search   ?? DBNull.Value));
    }

    public async Task<ClassModel?> GetDetailAsync(int id)
    {
        const string sql = @"
            SELECT CL.ID, CL.COURSE_ID, CL.CLASS_NAME, CL.DESCRIPTION, CL.STATUS,
                   CL.REGISTRATION_MODE, CL.MAX_STUDENTS, CL.REGISTRATION_DEADLINE,
                   CL.START_DATE, CL.END_DATE,
                   CL.MIN_ATTENDANCE_PERCENT, CL.FINAL_TEST_ID, CL.REQUIRE_POST_REVIEW,
                   CL.IS_EXPRESS, CL.CLONED_FROM_CLASS_ID, CL.CLONED_FROM_TYPE,
                   CL.INST_ID, CL.INST_DT, CL.UPDT_ID, CL.UPDT_DT,
                   CO.TITLE AS COURSE_TITLE, CO.COURSE_MODE
              FROM HRMS.HR_TRAINING_CLASS CL
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
             WHERE CL.ID = :ID";

        var list = await _db.ExecuteQueryAsync(sql, MapClassFullWithCourse,
            new OracleParameter("ID", id));
        return list.FirstOrDefault();
    }

    public async Task<List<ClassSessionLightModel>> GetSessionsAsync(int classId)
    {
        const string sql = @"
            SELECT S.ID, S.CLASS_ID, S.SESSION_NO, S.SESSION_DATE, S.START_TIME, S.END_TIME,
                   S.TOPIC, S.LOCATION, S.STATUS, S.GROUP_ID,
                   G.GROUP_NAME
              FROM HRMS.HR_TRAINING_SESSION S
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = S.GROUP_ID
             WHERE S.CLASS_ID = :CID
             ORDER BY S.SESSION_DATE, S.START_TIME";
        return await _db.ExecuteQueryAsync(sql, r => new ClassSessionLightModel
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
        }, new OracleParameter("CID", classId));
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAVE (create + update)
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> SaveAsync(SaveClassRequest req)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(req.CLASS_NAME))
            throw new InvalidOperationException("Tên lớp không được để trống");
        if (req.CLASS_NAME.Length > 80)
            throw new InvalidOperationException("Tên lớp tối đa 80 ký tự (§12 cert display)");
        if (req.REGISTRATION_MODE != "ASSIGNED" && req.REGISTRATION_MODE != "OPEN" && req.REGISTRATION_MODE != "HYBRID")
            throw new InvalidOperationException("REGISTRATION_MODE phải là ASSIGNED, OPEN hoặc HYBRID");
        // OPEN or HYBRID mode: MAX_STUDENTS nullable (null = unlimited §3.2), REGISTRATION_DEADLINE bắt buộc
        if ((req.REGISTRATION_MODE == "OPEN" || req.REGISTRATION_MODE == "HYBRID") && req.REGISTRATION_DEADLINE == null)
            throw new InvalidOperationException("Class OPEN hoặc HYBRID phải có REGISTRATION_DEADLINE");
        if (req.MIN_ATTENDANCE_PERCENT.HasValue &&
            (req.MIN_ATTENDANCE_PERCENT < 0 || req.MIN_ATTENDANCE_PERCENT > 100))
            throw new InvalidOperationException("MIN_ATTENDANCE_PERCENT phải trong 0..100");
        if (req.START_DATE.HasValue && req.END_DATE.HasValue && req.END_DATE < req.START_DATE)
            throw new InvalidOperationException("END_DATE phải sau START_DATE");

        if (req.ID == null)
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_CLASS
                    (COURSE_ID, CLASS_NAME, DESCRIPTION, STATUS,
                     REGISTRATION_MODE, MAX_STUDENTS, REGISTRATION_DEADLINE,
                     START_DATE, END_DATE,
                     MIN_ATTENDANCE_PERCENT, FINAL_TEST_ID, REQUIRE_POST_REVIEW,
                     IS_EXPRESS,
                     INST_ID)
                VALUES
                    (:COURSE_ID, :CLASS_NAME, :DESCRIPTION, 'DRAFT',
                     :REG_MODE, :MAX_STUDENTS, :REG_DEADLINE,
                     :START_DATE, :END_DATE,
                     :MIN_ATT, :FINAL_TEST, :REQ_REVIEW,
                     0,
                     :LOGIN_USER)
                RETURNING ID INTO :NEW_ID";

            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("COURSE_ID",    req.COURSE_ID),
                new OracleParameter("CLASS_NAME",   req.CLASS_NAME),
                new OracleParameter("DESCRIPTION",  (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("REG_MODE",     req.REGISTRATION_MODE),
                new OracleParameter("MAX_STUDENTS", (object?)req.MAX_STUDENTS ?? DBNull.Value),
                new OracleParameter("REG_DEADLINE", (object?)req.REGISTRATION_DEADLINE ?? DBNull.Value),
                new OracleParameter("START_DATE",   (object?)req.START_DATE ?? DBNull.Value),
                new OracleParameter("END_DATE",     (object?)req.END_DATE ?? DBNull.Value),
                new OracleParameter("MIN_ATT",      (object?)req.MIN_ATTENDANCE_PERCENT ?? 75m),
                new OracleParameter("FINAL_TEST",   (object?)req.FINAL_TEST_ID ?? DBNull.Value),
                new OracleParameter("REQ_REVIEW",   req.REQUIRE_POST_REVIEW ?? 0),
                new OracleParameter("LOGIN_USER",   req.LOGIN_USER),
                idParam);
            return OracleService.ConvertToInt(idParam.Value);
        }
        else
        {
            // Cho phép edit BẤT KỲ status (kể cả IN_PROGRESS / COMPLETED) — quyết định 2026-07-06
            // Case bất khả kháng: HR cần sửa deadline, đổi tên, config attendance %...
            // Chỉ chặn khi Class không tồn tại.
            _ = await GetStatusAsync(req.ID.Value)
                ?? throw new InvalidOperationException("Không tìm thấy Class");

            const string sqlUpd = @"
                UPDATE HRMS.HR_TRAINING_CLASS
                   SET CLASS_NAME             = :CLASS_NAME,
                       DESCRIPTION            = :DESCRIPTION,
                       REGISTRATION_MODE      = :REG_MODE,
                       MAX_STUDENTS           = :MAX_STUDENTS,
                       REGISTRATION_DEADLINE  = :REG_DEADLINE,
                       START_DATE             = :START_DATE,
                       END_DATE               = :END_DATE,
                       MIN_ATTENDANCE_PERCENT = :MIN_ATT,
                       FINAL_TEST_ID          = :FINAL_TEST,
                       REQUIRE_POST_REVIEW    = :REQ_REVIEW,
                       UPDT_ID                = :LOGIN_USER
                 WHERE ID = :ID";
            await _db.ExecuteNonQueryAsync(sqlUpd,
                new OracleParameter("CLASS_NAME",   req.CLASS_NAME),
                new OracleParameter("DESCRIPTION",  (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("REG_MODE",     req.REGISTRATION_MODE),
                new OracleParameter("MAX_STUDENTS", (object?)req.MAX_STUDENTS ?? DBNull.Value),
                new OracleParameter("REG_DEADLINE", (object?)req.REGISTRATION_DEADLINE ?? DBNull.Value),
                new OracleParameter("START_DATE",   (object?)req.START_DATE ?? DBNull.Value),
                new OracleParameter("END_DATE",     (object?)req.END_DATE ?? DBNull.Value),
                new OracleParameter("MIN_ATT",      (object?)req.MIN_ATTENDANCE_PERCENT ?? 75m),
                new OracleParameter("FINAL_TEST",   (object?)req.FINAL_TEST_ID ?? DBNull.Value),
                new OracleParameter("REQ_REVIEW",   req.REQUIRE_POST_REVIEW ?? 0),
                new OracleParameter("LOGIN_USER",   req.LOGIN_USER),
                new OracleParameter("ID",           req.ID.Value));
            return req.ID.Value;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  STATE MACHINE
    // ═══════════════════════════════════════════════════════════════

    // DRAFT → OPEN_FOR_REGISTRATION (chỉ Class OPEN mode)
    public async Task PublishRegistrationAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (current != "DRAFT")
            throw new InvalidOperationException($"Chỉ publish từ DRAFT, hiện đang {current}");

        var mode = await GetFieldAsync<string>(classId, "REGISTRATION_MODE")
            ?? throw new InvalidOperationException("Không đọc được REGISTRATION_MODE");
        if (mode != "OPEN")
            throw new InvalidOperationException("Chỉ Class OPEN mới publish-registration. ASSIGNED dùng finalize thẳng.");

        await SetStatusAsync(classId, "OPEN_FOR_REGISTRATION", actor);
    }

    // DRAFT / OPEN_FOR_REGISTRATION → SCHEDULED (chốt DS)
    public async Task FinalizeEnrollmentAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (current != "DRAFT" && current != "OPEN_FOR_REGISTRATION")
            throw new InvalidOperationException($"Chỉ chốt từ DRAFT / OPEN_FOR_REGISTRATION, hiện đang {current}");

        // Reject mọi enrollment còn PENDING_APPROVAL
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET STATUS = 'REJECTED', UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND STATUS = 'PENDING_APPROVAL'",
            new OracleParameter("USR", actor),
            new OracleParameter("CID", classId));

        await SetStatusAsync(classId, "SCHEDULED", actor);
    }

    // Bất kỳ state (trừ CLOSED / CANCELLED / COMPLETED) → CANCELLED
    public async Task CancelAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (current == "CLOSED" || current == "CANCELLED" || current == "COMPLETED")
            throw new InvalidOperationException($"Class đang {current}, không cancel được");
        await SetStatusAsync(classId, "CANCELLED", actor);
    }

    // COMPLETED → CLOSED (chốt report cuối, không sửa nữa)
    public async Task CloseAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (current != "COMPLETED")
            throw new InvalidOperationException($"Chỉ close từ COMPLETED, hiện đang {current}");
        await SetStatusAsync(classId, "CLOSED", actor);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<ClassTeacherModel>> GetTeachersAsync(int classId)
    {
        const string sql = @"
            SELECT T.CLASS_ID, T.EMPCD, T.IS_PRIMARY, EC.CNAME AS EMP_NAME
              FROM HRMS.HR_TRAINING_CLASS_TEACHER T
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = T.EMPCD
             WHERE T.CLASS_ID = :CID
             ORDER BY T.IS_PRIMARY DESC, T.EMPCD";
        return await _db.ExecuteQueryAsync(sql, r => new ClassTeacherModel
        {
            CLASS_ID   = Convert.ToInt32(r["CLASS_ID"]),
            EMPCD      = r["EMPCD"]?.ToString() ?? "",
            EMP_NAME   = r["EMP_NAME"] as string,
            IS_PRIMARY = Convert.ToInt32(r["IS_PRIMARY"]),
        }, new OracleParameter("CID", classId));
    }

    public async Task AssignTeacherAsync(AssignTeacherRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EMPCD))
            throw new InvalidOperationException("EMPCD không được để trống");

        // Nếu IS_PRIMARY=1 → clear PRIMARY hiện tại (Class chỉ 1 primary teacher)
        if (req.IS_PRIMARY == 1)
        {
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_CLASS_TEACHER
                   SET IS_PRIMARY = 0
                 WHERE CLASS_ID = :CID AND IS_PRIMARY = 1",
                new OracleParameter("CID", req.CLASS_ID));
        }

        // Upsert (Oracle 10 MERGE)
        await _db.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_TRAINING_CLASS_TEACHER T
            USING (SELECT :CID AS CID, :EMP AS EMP FROM DUAL) S
               ON (T.CLASS_ID = S.CID AND T.EMPCD = S.EMP)
            WHEN MATCHED THEN
              UPDATE SET IS_PRIMARY = :PRI
            WHEN NOT MATCHED THEN
              INSERT (CLASS_ID, EMPCD, IS_PRIMARY, INST_ID)
              VALUES (S.CID, S.EMP, :PRI, :USR)",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD),
            new OracleParameter("PRI", req.IS_PRIMARY),
            new OracleParameter("USR", req.LOGIN_USER));
    }

    public async Task RemoveTeacherAsync(RemoveTeacherRequest req)
    {
        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_CLASS_TEACHER
             WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));
    }

    // ═══════════════════════════════════════════════════════════════
    //  CLONE §15b Cách 1 — Class từ Course template
    //  Chạy tuần tự các INSERT (OracleService không expose transaction) — nếu bước fail,
    //  các row đã INSERT trước có thể để lại data rác. Client bấm lại tạo Class khác nhau.
    //  V2 có thể refactor sang stored procedure hoặc mở transaction API trong OracleService.
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> CloneFromCourseAsync(CloneFromCourseRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CLASS_NAME))
            throw new InvalidOperationException("CLASS_NAME required");
        if (req.CLASS_NAME.Length > 80)
            throw new InvalidOperationException("CLASS_NAME tối đa 80 ký tự (§12)");

        var course = (await _db.ExecuteQueryAsync(@"
            SELECT ID, COURSE_MODE, DESCRIPTION, DEFAULT_MIN_ATTEND_PCT
              FROM HRMS.HR_TRAINING_COURSE WHERE ID = :ID AND IS_ACTIVE = 1",
            r => new
            {
                MODE     = r["COURSE_MODE"]?.ToString() ?? "STANDARD",
                DESC     = r["DESCRIPTION"] as string,
                MIN_ATT  = r["DEFAULT_MIN_ATTEND_PCT"] is DBNull ? 75m : Convert.ToDecimal(r["DEFAULT_MIN_ATTEND_PCT"]),
            }, new OracleParameter("ID", req.COURSE_ID))).FirstOrDefault();
        if (course == null) throw new InvalidOperationException("Course không tồn tại hoặc đã archived");
        if (course.MODE == "EXPRESS") throw new InvalidOperationException("Course EXPRESS dùng Express Create, không dùng Clone template");

        // Fetch template sessions để tính END_DATE = START + max DAY_OFFSET
        var templates = await _db.ExecuteQueryAsync(@"
            SELECT SESSION_NO, DAY_OFFSET, START_TIME, END_TIME, TOPIC, LOCATION
              FROM HRMS.HR_TR_COURSE_SES_TMPL
             WHERE COURSE_ID = :ID
             ORDER BY SESSION_NO",
            r => new
            {
                NO    = Convert.ToInt32(r["SESSION_NO"]),
                OFF   = Convert.ToInt32(r["DAY_OFFSET"]),
                ST    = r["START_TIME"]?.ToString() ?? "0800",
                ET    = r["END_TIME"]?.ToString()   ?? "1130",
                TOPIC = r["TOPIC"] as string,
                LOC   = r["LOCATION"] as string,
            }, new OracleParameter("ID", req.COURSE_ID));
        if (templates.Count == 0)
            throw new InvalidOperationException("Course không có template sessions — không clone được");

        var endDate = req.START_DATE.AddDays(templates.Max(t => t.OFF));

        // Step A: INSERT Class DRAFT
        var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_CLASS
                (COURSE_ID, CLASS_NAME, DESCRIPTION,
                 STATUS, REGISTRATION_MODE,
                 START_DATE, END_DATE,
                 MIN_ATTENDANCE_PERCENT,
                 CLONED_FROM_TYPE, IS_EXPRESS,
                 INST_ID)
            VALUES (:COID, :NAME, :DESC,
                    'DRAFT', 'ASSIGNED',
                    :SD, :ED,
                    :MIN,
                    'COURSE_TEMPLATE', 0,
                    :USR)
            RETURNING ID INTO :NEW_ID",
            new OracleParameter("COID", req.COURSE_ID),
            new OracleParameter("NAME", req.CLASS_NAME),
            new OracleParameter("DESC", (object?)(req.DESCRIPTION ?? course.DESC) ?? DBNull.Value),
            new OracleParameter("SD",   req.START_DATE.Date),
            new OracleParameter("ED",   endDate),
            new OracleParameter("MIN",  course.MIN_ATT),
            new OracleParameter("USR",  req.LOGIN_USER),
            idParam);
        var newClassId = OracleService.ConvertToInt(idParam.Value);

        // Compensating rollback: nếu Step B/C/D fail, DELETE Class + children (avoid orphan).
        // Không dùng OracleTransaction vì OracleService.ExecuteNonQueryAsync tạo connection mới per call.
        try
        {
            // Step B: Clone sessions
            foreach (var t in templates)
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_SESSION
                        (CLASS_ID, SESSION_NO, SESSION_DATE, START_TIME, END_TIME, TOPIC, LOCATION, STATUS, INST_ID)
                    VALUES (:CID, :NO, :DT, :ST, :ET, :TP, :LC, 'UPCOMING', :USR)",
                    new OracleParameter("CID", newClassId),
                    new OracleParameter("NO",  t.NO),
                    new OracleParameter("DT",  req.START_DATE.AddDays(t.OFF).Date),
                    new OracleParameter("ST",  t.ST),
                    new OracleParameter("ET",  t.ET),
                    new OracleParameter("TP",  (object?)t.TOPIC ?? DBNull.Value),
                    new OracleParameter("LC",  (object?)t.LOC ?? DBNull.Value),
                    new OracleParameter("USR", req.LOGIN_USER));
            }

            // Step C: Primary teacher (option)
            if (!string.IsNullOrWhiteSpace(req.PRIMARY_TEACHER_EMPCD))
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_CLASS_TEACHER (CLASS_ID, EMPCD, IS_PRIMARY, INST_ID)
                    VALUES (:CID, :EMP, 1, :USR)",
                    new OracleParameter("CID", newClassId),
                    new OracleParameter("EMP", req.PRIMARY_TEACHER_EMPCD),
                    new OracleParameter("USR", req.LOGIN_USER));
            }

            // Step D: Enrollments (option — có thể bỏ trống, HR nhập sau)
            foreach (var emp in (req.EMPCDS ?? new()).Distinct())
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_ENROLLMENT
                        (CLASS_ID, EMPCD, SOURCE, STATUS, INST_ID)
                    VALUES (:CID, :EMP, 'ASSIGNED', 'ENROLLED', :USR)",
                    new OracleParameter("CID", newClassId),
                    new OracleParameter("EMP", emp),
                    new OracleParameter("USR", req.LOGIN_USER));
            }
        }
        catch
        {
            await CompensatingDeleteClassAsync(newClassId);
            throw;   // re-throw để controller trả error
        }

        return newClassId;
    }

    // Compensation: xoá Class + tất cả child rows nếu clone thất bại giữa chừng.
    // Best-effort — nếu delete cũng fail, ghi log (silent) để tránh double-throw.
    private async Task CompensatingDeleteClassAsync(int classId)
    {
        try
        {
            await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TRAINING_ENROLLMENT     WHERE CLASS_ID = :ID",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TRAINING_CLASS_TEACHER  WHERE CLASS_ID = :ID",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TRAINING_SESSION        WHERE CLASS_ID = :ID",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TRAINING_CLASS_GROUP    WHERE CLASS_ID = :ID",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TRAINING_CLASS          WHERE ID = :ID",
                new OracleParameter("ID", classId));
        }
        catch { /* silent — best-effort cleanup */ }
    }

    // ═══════════════════════════════════════════════════════════════
    //  EXPRESS CREATE §4.2 — 1-form nhanh cho Course EXPRESS
    //  Bỏ DRAFT — STATUS='SCHEDULED' luôn.
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> ExpressCreateAsync(ExpressCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CLASS_NAME))
            throw new InvalidOperationException("CLASS_NAME required");
        if (req.CLASS_NAME.Length > 80)
            throw new InvalidOperationException("CLASS_NAME tối đa 80 ký tự");
        if (req.START_TIME?.Length != 4 || req.END_TIME?.Length != 4)
            throw new InvalidOperationException("START_TIME/END_TIME phải HHMM");
        if (string.IsNullOrWhiteSpace(req.PRIMARY_TEACHER_EMPCD))
            throw new InvalidOperationException("PRIMARY_TEACHER_EMPCD required");

        var mode = (await _db.ExecuteQueryAsync(
            "SELECT COURSE_MODE FROM HRMS.HR_TRAINING_COURSE WHERE ID = :ID",
            r => r["COURSE_MODE"]?.ToString() ?? "",
            new OracleParameter("ID", req.COURSE_ID))).FirstOrDefault();
        if (mode == null) throw new InvalidOperationException("Course không tồn tại");
        if (mode != "EXPRESS")
            throw new InvalidOperationException("Course không phải EXPRESS — dùng Clone template thay vì Express Create");

        // Step A: INSERT Class SCHEDULED (bỏ qua DRAFT)
        var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_CLASS
                (COURSE_ID, CLASS_NAME,
                 STATUS, REGISTRATION_MODE,
                 START_DATE, END_DATE,
                 MIN_ATTENDANCE_PERCENT, FINAL_TEST_ID, REQUIRE_POST_REVIEW,
                 IS_EXPRESS,
                 INST_ID)
            VALUES (:COID, :NAME,
                    'SCHEDULED', 'ASSIGNED',
                    :SD, :SD,
                    75, :FT, 0,
                    1,
                    :USR)
            RETURNING ID INTO :NEW_ID",
            new OracleParameter("COID", req.COURSE_ID),
            new OracleParameter("NAME", req.CLASS_NAME),
            new OracleParameter("SD",   req.SESSION_DATE.Date),
            new OracleParameter("FT",   (object?)req.FINAL_TEST_ID ?? DBNull.Value),
            new OracleParameter("USR",  req.LOGIN_USER),
            idParam);
        var newClassId = OracleService.ConvertToInt(idParam.Value);

        // Compensating rollback nếu Step B/C/D fail
        try
        {
            // Step B: 1 session
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_SESSION
                    (CLASS_ID, SESSION_NO, SESSION_DATE, START_TIME, END_TIME, TOPIC, LOCATION, STATUS, INST_ID)
                VALUES (:CID, 1, :DT, :ST, :ET, :TP, :LC, 'UPCOMING', :USR)",
                new OracleParameter("CID", newClassId),
                new OracleParameter("DT",  req.SESSION_DATE.Date),
                new OracleParameter("ST",  req.START_TIME),
                new OracleParameter("ET",  req.END_TIME),
                new OracleParameter("TP",  (object?)req.TOPIC ?? DBNull.Value),
                new OracleParameter("LC",  (object?)req.LOCATION ?? DBNull.Value),
                new OracleParameter("USR", req.LOGIN_USER));

            // Step C: Primary teacher
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_CLASS_TEACHER (CLASS_ID, EMPCD, IS_PRIMARY, INST_ID)
                VALUES (:CID, :EMP, 1, :USR)",
                new OracleParameter("CID", newClassId),
                new OracleParameter("EMP", req.PRIMARY_TEACHER_EMPCD),
                new OracleParameter("USR", req.LOGIN_USER));

            // Step D: Enrollments
            foreach (var emp in (req.EMPCDS ?? new()).Distinct())
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_ENROLLMENT
                        (CLASS_ID, EMPCD, SOURCE, STATUS, INST_ID)
                    VALUES (:CID, :EMP, 'ASSIGNED', 'ENROLLED', :USR)",
                    new OracleParameter("CID", newClassId),
                    new OracleParameter("EMP", emp),
                    new OracleParameter("USR", req.LOGIN_USER));
            }
        }
        catch
        {
            await CompensatingDeleteClassAsync(newClassId);
            throw;
        }

        return newClassId;
    }

    // List rows HR_TRAINING_CLASS_TEACHER cho 1 EMPCD (dùng TrainingTeachController.my-classes)
    public async Task<List<ClassTeacherModel>> GetTeachersForEmpAsync(string empcd)
    {
        return await _db.ExecuteQueryAsync(@"
            SELECT CLASS_ID, EMPCD, IS_PRIMARY FROM HRMS.HR_TRAINING_CLASS_TEACHER
             WHERE EMPCD = :EMP",
            r => new ClassTeacherModel
            {
                CLASS_ID   = Convert.ToInt32(r["CLASS_ID"]),
                EMPCD      = r["EMPCD"]?.ToString() ?? "",
                IS_PRIMARY = Convert.ToInt32(r["IS_PRIMARY"]),
            }, new OracleParameter("EMP", empcd));
    }

    // ═══════════════════════════════════════════════════════════════
    //  GROUP (§5b)
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<ClassGroupModel>> GetGroupsAsync(int classId)
    {
        const string sql = @"
            SELECT G.ID, G.CLASS_ID, G.GROUP_NAME, G.MAX_STUDENTS,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_ENROLLMENT E
                     WHERE E.GROUP_ID = G.ID
                       AND E.STATUS IN ('ENROLLED','PENDING_APPROVAL')) AS ENROLLMENT_COUNT
              FROM HRMS.HR_TRAINING_CLASS_GROUP G
             WHERE G.CLASS_ID = :CID
             ORDER BY G.GROUP_NAME";
        return await _db.ExecuteQueryAsync(sql, r => new ClassGroupModel
        {
            ID               = Convert.ToInt32(r["ID"]),
            CLASS_ID         = Convert.ToInt32(r["CLASS_ID"]),
            GROUP_NAME       = r["GROUP_NAME"]?.ToString() ?? "",
            MAX_STUDENTS     = r["MAX_STUDENTS"] is DBNull ? null : Convert.ToInt32(r["MAX_STUDENTS"]),
            ENROLLMENT_COUNT = Convert.ToInt32(r["ENROLLMENT_COUNT"]),
        }, new OracleParameter("CID", classId));
    }

    public async Task<int> SaveGroupAsync(SaveGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.GROUP_NAME))
            throw new InvalidOperationException("Tên nhóm không được để trống");
        if (req.GROUP_NAME.Length > 100)
            throw new InvalidOperationException("Tên nhóm tối đa 100 ký tự");

        if (req.ID == null)
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_CLASS_GROUP
                    (CLASS_ID, GROUP_NAME, MAX_STUDENTS, INST_ID)
                VALUES
                    (:CID, :NAME, :MAX, :USR)
                RETURNING ID INTO :NEW_ID";
            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            try
            {
                await _db.ExecuteNonQueryAsync(sqlIns,
                    new OracleParameter("CID",  req.CLASS_ID),
                    new OracleParameter("NAME", req.GROUP_NAME),
                    new OracleParameter("MAX",  (object?)req.MAX_STUDENTS ?? DBNull.Value),
                    new OracleParameter("USR",  req.LOGIN_USER),
                    idParam);
            }
            catch (OracleException ex) when (ex.Number == 1)   // ORA-00001 unique
            {
                throw new InvalidOperationException($"Nhóm '{req.GROUP_NAME}' đã tồn tại trong lớp này");
            }
            return OracleService.ConvertToInt(idParam.Value);
        }
        else
        {
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_CLASS_GROUP
                   SET GROUP_NAME = :NAME, MAX_STUDENTS = :MAX, UPDT_ID = :USR
                 WHERE ID = :ID",
                new OracleParameter("NAME", req.GROUP_NAME),
                new OracleParameter("MAX",  (object?)req.MAX_STUDENTS ?? DBNull.Value),
                new OracleParameter("USR",  req.LOGIN_USER),
                new OracleParameter("ID",   req.ID.Value));
            return req.ID.Value;
        }
    }

    public async Task DeleteGroupAsync(DeleteGroupRequest req)
    {
        // Chặn nếu còn enrollment (§5b.6)
        var count = await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT WHERE GROUP_ID = :ID",
            r => Convert.ToInt32(r["CNT"]), new OracleParameter("ID", req.ID));
        if (count.FirstOrDefault() > 0)
            throw new InvalidOperationException("Còn học viên trong nhóm — cần reassign trước khi xoá");

        // Chặn nếu có session dùng
        var sc = await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_SESSION WHERE GROUP_ID = :ID",
            r => Convert.ToInt32(r["CNT"]), new OracleParameter("ID", req.ID));
        if (sc.FirstOrDefault() > 0)
            throw new InvalidOperationException("Còn session gán vào nhóm — cần chuyển GROUP_ID=NULL hoặc xoá session trước");

        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE ID = :ID",
            new OracleParameter("ID", req.ID));
    }

    // Chia đều DS ENROLLED (SOURCE-agnostic) chưa gán group vào N group input (round-robin theo EMPCD).
    public async Task<int> AutoSplitGroupAsync(AutoSplitGroupRequest req)
    {
        if (req.GROUP_IDS == null || req.GROUP_IDS.Count == 0)
            throw new InvalidOperationException("Cần ≥ 1 group để chia");

        // Verify tất cả groups thuộc Class
        var validGroups = await _db.ExecuteQueryAsync(
            "SELECT ID FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE CLASS_ID = :CID",
            r => Convert.ToInt32(r["ID"]),
            new OracleParameter("CID", req.CLASS_ID));
        foreach (var gid in req.GROUP_IDS)
            if (!validGroups.Contains(gid))
                throw new InvalidOperationException($"Group ID {gid} không thuộc Class {req.CLASS_ID}");

        // List học viên chưa gán group
        var empcds = await _db.ExecuteQueryAsync(@"
            SELECT EMPCD FROM HRMS.HR_TRAINING_ENROLLMENT
             WHERE CLASS_ID = :CID
               AND STATUS = 'ENROLLED'
               AND GROUP_ID IS NULL
             ORDER BY EMPCD",
            r => r["EMPCD"]?.ToString() ?? "",
            new OracleParameter("CID", req.CLASS_ID));

        // Round-robin gán
        int updated = 0;
        for (int i = 0; i < empcds.Count; i++)
        {
            var gid = req.GROUP_IDS[i % req.GROUP_IDS.Count];
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_ENROLLMENT
                   SET GROUP_ID = :GID, UPDT_ID = :USR
                 WHERE CLASS_ID = :CID AND EMPCD = :EMP",
                new OracleParameter("GID", gid),
                new OracleParameter("USR", req.LOGIN_USER),
                new OracleParameter("CID", req.CLASS_ID),
                new OracleParameter("EMP", empcds[i]));
            updated++;
        }
        return updated;
    }

    // ═══════════════════════════════════════════════════════════════
    //  INTERNAL helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<string?> GetStatusAsync(int classId)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => r["STATUS"]?.ToString(),
            new OracleParameter("ID", classId))).FirstOrDefault();
    }

    private async Task<T?> GetFieldAsync<T>(int classId, string column)
    {
        var sql = $"SELECT {column} FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID";
        var rows = await _db.ExecuteQueryAsync(sql,
            r => r[column],
            new OracleParameter("ID", classId));
        var v = rows.FirstOrDefault();
        if (v is null or DBNull) return default;
        return (T)Convert.ChangeType(v, typeof(T));
    }

    private async Task SetStatusAsync(int classId, string status, string actor)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_CLASS
               SET STATUS = :S, UPDT_ID = :USR
             WHERE ID = :ID",
            new OracleParameter("S",   status),
            new OracleParameter("USR", actor),
            new OracleParameter("ID",  classId));
    }

    private static ClassModel MapClassLight(OracleDataReader r)
    {
        var c = MapClassFullWithCourse(r);
        c.ENROLLMENT_COUNT = Convert.ToInt32(r["ENROLLMENT_COUNT"]);
        c.SESSION_COUNT    = Convert.ToInt32(r["SESSION_COUNT"]);
        return c;
    }

    private static ClassModel MapClassFullWithCourse(OracleDataReader r) => new()
    {
        ID                     = Convert.ToInt32(r["ID"]),
        COURSE_ID              = Convert.ToInt32(r["COURSE_ID"]),
        CLASS_NAME             = r["CLASS_NAME"]?.ToString() ?? "",
        DESCRIPTION            = r["DESCRIPTION"] as string,
        STATUS                 = r["STATUS"]?.ToString() ?? "DRAFT",
        REGISTRATION_MODE      = r["REGISTRATION_MODE"]?.ToString() ?? "ASSIGNED",
        MAX_STUDENTS           = r["MAX_STUDENTS"] is DBNull ? null : Convert.ToInt32(r["MAX_STUDENTS"]),
        REGISTRATION_DEADLINE  = r["REGISTRATION_DEADLINE"] as DateTime?,
        START_DATE             = r["START_DATE"] as DateTime?,
        END_DATE               = r["END_DATE"] as DateTime?,
        MIN_ATTENDANCE_PERCENT = Convert.ToDecimal(r["MIN_ATTENDANCE_PERCENT"] is DBNull ? 75 : r["MIN_ATTENDANCE_PERCENT"]),
        FINAL_TEST_ID          = r["FINAL_TEST_ID"] is DBNull ? null : Convert.ToInt32(r["FINAL_TEST_ID"]),
        REQUIRE_POST_REVIEW    = Convert.ToInt32(r["REQUIRE_POST_REVIEW"]),
        IS_EXPRESS             = Convert.ToInt32(r["IS_EXPRESS"]),
        CLONED_FROM_CLASS_ID   = r["CLONED_FROM_CLASS_ID"] is DBNull ? null : Convert.ToInt32(r["CLONED_FROM_CLASS_ID"]),
        CLONED_FROM_TYPE       = r["CLONED_FROM_TYPE"] as string,
        INST_ID                = r["INST_ID"] as string,
        INST_DT                = r["INST_DT"] as DateTime?,
        UPDT_ID                = r["UPDT_ID"] as string,
        UPDT_DT                = r["UPDT_DT"] as DateTime?,
        COURSE_TITLE           = r["COURSE_TITLE"] as string,
        COURSE_MODE            = r["COURSE_MODE"] as string,
    };
}
