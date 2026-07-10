using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Review post-class (§10).
// V1: OPTIONAL — Class.REQUIRE_POST_REVIEW default 0. HR set 1 riêng lớp nào cần.
// V1: KHÔNG anonymous — feedback lưu thô, hiển thị cả tên. V2 mới tách quyền view.
public class TrainingReviewService
{
    private readonly OracleService _db;

    public TrainingReviewService(OracleService db) { _db = db; }

    // Student submit review — atomic: xoá cũ + insert lại (v1 cho phép edit đến hết class).
    // Chỉ chấp nhận khi Class ENROLLMENT của student = 'ENROLLED' hoặc terminal (COMPLETED/FAILED).
    public async Task<(bool ok, string? error)> SubmitAsync(SubmitReviewRequest req)
    {
        if (req.CONTENT_RATING < 1 || req.CONTENT_RATING > 5)
            return (false, "CONTENT_RATING phải 1..5");
        if (req.ORGANIZATION_RATING < 1 || req.ORGANIZATION_RATING > 5)
            return (false, "ORGANIZATION_RATING phải 1..5");
        foreach (var t in req.TEACHER_RATINGS ?? new())
        {
            if (t.RATING < 1 || t.RATING > 5) return (false, $"Teacher {t.TEACHER_EMPCD}: RATING phải 1..5");
            if (string.IsNullOrWhiteSpace(t.TEACHER_EMPCD)) return (false, "TEACHER_EMPCD trống");
        }

        // Verify student thuộc Class
        var okEnroll = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
             WHERE CLASS_ID = :CID AND EMPCD = :EMP
               AND STATUS IN ('ENROLLED','COMPLETED','FAILED')",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD))).First();
        if (okEnroll == 0) return (false, "Bạn không thuộc lớp này");

        // Xoá cũ (edit case) + insert mới
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_REVIEW WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_REVIEW_TEACHER WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));

        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_REVIEW
                (CLASS_ID, EMPCD, CONTENT_RATING, ORGANIZATION_RATING, FEEDBACK_TEXT)
            VALUES (:CID, :EMP, :C, :O, :FB)",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD),
            new OracleParameter("C",   req.CONTENT_RATING),
            new OracleParameter("O",   req.ORGANIZATION_RATING),
            new OracleParameter("FB",  (object?)req.FEEDBACK_TEXT ?? DBNull.Value));

        foreach (var t in req.TEACHER_RATINGS ?? new())
        {
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_REVIEW_TEACHER
                    (CLASS_ID, EMPCD, TEACHER_EMPCD, RATING, FEEDBACK_TEXT)
                VALUES (:CID, :EMP, :TEACH, :R, :FB)",
                new OracleParameter("CID",   req.CLASS_ID),
                new OracleParameter("EMP",   req.EMPCD),
                new OracleParameter("TEACH", t.TEACHER_EMPCD),
                new OracleParameter("R",     t.RATING),
                new OracleParameter("FB",    (object?)t.FEEDBACK_TEXT ?? DBNull.Value));
        }
        return (true, null);
    }

    // Check student đã submit review chưa (dùng cho completion criteria §7)
    public async Task<bool> HasSubmittedAsync(int classId, string empcd)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_REVIEW WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd))).First() > 0;
    }

    // Student view review của chính mình (edit case)
    public async Task<ReviewModel?> GetMyReviewAsync(int classId, string empcd)
    {
        var review = (await _db.ExecuteQueryAsync(@"
            SELECT R.CLASS_ID, R.EMPCD, EC.CNAME EMP_NAME,
                   R.CONTENT_RATING, R.ORGANIZATION_RATING, R.FEEDBACK_TEXT, R.INST_DT
              FROM HRMS.HR_TRAINING_REVIEW R
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = R.EMPCD
             WHERE R.CLASS_ID = :CID AND R.EMPCD = :EMP",
            MapReview,
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd))).FirstOrDefault();
        if (review == null) return null;

        review.TEACHER_RATINGS = await _db.ExecuteQueryAsync(@"
            SELECT RT.CLASS_ID, RT.EMPCD, RT.TEACHER_EMPCD, EC.CNAME TEACHER_NAME,
                   RT.RATING, RT.FEEDBACK_TEXT
              FROM HRMS.HR_TRAINING_REVIEW_TEACHER RT
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = RT.TEACHER_EMPCD
             WHERE RT.CLASS_ID = :CID AND RT.EMPCD = :EMP",
            MapTeacherRating,
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd));
        return review;
    }

    // Report §14.4 — HR/Teacher xem aggregate (Anonymous v1 SKIP — hiển thị tên).
    // Teacher chỉ xem lớp mình dạy (filter ở endpoint layer khi cần).
    public async Task<ReviewReportModel> GetReportAsync(int classId)
    {
        var summary = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) N,
                   AVG(CONTENT_RATING)      AVG_C,
                   AVG(ORGANIZATION_RATING) AVG_O
              FROM HRMS.HR_TRAINING_REVIEW
             WHERE CLASS_ID = :CID",
            r => new
            {
                N   = Convert.ToInt32(r["N"]),
                AVG_C = r["AVG_C"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_C"]),
                AVG_O = r["AVG_O"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["AVG_O"]),
            }, new OracleParameter("CID", classId))).First();

        var teacherAgg = await _db.ExecuteQueryAsync(@"
            SELECT RT.TEACHER_EMPCD, EC.CNAME TEACHER_NAME,
                   AVG(RT.RATING) AVG_R, COUNT(*) N
              FROM HRMS.HR_TRAINING_REVIEW_TEACHER RT
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = RT.TEACHER_EMPCD
             WHERE RT.CLASS_ID = :CID
             GROUP BY RT.TEACHER_EMPCD, EC.CNAME
             ORDER BY AVG_R DESC",
            r => new TeacherAggregateModel
            {
                TEACHER_EMPCD = r["TEACHER_EMPCD"]?.ToString() ?? "",
                TEACHER_NAME  = r["TEACHER_NAME"] as string,
                AVG_RATING    = Convert.ToDecimal(r["AVG_R"]),
                COUNT         = Convert.ToInt32(r["N"]),
            }, new OracleParameter("CID", classId));

        // Feedback text list — paginate top 100 nếu quá nhiều
        var feedback = await _db.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT FEEDBACK_TEXT FROM HRMS.HR_TRAINING_REVIEW
                 WHERE CLASS_ID = :CID AND FEEDBACK_TEXT IS NOT NULL
                 ORDER BY INST_DT DESC
            ) WHERE ROWNUM <= 100",
            r => r["FEEDBACK_TEXT"]?.ToString() ?? "",
            new OracleParameter("CID", classId));

        return new ReviewReportModel
        {
            CLASS_ID           = classId,
            RESPONSE_COUNT     = summary.N,
            AVG_CONTENT        = summary.AVG_C,
            AVG_ORGANIZATION   = summary.AVG_O,
            TEACHER_AGGREGATES = teacherAgg,
            FEEDBACK_LIST      = feedback,
        };
    }

    private static ReviewModel MapReview(OracleDataReader r) => new()
    {
        CLASS_ID            = Convert.ToInt32(r["CLASS_ID"]),
        EMPCD               = r["EMPCD"]?.ToString() ?? "",
        EMP_NAME            = r["EMP_NAME"] as string,
        CONTENT_RATING      = Convert.ToInt32(r["CONTENT_RATING"]),
        ORGANIZATION_RATING = Convert.ToInt32(r["ORGANIZATION_RATING"]),
        FEEDBACK_TEXT       = r["FEEDBACK_TEXT"] as string,
        INST_DT             = Convert.ToDateTime(r["INST_DT"]),
    };

    private static TeacherRatingModel MapTeacherRating(OracleDataReader r) => new()
    {
        CLASS_ID      = Convert.ToInt32(r["CLASS_ID"]),
        EMPCD         = r["EMPCD"]?.ToString() ?? "",
        TEACHER_EMPCD = r["TEACHER_EMPCD"]?.ToString() ?? "",
        TEACHER_NAME  = r["TEACHER_NAME"] as string,
        RATING        = Convert.ToInt32(r["RATING"]),
        FEEDBACK_TEXT = r["FEEDBACK_TEXT"] as string,
    };
}
