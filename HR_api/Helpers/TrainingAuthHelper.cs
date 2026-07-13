using HR_api.Data;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;

public class TrainingAuthHelper
{
    private readonly OracleService _db;
    private readonly IMemoryCache  _cache;

    public TrainingAuthHelper(OracleService db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // Check user là student của class không
    public async Task<bool> IsStudentAsync(string empcd, int classId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_ENROLLMENT
             WHERE CLASS_ID = :CLASS_ID
               AND EMPCD = :EMPCD
               AND STATUS IN ('ENROLLED', 'COMPLETED', 'FAILED')
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("CLASS_ID", classId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là student của test không
    public async Task<bool> IsStudentOfTestAsync(string empcd, int testId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_ENROLLMENT E
              JOIN HRMS.HR_TRAINING_TEST T ON T.CLASS_ID = E.CLASS_ID
             WHERE T.ID = :TEST_ID
               AND E.EMPCD = :EMPCD
               AND E.STATUS IN ('ENROLLED', 'COMPLETED', 'FAILED')
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("TEST_ID", testId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user sở hữu attempt không
    public async Task<bool> IsOwnerOfAttemptAsync(string empcd, int attemptId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_TEST_ATTEMPT
             WHERE ID = :ATTEMPT_ID
               AND EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("ATTEMPT_ID", attemptId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là teacher của class cụ thể (per-class)
    public async Task<bool> IsTeacherAsync(string empcd, int classId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER
             WHERE CLASS_ID = :CLASS_ID
               AND EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("CLASS_ID", classId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là teacher của test không (test thuộc class user teach, HOẶC user là người tạo test)
    public async Task<bool> IsTeacherOfTestAsync(string empcd, int testId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_TEST T
             WHERE T.ID = :TEST_ID
               AND (
                     T.CREATED_BY = :EMPCD1
                     OR EXISTS (
                         SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
                          WHERE CT.CLASS_ID = T.CLASS_ID
                            AND CT.EMPCD = :EMPCD2
                     )
                   )
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("TEST_ID", testId),
            new OracleParameter("EMPCD1", empcd),
            new OracleParameter("EMPCD2", empcd));
        return rows.Any();
    }

    // Check user là teacher của session không
    public async Task<bool> IsTeacherOfSessionAsync(string empcd, int sessionId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
              JOIN HRMS.HR_TRAINING_SESSION S ON S.CLASS_ID = CT.CLASS_ID
             WHERE S.ID = :SESSION_ID
               AND CT.EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("SESSION_ID", sessionId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là teacher của QA question không
    public async Task<bool> IsTeacherOfQuestionAsync(string empcd, int questionId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
              JOIN HRMS.HR_TRAINING_QUESTION Q ON Q.CLASS_ID = CT.CLASS_ID
             WHERE Q.ID = :QUESTION_ID
               AND CT.EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("QUESTION_ID", questionId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là teacher của material không
    public async Task<bool> IsTeacherOfMaterialAsync(string empcd, int materialId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
              JOIN HRMS.HR_TRAINING_MATERIAL M ON M.CLASS_ID = CT.CLASS_ID
             WHERE M.ID = :MATERIAL_ID
               AND CT.EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("MATERIAL_ID", materialId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là teacher của attempt không (test của attempt thuộc lớp teach)
    public async Task<bool> IsTeacherOfAttemptAsync(string empcd, int attemptId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
              JOIN HRMS.HR_TRAINING_TEST T ON T.CLASS_ID = CT.CLASS_ID
              JOIN HRMS.HR_TRAINING_TEST_ATTEMPT A ON A.TEST_ID = T.ID
             WHERE A.ID = :ATTEMPT_ID
               AND CT.EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("ATTEMPT_ID", attemptId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là teacher của answer không
    public async Task<bool> IsTeacherOfAnswerAsync(string empcd, int answerId)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER CT
              JOIN HRMS.HR_TRAINING_TEST_ANSWER A ON A.ID = :ANSWER_ID
              JOIN HRMS.HR_TRAINING_TEST_ATTEMPT AT ON AT.ID = A.ATTEMPT_ID
              JOIN HRMS.HR_TRAINING_TEST T ON T.ID = AT.TEST_ID
             WHERE T.CLASS_ID = CT.CLASS_ID
               AND CT.EMPCD = :EMPCD
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("ANSWER_ID", answerId),
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user là HR/Admin (truy vấn từ database)
    public async Task<bool> IsHrOrAdminAsync(string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_USERS U
              JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
             WHERE U.EMPCD = :EMPCD
               AND RR.ROLE_NAME IN ('HR', 'Admin')
               AND ROWNUM = 1",
            r => 1,
            new OracleParameter("EMPCD", empcd));
        return rows.Any();
    }

    // Check user hiện đang là teacher của BẤT KỲ Class active nào
    // → dùng cho SideMenuBuilder + trang cá nhân. Cache 5 phút / EMPCD.
    public async Task<bool> IsActiveTeacherAsync(string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return false;
        var cacheKey = "training_active_teacher_" + empcd;
        if (_cache.TryGetValue<bool>(cacheKey, out var cached)) return cached;

        var rows = await _db.ExecuteQueryAsync(@"
            SELECT 1 FROM HRMS.HR_TRAINING_CLASS_TEACHER T
              JOIN HRMS.HR_TRAINING_CLASS C ON C.ID = T.CLASS_ID
             WHERE T.EMPCD = :EMPCD
               AND (
                    C.STATUS IN ('SCHEDULED','IN_PROGRESS')
                 OR EXISTS (
                        SELECT 1 FROM HRMS.HR_TRAINING_TEST TT
                          JOIN HRMS.HR_TRAINING_TEST_ATTEMPT TA ON TA.TEST_ID = TT.ID
                         WHERE TT.CLASS_ID = C.ID
                           AND TA.STATUS = 'SUBMITTED'
                           AND TA.IS_GRADED = 0
                    )
               )
               AND ROWNUM = 1",
            r => 1, new OracleParameter("EMPCD", empcd));
        var isTeacher = rows.Any();

        _cache.Set(cacheKey, isTeacher, TimeSpan.FromMinutes(5));
        return isTeacher;
    }

    // Invalidate cache khi HR assign / remove teacher
    public void InvalidateActiveTeacher(string empcd)
    {
        if (!string.IsNullOrWhiteSpace(empcd))
        {
            _cache.Remove("training_active_teacher_" + empcd);
        }
    }
}
