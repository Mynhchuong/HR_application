using HR_api.Data;
using HR_api.Models.Training;
using HR_api.Models.Bulletin;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// CRUD Class + state machine + teacher + group management.
// State machine: DRAFT → OPEN_FOR_REGISTRATION → SCHEDULED → IN_PROGRESS → COMPLETED → CLOSED
//                                              ↘ CANCELLED
// IN_PROGRESS ← auto (batch job, phase 2). Ở đây chỉ expose transition HR chủ động.
public class TrainingClassService
{
    private readonly OracleService _db;
    private readonly TrainingNotificationService _noti;
    private readonly BulletinService _bulletin;
    private readonly IConfiguration _config;

    public TrainingClassService(OracleService db, TrainingNotificationService noti, BulletinService bulletin, IConfiguration config)
    {
        _db = db;
        _noti = noti;
        _bulletin = bulletin;
        _config = config;
    }

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
                   CL.BULLETIN_ID,
                   CL.INST_ID, CL.INST_DT, CL.UPDT_ID, CL.UPDT_DT,
                   CO.TITLE AS COURSE_TITLE, CO.COURSE_MODE,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_ENROLLMENT E
                     WHERE E.CLASS_ID = CL.ID
                       AND E.STATUS IN ('ENROLLED','PENDING_APPROVAL','COMPLETED','FAILED')) AS ENROLLMENT_COUNT,
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

    public async Task<ClassModel?> GetDetailAsync(int id, string? empcd = null)
    {
        const string sql = @"
            SELECT CL.ID, CL.COURSE_ID, CL.CLASS_NAME, CL.DESCRIPTION, CL.STATUS,
                   CL.REGISTRATION_MODE, CL.MAX_STUDENTS, CL.REGISTRATION_DEADLINE,
                   CL.START_DATE, CL.END_DATE,
                   CL.MIN_ATTENDANCE_PERCENT, CL.FINAL_TEST_ID, CL.REQUIRE_POST_REVIEW,
                   CL.IS_EXPRESS, CL.CLONED_FROM_CLASS_ID, CL.CLONED_FROM_TYPE,
                   CL.BULLETIN_ID,
                   CL.INST_ID, CL.INST_DT, CL.UPDT_ID, CL.UPDT_DT,
                   CO.TITLE AS COURSE_TITLE, CO.COURSE_MODE
              FROM HRMS.HR_TRAINING_CLASS CL
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
             WHERE CL.ID = :ID";

        var list = await _db.ExecuteQueryAsync(sql, MapClassFullWithCourse,
            new OracleParameter("ID", id));
        var cls = list.FirstOrDefault();
        if (cls == null) return null;

        if (!string.IsNullOrWhiteSpace(empcd))
        {
            var progress = (await _db.ExecuteQueryAsync(@"
                SELECT 
                    (SELECT COUNT(*) FROM HRMS.HR_TRAINING_SESSION S
                      WHERE S.CLASS_ID = E.CLASS_ID
                        AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)) AS TOTAL_SESSIONS,
                    (SELECT COUNT(*) FROM HRMS.HR_TRAINING_ATTENDANCE A
                      WHERE A.EMPCD = E.EMPCD
                        AND A.STATUS IN ('PRESENT', 'LATE')
                        AND A.TEACHER_CONFIRMED = 1
                        AND A.SESSION_ID IN (
                            SELECT S.ID FROM HRMS.HR_TRAINING_SESSION S
                             WHERE S.CLASS_ID = E.CLASS_ID
                               AND S.STATUS = 'COMPLETED'
                               AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)
                        )) AS COMPLETED_SESSIONS
                  FROM HRMS.HR_TRAINING_ENROLLMENT E
                 WHERE E.CLASS_ID = :CID AND E.EMPCD = :EMP",
                r => new {
                    Total = Convert.ToInt32(r["TOTAL_SESSIONS"]),
                    Completed = Convert.ToInt32(r["COMPLETED_SESSIONS"])
                },
                new OracleParameter("CID", id),
                new OracleParameter("EMP", empcd)
            )).FirstOrDefault();

            if (progress != null)
            {
                cls.TOTAL_SESSIONS = progress.Total;
                cls.COMPLETED_SESSIONS = progress.Completed;
            }
            else
            {
                var total = (await _db.ExecuteQueryAsync(@"
                    SELECT COUNT(*) AS CNT FROM HRMS.HR_TRAINING_SESSION WHERE CLASS_ID = :CID",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("CID", id)
                )).FirstOrDefault();
                cls.TOTAL_SESSIONS = total;
                cls.COMPLETED_SESSIONS = 0;
            }
        }
        else
        {
            var total = (await _db.ExecuteQueryAsync(@"
                SELECT COUNT(*) AS CNT FROM HRMS.HR_TRAINING_SESSION WHERE CLASS_ID = :CID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CID", id)
            )).FirstOrDefault();
            cls.TOTAL_SESSIONS = total;
            cls.COMPLETED_SESSIONS = 0;
        }

        return cls;
    }

    public async Task<List<ClassSessionLightModel>> GetSessionsAsync(int classId, string? empcd = null)
    {
        const string sql = @"
            SELECT S.ID, S.CLASS_ID, S.SESSION_NO, S.SESSION_DATE, S.START_TIME, S.END_TIME,
                   S.TOPIC, S.LOCATION, S.STATUS, S.GROUP_ID,
                   G.GROUP_NAME,
                   A.STATUS AS ATTENDANCE_STATUS
              FROM HRMS.HR_TRAINING_SESSION S
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = S.GROUP_ID
              LEFT JOIN HRMS.HR_TRAINING_ATTENDANCE A ON A.SESSION_ID = S.ID AND A.EMPCD = :EMP
             WHERE S.CLASS_ID = :CID
             ORDER BY S.SESSION_DATE, S.START_TIME";
             
        return await _db.ExecuteQueryAsync(sql, r => new ClassSessionLightModel
        {
            ID                = Convert.ToInt32(r["ID"]),
            CLASS_ID          = Convert.ToInt32(r["CLASS_ID"]),
            SESSION_NO        = Convert.ToInt32(r["SESSION_NO"]),
            SESSION_DATE      = Convert.ToDateTime(r["SESSION_DATE"]),
            START_TIME        = r["START_TIME"]?.ToString() ?? "",
            END_TIME          = r["END_TIME"]?.ToString() ?? "",
            TOPIC             = r["TOPIC"] as string,
            LOCATION          = r["LOCATION"] as string,
            STATUS            = r["STATUS"]?.ToString() ?? "UPCOMING",
            GROUP_ID          = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
            GROUP_NAME        = r["GROUP_NAME"] as string,
            ATTENDANCE_STATUS = r["ATTENDANCE_STATUS"] as string
        }, 
        new OracleParameter("EMP", (object?)empcd ?? DBNull.Value),
        new OracleParameter("CID", classId));
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
        // DDL CHK_TRCL_MODE chỉ cho phép ASSIGNED / OPEN (không có HYBRID — pre-assign bắt buộc trong
        // Class OPEN dùng SOURCE='ASSIGNED' trên từng enrollment, xem rules §3.3, không phải mode riêng).
        if (req.REGISTRATION_MODE != "ASSIGNED" && req.REGISTRATION_MODE != "OPEN")
            throw new InvalidOperationException("REGISTRATION_MODE phải là ASSIGNED hoặc OPEN");
        // OPEN mode: MAX_STUDENTS nullable (null = unlimited §3.2), REGISTRATION_DEADLINE bắt buộc
        if (req.REGISTRATION_MODE == "OPEN" && req.REGISTRATION_DEADLINE == null)
            throw new InvalidOperationException("Class OPEN phải có REGISTRATION_DEADLINE");
        if (req.MIN_ATTENDANCE_PERCENT.HasValue &&
            (req.MIN_ATTENDANCE_PERCENT < 0 || req.MIN_ATTENDANCE_PERCENT > 100))
            throw new InvalidOperationException("MIN_ATTENDANCE_PERCENT phải trong 0..100");
        if (req.START_DATE.HasValue && req.END_DATE.HasValue && req.END_DATE < req.START_DATE)
            throw new InvalidOperationException("END_DATE phải sau START_DATE");

        if (req.ID == null)
        {
            // FINAL_TEST_ID chỉ có thể trỏ tới 1 test đã tồn tại VÀ thuộc đúng Class (§16 ràng buộc
            // code layer) — nhưng lúc tạo mới Class chưa có ID nên không thể có test nào thuộc về nó.
            // Chặn ngay từ đầu, hướng dẫn HR gán final test sau khi Class + Test đã tồn tại.
            if (req.FINAL_TEST_ID.HasValue)
                throw new InvalidOperationException(
                    "Không thể gán FINAL_TEST_ID khi tạo Class mới — hãy tạo Class, tạo Test cho Class đó, rồi gán Final Test sau.");

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

            // §16 ràng buộc code layer: FINAL_TEST_ID phải trỏ tới test CLASS_ID = chính Class này
            // và IS_TEMPLATE=0 (không phải test mẫu của Course).
            await ValidateFinalTestAsync(req.FINAL_TEST_ID, req.ID.Value);

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

    // DRAFT → OPEN_FOR_REGISTRATION (chỉ Class OPEN mode).
    // Idempotent (§4.2): bấm lại khi đã OPEN_FOR_REGISTRATION/SCHEDULED/IN_PROGRESS → không lỗi,
    // không đổi status lùi, chỉ báo lại đã publish rồi (không có gì để re-enqueue ở bước này vì
    // OPEN_FOR_REGISTRATION chưa có ai ENROLLED — noti "mời đăng ký" đi qua bulletin, không qua đây).
    public async Task<PublishResult> PublishRegistrationAsync(int classId, string actor, string? registerUrl = null)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");

        if (current is "OPEN_FOR_REGISTRATION" or "SCHEDULED" or "IN_PROGRESS")
            return new PublishResult { ALREADY_PUBLISHED = true, NOTIFIED_COUNT = 0 };

        if (current != "DRAFT")
            throw new InvalidOperationException($"Chỉ publish từ DRAFT, hiện đang {current}");

        var mode = await GetFieldAsync<string>(classId, "REGISTRATION_MODE")
            ?? throw new InvalidOperationException("Không đọc được REGISTRATION_MODE");
        if (mode != "OPEN")
            throw new InvalidOperationException("Chỉ Class OPEN mới publish-registration. ASSIGNED dùng finalize thẳng.");

        await SetStatusAsync(classId, "OPEN_FOR_REGISTRATION", actor);

        // Quảng cáo lớp qua Bản tin (Bulletin) — lỗi tạo bản tin KHÔNG được rollback việc mở đăng ký
        // đã thành công, chỉ log lại để HR biết tạo tay qua BulletinAdmin nếu cần.
        try { await CreateRegistrationBulletinAsync(classId, actor, registerUrl); }
        catch (Exception ex) { Console.WriteLine($"[PublishRegistrationAsync] Lỗi tạo bản tin cho Class {classId}: {ex.Message}"); }

        return new PublishResult { ALREADY_PUBLISHED = false, NOTIFIED_COUNT = 0 };
    }

    // Tự tạo + publish 1 Bản tin quảng cáo lớp OPEN đang mở đăng ký, lưu BULLETIN_ID vào Class để
    // (1) không tạo trùng nếu gọi lại, (2) unpublish được khi lớp bị hủy (xem CancelAsync).
    // registerUrl: build sẵn bằng Url.Action từ HR_web (đúng PathBase/scheme/host thật) — chỉ dùng
    // config Training:WebBaseUrl làm fallback khi gọi thẳng API (script/Swagger, không qua web).
    private async Task CreateRegistrationBulletinAsync(int classId, string actor, string? registerUrl)
    {
        var cls = await GetDetailAsync(classId);
        if (cls == null) return;

        var sessionCount = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_SESSION WHERE CLASS_ID = :CID",
            r => Convert.ToInt32(r["CNT"]), new OracleParameter("CID", classId))).FirstOrDefault();

        var deadlineText = cls.REGISTRATION_DEADLINE?.ToString("dd/MM/yyyy") ?? "Không giới hạn";
        var slotText = cls.MAX_STUDENTS.HasValue ? $"{cls.MAX_STUDENTS} học viên" : "Không giới hạn";

        // Ưu tiên URL do HR_web build sẵn bằng Url.Action (đúng PathBase/scheme/host thật ở production).
        // Fallback: WebBaseUrl cấu hình trong appsettings — chỉ dùng khi gọi thẳng API, không qua web
        // (Swagger/script) — trường hợp này không có Url.Action nên phải tự ghép, để trống nếu root.
        var effectiveRegisterUrl = !string.IsNullOrWhiteSpace(registerUrl)
            ? registerUrl
            : $"{(_config["Training:WebBaseUrl"] ?? "").TrimEnd('/')}/Training/ClassRegister/{classId}";

        var content = $@"
            <p><strong>{System.Net.WebUtility.HtmlEncode(cls.COURSE_TITLE)}</strong> — Lớp {System.Net.WebUtility.HtmlEncode(cls.CLASS_NAME)} đang mở đăng ký tự do.</p>
            <p>Số buổi học: {sessionCount} buổi<br/>
               Hạn đăng ký: {deadlineText}<br/>
               Số slot tối đa: {slotText}</p>
            <p>
                <a href=""{effectiveRegisterUrl}""
                   style=""display:inline-block;padding:10px 20px;background-color:#198754;color:#ffffff;
                          font-weight:bold;text-decoration:none;border-radius:8px;"">
                    Nhấn vào đây để đăng ký ngay
                </a>
            </p>";

        var publishTo = cls.REGISTRATION_DEADLINE ?? DateTime.Today.AddDays(30);

        var (saveOk, _, bulletinId) = await _bulletin.SaveAsync(new SaveBulletinRequest
        {
            TITLE        = $"[Đào tạo] Mở đăng ký: {cls.COURSE_TITLE}",
            CONTENT      = content,
            PUBLISH_FROM = DateTime.Today,
            PUBLISH_TO   = publishTo,
            IS_PINNED    = 0,
            PIN_ORDER    = 0,
            LOGIN_USER   = actor,
        });
        if (!saveOk || bulletinId == 0) return;

        await _bulletin.PublishAsync(bulletinId, actor);

        await _db.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_TRAINING_CLASS SET BULLETIN_ID = :BID WHERE ID = :CID",
            new OracleParameter("BID", bulletinId),
            new OracleParameter("CID", classId));
    }

    // DRAFT / OPEN_FOR_REGISTRATION → SCHEDULED (chốt DS).
    // Idempotent (§4.2): bấm Publish/Finalize lại khi Class đã SCHEDULED/IN_PROGRESS → KHÔNG throw,
    // KHÔNG đổi status — chỉ re-enqueue TRAINING_ASSIGNED cho các học viên ENROLLED chưa có noti
    // nào (PENDING/CLAIMED/SENT) cho Class này, để bù các trường hợp FCM lỗi/học viên mới được
    // pre-assign sau khi đã chốt lần đầu.
    public async Task<PublishResult> FinalizeEnrollmentAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");

        if (current is "SCHEDULED" or "IN_PROGRESS")
        {
            var resent = await ReEnqueueAssignedNotiAsync(classId);
            return new PublishResult { ALREADY_PUBLISHED = true, NOTIFIED_COUNT = resent };
        }

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

        // Publish lần đầu — gửi TRAINING_ASSIGNED cho toàn bộ học viên ENROLLED (dedup theo noti
        // queue để không gửi trùng nếu người đó đã được thông báo lúc bulk-assign trước đó).
        var notified = await ReEnqueueAssignedNotiAsync(classId);
        return new PublishResult { ALREADY_PUBLISHED = false, NOTIFIED_COUNT = notified };
    }

    // Enqueue TRAINING_ASSIGNED cho học viên ENROLLED của Class CHƯA có row nào trong
    // HR_TRAINING_NOTI_QUEUE (PENDING/CLAIMED/SENT) — dedup theo bảng queue hiện có.
    // Set-based 1 câu INSERT-SELECT (EnqueueAssignedForMissingAsync) — lớp 800+ học viên
    // trước đây loop 800+ INSERT làm nút "Lên lịch học" treo hàng chục giây.
    private async Task<int> ReEnqueueAssignedNotiAsync(int classId)
    {
        var ph = (await _db.ExecuteQueryAsync(@"
            SELECT CLASS_NAME, TO_CHAR(START_DATE,'DD/MM/YYYY') START_DATE_STR
              FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => new Dictionary<string, string>
            {
                ["className"] = r["CLASS_NAME"]?.ToString() ?? "",
                ["startDate"] = r["START_DATE_STR"]?.ToString() ?? "",
            }, new OracleParameter("ID", classId))).FirstOrDefault() ?? new();

        return await _noti.EnqueueAssignedForMissingAsync(classId, ph);
    }

    // Bất kỳ state (trừ CLOSED / CANCELLED / COMPLETED) → CANCELLED
    public async Task CancelAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (current == "CLOSED" || current == "CANCELLED" || current == "COMPLETED")
            throw new InvalidOperationException($"Class đang {current}, không cancel được");
        await SetStatusAsync(classId, "CANCELLED", actor);

        // Lớp hủy rồi → gỡ quảng cáo khỏi Bulletin (nếu có), tránh nhân viên đăng ký vào lớp đã hủy.
        // GetFieldAsync<int> trả 0 khi NULL (Convert.ChangeType không hỗ trợ Nullable<T> đích).
        var bulletinId = await GetFieldAsync<int>(classId, "BULLETIN_ID");
        if (bulletinId > 0) await _bulletin.UnpublishAsync(bulletinId, actor);
    }

    // COMPLETED → CLOSED (chốt report cuối, không sửa nữa)
    public async Task CloseAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (current != "COMPLETED")
            throw new InvalidOperationException($"Chỉ close từ COMPLETED, hiện đang {current}");

        // CLOSED = frozen — đóng khi chưa chốt kết quả sẽ làm học viên kẹt không có chứng chỉ
        // (finalize yêu cầu COMPLETED). Còn row ENROLLED nghĩa là chưa chạy finalize.
        var unfinalized = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND STATUS = 'ENROLLED'",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", classId))).First();
        if (unfinalized > 0)
            throw new InvalidOperationException(
                $"Còn {unfinalized} học viên chưa được chốt kết quả — bấm 'Chốt kết quả & cấp chứng chỉ' trước khi đóng lớp");

        await SetStatusAsync(classId, "CLOSED", actor);
    }

    // Xóa vĩnh viễn Lớp học và toàn bộ dữ liệu liên quan (Cascade Delete)
    public async Task DeleteAsync(int classId, string actor)
    {
        var current = await GetStatusAsync(classId);
        if (current == null)
            throw new InvalidOperationException("Không tìm thấy Lớp học");

        // Bắt buộc phải Hủy Lớp trước khi xóa vĩnh viễn — Cancel mới gửi thông báo hủy
        // cho học viên và gỡ Bulletin; xóa thẳng từ SCHEDULED/IN_PROGRESS sẽ xóa mất
        // dữ liệu học viên đang học mà không ai được báo trước.
        if (current != "CANCELLED")
            throw new InvalidOperationException($"Chỉ xóa vĩnh viễn được khi Lớp đã Hủy — hiện đang {current}. Bấm 'Hủy Lớp' trước.");

        // Gỡ BULLETIN liên kết nếu có
        var bulletinId = await GetFieldAsync<int>(classId, "BULLETIN_ID");
        if (bulletinId > 0)
        {
            try { await _bulletin.UnpublishAsync(bulletinId, actor); } catch {}
        }

        // Gỡ FK FINAL_TEST_ID trước khi xóa TEST — nếu không sẽ dính ORA-02292 (FK_TRCL_FINAL_TEST)
        // bất cứ khi nào Class có gán bài thi cuối khóa.
        await _db.ExecuteNonQueryAsync("UPDATE HRMS.HR_TRAINING_CLASS SET FINAL_TEST_ID = NULL WHERE ID = :ID",
            new OracleParameter("ID", classId));

        // Xóa dữ liệu liên quan theo thứ tự ngược lại (tránh dính FK constraint)
        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST_ANSWER
            WHERE ATTEMPT_ID IN (
                SELECT TA.ID FROM HRMS.HR_TRAINING_TEST_ATTEMPT TA
                JOIN HRMS.HR_TRAINING_TEST TT ON TA.TEST_ID = TT.ID
                WHERE TT.CLASS_ID = :CID
            )", new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST_ATTEMPT 
            WHERE TEST_ID IN (
                SELECT ID FROM HRMS.HR_TRAINING_TEST WHERE CLASS_ID = :CID
            )", new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST_OPTION 
            WHERE QUESTION_ID IN (
                SELECT TQ.ID FROM HRMS.HR_TRAINING_TEST_QUESTION TQ
                JOIN HRMS.HR_TRAINING_TEST TT ON TQ.TEST_ID = TT.ID
                WHERE TT.CLASS_ID = :CID
            )", new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST_QUESTION 
            WHERE TEST_ID IN (
                SELECT ID FROM HRMS.HR_TRAINING_TEST WHERE CLASS_ID = :CID
            )", new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_ATTENDANCE 
            WHERE SESSION_ID IN (
                SELECT ID FROM HRMS.HR_TRAINING_SESSION WHERE CLASS_ID = :CID
            )", new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_SESSION WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_CLASS_TEACHER WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_MATERIAL_VIEW 
            WHERE MATERIAL_ID IN (
                SELECT ID FROM HRMS.HR_TRAINING_MATERIAL WHERE CLASS_ID = :CID
            )", new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_MATERIAL WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_REVIEW_TEACHER WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_REVIEW WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_NOTI_QUEUE WHERE RELATED_CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_AUDIT WHERE CLASS_ID = :CID",
            new OracleParameter("CID", classId));

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_CLASS WHERE ID = :CID",
            new OracleParameter("CID", classId));
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<ClassTeacherModel>> GetTeachersAsync(int classId)
    {
        const string sql = @"
            SELECT T.CLASS_ID, T.EMPCD, T.IS_PRIMARY, EC.CNAME AS EMP_NAME,
                   T.GROUP_ID, G.GROUP_NAME
              FROM HRMS.HR_TRAINING_CLASS_TEACHER T
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = T.EMPCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = T.GROUP_ID
             WHERE T.CLASS_ID = :CID
             ORDER BY T.IS_PRIMARY DESC, T.EMPCD, G.GROUP_NAME";
        return await _db.ExecuteQueryAsync(sql, r => new ClassTeacherModel
        {
            CLASS_ID   = Convert.ToInt32(r["CLASS_ID"]),
            EMPCD      = r["EMPCD"]?.ToString() ?? "",
            EMP_NAME   = r["EMP_NAME"] as string,
            IS_PRIMARY = Convert.ToInt32(r["IS_PRIMARY"]),
            GROUP_ID   = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
            GROUP_NAME = r["GROUP_NAME"] as string,
        }, new OracleParameter("CID", classId));
    }

    public async Task AssignTeacherAsync(AssignTeacherRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EMPCD))
            throw new InvalidOperationException("EMPCD không được để trống");

        // 1 GV có thể phụ trách NHIỀU nhóm: GROUP_IDS là danh sách ĐẦY ĐỦ nhóm phụ trách —
        // replace toàn bộ rows của GV (delete + insert lại, không MERGE). Rỗng = dạy cả lớp
        // (1 row GROUP_ID NULL). GROUP_ID đơn (legacy) được gộp vào danh sách.
        var groupIds = (req.GROUP_IDS ?? new List<int>()).Distinct().ToList();
        if (req.GROUP_ID.HasValue && !groupIds.Contains(req.GROUP_ID.Value))
            groupIds.Add(req.GROUP_ID.Value);

        // Verify từng GROUP_ID thuộc Class (§16 — FK không check chéo được)
        foreach (var gid in groupIds)
        {
            var ok = (await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE ID = :GID AND CLASS_ID = :CID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("GID", gid),
                new OracleParameter("CID", req.CLASS_ID))).First();
            if (ok == 0) throw new InvalidOperationException("Group không thuộc Class này");
        }

        // Nếu IS_PRIMARY=1 → clear PRIMARY của GV khác (Class chỉ 1 primary teacher)
        if (req.IS_PRIMARY == 1)
        {
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_CLASS_TEACHER
                   SET IS_PRIMARY = 0
                 WHERE CLASS_ID = :CID AND IS_PRIMARY = 1 AND EMPCD <> :EMP",
                new OracleParameter("CID", req.CLASS_ID),
                new OracleParameter("EMP", req.EMPCD));
        }

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_CLASS_TEACHER
             WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));

        // IS_PRIMARY đồng nhất trên mọi row của GV (unique: CLASS_ID + EMPCD + GROUP_ID)
        var insertGids = groupIds.Count == 0 ? new List<int?> { null } : groupIds.Select(g => (int?)g).ToList();
        foreach (var gid in insertGids)
        {
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_CLASS_TEACHER (CLASS_ID, EMPCD, IS_PRIMARY, GROUP_ID, INST_ID)
                VALUES (:CID, :EMP, :PRI, :GID, :USR)",
                new OracleParameter("CID", req.CLASS_ID),
                new OracleParameter("EMP", req.EMPCD),
                new OracleParameter("PRI", req.IS_PRIMARY),
                new OracleParameter("GID", (object?)gid ?? DBNull.Value),
                new OracleParameter("USR", req.LOGIN_USER));
        }
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
            VALUES (:COID, :NAME, :DESCX,
                    'DRAFT', 'ASSIGNED',
                    :SD, :ED,
                    :MIN,
                    'COURSE_TEMPLATE', 0,
                    :USR)
            RETURNING ID INTO :NEW_ID",
            new OracleParameter("COID", req.COURSE_ID),
            new OracleParameter("NAME", req.CLASS_NAME),
            new OracleParameter("DESCX", (object?)(req.DESCRIPTION ?? course.DESC) ?? DBNull.Value),
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

            // Step C.5: Test bank deep copy (§15b Cách 1, training_plan §5.7 Step C) — chỉ nếu
            // Course có test template (IS_TEMPLATE=1, TEMPLATE_COURSE_ID=courseId). Test mới ở
            // Class DRAFT — teacher publish lại với window ngày phù hợp đợt mới.
            await DeepCopyTestsAsync(@"
                SELECT ID, TITLE, DESCRIPTION, DURATION_MINUTES, PASS_SCORE
                  FROM HRMS.HR_TRAINING_TEST
                 WHERE IS_TEMPLATE = 1 AND TEMPLATE_COURSE_ID = :CID
                 ORDER BY ID",
                new OracleParameter("CID", req.COURSE_ID), newClassId, req.LOGIN_USER);

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

    // ═══════════════════════════════════════════════════════════════
    //  CLONE §15b Cách 2 — "Sao chép sang đợt mới" từ 1 Class đã có (COMPLETED hoặc SCHEDULED)
    //  Giữ nguyên mọi setting khác của Class nguồn — chỉ đổi CLASS_NAME + START_DATE + DS học viên.
    //  Sessions dịch ngày theo delta (START_DATE mới - START_DATE cũ). Teachers copy toàn bộ.
    //  Test deep-copy riêng cho đợt mới (không đè scoring cũ). Groups clone cấu trúc (tên +
    //  MAX_STUDENTS/group), KHÔNG clone ENROLLMENT.GROUP_ID — HR chia lại nhóm cho DS mới.
    //  Pre-assign KHÔNG copy — Enrollments đợt mới toàn bộ từ req.EMPCDS (SOURCE='ASSIGNED').
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> CloneFromClassAsync(CloneFromClassRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CLASS_NAME))
            throw new InvalidOperationException("CLASS_NAME required");
        if (req.CLASS_NAME.Length > 80)
            throw new InvalidOperationException("CLASS_NAME tối đa 80 ký tự (§12)");

        var old = (await _db.ExecuteQueryAsync(@"
            SELECT COURSE_ID, STATUS, DESCRIPTION, REGISTRATION_MODE, MAX_STUDENTS,
                   REGISTRATION_DEADLINE, START_DATE, END_DATE,
                   MIN_ATTENDANCE_PERCENT, REQUIRE_POST_REVIEW, FINAL_TEST_ID, IS_EXPRESS
              FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => new
            {
                COURSE_ID    = Convert.ToInt32(r["COURSE_ID"]),
                STATUS       = r["STATUS"]?.ToString() ?? "",
                DESCRIPTION  = r["DESCRIPTION"] as string,
                REG_MODE     = r["REGISTRATION_MODE"]?.ToString() ?? "ASSIGNED",
                MAX_STUDENTS = r["MAX_STUDENTS"] is DBNull ? (int?)null : Convert.ToInt32(r["MAX_STUDENTS"]),
                REG_DEADLINE = r["REGISTRATION_DEADLINE"] as DateTime?,
                START_DATE   = r["START_DATE"] as DateTime?,
                END_DATE     = r["END_DATE"] as DateTime?,
                MIN_ATT      = Convert.ToDecimal(r["MIN_ATTENDANCE_PERCENT"]),
                REQ_REVIEW   = Convert.ToInt32(r["REQUIRE_POST_REVIEW"]),
                FINAL_TEST   = r["FINAL_TEST_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["FINAL_TEST_ID"]),
                IS_EXPRESS   = Convert.ToInt32(r["IS_EXPRESS"]),
            }, new OracleParameter("ID", req.SOURCE_CLASS_ID))).FirstOrDefault();

        if (old == null) throw new InvalidOperationException("Không tìm thấy Class nguồn");
        if (old.STATUS != "COMPLETED" && old.STATUS != "SCHEDULED")
            throw new InvalidOperationException($"Class nguồn đang {old.STATUS} — chỉ sao chép được từ COMPLETED hoặc SCHEDULED");
        if (old.IS_EXPRESS == 1)
            throw new InvalidOperationException("Class EXPRESS không dùng Clone — dùng Express Create để tạo đợt mới");

        // Dịch ngày: session/deadline/end_date của bản gốc dịch theo cùng 1 delta so với START_DATE mới.
        var deltaDays = old.START_DATE.HasValue
            ? (req.START_DATE.Date - old.START_DATE.Value.Date).TotalDays
            : 0;
        DateTime? newEndDate = old.END_DATE.HasValue ? old.END_DATE.Value.AddDays(deltaDays) : (DateTime?)null;
        DateTime? newDeadline = old.REG_DEADLINE.HasValue ? old.REG_DEADLINE.Value.AddDays(deltaDays) : (DateTime?)null;

        // Step A: INSERT Class DRAFT (giữ nguyên setting, trừ tên/ngày/DS học viên)
        var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_CLASS
                (COURSE_ID, CLASS_NAME, DESCRIPTION,
                 STATUS, REGISTRATION_MODE, MAX_STUDENTS, REGISTRATION_DEADLINE,
                 START_DATE, END_DATE,
                 MIN_ATTENDANCE_PERCENT, REQUIRE_POST_REVIEW,
                 CLONED_FROM_CLASS_ID, CLONED_FROM_TYPE, IS_EXPRESS,
                 INST_ID)
            VALUES (:COID, :NAME, :DESCX,
                    'DRAFT', :REG_MODE, :MAX_STUDENTS, :REG_DEADLINE,
                    :SD, :ED,
                    :MIN_ATT, :REQ_REVIEW,
                    :SRC_ID, 'PREV_CLASS', 0,
                    :USR)
            RETURNING ID INTO :NEW_ID",
            new OracleParameter("COID",        old.COURSE_ID),
            new OracleParameter("NAME",        req.CLASS_NAME),
            new OracleParameter("DESCX",       (object?)old.DESCRIPTION ?? DBNull.Value),
            new OracleParameter("REG_MODE",    old.REG_MODE),
            new OracleParameter("MAX_STUDENTS",(object?)old.MAX_STUDENTS ?? DBNull.Value),
            new OracleParameter("REG_DEADLINE",(object?)newDeadline ?? DBNull.Value),
            new OracleParameter("SD",          req.START_DATE.Date),
            new OracleParameter("ED",          (object?)newEndDate ?? DBNull.Value),
            new OracleParameter("MIN_ATT",     old.MIN_ATT),
            new OracleParameter("REQ_REVIEW",  old.REQ_REVIEW),
            new OracleParameter("SRC_ID",      req.SOURCE_CLASS_ID),
            new OracleParameter("USR",         req.LOGIN_USER),
            idParam);
        var newClassId = OracleService.ConvertToInt(idParam.Value);

        try
        {
            // Step B: Clone Group structure (tên + MAX_STUDENTS/group) — map oldGroupId → newGroupId
            // để Step C (sessions) gán đúng GROUP_ID mới. KHÔNG clone ENROLLMENT.GROUP_ID (§15b).
            var oldGroups = await _db.ExecuteQueryAsync(@"
                SELECT ID, GROUP_NAME, MAX_STUDENTS FROM HRMS.HR_TRAINING_CLASS_GROUP
                 WHERE CLASS_ID = :ID ORDER BY GROUP_NAME",
                r => new
                {
                    ID   = Convert.ToInt32(r["ID"]),
                    NAME = r["GROUP_NAME"]?.ToString() ?? "",
                    MAX  = r["MAX_STUDENTS"] is DBNull ? (int?)null : Convert.ToInt32(r["MAX_STUDENTS"]),
                }, new OracleParameter("ID", req.SOURCE_CLASS_ID));

            var groupIdMap = new Dictionary<int, int>();
            foreach (var g in oldGroups)
            {
                var gIdParam = new OracleParameter("NEW_GID", OracleDbType.Int32)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_CLASS_GROUP (CLASS_ID, GROUP_NAME, MAX_STUDENTS, INST_ID)
                    VALUES (:CID, :NAME, :MAX, :USR)
                    RETURNING ID INTO :NEW_GID",
                    new OracleParameter("CID",  newClassId),
                    new OracleParameter("NAME", g.NAME),
                    new OracleParameter("MAX",  (object?)g.MAX ?? DBNull.Value),
                    new OracleParameter("USR",  req.LOGIN_USER),
                    gIdParam);
                groupIdMap[g.ID] = OracleService.ConvertToInt(gIdParam.Value);
            }

            // Step C: Clone sessions — dịch ngày theo delta, map GROUP_ID cũ → mới (NULL nếu session
            // chung hoặc group đã bị xoá ở bản gốc).
            var oldSessions = await _db.ExecuteQueryAsync(@"
                SELECT SESSION_NO, SESSION_DATE, START_TIME, END_TIME, TOPIC, LOCATION, GROUP_ID
                  FROM HRMS.HR_TRAINING_SESSION
                 WHERE CLASS_ID = :ID ORDER BY SESSION_NO",
                r => new
                {
                    NO    = Convert.ToInt32(r["SESSION_NO"]),
                    DATE  = Convert.ToDateTime(r["SESSION_DATE"]),
                    ST    = r["START_TIME"]?.ToString() ?? "0800",
                    ET    = r["END_TIME"]?.ToString()   ?? "1130",
                    TOPIC = r["TOPIC"] as string,
                    LOC   = r["LOCATION"] as string,
                    GID   = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                }, new OracleParameter("ID", req.SOURCE_CLASS_ID));

            foreach (var s in oldSessions)
            {
                int? newGid = s.GID.HasValue && groupIdMap.TryGetValue(s.GID.Value, out var mapped) ? mapped : (int?)null;
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_SESSION
                        (CLASS_ID, SESSION_NO, SESSION_DATE, START_TIME, END_TIME, TOPIC, LOCATION, STATUS, GROUP_ID, INST_ID)
                    VALUES (:CID, :NO, :DT, :ST, :ET, :TP, :LC, 'UPCOMING', :GID, :USR)",
                    new OracleParameter("CID", newClassId),
                    new OracleParameter("NO",  s.NO),
                    new OracleParameter("DT",  s.DATE.AddDays(deltaDays).Date),
                    new OracleParameter("ST",  s.ST),
                    new OracleParameter("ET",  s.ET),
                    new OracleParameter("TP",  (object?)s.TOPIC ?? DBNull.Value),
                    new OracleParameter("LC",  (object?)s.LOC ?? DBNull.Value),
                    new OracleParameter("GID", (object?)newGid ?? DBNull.Value),
                    new OracleParameter("USR", req.LOGIN_USER));
            }

            // Step D: Copy toàn bộ teachers — 1 GV có thể nhiều rows (1 row/nhóm), map GROUP_ID
            // cũ → mới; group đã bị xoá ở bản gốc → NULL (cả lớp). Dedupe vì nhiều group mất map
            // sẽ cùng về NULL → vi phạm unique (CLASS_ID, EMPCD, GROUP_ID).
            var oldTeachers = await _db.ExecuteQueryAsync(@"
                SELECT EMPCD, IS_PRIMARY, GROUP_ID FROM HRMS.HR_TRAINING_CLASS_TEACHER WHERE CLASS_ID = :ID",
                r => new
                {
                    EMPCD = r["EMPCD"]?.ToString() ?? "",
                    PRI   = Convert.ToInt32(r["IS_PRIMARY"]),
                    GID   = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                }, new OracleParameter("ID", req.SOURCE_CLASS_ID));

            var seenTeacherRows = new HashSet<string>();
            foreach (var t in oldTeachers)
            {
                int? newTGid = t.GID.HasValue && groupIdMap.TryGetValue(t.GID.Value, out var mappedTGid) ? mappedTGid : (int?)null;
                if (!seenTeacherRows.Add($"{t.EMPCD}|{newTGid}")) continue;
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_CLASS_TEACHER (CLASS_ID, EMPCD, IS_PRIMARY, GROUP_ID, INST_ID)
                    VALUES (:CID, :EMP, :PRI, :GID, :USR)",
                    new OracleParameter("CID", newClassId),
                    new OracleParameter("EMP", t.EMPCD),
                    new OracleParameter("PRI", t.PRI),
                    new OracleParameter("GID", (object?)newTGid ?? DBNull.Value),
                    new OracleParameter("USR", req.LOGIN_USER));
            }

            // Step E: Test deep copy — mỗi đợt có test riêng để không đè scoring cũ (§15b).
            // Nếu bản gốc có FINAL_TEST_ID và test đó nằm trong DS được clone → remap sang bản mới.
            var testIdMap = await DeepCopyTestsAsync(@"
                SELECT ID, TITLE, DESCRIPTION, DURATION_MINUTES, PASS_SCORE
                  FROM HRMS.HR_TRAINING_TEST
                 WHERE CLASS_ID = :ID AND IS_TEMPLATE = 0
                 ORDER BY ID",
                new OracleParameter("ID", req.SOURCE_CLASS_ID), newClassId, req.LOGIN_USER);

            if (old.FINAL_TEST.HasValue && testIdMap.TryGetValue(old.FINAL_TEST.Value, out var newFinalTestId))
            {
                await _db.ExecuteNonQueryAsync(
                    "UPDATE HRMS.HR_TRAINING_CLASS SET FINAL_TEST_ID = :FTID WHERE ID = :CID",
                    new OracleParameter("FTID", newFinalTestId),
                    new OracleParameter("CID", newClassId));
            }

            // Step F: Enrollments — hoàn toàn từ input mới (KHÔNG copy DS/pre-assign cũ, §15b).
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

    // Compensation: xoá Class + tất cả child rows nếu clone thất bại giữa chừng.
    // Best-effort — nếu delete cũng fail, ghi log (silent) để tránh double-throw.
    private async Task CompensatingDeleteClassAsync(int classId)
    {
        try
        {
            // Gỡ FK FINAL_TEST_ID trước (nếu trỏ tới 1 trong các test sắp xoá của chính Class này).
            await _db.ExecuteNonQueryAsync("UPDATE HRMS.HR_TRAINING_CLASS SET FINAL_TEST_ID = NULL WHERE ID = :ID",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_TRAINING_TEST_OPTION
                 WHERE QUESTION_ID IN (
                     SELECT Q.ID FROM HRMS.HR_TRAINING_TEST_QUESTION Q
                       JOIN HRMS.HR_TRAINING_TEST T ON T.ID = Q.TEST_ID
                      WHERE T.CLASS_ID = :ID)",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_TRAINING_TEST_QUESTION
                 WHERE TEST_ID IN (SELECT ID FROM HRMS.HR_TRAINING_TEST WHERE CLASS_ID = :ID)",
                new OracleParameter("ID", classId));
            await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_TRAINING_TEST            WHERE CLASS_ID = :ID",
                new OracleParameter("ID", classId));
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

    // Deep copy N test (đề bài + câu hỏi + đáp án) sang Class mới. Test mới luôn CLASS_ID=newClassId,
    // IS_TEMPLATE=0, STATUS reset về DRAFT (AVAILABLE_FROM/TO của bản gốc không còn hợp lệ cho đợt
    // mới — teacher phải Publish lại với window mới). Trả về map oldTestId → newTestId để caller
    // tự cập nhật FINAL_TEST_ID nếu cần.
    // sourceTestsSql: câu SELECT trả về ID, TITLE, DESCRIPTION, DURATION_MINUTES, PASS_SCORE của các
    // test nguồn (dùng chung cho cả clone-from-course (test bank IS_TEMPLATE=1) và
    // clone-from-class (test thực IS_TEMPLATE=0 của Class cũ)).
    private async Task<Dictionary<int, int>> DeepCopyTestsAsync(
        string sourceTestsSql, OracleParameter sourceParam, int newClassId, string actor)
    {
        var testIdMap = new Dictionary<int, int>();

        var sourceTests = await _db.ExecuteQueryAsync(sourceTestsSql,
            r => new
            {
                ID     = Convert.ToInt32(r["ID"]),
                TITLE  = r["TITLE"]?.ToString() ?? "",
                DESC   = r["DESCRIPTION"] as string,
                DUR    = Convert.ToInt32(r["DURATION_MINUTES"]),
                PASS   = r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
            }, sourceParam);

        foreach (var t in sourceTests)
        {
            var testIdParam = new OracleParameter("NEW_TID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_TEST
                    (CLASS_ID, IS_TEMPLATE, TITLE, DESCRIPTION, STATUS,
                     DURATION_MINUTES, PASS_SCORE, MAX_ATTEMPTS, CREATED_BY, INST_ID)
                VALUES (:CID, 0, :TT, :DS, 'DRAFT',
                        :DUR, :PS, 1, :USR, :USR)
                RETURNING ID INTO :NEW_TID",
                new OracleParameter("CID", newClassId),
                new OracleParameter("TT",  t.TITLE),
                new OracleParameter("DS",  (object?)t.DESC ?? DBNull.Value),
                new OracleParameter("DUR", t.DUR),
                new OracleParameter("PS",  (object?)t.PASS ?? DBNull.Value),
                new OracleParameter("USR", actor),
                testIdParam);
            var newTestId = OracleService.ConvertToInt(testIdParam.Value);
            testIdMap[t.ID] = newTestId;

            var questions = await _db.ExecuteQueryAsync(@"
                SELECT ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS
                  FROM HRMS.HR_TRAINING_TEST_QUESTION
                 WHERE TEST_ID = :TID
                 ORDER BY DISPLAY_ORDER, ID",
                r => new
                {
                    ID     = Convert.ToInt32(r["ID"]),
                    TEXT   = r["QUESTION_TEXT"]?.ToString() ?? "",
                    TYPE   = r["QUESTION_TYPE"]?.ToString() ?? "SINGLE",
                    REQ    = Convert.ToInt32(r["IS_REQUIRED"]),
                    ORD    = Convert.ToInt32(r["DISPLAY_ORDER"]),
                    POINTS = Convert.ToDecimal(r["POINTS"]),
                }, new OracleParameter("TID", t.ID));

            foreach (var q in questions)
            {
                var qIdParam = new OracleParameter("NEW_QID", OracleDbType.Int32)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_TEST_QUESTION
                        (TEST_ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS, INST_ID)
                    VALUES (:TID, :TX, :TP, :RQ, :ORD, :PT, :USR)
                    RETURNING ID INTO :NEW_QID",
                    new OracleParameter("TID", newTestId),
                    new OracleParameter("TX",  q.TEXT),
                    new OracleParameter("TP",  q.TYPE),
                    new OracleParameter("RQ",  q.REQ),
                    new OracleParameter("ORD", q.ORD),
                    new OracleParameter("PT",  q.POINTS),
                    new OracleParameter("USR", actor),
                    qIdParam);
                var newQid = OracleService.ConvertToInt(qIdParam.Value);

                var options = await _db.ExecuteQueryAsync(@"
                    SELECT OPTION_TEXT, DISPLAY_ORDER, IS_CORRECT
                      FROM HRMS.HR_TRAINING_TEST_OPTION
                     WHERE QUESTION_ID = :QID
                     ORDER BY DISPLAY_ORDER, ID",
                    r => new
                    {
                        TEXT = r["OPTION_TEXT"]?.ToString() ?? "",
                        ORD  = Convert.ToInt32(r["DISPLAY_ORDER"]),
                        OK   = Convert.ToInt32(r["IS_CORRECT"]),
                    }, new OracleParameter("QID", q.ID));

                foreach (var o in options)
                {
                    await _db.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_TRAINING_TEST_OPTION
                            (QUESTION_ID, OPTION_TEXT, DISPLAY_ORDER, IS_CORRECT)
                        VALUES (:QID, :TX, :ORD, :OK)",
                        new OracleParameter("QID", newQid),
                        new OracleParameter("TX",  o.TEXT),
                        new OracleParameter("ORD", o.ORD),
                        new OracleParameter("OK",  o.OK));
                }
            }
        }

        return testIdMap;
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

        // §16 ràng buộc code layer: FINAL_TEST_ID phải trỏ tới test CLASS_ID = đúng Class này +
        // IS_TEMPLATE=0. Lúc Express Create Class chưa tồn tại nên không thể có test nào thuộc về
        // nó — chặn ngay, HR gán final test sau khi Class + Test đã tồn tại (qua set-final-test).
        if (req.FINAL_TEST_ID.HasValue)
            throw new InvalidOperationException(
                "Không thể gán FINAL_TEST_ID khi tạo Express Class — hãy tạo Class trước, tạo Test cho Class đó, rồi gán Final Test sau.");

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
    // DISTINCT vì 1 GV có thể nhiều rows/lớp (1 row/nhóm phụ trách) — chỉ cần 1 dòng/lớp.
    public async Task<List<ClassTeacherModel>> GetTeachersForEmpAsync(string empcd)
    {
        return await _db.ExecuteQueryAsync(@"
            SELECT DISTINCT CLASS_ID, EMPCD, IS_PRIMARY FROM HRMS.HR_TRAINING_CLASS_TEACHER
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
            try
            {
                await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_TRAINING_CLASS_GROUP
                       SET GROUP_NAME = :NAME, MAX_STUDENTS = :MAX, UPDT_ID = :USR
                     WHERE ID = :ID",
                    new OracleParameter("NAME", req.GROUP_NAME),
                    new OracleParameter("MAX",  (object?)req.MAX_STUDENTS ?? DBNull.Value),
                    new OracleParameter("USR",  req.LOGIN_USER),
                    new OracleParameter("ID",   req.ID.Value));
            }
            catch (OracleException ex) when (ex.Number == 1)   // ORA-00001 unique
            {
                throw new InvalidOperationException($"Nhóm '{req.GROUP_NAME}' đã tồn tại trong lớp này");
            }
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

    // Chia đều DS ENROLLED (SOURCE-agnostic) chưa gán group vào N group input.
    // CÂN BẰNG theo số người ĐANG có sẵn ở mỗi group (không round-robin mù theo index) — bug cũ:
    // nếu 1 group đã có sẵn người gán tay (VD 10/28), round-robin mù theo i%N sẽ CHIA THÊM ĐỀU
    // cho group đó y như group rỗng, làm lệch nặng hơn thay vì cân bằng lại. Giờ luôn ưu tiên gán
    // vào group đang có ÍT người nhất tại thời điểm gán, nên kết quả cuối luôn cân bằng nhất có thể
    // bất kể điểm xuất phát lệch cỡ nào.
    public async Task<int> AutoSplitGroupAsync(AutoSplitGroupRequest req)
    {
        // Dedupe — UI gửi trùng ID (double-click...) sẽ làm ToDictionary bên dưới crash ArgumentException.
        var groupIds = req.GROUP_IDS?.Distinct().ToList() ?? new();
        if (groupIds.Count == 0)
            throw new InvalidOperationException("Cần ≥ 1 group để chia");

        // Toàn bộ đọc count + loop UPDATE chạy chung 1 transaction — all-or-nothing,
        // và count không bị lệch bởi thao tác gán tay song song giữa chừng.
        await using var scope = await _db.BeginTransactionAsync();
        try
        {
            // Verify tất cả groups thuộc Class + lấy cap riêng từng group (§5b.1)
            var groupInfos = await _db.ExecuteQueryAsync(scope,
                "SELECT ID, MAX_STUDENTS FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE CLASS_ID = :CID",
                r => new
                {
                    ID  = Convert.ToInt32(r["ID"]),
                    MAX = r["MAX_STUDENTS"] is DBNull ? (int?)null : Convert.ToInt32(r["MAX_STUDENTS"]),
                },
                new OracleParameter("CID", req.CLASS_ID));
            var capMap = groupInfos.ToDictionary(g => g.ID, g => g.MAX);
            foreach (var gid in groupIds)
                if (!capMap.ContainsKey(gid))
                    throw new InvalidOperationException($"Group ID {gid} không thuộc Class {req.CLASS_ID}");

            // Đếm số người ENROLLED hiện có của từng group được chọn (điểm xuất phát để cân bằng).
            var idList = string.Join(",", groupIds);
            var currentCounts = await _db.ExecuteQueryAsync(scope, $@"
                SELECT GROUP_ID, COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
                 WHERE CLASS_ID = :CID AND STATUS = 'ENROLLED' AND GROUP_ID IN ({idList})
                 GROUP BY GROUP_ID",
                r => new { GID = Convert.ToInt32(r["GROUP_ID"]), CNT = Convert.ToInt32(r["CNT"]) },
                new OracleParameter("CID", req.CLASS_ID));

            var countMap = groupIds.ToDictionary(g => g, g => 0);
            foreach (var c in currentCounts) countMap[c.GID] = c.CNT;

            // List học viên chưa gán group
            var empcds = await _db.ExecuteQueryAsync(scope, @"
                SELECT EMPCD FROM HRMS.HR_TRAINING_ENROLLMENT
                 WHERE CLASS_ID = :CID
                   AND STATUS = 'ENROLLED'
                   AND GROUP_ID IS NULL
                 ORDER BY EMPCD",
                r => r["EMPCD"]?.ToString() ?? "",
                new OracleParameter("CID", req.CLASS_ID));

            // Check tổng chỗ trống trước khi chia (group MAX null = không giới hạn) —
            // thiếu chỗ thì chặn luôn, không chia nửa chừng (all-or-nothing).
            if (groupIds.All(g => capMap[g].HasValue))
            {
                var free = groupIds.Sum(g => Math.Max(0, capMap[g]!.Value - countMap[g]));
                if (free < empcds.Count)
                    throw new InvalidOperationException(
                        $"Không đủ chỗ: còn {empcds.Count} học viên chưa gán nhưng các nhóm chỉ còn {free} chỗ trống. Tăng giới hạn nhóm hoặc tạo thêm nhóm.");
            }

            int updated = 0;
            foreach (var empcd in empcds)
            {
                // Chỉ xét group còn chỗ (theo MAX_STUDENTS riêng); chọn group đang ít người nhất,
                // hòa thì ưu tiên theo thứ tự groupIds truyền vào (OrderBy stable).
                var targetGid = groupIds
                    .Where(g => !capMap[g].HasValue || countMap[g] < capMap[g]!.Value)
                    .OrderBy(g => countMap[g])
                    .First();

                // Guard GROUP_ID IS NULL + STATUS: nếu 1 HR khác vừa gán tay/đổi status học viên này
                // song song thì bỏ qua (0 row), không ghi đè lựa chọn tay của họ.
                var affected = await _db.ExecuteNonQueryAsync(scope, @"
                    UPDATE HRMS.HR_TRAINING_ENROLLMENT
                       SET GROUP_ID = :GID, UPDT_ID = :USR
                     WHERE CLASS_ID = :CID AND EMPCD = :EMP
                       AND STATUS = 'ENROLLED' AND GROUP_ID IS NULL",
                    new OracleParameter("GID", targetGid),
                    new OracleParameter("USR", req.LOGIN_USER),
                    new OracleParameter("CID", req.CLASS_ID),
                    new OracleParameter("EMP", empcd));

                if (affected > 0)
                {
                    countMap[targetGid]++;
                    updated++;
                }
            }

            await scope.CommitAsync();
            return updated;
        }
        catch
        {
            await scope.RollbackAsync();
            throw;
        }
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

    // §16 ràng buộc code layer (không check được bằng DB CHECK constraint — Oracle không cho subquery
    // trong CHECK): HR_TRAINING_CLASS.FINAL_TEST_ID chỉ được trỏ tới test CLASS_ID = đúng Class này
    // và IS_TEMPLATE = 0. Không validate gì nếu testId NULL (final test optional).
    // Public — dùng lại ở TrainingTeachController.SetFinalTest.
    public async Task ValidateFinalTestAsync(int? testId, int classId)
    {
        if (!testId.HasValue) return;

        var ok = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_TEST WHERE ID = :TID AND CLASS_ID = :CID AND IS_TEMPLATE = 0",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("TID", testId.Value),
            new OracleParameter("CID", classId))).First();

        if (ok == 0)
            throw new InvalidOperationException(
                "FINAL_TEST_ID không hợp lệ — test phải thuộc đúng Class này và không phải test mẫu (template).");
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
        BULLETIN_ID            = r["BULLETIN_ID"] is DBNull ? null : Convert.ToInt32(r["BULLETIN_ID"]),
        INST_ID                = r["INST_ID"] as string,
        INST_DT                = r["INST_DT"] as DateTime?,
        UPDT_ID                = r["UPDT_ID"] as string,
        UPDT_DT                = r["UPDT_DT"] as DateTime?,
        COURSE_TITLE           = r["COURSE_TITLE"] as string,
        COURSE_MODE            = r["COURSE_MODE"] as string,
    };
}
