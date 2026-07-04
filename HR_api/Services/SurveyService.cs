using HR_api.Data;
using HR_api.Models.Survey;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.Data;

namespace HR_api.Services;

// User-facing: xem survey pending, load detail (không lộ IS_CORRECT), auto-save từng câu,
// submit chốt điểm, skip nếu mù chữ.
public class SurveyService
{
    private readonly OracleService _db;
    private readonly SurveyRecipientService _recipient;
    private readonly SurveyScoreService _score;

    public SurveyService(OracleService db, SurveyRecipientService recipient, SurveyScoreService score)
    {
        _db = db;
        _recipient = recipient;
        _score = score;
    }

    // ═══════════════════════════════════════════════════════════════
    //  PENDING
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<SurveyModel>> GetPendingAsync(string empcd)
    {
        const string sql = @"
            SELECT S.ID, S.TITLE, S.DESCRIPTION, S.SURVEY_TYPE, S.LANG, S.STATUS,
                   S.START_DATE, S.END_DATE, S.RECIPIENT_MODE, S.PASS_SCORE,
                   S.PUBLISHED_DT, S.PUBLISHED_BY,
                   S.INST_ID, S.INST_DT, S.UPDT_ID, S.UPDT_DT
              FROM HRMS.HR_SURVEY S
              JOIN HRMS.HR_SURVEY_RECIPIENT R ON R.SURVEY_ID = S.ID
             WHERE S.STATUS = 'ACTIVE'
               AND R.EMPCD = :EMPCD
               AND NOT EXISTS (
                    SELECT 1 FROM HRMS.HR_SURVEY_RESPONSE X
                     WHERE X.SURVEY_ID = S.ID
                       AND X.EMPCD = :EMPCD
                       AND X.STATUS IN ('SUBMITTED','AUTO_SUBMITTED','ILLITERATE_SKIP')
                )
             ORDER BY S.ID";

        return await _db.ExecuteQueryAsync(sql, MapSurveyLight,
            new OracleParameter("EMPCD", empcd));
    }

    // ═══════════════════════════════════════════════════════════════
    //  DETAIL cho user làm bài (KHÔNG trả IS_CORRECT)
    //  Kèm response IN_PROGRESS + answers đã save (nếu có).
    // ═══════════════════════════════════════════════════════════════

    public async Task<(SurveyModel? survey, SurveyResponseModel? response, string? error)> GetDetailForUserAsync(string empcd, int surveyId)
    {
        // Check user thuộc recipient
        if (!await _recipient.IsRecipientAsync(surveyId, empcd))
            return (null, null, "Bạn không thuộc phạm vi survey này");

        // Survey meta
        const string sqlS = @"
            SELECT ID, TITLE, DESCRIPTION, SURVEY_TYPE, LANG, STATUS,
                   START_DATE, END_DATE, RECIPIENT_MODE, PASS_SCORE,
                   PUBLISHED_DT, PUBLISHED_BY, INST_ID, INST_DT, UPDT_ID, UPDT_DT
              FROM HRMS.HR_SURVEY
             WHERE ID = :ID";
        var survey = (await _db.ExecuteQueryAsync(sqlS, MapSurveyLight,
            new OracleParameter("ID", surveyId))).FirstOrDefault();
        if (survey == null) return (null, null, "Không tìm thấy survey");
        if (survey.STATUS == "PAUSED")  return (survey, null, "Survey đang tạm dừng");
        if (survey.STATUS != "ACTIVE")  return (survey, null, "Survey không còn hiệu lực");

        // Nếu đã có response terminal → user vào lại link đã hoàn thành → chặn UX render
        var existingResp = await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_SURVEY_RESPONSE WHERE SURVEY_ID = :SID AND EMPCD = :EMP",
            r => r["STATUS"]?.ToString() ?? "",
            new OracleParameter("SID", surveyId),
            new OracleParameter("EMP", empcd));
        var respStatus = existingResp.FirstOrDefault();
        if (respStatus is "SUBMITTED" or "AUTO_SUBMITTED" or "ILLITERATE_SKIP")
            return (survey, null, "Bạn đã hoàn thành survey này");

        // Questions + options (KHÔNG trả IS_CORRECT cho user)
        const string sqlQ = @"
            SELECT ID, SURVEY_ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS
              FROM HRMS.HR_SURVEY_QUESTION
             WHERE SURVEY_ID = :SID
             ORDER BY DISPLAY_ORDER, ID";
        var questions = await _db.ExecuteQueryAsync(sqlQ, r => new SurveyQuestionModel
        {
            ID            = Convert.ToInt32(r["ID"]),
            SURVEY_ID     = Convert.ToInt32(r["SURVEY_ID"]),
            QUESTION_TEXT = r["QUESTION_TEXT"]?.ToString() ?? "",
            QUESTION_TYPE = r["QUESTION_TYPE"]?.ToString() ?? "SINGLE",
            IS_REQUIRED   = Convert.ToInt32(r["IS_REQUIRED"]),
            DISPLAY_ORDER = Convert.ToInt32(r["DISPLAY_ORDER"]),
            POINTS        = Convert.ToDecimal(r["POINTS"] is DBNull ? 0 : r["POINTS"]),
        }, new OracleParameter("SID", surveyId));

        const string sqlO = @"
            SELECT O.ID, O.QUESTION_ID, O.OPTION_TEXT, O.DISPLAY_ORDER
              FROM HRMS.HR_SURVEY_OPTION O
              JOIN HRMS.HR_SURVEY_QUESTION Q ON Q.ID = O.QUESTION_ID
             WHERE Q.SURVEY_ID = :SID
             ORDER BY O.QUESTION_ID, O.DISPLAY_ORDER, O.ID";
        var options = await _db.ExecuteQueryAsync(sqlO, r => new SurveyOptionModel
        {
            ID            = Convert.ToInt32(r["ID"]),
            QUESTION_ID   = Convert.ToInt32(r["QUESTION_ID"]),
            OPTION_TEXT   = r["OPTION_TEXT"]?.ToString() ?? "",
            DISPLAY_ORDER = Convert.ToInt32(r["DISPLAY_ORDER"]),
            IS_CORRECT    = null,   // ẩn khỏi user
        }, new OracleParameter("SID", surveyId));

        var byQ = options.GroupBy(o => o.QUESTION_ID).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var q in questions)
            if (byQ.TryGetValue(q.ID, out var opts)) q.OPTIONS = opts;
        survey.QUESTIONS = questions;

        // Response IN_PROGRESS (nếu có) + answers
        var response = await GetOrCreateInProgressAsync(surveyId, empcd, null, null);
        return (survey, response, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAVE ANSWER (upsert)
    // ═══════════════════════════════════════════════════════════════

    private const int MAX_TEXT_LENGTH = 1000;

    public async Task<(bool ok, string? error)> SaveAnswerAsync(SaveAnswerRequest req)
    {
        // Rules §3: TEXT free text, tối đa 1000 ký tự
        if (!string.IsNullOrEmpty(req.ANSWER_TEXT) && req.ANSWER_TEXT.Length > MAX_TEXT_LENGTH)
            return (false, $"Câu trả lời text vượt quá {MAX_TEXT_LENGTH} ký tự");

        // Verify recipient + survey ACTIVE
        if (!await _recipient.IsRecipientAsync(req.SURVEY_ID, req.EMPCD))
            return (false, "Không thuộc phạm vi survey");

        var status = await GetSurveyStatusAsync(req.SURVEY_ID);
        if (status != "ACTIVE") return (false, $"Survey không ở trạng thái ACTIVE ({status})");

        var response = await GetOrCreateInProgressAsync(req.SURVEY_ID, req.EMPCD, req.IP_ADDRESS, req.USER_AGENT);
        if (response == null) return (false, "Không thể tạo response");
        if (response.STATUS != "IN_PROGRESS")
            return (false, "Response đã submit, không thể sửa");

        // Upsert answer: nếu đã có row cho (RESPONSE_ID, QUESTION_ID) → UPDATE, không thì INSERT
        var existing = await _db.ExecuteQueryAsync(
            "SELECT ID FROM HRMS.HR_SURVEY_ANSWER WHERE RESPONSE_ID = :RID AND QUESTION_ID = :QID",
            r => Convert.ToInt32(r["ID"]),
            new OracleParameter("RID", response.ID),
            new OracleParameter("QID", req.QUESTION_ID));

        if (existing.Count > 0)
        {
            const string sqlU = @"
                UPDATE HRMS.HR_SURVEY_ANSWER
                   SET ANSWER_OPTION_IDS = :OPTS,
                       ANSWER_TEXT       = :TXT,
                       ANSWER_NUMBER     = :NUM
                 WHERE ID = :ID";
            await _db.ExecuteNonQueryAsync(sqlU,
                new OracleParameter("OPTS", (object?)req.ANSWER_OPTION_IDS ?? DBNull.Value),
                new OracleParameter("TXT",  OracleDbType.NClob) { Value = (object?)req.ANSWER_TEXT ?? DBNull.Value },
                new OracleParameter("NUM",  (object?)req.ANSWER_NUMBER ?? DBNull.Value),
                new OracleParameter("ID",   existing[0]));
        }
        else
        {
            const string sqlI = @"
                INSERT INTO HRMS.HR_SURVEY_ANSWER
                    (RESPONSE_ID, QUESTION_ID, ANSWER_OPTION_IDS, ANSWER_TEXT, ANSWER_NUMBER, INST_DT)
                VALUES
                    (:RID, :QID, :OPTS, :TXT, :NUM, SYSDATE)";
            await _db.ExecuteNonQueryAsync(sqlI,
                new OracleParameter("RID",  response.ID),
                new OracleParameter("QID",  req.QUESTION_ID),
                new OracleParameter("OPTS", (object?)req.ANSWER_OPTION_IDS ?? DBNull.Value),
                new OracleParameter("TXT",  OracleDbType.NClob) { Value = (object?)req.ANSWER_TEXT ?? DBNull.Value },
                new OracleParameter("NUM",  (object?)req.ANSWER_NUMBER ?? DBNull.Value));
        }

        return (true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SUBMIT
    // ═══════════════════════════════════════════════════════════════

    public async Task<(SurveySubmitResultModel result, string? error)> SubmitAsync(string empcd, int surveyId)
    {
        if (!await _recipient.IsRecipientAsync(surveyId, empcd))
            return (new SurveySubmitResultModel { SUCCESS = false }, "Không thuộc phạm vi survey");

        var status = await GetSurveyStatusAsync(surveyId);
        if (status != "ACTIVE")
            return (new SurveySubmitResultModel { SUCCESS = false }, $"Survey không ở trạng thái ACTIVE ({status})");

        var response = await GetOrCreateInProgressAsync(surveyId, empcd, null, null);
        if (response == null) return (new SurveySubmitResultModel { SUCCESS = false }, "Không tìm thấy response");
        if (response.STATUS != "IN_PROGRESS")
            return (new SurveySubmitResultModel { SUCCESS = false }, "Response đã submit");

        var (score, maxScore, isPass) = await _score.ComputeAsync(response.ID);

        // Survey type để trả về cho client
        var surveyType = (await _db.ExecuteQueryAsync(
            "SELECT SURVEY_TYPE FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => r["SURVEY_TYPE"]?.ToString() ?? "POLL",
            new OracleParameter("ID", surveyId))).FirstOrDefault() ?? "POLL";

        const string sqlU = @"
            UPDATE HRMS.HR_SURVEY_RESPONSE
               SET STATUS    = 'SUBMITTED',
                   SUBMIT_DT = SYSDATE,
                   SCORE     = :SCORE,
                   MAX_SCORE = :MAX,
                   IS_PASS   = :PASS
             WHERE ID = :ID";
        await _db.ExecuteNonQueryAsync(sqlU,
            new OracleParameter("SCORE", (object?)score    ?? DBNull.Value),
            new OracleParameter("MAX",   (object?)maxScore ?? DBNull.Value),
            new OracleParameter("PASS",  (object?)isPass   ?? DBNull.Value),
            new OracleParameter("ID",    response.ID));

        return (new SurveySubmitResultModel
        {
            SUCCESS     = true,
            SCORE       = score,
            MAX_SCORE   = maxScore,
            IS_PASS     = isPass,
            SURVEY_TYPE = surveyType,
        }, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SKIP — "Tôi không biết chữ"
    //  Insert response ILLITERATE_SKIP + insert/update HR_SURVEY_EXEMPT.
    // ═══════════════════════════════════════════════════════════════

    public async Task<(bool ok, string? error)> SkipIlliterateAsync(string empcd, int surveyId)
    {
        if (!await _recipient.IsRecipientAsync(surveyId, empcd))
            return (false, "Không thuộc phạm vi survey");

        var status = await GetSurveyStatusAsync(surveyId);
        if (status != "ACTIVE")
            return (false, $"Survey không ở trạng thái ACTIVE ({status})");

        // Nếu đã có response bất kỳ → cập nhật; nếu chưa → insert mới ILLITERATE_SKIP
        var existing = await _db.ExecuteQueryAsync(
            "SELECT ID, STATUS FROM HRMS.HR_SURVEY_RESPONSE WHERE SURVEY_ID = :SID AND EMPCD = :EMP",
            r => new { Id = Convert.ToInt32(r["ID"]), Status = r["STATUS"]?.ToString() ?? "" },
            new OracleParameter("SID", surveyId),
            new OracleParameter("EMP", empcd));

        if (existing.Count > 0)
        {
            if (existing[0].Status is "SUBMITTED" or "AUTO_SUBMITTED" or "ILLITERATE_SKIP")
                return (false, "Đã có response trước đó");

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_SURVEY_RESPONSE
                   SET STATUS = 'ILLITERATE_SKIP', SUBMIT_DT = SYSDATE
                 WHERE ID = :ID",
                new OracleParameter("ID", existing[0].Id));
        }
        else
        {
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_SURVEY_RESPONSE
                    (SURVEY_ID, EMPCD, STATUS, START_DT, SUBMIT_DT, INST_DT)
                VALUES
                    (:SID, :EMP, 'ILLITERATE_SKIP', SYSDATE, SYSDATE, SYSDATE)",
                new OracleParameter("SID", surveyId),
                new OracleParameter("EMP", empcd));
        }

        // Upsert HR_SURVEY_EXEMPT (EMPCD, EXEMPT_TYPE='ILLITERATE')
        const string sqlUpsert = @"
            MERGE INTO HRMS.HR_SURVEY_EXEMPT EX
            USING (SELECT :EMP AS EMPCD, 'ILLITERATE' AS EXEMPT_TYPE FROM DUAL) SRC
               ON (EX.EMPCD = SRC.EMPCD AND EX.EXEMPT_TYPE = SRC.EXEMPT_TYPE)
             WHEN MATCHED THEN UPDATE
                SET IS_ACTIVE      = 1,
                    NOTE           = :NOTE,
                    EFFECTIVE_DATE = TRUNC(SYSDATE),
                    UPDT_ID        = :EMP
             WHEN NOT MATCHED THEN INSERT (EMPCD, EXEMPT_TYPE, NOTE, EFFECTIVE_DATE, IS_ACTIVE, INST_ID)
                VALUES (:EMP, 'ILLITERATE', :NOTE, TRUNC(SYSDATE), 1, :EMP)";
        await _db.ExecuteNonQueryAsync(sqlUpsert,
            new OracleParameter("EMP",  empcd),
            new OracleParameter("NOTE", "Auto từ nút Tôi không biết chữ"));

        return (true, null);
    }

    // ═══════════════════════════════════════════════════════════════
    //  INTERNAL HELPERS
    // ═══════════════════════════════════════════════════════════════

    private async Task<SurveyResponseModel?> GetOrCreateInProgressAsync(int surveyId, string empcd, string? ip, string? ua)
    {
        var existing = await _db.ExecuteQueryAsync(@"
            SELECT ID, SURVEY_ID, EMPCD, STATUS, START_DT, SUBMIT_DT, SCORE, MAX_SCORE, IS_PASS
              FROM HRMS.HR_SURVEY_RESPONSE
             WHERE SURVEY_ID = :SID AND EMPCD = :EMP",
            r => new SurveyResponseModel
            {
                ID        = Convert.ToInt32(r["ID"]),
                SURVEY_ID = Convert.ToInt32(r["SURVEY_ID"]),
                EMPCD     = r["EMPCD"]?.ToString() ?? "",
                STATUS    = r["STATUS"]?.ToString() ?? "IN_PROGRESS",
                START_DT  = Convert.ToDateTime(r["START_DT"]),
                SUBMIT_DT = r["SUBMIT_DT"] as DateTime?,
                SCORE     = r["SCORE"]     is DBNull ? null : Convert.ToDecimal(r["SCORE"]),
                MAX_SCORE = r["MAX_SCORE"] is DBNull ? null : Convert.ToDecimal(r["MAX_SCORE"]),
                IS_PASS   = r["IS_PASS"]   is DBNull ? null : Convert.ToInt32(r["IS_PASS"]),
            },
            new OracleParameter("SID", surveyId),
            new OracleParameter("EMP", empcd));

        SurveyResponseModel response;
        if (existing.Count > 0)
        {
            response = existing[0];
        }
        else
        {
            const string sqlI = @"
                INSERT INTO HRMS.HR_SURVEY_RESPONSE
                    (SURVEY_ID, EMPCD, STATUS, START_DT, IP_ADDRESS, USER_AGENT, INST_DT)
                VALUES
                    (:SID, :EMP, 'IN_PROGRESS', SYSDATE, :IP, :UA, SYSDATE)
                RETURNING ID INTO :OUT_ID";
            var outId = new OracleParameter("OUT_ID", OracleDbType.Decimal, ParameterDirection.Output);
            await _db.ExecuteNonQueryAsync(sqlI,
                new OracleParameter("SID", surveyId),
                new OracleParameter("EMP", empcd),
                new OracleParameter("IP",  (object?)ip ?? DBNull.Value),
                new OracleParameter("UA",  (object?)ua ?? DBNull.Value),
                outId);
            int newId = outId.Value is OracleDecimal od && !od.IsNull ? (int)od.Value : 0;
            response = new SurveyResponseModel
            {
                ID        = newId,
                SURVEY_ID = surveyId,
                EMPCD     = empcd,
                STATUS    = "IN_PROGRESS",
                START_DT  = DateTime.Now,
            };
        }

        // Load answers
        response.ANSWERS = await _db.ExecuteQueryAsync(@"
            SELECT ID, RESPONSE_ID, QUESTION_ID, ANSWER_OPTION_IDS, ANSWER_TEXT, ANSWER_NUMBER
              FROM HRMS.HR_SURVEY_ANSWER
             WHERE RESPONSE_ID = :RID",
            r => new SurveyAnswerModel
            {
                ID                = Convert.ToInt32(r["ID"]),
                RESPONSE_ID       = Convert.ToInt32(r["RESPONSE_ID"]),
                QUESTION_ID       = Convert.ToInt32(r["QUESTION_ID"]),
                ANSWER_OPTION_IDS = r["ANSWER_OPTION_IDS"] as string,
                ANSWER_TEXT       = r["ANSWER_TEXT"] is DBNull ? null : r["ANSWER_TEXT"].ToString(),
                ANSWER_NUMBER     = r["ANSWER_NUMBER"] is DBNull ? null : Convert.ToDecimal(r["ANSWER_NUMBER"]),
            },
            new OracleParameter("RID", response.ID));

        return response;
    }

    private async Task<string?> GetSurveyStatusAsync(int surveyId)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => r["STATUS"]?.ToString(),
            new OracleParameter("ID", surveyId));
        return rows.FirstOrDefault();
    }

    private static SurveyModel MapSurveyLight(OracleDataReader r) => new()
    {
        ID             = Convert.ToInt32(r["ID"]),
        TITLE          = r["TITLE"]?.ToString() ?? "",
        DESCRIPTION    = r["DESCRIPTION"] as string,
        SURVEY_TYPE    = r["SURVEY_TYPE"]?.ToString() ?? "POLL",
        LANG           = r["LANG"]?.ToString() ?? "VI",
        STATUS         = r["STATUS"]?.ToString() ?? "DRAFT",
        START_DATE     = r["START_DATE"] as DateTime?,
        END_DATE       = r["END_DATE"]   as DateTime?,
        RECIPIENT_MODE = r["RECIPIENT_MODE"]?.ToString() ?? "ALL",
        PASS_SCORE     = r["PASS_SCORE"] is DBNull ? null : Convert.ToDecimal(r["PASS_SCORE"]),
        PUBLISHED_DT   = r["PUBLISHED_DT"] as DateTime?,
        PUBLISHED_BY   = r["PUBLISHED_BY"] as string,
        INST_ID        = r["INST_ID"] as string,
        INST_DT        = r["INST_DT"] as DateTime?,
        UPDT_ID        = r["UPDT_ID"] as string,
        UPDT_DT        = r["UPDT_DT"] as DateTime?,
    };
}
