using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Compute completion criteria (§7) + set IS_CERTIFIED / STATUS / SCORE / ATTENDANCE_PERCENT / COMPLETION_DATE.
// Trigger tay bằng endpoint HR bấm "Chốt lớp" — Phase 5 sẽ có batch job tự chạy khi Class chuyển COMPLETED.
// Cert list (§12) cũng ở đây vì tự nhiên liên quan.
public class TrainingCompletionService
{
    private readonly OracleService _db;
    private readonly TrainingReviewService _review;
    private readonly TrainingNotificationService _noti;

    public TrainingCompletionService(OracleService db, TrainingReviewService review, TrainingNotificationService noti)
    {
        _db = db;
        _review = review;
        _noti = noti;
    }

    // ═══════════════════════════════════════════════════════════════
    //  FINALIZE — chốt lớp: compute từng enrollment, update DB
    //  Class phải đang COMPLETED status. Chỉ HR/Admin bấm.
    // ═══════════════════════════════════════════════════════════════

    public async Task<FinalizeClassResult> FinalizeClassAsync(FinalizeClassRequest req)
    {
        // Load Class + Course để biết mode + criteria
        var meta = (await _db.ExecuteQueryAsync(@"
            SELECT CL.ID, CL.STATUS, CL.MIN_ATTENDANCE_PERCENT, CL.FINAL_TEST_ID, CL.REQUIRE_POST_REVIEW,
                   CL.IS_EXPRESS, CO.COURSE_MODE
              FROM HRMS.HR_TRAINING_CLASS CL
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
             WHERE CL.ID = :ID",
            r => new
            {
                ST          = r["STATUS"]?.ToString() ?? "",
                MIN_ATT     = Convert.ToDecimal(r["MIN_ATTENDANCE_PERCENT"]),
                FINAL_TEST  = r["FINAL_TEST_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["FINAL_TEST_ID"]),
                REQ_REVIEW  = Convert.ToInt32(r["REQUIRE_POST_REVIEW"]),
                IS_EXPRESS  = Convert.ToInt32(r["IS_EXPRESS"]),
                COURSE_MODE = r["COURSE_MODE"]?.ToString() ?? "STANDARD",
            }, new OracleParameter("ID", req.CLASS_ID))).FirstOrDefault();
        if (meta == null) throw new InvalidOperationException("Không tìm thấy Class");
        // COMPLETED là trạng thái chuẩn để chốt. CLOSED vẫn cho chạy — recovery cho lớp lỡ bị đóng
        // trước khi chốt (query học viên chỉ lấy STATUS='ENROLLED' nên lớp đã chốt xong mà chạy lại
        // chỉ là no-op, không đè kết quả cũ).
        if (meta.ST != "COMPLETED" && meta.ST != "CLOSED")
            throw new InvalidOperationException($"Class đang {meta.ST}, chỉ chốt được khi COMPLETED");

        // List học viên ENROLLED của Class
        var students = await _db.ExecuteQueryAsync(@"
            SELECT E.EMPCD, EC.CNAME EMP_NAME, E.GROUP_ID
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
             WHERE E.CLASS_ID = :CID AND E.STATUS = 'ENROLLED'",
            r => new
            {
                EMPCD    = r["EMPCD"]?.ToString() ?? "",
                EMP_NAME = r["EMP_NAME"] as string,
                GROUP_ID = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
            }, new OracleParameter("CID", req.CLASS_ID));

        var res = new FinalizeClassResult { CLASS_ID = req.CLASS_ID, TOTAL_ENROLLED = students.Count };

        foreach (var s in students)
        {
            var (att, score, passed, reason) = meta.COURSE_MODE == "EXPRESS"
                ? await ComputeExpressAsync(req.CLASS_ID, s.EMPCD, s.GROUP_ID, meta.FINAL_TEST)
                : await ComputeStandardAsync(req.CLASS_ID, s.EMPCD, s.GROUP_ID,
                    meta.MIN_ATT, meta.FINAL_TEST, meta.REQ_REVIEW == 1);

            // Update enrollment
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_ENROLLMENT
                   SET STATUS             = :ST,
                       ATTENDANCE_PERCENT = :ATT,
                       FINAL_SCORE        = :SC,
                       IS_CERTIFIED       = :CERT,
                       COMPLETION_DATE    = CASE WHEN :ST = 'COMPLETED' THEN SYSDATE ELSE NULL END,
                       UPDT_ID            = :USR
                 WHERE CLASS_ID = :CID AND EMPCD = :EMP",
                new OracleParameter("ST",   passed ? "COMPLETED" : "FAILED"),
                new OracleParameter("ATT",  (object?)att ?? DBNull.Value),
                new OracleParameter("SC",   (object?)score ?? DBNull.Value),
                new OracleParameter("CERT", passed ? 1 : 0),
                new OracleParameter("USR",  req.LOGIN_USER),
                new OracleParameter("CID",  req.CLASS_ID),
                new OracleParameter("EMP",  s.EMPCD));

            res.DETAILS.Add(new CompletionResultModel
            {
                EMPCD              = s.EMPCD,
                EMP_NAME           = s.EMP_NAME,
                ATTENDANCE_PERCENT = att,
                FINAL_SCORE        = score,
                PASSED             = passed ? 1 : 0,
                FAIL_REASON        = reason,
            });
            if (passed) { res.COMPLETED_COUNT++; res.CERTIFIED_COUNT++; }
            else res.FAILED_COUNT++;
        }

        // Enqueue TRAINING_CLASS_COMPLETED cho từng học viên (§13)
        var className = (await _db.ExecuteQueryAsync(
            "SELECT CLASS_NAME FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => r["CLASS_NAME"]?.ToString() ?? "",
            new OracleParameter("ID", req.CLASS_ID))).FirstOrDefault() ?? "";

        foreach (var d in res.DETAILS)
        {
            await _noti.EnqueueAsync("TRAINING_CLASS_COMPLETED", d.EMPCD,
                new Dictionary<string, string>
                {
                    ["className"] = className,
                    ["result"]    = d.PASSED == 1 ? "Đạt (có chứng chỉ)" : (d.FAIL_REASON ?? "Không đạt"),
                }, classId: req.CLASS_ID);
        }
        return res;
    }

    // ═══════════════════════════════════════════════════════════════
    //  COMPUTE — STANDARD
    // ═══════════════════════════════════════════════════════════════

    private async Task<(decimal? att, decimal? score, bool passed, string? reason)>
        ComputeStandardAsync(int classId, string empcd, int? groupId, decimal minAtt, int? finalTestId, bool requireReview)
    {
        // Attendance % (§5b.4 group-filtered)
        var att = await ComputeAttendancePercentAsync(classId, empcd, groupId);
        if (att < minAtt)
            return (att, null, false, $"Attendance {att}% < required {minAtt}%");

        // Final test check
        decimal? score = null;
        if (finalTestId.HasValue)
        {
            // Học viên có thể có nhiều lần thi (HR cấp thi lại) — lấy attempt TỐT NHẤT: đậu thắng
            // rớt, điểm cao thắng điểm thấp, mới nhất thắng khi hoà.
            var attempt = (await _db.ExecuteQueryAsync(@"
                SELECT * FROM (
                    SELECT SCORE, IS_PASS, IS_GRADED, STATUS
                      FROM HRMS.HR_TRAINING_TEST_ATTEMPT
                     WHERE TEST_ID = :TID AND EMPCD = :EMP
                     ORDER BY IS_PASS DESC NULLS LAST, SCORE DESC NULLS LAST, ATTEMPT_NO DESC
                ) WHERE ROWNUM = 1",
                r => new
                {
                    SC = r["SCORE"]   is DBNull ? (decimal?)null : Convert.ToDecimal(r["SCORE"]),
                    PS = r["IS_PASS"] is DBNull ? (int?)null : Convert.ToInt32(r["IS_PASS"]),
                    GR = Convert.ToInt32(r["IS_GRADED"]),
                    ST = r["STATUS"]?.ToString() ?? "",
                }, new OracleParameter("TID", finalTestId.Value),
                   new OracleParameter("EMP", empcd))).FirstOrDefault();

            if (attempt == null)   return (att, null, false, "Chưa làm final test");
            if (attempt.GR == 0)   return (att, attempt.SC, false, "Test còn ESSAY chưa chấm");
            if (attempt.PS != 1)   return (att, attempt.SC, false, "Không pass final test");
            score = attempt.SC;
        }

        // Review check
        if (requireReview)
        {
            var reviewed = await _review.HasSubmittedAsync(classId, empcd);
            if (!reviewed) return (att, score, false, "Chưa submit review");
        }

        return (att, score, true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  COMPUTE — EXPRESS (§7 v1 + §2.1 EXPRESS + test optional)
    // ═══════════════════════════════════════════════════════════════

    private async Task<(decimal? att, decimal? score, bool passed, string? reason)>
        ComputeExpressAsync(int classId, string empcd, int? groupId, int? finalTestId)
    {
        // Session không CANCELLED + có row PRESENT/LATE/EXCUSED
        var att = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT
              FROM HRMS.HR_TRAINING_SESSION S
              JOIN HRMS.HR_TRAINING_ATTENDANCE A ON A.SESSION_ID = S.ID
             WHERE S.CLASS_ID = :CID
               AND S.STATUS != 'CANCELLED'
               AND A.EMPCD = :EMP
               AND A.STATUS IN ('PRESENT','LATE','EXCUSED')",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd))).First();

        if (att == 0)
            return (0m, null, false, "Không có buổi PRESENT/LATE/EXCUSED");

        // Attendance % cho EXPRESS: đơn giản 100 nếu có, 0 nếu không (§7 không dùng %)
        decimal attPct = 100m;

        // Test optional (§2.1 EXPRESS + test 2026-07-06)
        decimal? score = null;
        if (finalTestId.HasValue)
        {
            var attempt = (await _db.ExecuteQueryAsync(@"
                SELECT * FROM (
                    SELECT SCORE, IS_PASS, IS_GRADED
                      FROM HRMS.HR_TRAINING_TEST_ATTEMPT
                     WHERE TEST_ID = :TID AND EMPCD = :EMP
                     ORDER BY IS_PASS DESC NULLS LAST, SCORE DESC NULLS LAST, ATTEMPT_NO DESC
                ) WHERE ROWNUM = 1",
                r => new
                {
                    SC = r["SCORE"]   is DBNull ? (decimal?)null : Convert.ToDecimal(r["SCORE"]),
                    PS = r["IS_PASS"] is DBNull ? (int?)null : Convert.ToInt32(r["IS_PASS"]),
                    GR = Convert.ToInt32(r["IS_GRADED"]),
                }, new OracleParameter("TID", finalTestId.Value),
                   new OracleParameter("EMP", empcd))).FirstOrDefault();

            if (attempt == null)   return (attPct, null, false, "Chưa làm test");
            if (attempt.GR == 0)   return (attPct, attempt.SC, false, "Test chưa chấm xong");
            if (attempt.PS != 1)   return (attPct, attempt.SC, false, "Không pass test");
            score = attempt.SC;
        }

        return (attPct, score, true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Attendance % helper — dùng §5b.4 (filter cả tử/mẫu theo group)
    // ═══════════════════════════════════════════════════════════════

    private async Task<decimal> ComputeAttendancePercentAsync(int classId, string empcd, int? groupId)
    {
        var row = (await _db.ExecuteQueryAsync(@"
            SELECT
                COUNT(*) DENOM,
                SUM(CASE WHEN A.STATUS IN ('PRESENT','LATE','EXCUSED')
                          AND A.TEACHER_CONFIRMED = 1 THEN 1 ELSE 0 END) NUMER
              FROM HRMS.HR_TRAINING_SESSION S
              LEFT JOIN HRMS.HR_TRAINING_ATTENDANCE A
                     ON A.SESSION_ID = S.ID AND A.EMPCD = :EMP
             WHERE S.CLASS_ID = :CID
               AND S.STATUS = 'COMPLETED'
               AND (S.GROUP_ID IS NULL OR S.GROUP_ID = :GID)",
            r => new
            {
                D = Convert.ToInt32(r["DENOM"]),
                N = r["NUMER"] is DBNull ? 0 : Convert.ToInt32(r["NUMER"]),
            },
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd),
            new OracleParameter("GID", (object?)groupId ?? DBNull.Value))).First();

        if (row.D == 0) return 0m;
        return Math.Round(100m * row.N / row.D, 2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  CERTIFICATE LIST (§12)
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<CertificateItem>> ListCertificatesAsync(
        int? classId, int? courseId, string? deptcd, string? linecd, string? workcd,
        DateTime? from, DateTime? to, string? searchEmpcd = null,
        string? scopeSql = null, List<OracleParameter>? scopeParams = null)
    {
        var ps = new List<OracleParameter>
        {
            new OracleParameter("P_CID",  (object?)classId ?? DBNull.Value),
            new OracleParameter("P_CRS",  (object?)courseId ?? DBNull.Value),
            new OracleParameter("P_DPT",  (object?)deptcd  ?? DBNull.Value),
            new OracleParameter("P_LN",   (object?)linecd  ?? DBNull.Value),
            new OracleParameter("P_WK",   (object?)workcd  ?? DBNull.Value),
            new OracleParameter("P_FROM", (object?)from    ?? DBNull.Value),
            new OracleParameter("P_TO",   (object?)to      ?? DBNull.Value),
            new OracleParameter("P_SEMP", (object?)searchEmpcd ?? DBNull.Value)
        };
        if (scopeParams != null)
        {
            ps.AddRange(scopeParams);
        }

        string sql = $@"
            SELECT E.CLASS_ID, CL.CLASS_NAME, CO.TITLE COURSE_TITLE,
                   E.EMPCD, EC.CNAME EMP_NAME,
                   EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   B.DEPTNM, B.TEAMNM LINE_NAME, B.WORKNM,
                   E.COMPLETION_DATE, E.FINAL_SCORE, E.ATTENDANCE_PERCENT
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = E.CLASS_ID
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
             WHERE E.IS_CERTIFIED = 1
               AND (:P_CID  IS NULL OR E.CLASS_ID = :P_CID)
               AND (:P_CRS  IS NULL OR CL.COURSE_ID = :P_CRS)
               AND (:P_DPT  IS NULL OR EC.DEPTCD  = :P_DPT)
               AND (:P_LN   IS NULL OR EC.LINECD  = :P_LN)
               AND (:P_WK   IS NULL OR EC.WORKCD  = :P_WK)
               AND (:P_FROM IS NULL OR E.COMPLETION_DATE >= :P_FROM)
               AND (:P_TO   IS NULL OR E.COMPLETION_DATE <= :P_TO)
               AND (:P_SEMP IS NULL OR E.EMPCD = :P_SEMP)
               {scopeSql}
             ORDER BY E.COMPLETION_DATE DESC, E.EMPCD";

        return await _db.ExecuteQueryAsync(sql, r => new CertificateItem
        {
            CLASS_ID           = Convert.ToInt32(r["CLASS_ID"]),
            CLASS_NAME         = r["CLASS_NAME"]?.ToString()   ?? "",
            COURSE_TITLE       = r["COURSE_TITLE"]?.ToString() ?? "",
            EMPCD              = r["EMPCD"]?.ToString() ?? "",
            EMP_NAME           = r["EMP_NAME"] as string,
            DEPTCD             = r["DEPTCD"] as string,
            LINECD             = r["LINECD"] as string,
            WORKCD             = r["WORKCD"] as string,
            DEPT_NAME          = r["DEPTNM"] as string,
            LINE_NAME          = r["LINE_NAME"] as string,
            WORK_NAME          = r["WORKNM"] as string,
            COMPLETION_DATE    = r["COMPLETION_DATE"] as DateTime?,
            FINAL_SCORE        = r["FINAL_SCORE"]        is DBNull ? null : Convert.ToDecimal(r["FINAL_SCORE"]),
            ATTENDANCE_PERCENT = r["ATTENDANCE_PERCENT"] is DBNull ? null : Convert.ToDecimal(r["ATTENDANCE_PERCENT"]),
        }, ps.ToArray());
    }

    // HR revoke certificate — set IS_CERTIFIED=0 với audit
    public async Task RevokeCertificateAsync(int classId, string empcd, string actor)
    {
        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET IS_CERTIFIED = 0, UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND EMPCD = :EMP AND IS_CERTIFIED = 1",
            new OracleParameter("USR", actor),
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy certificate active");
    }
}
