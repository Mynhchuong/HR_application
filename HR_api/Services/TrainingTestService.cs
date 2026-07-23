using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Test CRUD + Questions/Options bulk replace + publish state machine.
// Attempt / grading tách sang TrainingAttemptService.
// Rules ref: training_rules.md §6, training_plan.md §16 (ràng buộc code layer).
public class TrainingTestService
{
    private readonly OracleService _db;
    private readonly TrainingNotificationService? _noti;

    public TrainingTestService(OracleService db, TrainingNotificationService? noti = null)
    {
        _db = db;
        _noti = noti;
    }

    // ═══════════════════════════════════════════════════════════════
    //  LIST + DETAIL
    // ═══════════════════════════════════════════════════════════════

    // isTemplate NULL = all, 0 = class test only, 1 = template only
    public async Task<List<TestModel>> ListAsync(int? classId, int? templateCourseId, int? isTemplate, string? status, string? empcd = null)
    {
        const string sql = @"
            SELECT T.ID, T.CLASS_ID, T.IS_TEMPLATE, T.TEMPLATE_COURSE_ID,
                   T.TITLE, T.DESCRIPTION, T.STATUS,
                   T.DURATION_MINUTES, T.AVAILABLE_FROM, T.AVAILABLE_TO,
                   T.PASS_SCORE, T.MAX_ATTEMPTS, T.CREATED_BY,
                   T.INST_DT, T.UPDT_DT,
                   CL.CLASS_NAME, CO.TITLE COURSE_TITLE,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_TEST_QUESTION Q WHERE Q.TEST_ID = T.ID) QUESTION_COUNT,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_TEST_ATTEMPT A WHERE A.TEST_ID = T.ID) ATTEMPT_COUNT,
                   ATT.STATUS AS ATTEMPT_STATUS, ATT.SCORE AS SCORE, ATT.MAX_SCORE AS MAX_SCORE, ATT.IS_GRADED AS IS_GRADED,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_RETAKE_GRANT G
                     WHERE G.TEST_ID = T.ID AND G.EMPCD = :P_EMP AND G.STATUS = 'PENDING') AS PENDING_GRANT_CNT
              FROM HRMS.HR_TRAINING_TEST T
              LEFT JOIN HRMS.HR_TRAINING_CLASS  CL ON CL.ID = T.CLASS_ID
              LEFT JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = T.TEMPLATE_COURSE_ID
              -- Chỉ join LƯỢT GẦN NHẤT của học viên — nếu không, học viên có ≥2 attempt (được
              -- cấp thi lại rồi thi tiếp) sẽ bị nhân dòng, hiện trùng lặp bài test trong danh sách.
              -- Lọc lượt gần nhất trong inline view TRƯỚC khi join (ORA-01799: Oracle không cho
              -- outer join khi ON clause chứa subquery so sánh trực tiếp).
              LEFT JOIN (
                  SELECT A.TEST_ID, A.EMPCD, A.STATUS, A.SCORE, A.MAX_SCORE, A.IS_GRADED
                    FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
                   WHERE A.EMPCD = :P_EMP
                     AND A.ATTEMPT_NO = (SELECT MAX(A2.ATTEMPT_NO) FROM HRMS.HR_TRAINING_TEST_ATTEMPT A2
                                           WHERE A2.TEST_ID = A.TEST_ID AND A2.EMPCD = A.EMPCD)
              ) ATT ON ATT.TEST_ID = T.ID
             WHERE (:P_CID  IS NULL OR T.CLASS_ID          = :P_CID)
               AND (:P_TCID IS NULL OR T.TEMPLATE_COURSE_ID = :P_TCID)
               AND (:P_TPL  IS NULL OR T.IS_TEMPLATE       = :P_TPL)
               AND (:P_ST   IS NULL OR T.STATUS            = :P_ST)
             ORDER BY T.ID DESC";
        return await _db.ExecuteQueryAsync(sql, MapTest,
            new OracleParameter("P_CID",  (object?)classId          ?? DBNull.Value),
            new OracleParameter("P_TCID", (object?)templateCourseId ?? DBNull.Value),
            new OracleParameter("P_TPL",  (object?)isTemplate       ?? DBNull.Value),
            new OracleParameter("P_ST",   (object?)status           ?? DBNull.Value),
            new OracleParameter("P_EMP",  (object?)empcd            ?? DBNull.Value));
    }

    public async Task<TestModel?> GetDetailAsync(int id)
    {
        const string sql = @"
            SELECT T.ID, T.CLASS_ID, T.IS_TEMPLATE, T.TEMPLATE_COURSE_ID,
                   T.TITLE, T.DESCRIPTION, T.STATUS,
                   T.DURATION_MINUTES, T.AVAILABLE_FROM, T.AVAILABLE_TO,
                   T.PASS_SCORE, T.MAX_ATTEMPTS, T.CREATED_BY,
                   T.INST_DT, T.UPDT_DT,
                   CL.CLASS_NAME, CO.TITLE COURSE_TITLE
              FROM HRMS.HR_TRAINING_TEST T
              LEFT JOIN HRMS.HR_TRAINING_CLASS  CL ON CL.ID = T.CLASS_ID
              LEFT JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = T.TEMPLATE_COURSE_ID
             WHERE T.ID = :ID";
        var list = await _db.ExecuteQueryAsync(sql, MapTestFull, new OracleParameter("ID", id));
        return list.FirstOrDefault();
    }

    // Trả questions + options. Nếu forStudent=true → strip IS_CORRECT (§6.5).
    public async Task<List<TestQuestionModel>> GetQuestionsAsync(int testId, bool forStudent = false)
    {
        const string sqlQ = @"
            SELECT ID, TEST_ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS
              FROM HRMS.HR_TRAINING_TEST_QUESTION
             WHERE TEST_ID = :TID
             ORDER BY DISPLAY_ORDER, ID";
        var qs = await _db.ExecuteQueryAsync(sqlQ, r => new TestQuestionModel
        {
            ID            = Convert.ToInt32(r["ID"]),
            TEST_ID       = Convert.ToInt32(r["TEST_ID"]),
            QUESTION_TEXT = r["QUESTION_TEXT"]?.ToString() ?? "",
            QUESTION_TYPE = r["QUESTION_TYPE"]?.ToString() ?? "SINGLE",
            IS_REQUIRED   = Convert.ToInt32(r["IS_REQUIRED"]),
            DISPLAY_ORDER = Convert.ToInt32(r["DISPLAY_ORDER"]),
            POINTS        = Convert.ToDecimal(r["POINTS"]),
        }, new OracleParameter("TID", testId));

        if (qs.Count == 0) return qs;

        const string sqlO = @"
            SELECT O.ID, O.QUESTION_ID, O.OPTION_TEXT, O.DISPLAY_ORDER, O.IS_CORRECT
              FROM HRMS.HR_TRAINING_TEST_OPTION O
              JOIN HRMS.HR_TRAINING_TEST_QUESTION Q ON Q.ID = O.QUESTION_ID
             WHERE Q.TEST_ID = :TID
             ORDER BY O.QUESTION_ID, O.DISPLAY_ORDER, O.ID";
        var opts = await _db.ExecuteQueryAsync(sqlO, r => new TestOptionModel
        {
            ID            = Convert.ToInt32(r["ID"]),
            QUESTION_ID   = Convert.ToInt32(r["QUESTION_ID"]),
            OPTION_TEXT   = r["OPTION_TEXT"]?.ToString() ?? "",
            DISPLAY_ORDER = Convert.ToInt32(r["DISPLAY_ORDER"]),
            IS_CORRECT    = forStudent ? 0 : Convert.ToInt32(r["IS_CORRECT"]),   // strip cho student
        }, new OracleParameter("TID", testId));

        var byQ = opts.GroupBy(o => o.QUESTION_ID).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var q in qs)
            if (q.ID.HasValue && byQ.TryGetValue(q.ID.Value, out var list)) q.OPTIONS = list;
        return qs;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAVE (create + update test meta)
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> SaveAsync(SaveTestRequest req)
    {
        // Validate
        if (string.IsNullOrWhiteSpace(req.TITLE))
            throw new InvalidOperationException("TITLE required");
        if (req.DURATION_MINUTES <= 0)
            throw new InvalidOperationException("DURATION_MINUTES phải > 0");
        // Template ↔ Class exclusivity (§ DDL CHK_TRT_TMPL_MODE)
        if (req.IS_TEMPLATE == 1)
        {
            if (req.TEMPLATE_COURSE_ID == null)
                throw new InvalidOperationException("Template test cần TEMPLATE_COURSE_ID");
            if (req.CLASS_ID != null)
                throw new InvalidOperationException("Template test không được có CLASS_ID");
        }
        else
        {
            if (req.CLASS_ID == null)
                throw new InvalidOperationException("Test class cần CLASS_ID");
            if (req.TEMPLATE_COURSE_ID != null)
                throw new InvalidOperationException("Test class không được có TEMPLATE_COURSE_ID");
        }
        if (req.AVAILABLE_FROM.HasValue && req.AVAILABLE_TO.HasValue && req.AVAILABLE_TO < req.AVAILABLE_FROM)
            throw new InvalidOperationException("AVAILABLE_TO phải sau AVAILABLE_FROM");

        var maxAttempts = (req.MAX_ATTEMPTS is int ma && ma > 0) ? ma : 1;

        if (req.ID == null)
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_TEST
                    (CLASS_ID, IS_TEMPLATE, TEMPLATE_COURSE_ID,
                     TITLE, DESCRIPTION, STATUS,
                     DURATION_MINUTES, AVAILABLE_FROM, AVAILABLE_TO,
                     PASS_SCORE, MAX_ATTEMPTS, CREATED_BY, INST_ID)
                VALUES
                    (:CID, :TPL, :TCID,
                     :TT, :DS, 'DRAFT',
                     :DUR, :AF, :AT,
                     :PS, :MA, :USR, :USR)
                RETURNING ID INTO :NEW_ID";
            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("CID",  (object?)req.CLASS_ID           ?? DBNull.Value),
                new OracleParameter("TPL",  req.IS_TEMPLATE),
                new OracleParameter("TCID", (object?)req.TEMPLATE_COURSE_ID ?? DBNull.Value),
                new OracleParameter("TT",   req.TITLE),
                new OracleParameter("DS",   (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("DUR",  req.DURATION_MINUTES),
                new OracleParameter("AF",   (object?)req.AVAILABLE_FROM ?? DBNull.Value),
                new OracleParameter("AT",   (object?)req.AVAILABLE_TO   ?? DBNull.Value),
                new OracleParameter("PS",   (object?)req.PASS_SCORE     ?? DBNull.Value),
                new OracleParameter("MA",   maxAttempts),
                new OracleParameter("USR",  req.LOGIN_USER),
                idParam);
            return OracleService.ConvertToInt(idParam.Value);
        }
        else
        {
            // Chỉ update khi DRAFT (§6.1). PUBLISHED trở lên phải cancel/tạo lại.
            var status = await GetStatusAsync(req.ID.Value)
                ?? throw new InvalidOperationException("Không tìm thấy test");
            if (status != "DRAFT")
                throw new InvalidOperationException($"Test đang {status}, chỉ sửa được ở DRAFT");

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_TEST
                   SET TITLE            = :TT,
                       DESCRIPTION      = :DS,
                       DURATION_MINUTES = :DUR,
                       AVAILABLE_FROM   = :AF,
                       AVAILABLE_TO     = :AT,
                       PASS_SCORE       = :PS,
                       MAX_ATTEMPTS     = :MA,
                       UPDT_ID          = :USR
                 WHERE ID = :ID",
                new OracleParameter("TT",  req.TITLE),
                new OracleParameter("DS",  (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("DUR", req.DURATION_MINUTES),
                new OracleParameter("AF",  (object?)req.AVAILABLE_FROM ?? DBNull.Value),
                new OracleParameter("AT",  (object?)req.AVAILABLE_TO   ?? DBNull.Value),
                new OracleParameter("PS",  (object?)req.PASS_SCORE     ?? DBNull.Value),
                new OracleParameter("MA",  maxAttempts),
                new OracleParameter("USR", req.LOGIN_USER),
                new OracleParameter("ID",  req.ID.Value));
            return req.ID.Value;
        }
    }

    // Bulk replace Q + O — dùng trong DRAFT. Bảo toàn thứ tự (DISPLAY_ORDER).
    // Validate loại question §6.1: chỉ SINGLE / MULTI / YESNO / DROPDOWN / TEXT.
    public async Task SaveQuestionsAsync(SaveTestQuestionsRequest req)
    {
        var status = await GetStatusAsync(req.TEST_ID)
            ?? throw new InvalidOperationException("Không tìm thấy test");
        if (status != "DRAFT")
            throw new InvalidOperationException($"Test đang {status}, chỉ sửa questions khi DRAFT");

        var validTypes = new[] { "SINGLE", "MULTI", "YESNO", "DROPDOWN", "TEXT" };
        foreach (var q in req.QUESTIONS ?? new())
        {
            if (string.IsNullOrWhiteSpace(q.QUESTION_TEXT))
                throw new InvalidOperationException("Câu hỏi không được để trống");
            if (!validTypes.Contains(q.QUESTION_TYPE))
                throw new InvalidOperationException($"QUESTION_TYPE '{q.QUESTION_TYPE}' invalid (chỉ SINGLE|MULTI|YESNO|DROPDOWN|TEXT)");
            if (q.QUESTION_TYPE != "TEXT")
            {
                if (q.OPTIONS == null || q.OPTIONS.Count < 2)
                    throw new InvalidOperationException("Question SINGLE/MULTI/YESNO/DROPDOWN cần ≥ 2 option");
                if (q.QUESTION_TYPE == "SINGLE" || q.QUESTION_TYPE == "YESNO" || q.QUESTION_TYPE == "DROPDOWN")
                {
                    if (q.OPTIONS.Count(o => o.IS_CORRECT == 1) != 1)
                        throw new InvalidOperationException($"Câu '{q.QUESTION_TEXT}' (type {q.QUESTION_TYPE}) phải có đúng 1 đáp án đúng");
                }
                if (q.QUESTION_TYPE == "MULTI")
                {
                    if (q.OPTIONS.Count(o => o.IS_CORRECT == 1) < 1)
                        throw new InvalidOperationException($"Câu MULTI '{q.QUESTION_TEXT}' cần ≥ 1 đáp án đúng");
                }
            }
        }

        // Delete cascade options via question — chạy chain vì FK bắt buộc
        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST_OPTION
             WHERE QUESTION_ID IN (SELECT ID FROM HRMS.HR_TRAINING_TEST_QUESTION WHERE TEST_ID = :TID)",
            new OracleParameter("TID", req.TEST_ID));
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_TEST_QUESTION WHERE TEST_ID = :TID",
            new OracleParameter("TID", req.TEST_ID));

        // Insert lại. Cần map QUESTION_ID cũ → mới cho OPTIONS (RETURNING ID).
        int order = 0;
        foreach (var q in req.QUESTIONS ?? new())
        {
            var qidParam = new OracleParameter("NEW_QID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_TEST_QUESTION
                    (TEST_ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS, INST_ID)
                VALUES (:TID, :TX, :TP, :RQ, :ORD, :PT, :USR)
                RETURNING ID INTO :NEW_QID",
                new OracleParameter("TID", req.TEST_ID),
                new OracleParameter("TX",  q.QUESTION_TEXT),
                new OracleParameter("TP",  q.QUESTION_TYPE),
                new OracleParameter("RQ",  q.IS_REQUIRED),
                new OracleParameter("ORD", order++),
                new OracleParameter("PT",  q.POINTS),
                new OracleParameter("USR", req.LOGIN_USER),
                qidParam);
            var newQid = OracleService.ConvertToInt(qidParam.Value);

            int oord = 0;
            foreach (var o in q.OPTIONS ?? new())
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_TEST_OPTION
                        (QUESTION_ID, OPTION_TEXT, DISPLAY_ORDER, IS_CORRECT)
                    VALUES (:QID, :TX, :ORD, :OK)",
                    new OracleParameter("QID", newQid),
                    new OracleParameter("TX",  o.OPTION_TEXT),
                    new OracleParameter("ORD", oord++),
                    new OracleParameter("OK",  o.IS_CORRECT));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  STATE MACHINE (DRAFT → PUBLISHED → OPEN → CLOSED → GRADING → COMPLETED)
    // ═══════════════════════════════════════════════════════════════

    // DRAFT → PUBLISHED: validate có ≥ 1 câu, update AVAILABLE_FROM/TO, gửi thông báo.
    public async Task PublishAsync(int testId, string actor, DateTime? availableFrom, DateTime? availableTo)
    {
        var test = await GetDetailAsync(testId)
            ?? throw new InvalidOperationException("Không tìm thấy test");
        if (test.STATUS != "DRAFT")
            throw new InvalidOperationException($"Test đang {test.STATUS}, chỉ publish từ DRAFT");
        if (test.IS_TEMPLATE == 1)
            throw new InvalidOperationException("Template test không dùng để làm bài, không cần publish");
        if (availableFrom == null || availableTo == null)
            throw new InvalidOperationException("Vui lòng chọn thời gian mở và đóng bài");
        if (availableTo <= availableFrom)
            throw new InvalidOperationException("Thời gian đóng bài phải sau thời gian mở bài");
        if (availableTo < DateTime.Now)
            throw new InvalidOperationException("Thời gian đóng bài không được ở trong quá khứ");

        var qCount = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_TEST_QUESTION WHERE TEST_ID = :ID",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("ID", testId))).First();
        if (qCount == 0)
            throw new InvalidOperationException("Không thể xuất bản bài kiểm tra chưa có câu hỏi");

        // Cập nhật AVAILABLE_FROM / AVAILABLE_TO
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_TEST
               SET AVAILABLE_FROM = :AF, AVAILABLE_TO = :AT, UPDT_DT = SYSDATE
             WHERE ID = :TID",
            new OracleParameter("AF",  availableFrom),
            new OracleParameter("AT",  availableTo),
            new OracleParameter("TID", testId));

        await SetStatusAsync(testId, "PUBLISHED", actor);

        // Enqueue TRAINING_TEST_OPEN cho students của Class
        test.AVAILABLE_FROM = availableFrom;
        test.AVAILABLE_TO   = availableTo;
        if (_noti != null && test.CLASS_ID.HasValue)
        {
            await _noti.EnqueueForClassEnrollmentsAsync(test.CLASS_ID.Value, "TRAINING_TEST_OPEN",
                new Dictionary<string, string>
                {
                    ["testTitle"]   = test.TITLE,
                    ["availableTo"] = availableTo?.ToString("dd/MM/yyyy HH:mm") ?? "",
                    ["duration"]    = test.DURATION_MINUTES.ToString(),
                }, testId: testId);
        }
    }

    // Sửa lại thời gian mở/đóng bài SAU KHI đã publish (vd chọn nhầm ngày, hoặc cần gia hạn cho học
    // viên nộp trễ) — không đổi STATUS, chỉ update AVAILABLE_FROM/TO. Trước đây không có đường nào
    // sửa lại vì PublishAsync bắt buộc STATUS phải đang DRAFT.
    public async Task UpdateScheduleAsync(int testId, string actor, DateTime? availableFrom, DateTime? availableTo)
    {
        var status = await GetStatusAsync(testId)
            ?? throw new InvalidOperationException("Không tìm thấy test");
        if (status != "PUBLISHED" && status != "OPEN")
            throw new InvalidOperationException($"Test đang {status}, chỉ sửa được lịch khi đang PUBLISHED/OPEN");
        if (availableFrom == null || availableTo == null)
            throw new InvalidOperationException("Vui lòng chọn thời gian mở và đóng bài");
        if (availableTo <= availableFrom)
            throw new InvalidOperationException("Thời gian đóng bài phải sau thời gian mở bài");

        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_TEST
               SET AVAILABLE_FROM = :AF, AVAILABLE_TO = :AT, UPDT_ID = :USR, UPDT_DT = SYSDATE
             WHERE ID = :TID",
            new OracleParameter("AF",  availableFrom),
            new OracleParameter("AT",  availableTo),
            new OracleParameter("USR", actor),
            new OracleParameter("TID", testId));
    }

    // PUBLISHED / OPEN → CLOSED
    public async Task CloseAsync(int testId, string actor)
    {
        var status = await GetStatusAsync(testId)
            ?? throw new InvalidOperationException("Không tìm thấy test");
        if (status != "PUBLISHED" && status != "OPEN")
            throw new InvalidOperationException($"Test đang {status}, chỉ close từ PUBLISHED/OPEN");
        await SetStatusAsync(testId, "CLOSED", actor);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<string?> GetStatusAsync(int id)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => r["STATUS"]?.ToString(),
            new OracleParameter("ID", id))).FirstOrDefault();
    }

    private async Task SetStatusAsync(int id, string status, string actor)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_TEST
               SET STATUS = :S, UPDT_ID = :USR
             WHERE ID = :ID",
            new OracleParameter("S",   status),
            new OracleParameter("USR", actor),
            new OracleParameter("ID",  id));
    }

    private static TestModel MapTest(OracleDataReader r)
    {
        var t = MapTestFull(r);
        t.QUESTION_COUNT = r["QUESTION_COUNT"] is DBNull ? 0 : Convert.ToInt32(r["QUESTION_COUNT"]);
        t.ATTEMPT_COUNT  = r["ATTEMPT_COUNT"]  is DBNull ? 0 : Convert.ToInt32(r["ATTEMPT_COUNT"]);
        
        t.ATTEMPT_STATUS = r["ATTEMPT_STATUS"] as string;
        t.SCORE          = r["SCORE"]     is DBNull ? null : Convert.ToDecimal(r["SCORE"]);
        t.MAX_SCORE      = r["MAX_SCORE"] is DBNull ? null : Convert.ToDecimal(r["MAX_SCORE"]);
        t.IS_GRADED      = r["IS_GRADED"] is DBNull ? null : Convert.ToInt32(r["IS_GRADED"]);
        t.HAS_PENDING_GRANT = !(r["PENDING_GRANT_CNT"] is DBNull) && Convert.ToInt32(r["PENDING_GRANT_CNT"]) > 0;
        return t;
    }

    private static TestModel MapTestFull(OracleDataReader r) => new()
    {
        ID                 = Convert.ToInt32(r["ID"]),
        CLASS_ID           = r["CLASS_ID"] is DBNull ? null : Convert.ToInt32(r["CLASS_ID"]),
        IS_TEMPLATE        = Convert.ToInt32(r["IS_TEMPLATE"]),
        TEMPLATE_COURSE_ID = r["TEMPLATE_COURSE_ID"] is DBNull ? null : Convert.ToInt32(r["TEMPLATE_COURSE_ID"]),
        TITLE              = r["TITLE"]?.ToString() ?? "",
        DESCRIPTION        = r["DESCRIPTION"] as string,
        STATUS             = r["STATUS"]?.ToString() ?? "DRAFT",
        DURATION_MINUTES   = Convert.ToInt32(r["DURATION_MINUTES"]),
        AVAILABLE_FROM     = r["AVAILABLE_FROM"] as DateTime?,
        AVAILABLE_TO       = r["AVAILABLE_TO"]   as DateTime?,
        PASS_SCORE         = r["PASS_SCORE"] is DBNull ? null : Convert.ToDecimal(r["PASS_SCORE"]),
        MAX_ATTEMPTS       = Convert.ToInt32(r["MAX_ATTEMPTS"]),
        CREATED_BY         = r["CREATED_BY"]?.ToString() ?? "",
        INST_DT            = r["INST_DT"] as DateTime?,
        UPDT_DT            = r["UPDT_DT"] as DateTime?,
        CLASS_NAME         = r["CLASS_NAME"] as string,
        COURSE_TITLE       = r["COURSE_TITLE"] as string,
    };

    // Xóa test DRAFT (cascade options + questions)
    public async Task DeleteAsync(int id)
    {
        var status = await GetStatusAsync(id)
            ?? throw new InvalidOperationException("Không tìm thấy bài kiểm tra");
        if (status != "DRAFT")
            throw new InvalidOperationException("Chỉ xóa được bài kiểm tra ở trạng thái Nháp");

        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_TRAINING_TEST_OPTION
             WHERE QUESTION_ID IN (SELECT ID FROM HRMS.HR_TRAINING_TEST_QUESTION WHERE TEST_ID = :TID)",
            new OracleParameter("TID", id));
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_TEST_QUESTION WHERE TEST_ID = :TID",
            new OracleParameter("TID", id));
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_TEST WHERE ID = :TID",
            new OracleParameter("TID", id));
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAO CHÉP ĐỀ THI GIỮA CÁC LỚP CÙNG KHÓA (COPY / CLONE TEST)
    // ═══════════════════════════════════════════════════════════════

    // Lấy danh sách tất cả các bài test thuộc Khóa học (từ tất cả các lớp khác + template)
    public async Task<List<TestModel>> GetCourseTestsAsync(int classId)
    {
        var courseId = (await _db.ExecuteQueryAsync(
            "SELECT COURSE_ID FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => r["COURSE_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["COURSE_ID"]),
            new OracleParameter("ID", classId))).FirstOrDefault();

        if (courseId == null) return new();

        const string sql = @"
            SELECT T.ID, T.CLASS_ID, T.IS_TEMPLATE, T.TEMPLATE_COURSE_ID,
                   T.TITLE, T.DESCRIPTION, T.STATUS,
                   T.DURATION_MINUTES, T.PASS_SCORE, T.MAX_ATTEMPTS,
                   CL.CLASS_NAME, CO.TITLE COURSE_TITLE,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_TEST_QUESTION Q WHERE Q.TEST_ID = T.ID) QUESTION_COUNT
              FROM HRMS.HR_TRAINING_TEST T
              LEFT JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = T.CLASS_ID
              LEFT JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID OR CO.ID = T.TEMPLATE_COURSE_ID
             WHERE (CL.COURSE_ID = :CID OR T.TEMPLATE_COURSE_ID = :CID)
             ORDER BY T.ID DESC";
        return await _db.ExecuteQueryAsync(sql, r => new TestModel
        {
            ID                 = Convert.ToInt32(r["ID"]),
            CLASS_ID           = r["CLASS_ID"] is DBNull ? null : Convert.ToInt32(r["CLASS_ID"]),
            IS_TEMPLATE        = Convert.ToInt32(r["IS_TEMPLATE"]),
            TEMPLATE_COURSE_ID = r["TEMPLATE_COURSE_ID"] is DBNull ? null : Convert.ToInt32(r["TEMPLATE_COURSE_ID"]),
            TITLE              = r["TITLE"]?.ToString() ?? "",
            DESCRIPTION        = r["DESCRIPTION"] as string,
            STATUS             = r["STATUS"]?.ToString() ?? "DRAFT",
            DURATION_MINUTES   = Convert.ToInt32(r["DURATION_MINUTES"]),
            PASS_SCORE         = r["PASS_SCORE"] is DBNull ? null : Convert.ToDecimal(r["PASS_SCORE"]),
            CLASS_NAME         = r["CLASS_NAME"] as string,
            COURSE_TITLE       = r["COURSE_TITLE"] as string,
            QUESTION_COUNT     = r["QUESTION_COUNT"] is DBNull ? 0 : Convert.ToInt32(r["QUESTION_COUNT"]),
        }, new OracleParameter("CID", courseId.Value));
    }

    // Copy đề thi từ sourceTestId sang targetClassIds (hoặc tất cả các lớp cùng khóa)
    public async Task<List<int>> CopyTestAsync(int sourceTestId, List<int> targetClassIds, string actor)
    {
        var sourceTest = await GetDetailAsync(sourceTestId)
            ?? throw new InvalidOperationException("Không tìm thấy bài kiểm tra nguồn");
        var questions = await GetQuestionsAsync(sourceTestId, forStudent: false);
        if (questions.Count == 0)
            throw new InvalidOperationException("Đề thi nguồn chưa có câu hỏi nào để sao chép");

        var createdIds = new List<int>();
        foreach (var targetClassId in targetClassIds)
        {
            // Tránh copy đè lại chính lớp nguồn nếu truyền nhầm
            if (sourceTest.CLASS_ID.HasValue && sourceTest.CLASS_ID.Value == targetClassId)
                continue;

            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_TEST
                    (CLASS_ID, IS_TEMPLATE, TEMPLATE_COURSE_ID,
                     TITLE, DESCRIPTION, STATUS,
                     DURATION_MINUTES, AVAILABLE_FROM, AVAILABLE_TO,
                     PASS_SCORE, MAX_ATTEMPTS, CREATED_BY, INST_ID)
                VALUES
                    (:CID, 0, NULL,
                     :TT, :DS, 'DRAFT',
                     :DUR, :AF, :AT,
                     :PS, :MA, :USR, :USR)
                RETURNING ID INTO :NEW_ID";
            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("CID",  targetClassId),
                new OracleParameter("TT",   sourceTest.TITLE),
                new OracleParameter("DS",   (object?)sourceTest.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("DUR",  sourceTest.DURATION_MINUTES),
                new OracleParameter("AF",   (object?)sourceTest.AVAILABLE_FROM ?? DBNull.Value),
                new OracleParameter("AT",   (object?)sourceTest.AVAILABLE_TO   ?? DBNull.Value),
                new OracleParameter("PS",   (object?)sourceTest.PASS_SCORE     ?? DBNull.Value),
                new OracleParameter("MA",   sourceTest.MAX_ATTEMPTS),
                new OracleParameter("USR",  actor),
                idParam);
            var newTestId = OracleService.ConvertToInt(idParam.Value);
            createdIds.Add(newTestId);

            // Copy từng câu hỏi + đáp án
            int qOrder = 0;
            foreach (var q in questions)
            {
                var qidParam = new OracleParameter("NEW_QID", OracleDbType.Int32)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_TRAINING_TEST_QUESTION
                        (TEST_ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS, INST_ID)
                    VALUES (:TID, :TX, :TP, :RQ, :ORD, :PT, :USR)
                    RETURNING ID INTO :NEW_QID",
                    new OracleParameter("TID", newTestId),
                    new OracleParameter("TX",  q.QUESTION_TEXT),
                    new OracleParameter("TP",  q.QUESTION_TYPE),
                    new OracleParameter("RQ",  q.IS_REQUIRED),
                    new OracleParameter("ORD", qOrder++),
                    new OracleParameter("PT",  q.POINTS),
                    new OracleParameter("USR", actor),
                    qidParam);
                var newQid = OracleService.ConvertToInt(qidParam.Value);

                int oOrder = 0;
                foreach (var o in q.OPTIONS)
                {
                    await _db.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_TRAINING_TEST_OPTION
                            (QUESTION_ID, OPTION_TEXT, DISPLAY_ORDER, IS_CORRECT)
                        VALUES (:QID, :TX, :ORD, :OK)",
                        new OracleParameter("QID", newQid),
                        new OracleParameter("TX",  o.OPTION_TEXT),
                        new OracleParameter("ORD", oOrder++),
                        new OracleParameter("OK",  o.IS_CORRECT));
                }
            }
        }
        return createdIds;
    }

    // Lấy tất cả CLASS_ID của khóa học trừ classId hiện tại
    public async Task<List<int>> GetOtherClassIdsInCourseAsync(int classId)
    {
        var courseId = (await _db.ExecuteQueryAsync(
            "SELECT COURSE_ID FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => r["COURSE_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["COURSE_ID"]),
            new OracleParameter("ID", classId))).FirstOrDefault();

        if (courseId == null) return new List<int>();

        return await _db.ExecuteQueryAsync(
            "SELECT ID FROM HRMS.HR_TRAINING_CLASS WHERE COURSE_ID = :CID AND ID <> :ID AND STATUS != 'CANCELLED'",
            r => Convert.ToInt32(r["ID"]),
            new OracleParameter("CID", courseId.Value),
            new OracleParameter("ID", classId));
    }
}
