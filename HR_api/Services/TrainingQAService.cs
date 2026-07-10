using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Q&A (§9): student ask, teacher answer, HR/teacher soft-delete.
public class TrainingQAService
{
    private readonly OracleService _db;

    public TrainingQAService(OracleService db) { _db = db; }

    public async Task<List<QuestionModel>> ListByClassAsync(int classId, bool includeDeleted = false)
    {
        var sql = @"
            SELECT Q.ID, Q.CLASS_ID, Q.ASKED_BY, EC1.CNAME AS ASKED_BY_NAME,
                   Q.QUESTION_TEXT, Q.ASKED_DT,
                   Q.ANSWERED_BY, EC2.CNAME AS ANSWERED_BY_NAME,
                   Q.ANSWER_TEXT, Q.ANSWERED_DT,
                   Q.IS_DELETED
              FROM HRMS.HR_TRAINING_QUESTION Q
              LEFT JOIN HRMS.ECM100 EC1 ON EC1.EMPCD = Q.ASKED_BY
              LEFT JOIN HRMS.ECM100 EC2 ON EC2.EMPCD = Q.ANSWERED_BY
             WHERE Q.CLASS_ID = :CID
               " + (includeDeleted ? "" : "AND Q.IS_DELETED = 0") + @"
             ORDER BY Q.ASKED_DT DESC";
        return await _db.ExecuteQueryAsync(sql, r => new QuestionModel
        {
            ID               = Convert.ToInt32(r["ID"]),
            CLASS_ID         = Convert.ToInt32(r["CLASS_ID"]),
            ASKED_BY         = r["ASKED_BY"]?.ToString() ?? "",
            ASKED_BY_NAME    = r["ASKED_BY_NAME"] as string,
            QUESTION_TEXT    = r["QUESTION_TEXT"]?.ToString() ?? "",
            ASKED_DT         = Convert.ToDateTime(r["ASKED_DT"]),
            ANSWERED_BY      = r["ANSWERED_BY"] as string,
            ANSWERED_BY_NAME = r["ANSWERED_BY_NAME"] as string,
            ANSWER_TEXT      = r["ANSWER_TEXT"] as string,
            ANSWERED_DT      = r["ANSWERED_DT"] as DateTime?,
            IS_DELETED       = Convert.ToInt32(r["IS_DELETED"]),
        }, new OracleParameter("CID", classId));
    }

    public async Task<int> AskAsync(AskQuestionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.QUESTION_TEXT))
            throw new InvalidOperationException("Nội dung câu hỏi không được để trống");
        if (req.QUESTION_TEXT.Length > 2000)
            throw new InvalidOperationException("Câu hỏi tối đa 2000 ký tự");

        // Verify user thuộc Class (enrolled hoặc teacher)
        var okStudent = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND EMPCD = :EMP AND STATUS = 'ENROLLED'",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD))).First();
        var okTeacher = (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_CLASS_TEACHER WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD))).First();
        if (okStudent == 0 && okTeacher == 0)
            throw new InvalidOperationException("Bạn không thuộc lớp học này");

        const string sqlIns = @"
            INSERT INTO HRMS.HR_TRAINING_QUESTION
                (CLASS_ID, ASKED_BY, QUESTION_TEXT)
            VALUES (:CID, :EMP, :Q)
            RETURNING ID INTO :NEW_ID";
        var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
        {
            Direction = System.Data.ParameterDirection.Output
        };
        await _db.ExecuteNonQueryAsync(sqlIns,
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD),
            new OracleParameter("Q",   req.QUESTION_TEXT),
            idParam);
        return Convert.ToInt32(idParam.Value);
    }

    public async Task AnswerAsync(AnswerQuestionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ANSWER_TEXT))
            throw new InvalidOperationException("Nội dung câu trả lời không được để trống");
        if (req.ANSWER_TEXT.Length > 2000)
            throw new InvalidOperationException("Câu trả lời tối đa 2000 ký tự");

        // Verify user là teacher của Class (từ HR_TRAINING_CLASS_TEACHER)
        var okTeacher = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT
              FROM HRMS.HR_TRAINING_CLASS_TEACHER T
              JOIN HRMS.HR_TRAINING_QUESTION Q ON Q.CLASS_ID = T.CLASS_ID
             WHERE Q.ID = :QID AND T.EMPCD = :EMP",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("QID", req.ID),
            new OracleParameter("EMP", req.LOGIN_USER))).First();
        if (okTeacher == 0)
            throw new InvalidOperationException("Bạn không phải teacher của lớp này");

        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_QUESTION
               SET ANSWER_TEXT = :A, ANSWERED_BY = :EMP, ANSWERED_DT = SYSDATE
             WHERE ID = :ID AND IS_DELETED = 0",
            new OracleParameter("A",   req.ANSWER_TEXT),
            new OracleParameter("EMP", req.LOGIN_USER),
            new OracleParameter("ID",  req.ID));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy câu hỏi (đã xoá?)");
    }

    // Soft delete — dùng cho câu hỏi spam / off-topic. Chỉ teacher + HR làm được.
    public async Task DeleteAsync(DeleteQuestionRequest req)
    {
        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_QUESTION
               SET IS_DELETED = 1
             WHERE ID = :ID",
            new OracleParameter("ID", req.ID));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy câu hỏi");
    }
}
