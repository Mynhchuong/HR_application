using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Test-taking + grading (§6). Complement TrainingTestService.
// Attempt state: IN_PROGRESS → SUBMITTED / AUTO_SUBMITTED
// Grading state: attempt.IS_GRADED=0 (có ESSAY chưa chấm) → 1 (đã chấm hết)
public class TrainingAttemptService
{
    private readonly OracleService _db;
    private readonly TrainingNotificationService? _noti;

    public TrainingAttemptService(OracleService db, TrainingNotificationService? noti = null)
    {
        _db = db;
        _noti = noti;
    }

    // ═══════════════════════════════════════════════════════════════
    //  START ATTEMPT — student bấm "Bắt đầu"
    //  Idempotent: nếu đã có attempt IN_PROGRESS → trả lại. PK violation nếu race.
    // ═══════════════════════════════════════════════════════════════

    public async Task<(AttemptModel? attempt, string? error)> StartAttemptAsync(StartAttemptRequest req)
    {
        // Fetch test meta
        var test = (await _db.ExecuteQueryAsync(@"
            SELECT ID, CLASS_ID, IS_TEMPLATE, STATUS, DURATION_MINUTES,
                   AVAILABLE_FROM, AVAILABLE_TO, PASS_SCORE, MAX_ATTEMPTS
              FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => new
            {
                ID       = Convert.ToInt32(r["ID"]),
                CLASS_ID = r["CLASS_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["CLASS_ID"]),
                TPL      = Convert.ToInt32(r["IS_TEMPLATE"]),
                ST       = r["STATUS"]?.ToString() ?? "",
                DUR      = Convert.ToInt32(r["DURATION_MINUTES"]),
                AF       = r["AVAILABLE_FROM"] as DateTime?,
                AT       = r["AVAILABLE_TO"]   as DateTime?,
                PS       = r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
                MAX_ATT  = r["MAX_ATTEMPTS"] is DBNull ? 1 : Convert.ToInt32(r["MAX_ATTEMPTS"]),
            }, new OracleParameter("ID", req.TEST_ID))).FirstOrDefault();

        if (test == null) return (null, "Không tìm thấy test");
        if (test.TPL == 1) return (null, "Template test không dùng để làm bài (§16)");
        if (test.ST is not ("PUBLISHED" or "OPEN"))
            return (null, $"Test đang {test.ST}, chưa mở làm bài");
        // Giờ DB làm nguồn duy nhất — giờ máy app có thể lệch vài phút so với SYSDATE
        // mà deadline/auto-submit đều so bằng SYSDATE.
        var now = await GetDbNowAsync();
        if (test.AF.HasValue && now < test.AF.Value) return (null, "Chưa đến giờ làm bài");

        // Verify student enrolled trong class của test
        if (test.CLASS_ID.HasValue)
        {
            var okEnroll = (await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND EMPCD = :EMP AND STATUS = 'ENROLLED'",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CID", test.CLASS_ID.Value),
                new OracleParameter("EMP", req.EMPCD))).First();
            if (okEnroll == 0) return (null, "Bạn không thuộc lớp này");
        }

        // Đã có attempt đang làm dở? Trả lại (idempotent, resume).
        var existing = await GetAttemptByTestEmpAsync(req.TEST_ID, req.EMPCD);
        if (existing != null && existing.STATUS == "IN_PROGRESS") return (existing, null);

        var nextAttemptNo = (existing?.ATTEMPT_NO ?? 0) + 1;

        // Đã có lần thi kết thúc rồi, hoặc chưa từng thi nhưng cửa sổ AVAILABLE_TO đã đóng.
        // Học viên TỰ thi lại được (không cần HR duyệt) miễn còn trong cửa sổ AVAILABLE_TO VÀ
        // chưa vượt quá "Số lượt thi tối đa" (MAX_ATTEMPTS) đã cấu hình cho bài — đúng như tên gọi
        // field. Chỉ cần HR cấp thêm (HR_TRAINING_RETAKE_GRANT PENDING) khi: cửa sổ đã đóng, hoặc
        // đã dùng hết số lượt cấu hình mà vẫn muốn cho thêm 1 lượt ngoại lệ.
        bool windowClosed = test.AT.HasValue && now > test.AT.Value;
        bool withinConfiguredAttempts = nextAttemptNo <= test.MAX_ATT;
        bool needsGrant = windowClosed || (existing != null && !withinConfiguredAttempts);
        bool usedGrant = false;

        if (needsGrant)
        {
            var grantRows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_RETAKE_GRANT
                   SET STATUS = 'USED', USED_DT = SYSDATE
                 WHERE ID = (SELECT MIN(ID) FROM HRMS.HR_TRAINING_RETAKE_GRANT
                              WHERE TEST_ID = :TID AND EMPCD = :EMP AND STATUS = 'PENDING')",
                new OracleParameter("TID", req.TEST_ID),
                new OracleParameter("EMP", req.EMPCD));

            if (grantRows == 0)
            {
                // Không có lượt cấp thêm — giữ hành vi cũ (idempotent trả kết quả cũ, hoặc chặn).
                if (existing != null) return (existing, null);
                return (null, "Đã hết hạn làm bài");
            }
            usedGrant = true;
        }

        // START_DT = SYSDATE, EFFECTIVE_DEADLINE = MIN(START + DURATION, AVAILABLE_TO) — tính hết
        // bằng giờ DB trong SQL, không dùng giờ app (từng bị START_DT > SUBMIT_DT do clock skew).
        // Lượt thi nhờ grant thì bỏ qua AVAILABLE_TO — mục đích của grant là vượt cửa sổ đã đóng.
        try
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_TEST_ATTEMPT
                    (TEST_ID, EMPCD, ATTEMPT_NO, STATUS, START_DT, EFFECTIVE_DEADLINE, IP_ADDRESS, USER_AGENT)
                VALUES (:TID, :EMP, :ANO, 'IN_PROGRESS', SYSDATE,
                        CASE WHEN :GRANTED = 1 THEN SYSDATE + :DUR / 1440
                             ELSE LEAST(SYSDATE + :DUR / 1440, NVL(:AT, SYSDATE + :DUR / 1440)) END,
                        :IP, :UA)
                RETURNING ID INTO :NEW_ID";
            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("TID", req.TEST_ID),
                new OracleParameter("EMP", req.EMPCD),
                new OracleParameter("ANO", nextAttemptNo),
                new OracleParameter("GRANTED", usedGrant ? 1 : 0),
                new OracleParameter("DUR", test.DUR),
                new OracleParameter("AT",  (object?)test.AT ?? DBNull.Value),
                new OracleParameter("IP",  (object?)req.IP_ADDRESS ?? DBNull.Value),
                new OracleParameter("UA",  (object?)req.USER_AGENT ?? DBNull.Value),
                idParam);
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            // Race: request thứ 2 lọt qua GetAttempt → unique conflict. Trả row mới nhất hiện có.
            return (await GetAttemptByTestEmpAsync(req.TEST_ID, req.EMPCD), null);
        }

        var created = await GetAttemptByTestEmpAsync(req.TEST_ID, req.EMPCD);
        return (created, null);
    }

    // HR cấp thêm 1 lượt thi cho học viên (bận / lỗi kỹ thuật lúc thi / thi rớt).
    // Áp dụng chung cho bài thi thường và bài thi cuối khóa — không phân biệt trong bảng attempt.
    public async Task<(bool ok, string? error)> GrantRetakeAsync(GrantRetakeRequest req)
    {
        var test = (await _db.ExecuteQueryAsync(@"
            SELECT TITLE, CLASS_ID FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => new
            {
                TITLE    = r["TITLE"]?.ToString() ?? "",
                CLASS_ID = r["CLASS_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["CLASS_ID"]),
            }, new OracleParameter("ID", req.TEST_ID))).FirstOrDefault();
        if (test == null) return (false, "Không tìm thấy test");

        await GrantOneRetakeAsync(req.TEST_ID, req.EMPCD, req.LOGIN_USER, req.REASON, test.TITLE, test.CLASS_ID);
        return (true, null);
    }

    // Cấp thi lại hàng loạt cho TẤT CẢ học viên đang rớt bài thi này — mỗi người chỉ cần
    // lượt thi GẦN NHẤT đã nộp (SUBMITTED/AUTO_SUBMITTED) và IS_PASS=0 (rớt), và chưa có
    // sẵn 1 lượt PENDING (tránh cấp chồng — HAS_PENDING_GRANT phía FE cũng dựa trên rule này).
    public async Task<(bool ok, string? error, int granted)> GrantRetakeAllFailedAsync(GrantRetakeAllRequest req)
    {
        var test = (await _db.ExecuteQueryAsync(@"
            SELECT TITLE, CLASS_ID FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => new
            {
                TITLE    = r["TITLE"]?.ToString() ?? "",
                CLASS_ID = r["CLASS_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["CLASS_ID"]),
            }, new OracleParameter("ID", req.TEST_ID))).FirstOrDefault();
        if (test == null) return (false, "Không tìm thấy test", 0);

        var failedEmpcds = await _db.ExecuteQueryAsync(@"
            SELECT A.EMPCD
              FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
             WHERE A.TEST_ID = :TID
               AND A.STATUS IN ('SUBMITTED','AUTO_SUBMITTED')
               AND A.IS_PASS = 0
               AND A.ATTEMPT_NO = (SELECT MAX(A2.ATTEMPT_NO) FROM HRMS.HR_TRAINING_TEST_ATTEMPT A2
                                     WHERE A2.TEST_ID = A.TEST_ID AND A2.EMPCD = A.EMPCD)
               AND NOT EXISTS (SELECT 1 FROM HRMS.HR_TRAINING_RETAKE_GRANT G
                                WHERE G.TEST_ID = A.TEST_ID AND G.EMPCD = A.EMPCD AND G.STATUS = 'PENDING')",
            r => r["EMPCD"]?.ToString() ?? "",
            new OracleParameter("TID", req.TEST_ID));

        foreach (var empcd in failedEmpcds)
        {
            if (string.IsNullOrEmpty(empcd)) continue;
            await GrantOneRetakeAsync(req.TEST_ID, empcd, req.LOGIN_USER, req.REASON, test.TITLE, test.CLASS_ID);
        }

        return (true, null, failedEmpcds.Count);
    }

    private async Task GrantOneRetakeAsync(int testId, string empcd, string actor, string? reason, string testTitle, int? classId)
    {
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_RETAKE_GRANT (TEST_ID, EMPCD, GRANTED_BY, REASON)
            VALUES (:TID, :EMP, :GBY, :REASON)",
            new OracleParameter("TID", testId),
            new OracleParameter("EMP", empcd),
            new OracleParameter("GBY", actor),
            new OracleParameter("REASON", (object?)reason ?? DBNull.Value));

        if (_noti != null)
        {
            await _noti.EnqueueAsync("TRAINING_RETAKE_GRANTED", empcd,
                new Dictionary<string, string> { ["testTitle"] = testTitle },
                classId: classId, testId: testId);
        }
    }

    // Giờ DB (SYSDATE) — nguồn giờ duy nhất cho mọi check deadline/window của attempt,
    // vì START_DT/EFFECTIVE_DEADLINE ghi bằng SYSDATE và batch auto-submit cũng so bằng SYSDATE.
    private async Task<DateTime> GetDbNowAsync() =>
        (await _db.ExecuteQueryAsync("SELECT SYSDATE AS DB_NOW FROM DUAL",
            r => Convert.ToDateTime(r["DB_NOW"]))).First();

    // ═══════════════════════════════════════════════════════════════
    //  SAVE ANSWER (auto-save mỗi câu)
    // ═══════════════════════════════════════════════════════════════

    public async Task<(bool ok, string? error)> SaveAnswerAsync(SaveAnswerRequest req)
    {
        // Verify attempt IN_PROGRESS + đúng EMPCD + trong deadline (so bằng giờ DB, không dùng giờ app)
        var attempt = (await _db.ExecuteQueryAsync(@"
            SELECT ID, EMPCD, STATUS, EFFECTIVE_DEADLINE, SYSDATE AS DB_NOW
              FROM HRMS.HR_TRAINING_TEST_ATTEMPT WHERE ID = :ID",
            r => new
            {
                ID       = Convert.ToInt32(r["ID"]),
                EMP      = r["EMPCD"]?.ToString() ?? "",
                ST       = r["STATUS"]?.ToString() ?? "",
                DEADLINE = Convert.ToDateTime(r["EFFECTIVE_DEADLINE"]),
                DB_NOW   = Convert.ToDateTime(r["DB_NOW"]),
            }, new OracleParameter("ID", req.ATTEMPT_ID))).FirstOrDefault();
        if (attempt == null) return (false, "Không tìm thấy attempt");
        if (attempt.EMP != req.EMPCD) return (false, "Attempt không thuộc bạn");
        if (attempt.ST != "IN_PROGRESS") return (false, $"Attempt đã {attempt.ST}");
        if (attempt.DB_NOW > attempt.DEADLINE) return (false, "Hết giờ, không lưu được");

        // Upsert answer — MERGE (UNIQUE (ATTEMPT_ID, QUESTION_ID))
        await _db.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_TRAINING_TEST_ANSWER T
            USING (SELECT :AID AS AID, :QID AS QID FROM DUAL) S
               ON (T.ATTEMPT_ID = S.AID AND T.QUESTION_ID = S.QID)
            WHEN MATCHED THEN
              UPDATE SET ANSWER_OPTION_IDS = :OPTS, ANSWER_TEXT = :TXT
            WHEN NOT MATCHED THEN
              INSERT (ATTEMPT_ID, QUESTION_ID, ANSWER_OPTION_IDS, ANSWER_TEXT)
              VALUES (S.AID, S.QID, :OPTS, :TXT)",
            new OracleParameter("AID",  req.ATTEMPT_ID),
            new OracleParameter("QID",  req.QUESTION_ID),
            new OracleParameter("OPTS", (object?)req.ANSWER_OPTION_IDS ?? DBNull.Value),
            new OracleParameter("TXT",  (object?)req.ANSWER_TEXT ?? DBNull.Value));
        return (true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SUBMIT (student bấm Nộp bài) — chấm auto + set IS_GRADED
    // ═══════════════════════════════════════════════════════════════

    public async Task<(AttemptModel? result, string? error)> SubmitAsync(SubmitAttemptRequest req)
    {
        var attempt = await GetAttemptByIdAsync(req.ATTEMPT_ID);
        if (attempt == null) return (null, "Không tìm thấy attempt");
        if (attempt.EMPCD != req.EMPCD) return (null, "Attempt không thuộc bạn");
        if (attempt.STATUS != "IN_PROGRESS") return (null, $"Attempt đã {attempt.STATUS}");

        await FinalizeAttemptAsync(req.ATTEMPT_ID, "SUBMITTED");
        return (await GetAttemptByIdAsync(req.ATTEMPT_ID), null);
    }

    // Batch auto-submit — batch job Phase 5 gọi.
    // Idempotent: chỉ đụng IN_PROGRESS + đã quá EFFECTIVE_DEADLINE.
    public async Task<int> AutoSubmitExpiredAsync()
    {
        var expired = await _db.ExecuteQueryAsync(@"
            SELECT ID FROM HRMS.HR_TRAINING_TEST_ATTEMPT
             WHERE STATUS = 'IN_PROGRESS'
               AND SYSDATE > EFFECTIVE_DEADLINE",
            r => Convert.ToInt32(r["ID"]));

        int n = 0;
        foreach (var id in expired)
        {
            await FinalizeAttemptAsync(id, "AUTO_SUBMITTED");
            n++;
        }
        return n;
    }

    // Core finalize: chấm auto các câu SINGLE/MULTI/YESNO/DROPDOWN, compute SCORE, MAX_SCORE, IS_PASS.
    // ESSAY (TEXT) → IS_GRADED=0, chờ teacher. Nếu không có ESSAY → IS_GRADED=1 luôn.
    private async Task FinalizeAttemptAsync(int attemptId, string status)
    {
        // Load questions + correct options + student answers
        var qs = await _db.ExecuteQueryAsync(@"
            SELECT Q.ID, Q.QUESTION_TYPE, Q.POINTS
              FROM HRMS.HR_TRAINING_TEST_QUESTION Q
              JOIN HRMS.HR_TRAINING_TEST_ATTEMPT A ON A.TEST_ID = Q.TEST_ID
             WHERE A.ID = :AID
             ORDER BY Q.DISPLAY_ORDER, Q.ID",
            r => new
            {
                ID     = Convert.ToInt32(r["ID"]),
                TYPE   = r["QUESTION_TYPE"]?.ToString() ?? "",
                POINTS = Convert.ToDecimal(r["POINTS"]),
            }, new OracleParameter("AID", attemptId));

        // Correct options per question (dùng batch fetch để tránh N+1)
        var correctByQ = new Dictionary<int, HashSet<int>>();
        if (qs.Count > 0)
        {
            var correctRows = await _db.ExecuteQueryAsync(@"
                SELECT O.QUESTION_ID, O.ID
                  FROM HRMS.HR_TRAINING_TEST_OPTION O
                  JOIN HRMS.HR_TRAINING_TEST_QUESTION Q ON Q.ID = O.QUESTION_ID
                  JOIN HRMS.HR_TRAINING_TEST_ATTEMPT A ON A.TEST_ID = Q.TEST_ID
                 WHERE A.ID = :AID AND O.IS_CORRECT = 1",
                r => new
                {
                    QID = Convert.ToInt32(r["QUESTION_ID"]),
                    OID = Convert.ToInt32(r["ID"]),
                }, new OracleParameter("AID", attemptId));
            foreach (var g in correctRows.GroupBy(x => x.QID))
                correctByQ[g.Key] = new HashSet<int>(g.Select(x => x.OID));
        }

        var answers = await _db.ExecuteQueryAsync(@"
            SELECT ID, QUESTION_ID, ANSWER_OPTION_IDS
              FROM HRMS.HR_TRAINING_TEST_ANSWER
             WHERE ATTEMPT_ID = :AID",
            r => new
            {
                ID   = Convert.ToInt32(r["ID"]),
                QID  = Convert.ToInt32(r["QUESTION_ID"]),
                OPTS = r["ANSWER_OPTION_IDS"] as string,
            }, new OracleParameter("AID", attemptId));
        var answerByQ = answers.ToDictionary(a => a.QID);

        // Chấm auto per question
        decimal earned = 0m;
        decimal maxScore = 0m;
        bool hasEssay = false;
        foreach (var q in qs)
        {
            maxScore += q.POINTS;
            if (q.TYPE == "TEXT")
            {
                hasEssay = true;
                continue;   // teacher chấm sau
            }
            if (!answerByQ.TryGetValue(q.ID, out var ans) || string.IsNullOrEmpty(ans.OPTS))
                continue;   // không trả lời

            var chosen = ParseOptionIds(ans.OPTS);
            var correct = correctByQ.TryGetValue(q.ID, out var s) ? s : new HashSet<int>();

            bool isRight = q.TYPE switch
            {
                "MULTI"    => chosen.Count > 0 && chosen.SetEquals(correct),
                _          => chosen.Count == 1 && correct.Contains(chosen.First()),  // SINGLE/YESNO/DROPDOWN
            };
            if (isRight) earned += q.POINTS;

            // Chấm auto rồi → set POINTS_AWARDED vào answer để report thấy được
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_TEST_ANSWER
                   SET POINTS_AWARDED = :PT
                 WHERE ID = :ID",
                new OracleParameter("PT", isRight ? q.POINTS : 0m),
                new OracleParameter("ID", ans.ID));
        }

        // Compute IS_PASS nếu test có PASS_SCORE + không còn essay chưa chấm
        var passScore = (await _db.ExecuteQueryAsync(@"
            SELECT T.PASS_SCORE FROM HRMS.HR_TRAINING_TEST T
              JOIN HRMS.HR_TRAINING_TEST_ATTEMPT A ON A.TEST_ID = T.ID
             WHERE A.ID = :AID",
            r => r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
            new OracleParameter("AID", attemptId))).FirstOrDefault();

        int isGraded = hasEssay ? 0 : 1;
        int? isPass = null;
        if (isGraded == 1 && passScore.HasValue)
            isPass = earned >= passScore.Value ? 1 : 0;

        await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_TEST_ATTEMPT
               SET STATUS    = :ST,
                   SUBMIT_DT = SYSDATE,
                   SCORE     = :SC,
                   MAX_SCORE = :MX,
                   IS_PASS   = :PS,
                   IS_GRADED = :GR
             WHERE ID = :ID",
            new OracleParameter("ST", status),
            new OracleParameter("SC", earned),
            new OracleParameter("MX", maxScore),
            new OracleParameter("PS", (object?)isPass ?? DBNull.Value),
            new OracleParameter("GR", isGraded),
            new OracleParameter("ID", attemptId));
    }

    // ═══════════════════════════════════════════════════════════════
    //  GRADING ESSAY (§6.4)
    // ═══════════════════════════════════════════════════════════════

    // Teacher chấm 1 câu ESSAY. Nếu tất cả ESSAY đã chấm xong → IS_GRADED=1 + compute SCORE + IS_PASS.
    public async Task<(bool ok, string? error)> GradeAnswerAsync(GradeAnswerRequest req)
    {
        // Verify teacher của Class có test này
        var ok = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT
              FROM HRMS.HR_TRAINING_CLASS_TEACHER T
              JOIN HRMS.HR_TRAINING_TEST TS ON TS.CLASS_ID = T.CLASS_ID
              JOIN HRMS.HR_TRAINING_TEST_QUESTION Q ON Q.TEST_ID = TS.ID
              JOIN HRMS.HR_TRAINING_TEST_ANSWER AN ON AN.QUESTION_ID = Q.ID
             WHERE AN.ID = :AID AND T.EMPCD = :EMP",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("AID", req.ANSWER_ID),
            new OracleParameter("EMP", req.LOGIN_USER))).First();
        if (ok == 0) return (false, "Bạn không phải teacher của lớp có test này");

        // Update answer
        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_TEST_ANSWER
               SET POINTS_AWARDED = :PT,
                   GRADER_COMMENT = :CM,
                   GRADED_BY      = :EMP,
                   GRADED_DT      = SYSDATE
             WHERE ID = :ID",
            new OracleParameter("PT",  req.POINTS_AWARDED),
            new OracleParameter("CM",  (object?)req.GRADER_COMMENT ?? DBNull.Value),
            new OracleParameter("EMP", req.LOGIN_USER),
            new OracleParameter("ID",  req.ANSWER_ID));
        if (rows == 0) return (false, "Không tìm thấy answer");

        // Attempt của answer
        var attemptId = (await _db.ExecuteQueryAsync(
            "SELECT ATTEMPT_ID FROM HRMS.HR_TRAINING_TEST_ANSWER WHERE ID = :ID",
            r => Convert.ToInt32(r["ATTEMPT_ID"]),
            new OracleParameter("ID", req.ANSWER_ID))).First();

        // Check còn ESSAY chưa chấm (POINTS_AWARDED NULL)
        var pending = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT
              FROM HRMS.HR_TRAINING_TEST_ANSWER AN
              JOIN HRMS.HR_TRAINING_TEST_QUESTION Q ON Q.ID = AN.QUESTION_ID
             WHERE AN.ATTEMPT_ID = :AID
               AND Q.QUESTION_TYPE = 'TEXT'
               AND AN.POINTS_AWARDED IS NULL",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("AID", attemptId))).First();

        if (pending == 0)
        {
            // Total = auto + essay. Recompute từ SUM(POINTS_AWARDED).
            var total = (await _db.ExecuteQueryAsync(@"
                SELECT NVL(SUM(POINTS_AWARDED),0) TT
                  FROM HRMS.HR_TRAINING_TEST_ANSWER
                 WHERE ATTEMPT_ID = :AID",
                r => Convert.ToDecimal(r["TT"]),
                new OracleParameter("AID", attemptId))).First();

            var passScore = (await _db.ExecuteQueryAsync(@"
                SELECT T.PASS_SCORE FROM HRMS.HR_TRAINING_TEST T
                  JOIN HRMS.HR_TRAINING_TEST_ATTEMPT A ON A.TEST_ID = T.ID
                 WHERE A.ID = :AID",
                r => r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
                new OracleParameter("AID", attemptId))).FirstOrDefault();

            int? isPass = passScore.HasValue ? (total >= passScore.Value ? 1 : 0) : (int?)null;

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_TEST_ATTEMPT
                   SET SCORE = :SC, IS_PASS = :PS, IS_GRADED = 1
                 WHERE ID = :AID",
                new OracleParameter("SC",  total),
                new OracleParameter("PS",  (object?)isPass ?? DBNull.Value),
                new OracleParameter("AID", attemptId));

            // Enqueue TRAINING_TEST_GRADED cho student (§13)
            if (_noti != null)
            {
                var info = (await _db.ExecuteQueryAsync(@"
                    SELECT A.EMPCD, T.TITLE, T.CLASS_ID
                      FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
                      JOIN HRMS.HR_TRAINING_TEST T ON T.ID = A.TEST_ID
                     WHERE A.ID = :AID",
                    r => new
                    {
                        EMP   = r["EMPCD"]?.ToString() ?? "",
                        TITLE = r["TITLE"]?.ToString() ?? "",
                        CID   = r["CLASS_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["CLASS_ID"]),
                    }, new OracleParameter("AID", attemptId))).First();
                await _noti.EnqueueAsync("TRAINING_TEST_GRADED", info.EMP,
                    new Dictionary<string, string>
                    {
                        ["testTitle"] = info.TITLE,
                        ["score"]     = total.ToString("0.##"),
                    }, classId: info.CID, testId: null);
            }
        }
        return (true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  STUDENT-FACING views
    // ═══════════════════════════════════════════════════════════════

    // Full data để student làm bài — meta + questions (không IS_CORRECT) + attempt + saved answers + timer.
    // Nếu chưa có attempt → tạo mới (tương đương StartAttempt gộp).
    public async Task<(TestForStudentView? view, string? error)> GetForStudentAsync(int testId, string empcd)
    {
        var test = (await _db.ExecuteQueryAsync(@"
            SELECT ID, CLASS_ID, IS_TEMPLATE, TITLE, DESCRIPTION, STATUS,
                   DURATION_MINUTES, AVAILABLE_FROM, AVAILABLE_TO, PASS_SCORE, MAX_ATTEMPTS, CREATED_BY,
                   INST_DT, UPDT_DT
              FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => new TestModel
            {
                ID                 = Convert.ToInt32(r["ID"]),
                CLASS_ID           = r["CLASS_ID"] is DBNull ? null : Convert.ToInt32(r["CLASS_ID"]),
                IS_TEMPLATE        = Convert.ToInt32(r["IS_TEMPLATE"]),
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
            }, new OracleParameter("ID", testId))).FirstOrDefault();
        if (test == null) return (null, "Không tìm thấy test");

        var attempt = await GetAttemptByTestEmpAsync(testId, empcd);

        // Questions + options (strip IS_CORRECT)
        var qs = await new TrainingTestService(_db).GetQuestionsAsync(testId, forStudent: true);

        var answers = new List<AttemptAnswerModel>();
        if (attempt != null)
        {
            answers = await _db.ExecuteQueryAsync(@"
                SELECT ID, ATTEMPT_ID, QUESTION_ID, ANSWER_OPTION_IDS,
                       DBMS_LOB.SUBSTR(ANSWER_TEXT, 4000, 1) AS ANSWER_TEXT
                  FROM HRMS.HR_TRAINING_TEST_ANSWER
                 WHERE ATTEMPT_ID = :AID",
                r => new AttemptAnswerModel
                {
                    ID                = Convert.ToInt32(r["ID"]),
                    ATTEMPT_ID        = Convert.ToInt32(r["ATTEMPT_ID"]),
                    QUESTION_ID       = Convert.ToInt32(r["QUESTION_ID"]),
                    ANSWER_OPTION_IDS = r["ANSWER_OPTION_IDS"] as string,
                    ANSWER_TEXT       = r["ANSWER_TEXT"] as string,
                }, new OracleParameter("AID", attempt.ID));
        }

        int remaining = 0;
        if (attempt != null && attempt.STATUS == "IN_PROGRESS")
        {
            // Timer đếm ngược tính từ giờ DB — auto-submit batch cũng so bằng SYSDATE
            remaining = Math.Max(0, (int)Math.Floor((attempt.EFFECTIVE_DEADLINE - await GetDbNowAsync()).TotalSeconds));
        }

        return (new TestForStudentView
        {
            TEST              = test,
            QUESTIONS         = qs,
            ATTEMPT           = attempt,
            ANSWERS           = answers,
            SECONDS_REMAINING = remaining,
        }, null);
    }

    // My result — sau khi submit (§6.5): điểm + PASS/FAIL, KHÔNG show đáp án đúng
    public async Task<MyResultView?> GetMyResultAsync(int testId, string empcd)
    {
        var att = await GetAttemptByTestEmpAsync(testId, empcd);
        if (att == null) return null;

        var test = (await _db.ExecuteQueryAsync(
            "SELECT PASS_SCORE, CLASS_ID FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => new
            {
                PASS_SCORE = r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
                CLASS_ID   = r["CLASS_ID"]   is DBNull ? (int?)null    : Convert.ToInt32(r["CLASS_ID"]),
            }, new OracleParameter("ID", testId))).FirstOrDefault();

        string? passFail = null;
        if (att.IS_GRADED == 1 && test?.PASS_SCORE != null && att.SCORE.HasValue)
            passFail = att.SCORE.Value >= test.PASS_SCORE.Value ? "PASS" : "FAIL";

        // Câu tự luận của chính học viên + điểm/nhận xét GV chấm (chỉ sau khi đã nộp bài).
        var essays = att.STATUS is "SUBMITTED" or "AUTO_SUBMITTED"
            ? await GetEssayItemsAsync(att.ID)
            : new List<EssayItem>();

        return new MyResultView { ATTEMPT = att, PASS_FAIL = passFail, ESSAYS = essays, CLASS_ID = test?.CLASS_ID };
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEACHER-FACING
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<GradingItemModel>> GetPendingGradeAsync(int testId, string? teacherEmpcd = null)
    {
        // GV phụ trách nhóm → chỉ thấy bài học viên các nhóm mình phụ trách (1 GV có thể NHIỀU nhóm
        // — nhiều rows CLASS_TEACHER, 1 row/nhóm). GV dạy cả lớp (có row GROUP_ID NULL) hoặc
        // HR/Admin (không có row teacher) → thấy toàn bộ.
        var teacherGroupIds = new List<int>();
        int? classId = null;
        if (!string.IsNullOrEmpty(teacherEmpcd))
        {
            var groupRows = await _db.ExecuteQueryAsync(@"
                SELECT CT.GROUP_ID, T.CLASS_ID
                  FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
                  JOIN HRMS.HR_TRAINING_TEST T ON T.CLASS_ID = CT.CLASS_ID
                 WHERE T.ID = :TID AND CT.EMPCD = :EMP",
                r => new
                {
                    GROUP_ID = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                    CLASS_ID = Convert.ToInt32(r["CLASS_ID"]),
                },
                new OracleParameter("TID", testId),
                new OracleParameter("EMP", teacherEmpcd));
            if (groupRows.Count > 0 && groupRows.All(g => g.GROUP_ID.HasValue))
            {
                teacherGroupIds = groupRows.Select(g => g.GROUP_ID!.Value).Distinct().ToList();
                classId = groupRows[0].CLASS_ID;
            }
        }

        // IN clause bind động theo số nhóm của GV (BindByName=true). Correlation với outer alias A
        // PHẢI nằm ở WHERE của subquery, KHÔNG được đặt trong ON của JOIN (Oracle ORA-00904) —
        // nên CLASS_ID lấy sẵn ở trên thay vì JOIN lại HR_TRAINING_TEST bên trong EXISTS.
        var groupFilter = "";
        var pars = new List<OracleParameter> { new("TID", testId) };
        if (teacherGroupIds.Count > 0 && classId.HasValue)
        {
            var binds = teacherGroupIds.Select((_, i) => $":TG{i}").ToList();
            for (int i = 0; i < teacherGroupIds.Count; i++)
                pars.Add(new OracleParameter($"TG{i}", teacherGroupIds[i]));
            pars.Add(new OracleParameter("GCID", classId.Value));
            groupFilter = $@"
               AND EXISTS (
                    SELECT 1 FROM HRMS.HR_TRAINING_ENROLLMENT E
                     WHERE E.CLASS_ID = :GCID
                       AND E.EMPCD = A.EMPCD
                       AND E.GROUP_ID IN ({string.Join(", ", binds)}))";
        }

        // Attempts có ESSAY chưa chấm (IS_GRADED=0 + có row TEXT chưa POINTS_AWARDED)
        var attempts = await _db.ExecuteQueryAsync($@"
            SELECT DISTINCT A.ID, A.TEST_ID, A.EMPCD, EC.CNAME EMP_NAME, A.ATTEMPT_NO,
                   A.STATUS, A.START_DT, A.SUBMIT_DT, A.EFFECTIVE_DEADLINE,
                   A.SCORE, A.MAX_SCORE, A.IS_PASS, A.IS_GRADED,
                   A.INST_DT, A.UPDT_DT
              FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = A.EMPCD
              JOIN HRMS.HR_TRAINING_TEST_ANSWER AN ON AN.ATTEMPT_ID = A.ID
              JOIN HRMS.HR_TRAINING_TEST_QUESTION Q ON Q.ID = AN.QUESTION_ID
             WHERE A.TEST_ID = :TID
               AND A.STATUS IN ('SUBMITTED','AUTO_SUBMITTED')
               AND A.IS_GRADED = 0
               AND Q.QUESTION_TYPE = 'TEXT'
               AND AN.POINTS_AWARDED IS NULL{groupFilter}
             ORDER BY A.SUBMIT_DT",
            r => MapAttemptWithName(r),
            pars.ToArray());

        var result = new List<GradingItemModel>();
        foreach (var att in attempts)
        {
            result.Add(new GradingItemModel { ATTEMPT = att, ESSAYS = await GetEssayItemsAsync(att.ID) });
        }
        return result;
    }

    // Câu TEXT của 1 attempt: đề + bài làm + điểm/nhận xét GV (nếu đã chấm).
    // Dùng cho teacher grading LẪN student xem kết quả — TEXT không có đáp án mẫu nên
    // show cho học viên không vi phạm §6.5 "không hiển đáp án đúng".
    private async Task<List<EssayItem>> GetEssayItemsAsync(int attemptId)
    {
        return await _db.ExecuteQueryAsync(@"
            SELECT AN.ID ANSWER_ID, Q.ID QUESTION_ID,
                   DBMS_LOB.SUBSTR(Q.QUESTION_TEXT, 4000, 1) AS QUESTION_TEXT, Q.POINTS,
                   DBMS_LOB.SUBSTR(AN.ANSWER_TEXT, 4000, 1) AS ANSWER_TEXT, AN.POINTS_AWARDED,
                   DBMS_LOB.SUBSTR(AN.GRADER_COMMENT, 4000, 1) AS GRADER_COMMENT
              FROM HRMS.HR_TRAINING_TEST_ANSWER AN
              JOIN HRMS.HR_TRAINING_TEST_QUESTION Q ON Q.ID = AN.QUESTION_ID
             WHERE AN.ATTEMPT_ID = :AID
               AND Q.QUESTION_TYPE = 'TEXT'
             ORDER BY Q.DISPLAY_ORDER",
            r => new EssayItem
            {
                ANSWER_ID       = Convert.ToInt32(r["ANSWER_ID"]),
                QUESTION_ID     = Convert.ToInt32(r["QUESTION_ID"]),
                QUESTION_TEXT   = r["QUESTION_TEXT"]?.ToString() ?? "",
                QUESTION_POINTS = Convert.ToDecimal(r["POINTS"]),
                ANSWER_TEXT     = r["ANSWER_TEXT"] as string,
                POINTS_AWARDED  = r["POINTS_AWARDED"] is DBNull ? null : Convert.ToDecimal(r["POINTS_AWARDED"]),
                GRADER_COMMENT  = r["GRADER_COMMENT"] as string,
            }, new OracleParameter("AID", attemptId));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    // Lấy lần thi MỚI NHẤT (ATTEMPT_NO cao nhất) của 1 (test, học viên) — có thể có nhiều dòng
    // lịch sử nếu đã được HR cấp thi lại, chỉ dòng mới nhất mới còn "active" cho luồng làm bài.
    private async Task<AttemptModel?> GetAttemptByTestEmpAsync(int testId, string empcd)
    {
        return (await _db.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT A.ID, A.TEST_ID, A.EMPCD, EC.CNAME EMP_NAME, A.ATTEMPT_NO,
                       A.STATUS, A.START_DT, A.SUBMIT_DT, A.EFFECTIVE_DEADLINE,
                       A.SCORE, A.MAX_SCORE, A.IS_PASS, A.IS_GRADED,
                       A.INST_DT, A.UPDT_DT
                  FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
                  LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = A.EMPCD
                 WHERE A.TEST_ID = :TID AND A.EMPCD = :EMP
                 ORDER BY A.ATTEMPT_NO DESC
            ) WHERE ROWNUM = 1",
            MapAttemptWithName,
            new OracleParameter("TID", testId),
            new OracleParameter("EMP", empcd))).FirstOrDefault();
    }

    private async Task<AttemptModel?> GetAttemptByIdAsync(int attemptId)
    {
        return (await _db.ExecuteQueryAsync(@"
            SELECT A.ID, A.TEST_ID, A.EMPCD, EC.CNAME EMP_NAME, A.ATTEMPT_NO,
                   A.STATUS, A.START_DT, A.SUBMIT_DT, A.EFFECTIVE_DEADLINE,
                   A.SCORE, A.MAX_SCORE, A.IS_PASS, A.IS_GRADED,
                   A.INST_DT, A.UPDT_DT
              FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = A.EMPCD
             WHERE A.ID = :ID",
            MapAttemptWithName,
            new OracleParameter("ID", attemptId))).FirstOrDefault();
    }

    private static AttemptModel MapAttemptWithName(OracleDataReader r) => new()
    {
        ID                 = Convert.ToInt32(r["ID"]),
        TEST_ID            = Convert.ToInt32(r["TEST_ID"]),
        EMPCD              = r["EMPCD"]?.ToString() ?? "",
        EMP_NAME           = r["EMP_NAME"] as string,
        ATTEMPT_NO         = Convert.ToInt32(r["ATTEMPT_NO"]),
        STATUS             = r["STATUS"]?.ToString() ?? "IN_PROGRESS",
        START_DT           = Convert.ToDateTime(r["START_DT"]),
        SUBMIT_DT          = r["SUBMIT_DT"] as DateTime?,
        EFFECTIVE_DEADLINE = Convert.ToDateTime(r["EFFECTIVE_DEADLINE"]),
        SCORE              = r["SCORE"]     is DBNull ? null : Convert.ToDecimal(r["SCORE"]),
        MAX_SCORE          = r["MAX_SCORE"] is DBNull ? null : Convert.ToDecimal(r["MAX_SCORE"]),
        IS_PASS            = r["IS_PASS"]   is DBNull ? null : Convert.ToInt32(r["IS_PASS"]),
        IS_GRADED          = Convert.ToInt32(r["IS_GRADED"]),
        INST_DT            = r["INST_DT"] as DateTime?,
        UPDT_DT            = r["UPDT_DT"] as DateTime?,
    };

    // CSV "12,34,56" → {12,34,56}
    private static HashSet<int> ParseOptionIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new HashSet<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                  .Select(s => s.Trim())
                  .Where(s => int.TryParse(s, out _))
                  .Select(int.Parse)
                  .ToHashSet();
    }
}
