using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Reports §14. Excel export delegate to caller (ClosedXML ở HR_web).
public class TrainingReportService
{
    private readonly OracleService _db;

    public TrainingReportService(OracleService db) { _db = db; }

    // ═══════════════════════════════════════════════════════════════
    //  §14.1 REPORT CLASS — overview + histogram + per-group breakdown
    // ═══════════════════════════════════════════════════════════════

    public async Task<ReportClassModel?> GetClassReportAsync(int classId)
    {
        var meta = (await _db.ExecuteQueryAsync(@"
            SELECT CL.ID, CL.CLASS_NAME, CL.STATUS, CO.TITLE COURSE_TITLE
              FROM HRMS.HR_TRAINING_CLASS CL
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
             WHERE CL.ID = :ID",
            r => new
            {
                ID     = Convert.ToInt32(r["ID"]),
                NAME   = r["CLASS_NAME"]?.ToString() ?? "",
                ST     = r["STATUS"]?.ToString() ?? "",
                COURSE = r["COURSE_TITLE"]?.ToString() ?? "",
            }, new OracleParameter("ID", classId))).FirstOrDefault();
        if (meta == null) return null;

        var counts = (await _db.ExecuteQueryAsync(@"
            SELECT
                SUM(CASE WHEN STATUS = 'ENROLLED'  THEN 1 ELSE 0 END) ENROLLED_CNT,
                SUM(CASE WHEN SOURCE = 'ASSIGNED'  AND STATUS <> 'REJECTED' THEN 1 ELSE 0 END) ASSIGNED_CNT,
                SUM(CASE WHEN SOURCE = 'SELF_REGISTER' AND STATUS <> 'REJECTED' THEN 1 ELSE 0 END) SELF_CNT,
                SUM(CASE WHEN STATUS = 'DROPPED'   THEN 1 ELSE 0 END) DROPPED_CNT,
                SUM(CASE WHEN STATUS = 'COMPLETED' THEN 1 ELSE 0 END) COMPLETED_CNT,
                SUM(CASE WHEN STATUS = 'FAILED'    THEN 1 ELSE 0 END) FAILED_CNT,
                SUM(CASE WHEN IS_CERTIFIED = 1     THEN 1 ELSE 0 END) CERT_CNT,
                AVG(ATTENDANCE_PERCENT) AVG_ATT,
                AVG(FINAL_SCORE) AVG_SC
              FROM HRMS.HR_TRAINING_ENROLLMENT
             WHERE CLASS_ID = :CID",
            r => new
            {
                E = r["ENROLLED_CNT"]  is DBNull ? 0 : Convert.ToInt32(r["ENROLLED_CNT"]),
                A = r["ASSIGNED_CNT"]  is DBNull ? 0 : Convert.ToInt32(r["ASSIGNED_CNT"]),
                S = r["SELF_CNT"]      is DBNull ? 0 : Convert.ToInt32(r["SELF_CNT"]),
                D = r["DROPPED_CNT"]   is DBNull ? 0 : Convert.ToInt32(r["DROPPED_CNT"]),
                C = r["COMPLETED_CNT"] is DBNull ? 0 : Convert.ToInt32(r["COMPLETED_CNT"]),
                F = r["FAILED_CNT"]    is DBNull ? 0 : Convert.ToInt32(r["FAILED_CNT"]),
                CE= r["CERT_CNT"]      is DBNull ? 0 : Convert.ToInt32(r["CERT_CNT"]),
                AA= r["AVG_ATT"]       is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_ATT"]),
                AS_ = r["AVG_SC"]      is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_SC"]),
            }, new OracleParameter("CID", classId))).First();

        // Score histogram (5 buckets 0-2, 2-4, 4-6, 6-8, 8-10)
        var hist = await GetHistogramAsync(classId);

        // Per-group breakdown (nếu Class có group)
        var groups = await _db.ExecuteQueryAsync(@"
            SELECT G.ID GROUP_ID, G.GROUP_NAME,
                   COUNT(CASE WHEN E.STATUS IN ('ENROLLED','COMPLETED','FAILED') THEN 1 END) ENROLLED_CNT,
                   COUNT(CASE WHEN E.STATUS = 'COMPLETED' THEN 1 END) COMPLETED_CNT,
                   COUNT(CASE WHEN E.IS_CERTIFIED = 1     THEN 1 END) CERT_CNT,
                   AVG(E.ATTENDANCE_PERCENT) AVG_ATT
              FROM HRMS.HR_TRAINING_CLASS_GROUP G
              LEFT JOIN HRMS.HR_TRAINING_ENROLLMENT E ON E.GROUP_ID = G.ID
             WHERE G.CLASS_ID = :CID
             GROUP BY G.ID, G.GROUP_NAME
             ORDER BY G.GROUP_NAME",
            r => new GroupBreakdown
            {
                GROUP_ID       = Convert.ToInt32(r["GROUP_ID"]),
                GROUP_NAME     = r["GROUP_NAME"]?.ToString() ?? "",
                ENROLLED       = Convert.ToInt32(r["ENROLLED_CNT"]),
                COMPLETED      = Convert.ToInt32(r["COMPLETED_CNT"]),
                CERTIFIED      = Convert.ToInt32(r["CERT_CNT"]),
                AVG_ATTENDANCE = r["AVG_ATT"] is DBNull ? null : Convert.ToDecimal(r["AVG_ATT"]),
            }, new OracleParameter("CID", classId));

        return new ReportClassModel
        {
            CLASS_ID              = meta.ID,
            CLASS_NAME            = meta.NAME,
            COURSE_TITLE          = meta.COURSE,
            CLASS_STATUS          = meta.ST,
            ENROLLED_COUNT        = counts.E,
            ASSIGNED_COUNT        = counts.A,
            SELF_REGISTER_COUNT   = counts.S,
            DROPPED_COUNT         = counts.D,
            COMPLETED_COUNT       = counts.C,
            FAILED_COUNT          = counts.F,
            CERTIFIED_COUNT       = counts.CE,
            AVG_ATTENDANCE_PERCENT= counts.AA,
            AVG_FINAL_SCORE       = counts.AS_,
            SCORE_HISTOGRAM       = hist,
            GROUP_BREAKDOWN       = groups,
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  REPORT COURSE — bảng từng lớp thuộc khóa + 1 dòng tổng cộng cả khóa
    // ═══════════════════════════════════════════════════════════════

    public async Task<ReportCourseModel?> GetCourseReportAsync(int courseId)
    {
        var meta = (await _db.ExecuteQueryAsync(@"
            SELECT ID, TITLE FROM HRMS.HR_TRAINING_COURSE WHERE ID = :ID",
            r => new { ID = Convert.ToInt32(r["ID"]), TITLE = r["TITLE"]?.ToString() ?? "" },
            new OracleParameter("ID", courseId))).FirstOrDefault();
        if (meta == null) return null;

        // Từng lớp trong khóa — LEFT JOIN để lớp chưa có ai enroll vẫn hiện dòng (số 0), không bị mất khỏi báo cáo.
        var classes = await _db.ExecuteQueryAsync(@"
            SELECT CL.ID CLASS_ID, CL.CLASS_NAME, CL.STATUS,
                   SUM(CASE WHEN E.STATUS = 'ENROLLED'  THEN 1 ELSE 0 END) ENROLLED_CNT,
                   SUM(CASE WHEN E.SOURCE = 'ASSIGNED'  AND E.STATUS <> 'REJECTED' THEN 1 ELSE 0 END) ASSIGNED_CNT,
                   SUM(CASE WHEN E.SOURCE = 'SELF_REGISTER' AND E.STATUS <> 'REJECTED' THEN 1 ELSE 0 END) SELF_CNT,
                   SUM(CASE WHEN E.STATUS = 'DROPPED'   THEN 1 ELSE 0 END) DROPPED_CNT,
                   SUM(CASE WHEN E.STATUS = 'COMPLETED' THEN 1 ELSE 0 END) COMPLETED_CNT,
                   SUM(CASE WHEN E.STATUS = 'FAILED'    THEN 1 ELSE 0 END) FAILED_CNT,
                   SUM(CASE WHEN E.IS_CERTIFIED = 1     THEN 1 ELSE 0 END) CERT_CNT,
                   AVG(E.ATTENDANCE_PERCENT) AVG_ATT,
                   AVG(E.FINAL_SCORE) AVG_SC
              FROM HRMS.HR_TRAINING_CLASS CL
              LEFT JOIN HRMS.HR_TRAINING_ENROLLMENT E ON E.CLASS_ID = CL.ID
             WHERE CL.COURSE_ID = :CID
             GROUP BY CL.ID, CL.CLASS_NAME, CL.STATUS
             ORDER BY CL.CLASS_NAME",
            r => new ReportCourseClassRow
            {
                CLASS_ID              = Convert.ToInt32(r["CLASS_ID"]),
                CLASS_NAME            = r["CLASS_NAME"]?.ToString() ?? "",
                CLASS_STATUS          = r["STATUS"]?.ToString(),
                ENROLLED_COUNT        = r["ENROLLED_CNT"]  is DBNull ? 0 : Convert.ToInt32(r["ENROLLED_CNT"]),
                ASSIGNED_COUNT        = r["ASSIGNED_CNT"]  is DBNull ? 0 : Convert.ToInt32(r["ASSIGNED_CNT"]),
                SELF_REGISTER_COUNT   = r["SELF_CNT"]      is DBNull ? 0 : Convert.ToInt32(r["SELF_CNT"]),
                DROPPED_COUNT         = r["DROPPED_CNT"]   is DBNull ? 0 : Convert.ToInt32(r["DROPPED_CNT"]),
                COMPLETED_COUNT       = r["COMPLETED_CNT"] is DBNull ? 0 : Convert.ToInt32(r["COMPLETED_CNT"]),
                FAILED_COUNT          = r["FAILED_CNT"]    is DBNull ? 0 : Convert.ToInt32(r["FAILED_CNT"]),
                CERTIFIED_COUNT       = r["CERT_CNT"]      is DBNull ? 0 : Convert.ToInt32(r["CERT_CNT"]),
                AVG_ATTENDANCE_PERCENT= r["AVG_ATT"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_ATT"]),
                AVG_FINAL_SCORE       = r["AVG_SC"]  is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_SC"]),
            }, new OracleParameter("CID", courseId));

        // Tổng cộng cả khóa — tính lại AVG trên TOÀN BỘ enrollment của khóa (không phải trung bình
        // cộng các AVG từng lớp, vì các lớp có thể lệch sĩ số nên trung bình-của-trung-bình sẽ sai).
        var total = (await _db.ExecuteQueryAsync(@"
            SELECT
                SUM(CASE WHEN E.STATUS = 'ENROLLED'  THEN 1 ELSE 0 END) ENROLLED_CNT,
                SUM(CASE WHEN E.SOURCE = 'ASSIGNED'  AND E.STATUS <> 'REJECTED' THEN 1 ELSE 0 END) ASSIGNED_CNT,
                SUM(CASE WHEN E.SOURCE = 'SELF_REGISTER' AND E.STATUS <> 'REJECTED' THEN 1 ELSE 0 END) SELF_CNT,
                SUM(CASE WHEN E.STATUS = 'DROPPED'   THEN 1 ELSE 0 END) DROPPED_CNT,
                SUM(CASE WHEN E.STATUS = 'COMPLETED' THEN 1 ELSE 0 END) COMPLETED_CNT,
                SUM(CASE WHEN E.STATUS = 'FAILED'    THEN 1 ELSE 0 END) FAILED_CNT,
                SUM(CASE WHEN E.IS_CERTIFIED = 1     THEN 1 ELSE 0 END) CERT_CNT,
                AVG(E.ATTENDANCE_PERCENT) AVG_ATT,
                AVG(E.FINAL_SCORE) AVG_SC
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = E.CLASS_ID
             WHERE CL.COURSE_ID = :CID",
            r => new ReportCourseClassRow
            {
                CLASS_ID              = null,
                CLASS_NAME            = "TỔNG CỘNG",
                CLASS_STATUS          = null,
                ENROLLED_COUNT        = r["ENROLLED_CNT"]  is DBNull ? 0 : Convert.ToInt32(r["ENROLLED_CNT"]),
                ASSIGNED_COUNT        = r["ASSIGNED_CNT"]  is DBNull ? 0 : Convert.ToInt32(r["ASSIGNED_CNT"]),
                SELF_REGISTER_COUNT   = r["SELF_CNT"]      is DBNull ? 0 : Convert.ToInt32(r["SELF_CNT"]),
                DROPPED_COUNT         = r["DROPPED_CNT"]   is DBNull ? 0 : Convert.ToInt32(r["DROPPED_CNT"]),
                COMPLETED_COUNT       = r["COMPLETED_CNT"] is DBNull ? 0 : Convert.ToInt32(r["COMPLETED_CNT"]),
                FAILED_COUNT          = r["FAILED_CNT"]    is DBNull ? 0 : Convert.ToInt32(r["FAILED_CNT"]),
                CERTIFIED_COUNT       = r["CERT_CNT"]      is DBNull ? 0 : Convert.ToInt32(r["CERT_CNT"]),
                AVG_ATTENDANCE_PERCENT= r["AVG_ATT"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_ATT"]),
                AVG_FINAL_SCORE       = r["AVG_SC"]  is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_SC"]),
            }, new OracleParameter("CID", courseId))).First();

        return new ReportCourseModel
        {
            COURSE_ID    = meta.ID,
            COURSE_TITLE = meta.TITLE,
            CLASSES      = classes,
            TOTAL        = total,
        };
    }

    // Histogram điểm final test (§14.1). Chia theo % của MAX_SCORE — 5 buckets 20% mỗi bucket.
    private async Task<List<ScoreBucket>> GetHistogramAsync(int classId)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT A.SCORE, A.MAX_SCORE
              FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
              JOIN HRMS.HR_TRAINING_TEST T ON T.ID = A.TEST_ID
              JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = T.CLASS_ID
             WHERE CL.ID = :CID
               AND CL.FINAL_TEST_ID = T.ID
               AND A.IS_GRADED = 1
               AND A.SCORE IS NOT NULL",
            r => new
            {
                SC = Convert.ToDecimal(r["SCORE"]),
                MX = r["MAX_SCORE"] is DBNull ? 0m : Convert.ToDecimal(r["MAX_SCORE"]),
            }, new OracleParameter("CID", classId));

        var buckets = new int[5];   // 0-2, 2-4, 4-6, 6-8, 8-10 (scaled to 10)
        foreach (var r in rows)
        {
            var pct = r.MX > 0 ? (r.SC / r.MX) * 10m : 0m;
            var idx = pct >= 10m ? 4 : (int)Math.Floor((double)pct / 2);
            if (idx < 0) idx = 0;
            if (idx > 4) idx = 4;
            buckets[idx]++;
        }

        var labels = new[] { "0-2", "2-4", "4-6", "6-8", "8-10" };
        return labels.Select((l, i) => new ScoreBucket { LABEL = l, COUNT = buckets[i] }).ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    //  §14.2 REPORT ATTENDANCE — matrix EMPCD × Session (group filtered)
    // ═══════════════════════════════════════════════════════════════

    public async Task<ReportAttendanceMatrix> GetAttendanceMatrixAsync(int classId)
    {
        var sessions = await _db.ExecuteQueryAsync(@"
            SELECT S.ID, S.SESSION_NO, S.SESSION_DATE, S.TOPIC, S.STATUS,
                   S.GROUP_ID, G.GROUP_NAME
              FROM HRMS.HR_TRAINING_SESSION S
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = S.GROUP_ID
             WHERE S.CLASS_ID = :CID
             ORDER BY S.SESSION_DATE, S.START_TIME",
            r => new AttendanceMatrixSession
            {
                SESSION_ID     = Convert.ToInt32(r["ID"]),
                SESSION_NO     = Convert.ToInt32(r["SESSION_NO"]),
                SESSION_DATE   = Convert.ToDateTime(r["SESSION_DATE"]),
                TOPIC          = r["TOPIC"] as string,
                SESSION_STATUS = r["STATUS"]?.ToString() ?? "",
                GROUP_ID       = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
                GROUP_NAME     = r["GROUP_NAME"] as string,
            }, new OracleParameter("CID", classId));

        var studentRows = await _db.ExecuteQueryAsync(@"
            SELECT E.EMPCD, EC.CNAME EMP_NAME, E.GROUP_ID, G.GROUP_NAME, E.ATTENDANCE_PERCENT
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = E.GROUP_ID
             WHERE E.CLASS_ID = :CID
               AND E.STATUS IN ('ENROLLED','COMPLETED','FAILED','DROPPED')
             ORDER BY E.EMPCD",
            r => new
            {
                EMPCD   = r["EMPCD"]?.ToString() ?? "",
                NAME    = r["EMP_NAME"] as string,
                GID     = r["GROUP_ID"] is DBNull ? (int?)null : Convert.ToInt32(r["GROUP_ID"]),
                GNAME   = r["GROUP_NAME"] as string,
                ATT_PCT = r["ATTENDANCE_PERCENT"] is DBNull ? 0m : Convert.ToDecimal(r["ATTENDANCE_PERCENT"]),
            }, new OracleParameter("CID", classId));

        // Fetch all attendance rows once (avoid N+1)
        var attendances = await _db.ExecuteQueryAsync(@"
            SELECT A.SESSION_ID, A.EMPCD, A.STATUS
              FROM HRMS.HR_TRAINING_ATTENDANCE A
              JOIN HRMS.HR_TRAINING_SESSION S ON S.ID = A.SESSION_ID
             WHERE S.CLASS_ID = :CID",
            r => new
            {
                SID   = Convert.ToInt32(r["SESSION_ID"]),
                EMPCD = r["EMPCD"]?.ToString() ?? "",
                ST    = r["STATUS"]?.ToString() ?? "",
            }, new OracleParameter("CID", classId));
        var byKey = attendances.ToDictionary(a => (a.SID, a.EMPCD), a => a.ST);

        var students = studentRows.Select(s => {
            var m = new AttendanceMatrixStudent
            {
                EMPCD              = s.EMPCD,
                EMP_NAME           = s.NAME,
                GROUP_ID           = s.GID,
                GROUP_NAME         = s.GNAME,
                ATTENDANCE_PERCENT = s.ATT_PCT,
            };
            foreach (var sess in sessions)
            {
                // Session không thuộc scope học viên (khác group + session không phải global) → ""
                if (sess.GROUP_ID.HasValue && sess.GROUP_ID != s.GID)
                {
                    m.STATUS_PER_SESSION[sess.SESSION_ID] = "";
                    continue;
                }
                m.STATUS_PER_SESSION[sess.SESSION_ID] =
                    byKey.TryGetValue((sess.SESSION_ID, s.EMPCD), out var st) ? st : "";
            }
            return m;
        }).ToList();

        return new ReportAttendanceMatrix { SESSIONS = sessions, STUDENTS = students };
    }

    // ═══════════════════════════════════════════════════════════════
    //  §14.3 REPORT TEST — scores + top 5 wrong questions
    // ═══════════════════════════════════════════════════════════════

    public async Task<ReportTestModel?> GetTestReportAsync(int testId)
    {
        var meta = (await _db.ExecuteQueryAsync(@"
            SELECT ID, TITLE, PASS_SCORE FROM HRMS.HR_TRAINING_TEST WHERE ID = :ID",
            r => new
            {
                ID    = Convert.ToInt32(r["ID"]),
                TITLE = r["TITLE"]?.ToString() ?? "",
                PASS  = r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
            }, new OracleParameter("ID", testId))).FirstOrDefault();
        if (meta == null) return null;

        var attempts = await _db.ExecuteQueryAsync(@"
            SELECT A.EMPCD, EC.CNAME EMP_NAME, A.ATTEMPT_NO, A.SCORE, A.MAX_SCORE, A.IS_PASS, A.STATUS, A.SUBMIT_DT,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_RETAKE_GRANT G
                     WHERE G.TEST_ID = A.TEST_ID AND G.EMPCD = A.EMPCD AND G.STATUS = 'PENDING') AS PENDING_GRANT_CNT
              FROM HRMS.HR_TRAINING_TEST_ATTEMPT A
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = A.EMPCD
             WHERE A.TEST_ID = :TID
             ORDER BY A.EMPCD, A.ATTEMPT_NO",
            r => new TestScoreItem
            {
                EMPCD      = r["EMPCD"]?.ToString() ?? "",
                EMP_NAME   = r["EMP_NAME"] as string,
                ATTEMPT_NO = Convert.ToInt32(r["ATTEMPT_NO"]),
                SCORE      = r["SCORE"]     is DBNull ? null : Convert.ToDecimal(r["SCORE"]),
                MAX_SCORE  = r["MAX_SCORE"] is DBNull ? null : Convert.ToDecimal(r["MAX_SCORE"]),
                IS_PASS    = r["IS_PASS"]   is DBNull ? null : Convert.ToInt32(r["IS_PASS"]),
                STATUS     = r["STATUS"]?.ToString() ?? "",
                SUBMIT_DT  = r["SUBMIT_DT"] as DateTime?,
                HAS_PENDING_GRANT = Convert.ToInt32(r["PENDING_GRANT_CNT"]) > 0,
            }, new OracleParameter("TID", testId));

        // Top 5 câu sai nhiều nhất — chỉ tính auto-grade (SINGLE/MULTI/YESNO/DROPDOWN)
        // Sai = POINTS_AWARDED = 0 (chấm auto set 0 nếu wrong).
        var topWrong = await _db.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT Q.ID, Q.QUESTION_TEXT, Q.QUESTION_TYPE,
                       COUNT(*) ATT_CNT,
                       SUM(CASE WHEN NVL(AN.POINTS_AWARDED,0) = 0 THEN 1 ELSE 0 END) WRONG_CNT
                  FROM HRMS.HR_TRAINING_TEST_QUESTION Q
                  JOIN HRMS.HR_TRAINING_TEST_ANSWER AN ON AN.QUESTION_ID = Q.ID
                 WHERE Q.TEST_ID = :TID
                   AND Q.QUESTION_TYPE <> 'TEXT'
                 GROUP BY Q.ID, Q.QUESTION_TEXT, Q.QUESTION_TYPE
                HAVING COUNT(*) > 0
                 ORDER BY WRONG_CNT DESC, ATT_CNT DESC
            ) WHERE ROWNUM <= 5",
            r => new TestWrongItem
            {
                QUESTION_ID   = Convert.ToInt32(r["ID"]),
                QUESTION_TEXT = r["QUESTION_TEXT"]?.ToString() ?? "",
                QUESTION_TYPE = r["QUESTION_TYPE"]?.ToString() ?? "",
                ATTEMPT_COUNT = Convert.ToInt32(r["ATT_CNT"]),
                WRONG_COUNT   = Convert.ToInt32(r["WRONG_CNT"]),
                WRONG_PERCENT = Math.Round(100m * Convert.ToInt32(r["WRONG_CNT"]) / Convert.ToDecimal(r["ATT_CNT"]), 2),
            }, new OracleParameter("TID", testId));

        var scores = attempts.Where(a => a.SCORE.HasValue).Select(a => a.SCORE!.Value).ToList();
        // Đếm ĐẬU/RỚT theo học viên duy nhất (không đếm trùng khi có nhiều lần thi do được cấp
        // thi lại) — đậu nếu có ÍT NHẤT 1 lần thi IS_PASS=1 (điểm cao nhất tính).
        var byStudent = attempts.GroupBy(a => a.EMPCD).ToList();
        return new ReportTestModel
        {
            TEST_ID      = meta.ID,
            TEST_TITLE   = meta.TITLE,
            PASS_SCORE   = meta.PASS,
            ATTEMPT_COUNT= attempts.Count,
            PASS_COUNT   = byStudent.Count(g => g.Any(a => a.IS_PASS == 1)),
            FAIL_COUNT   = byStudent.Count(g => g.All(a => a.IS_PASS != 1) && g.Any(a => a.IS_PASS != null)),
            AVG_SCORE    = scores.Count > 0 ? Math.Round(scores.Average(), 2) : null,
            MAX_SCORE    = scores.Count > 0 ? scores.Max() : null,
            MIN_SCORE    = scores.Count > 0 ? scores.Min() : null,
            SCORES       = attempts,
            TOP_WRONG_QUESTIONS = topWrong,
        };
    }
}
