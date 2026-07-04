using HR_api.Data;
using HR_api.Models.Survey;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Query aggregate report cho SurveyAdmin. Excel export nằm ở HR_web (dùng ClosedXML).
public class SurveyReportService
{
    private readonly OracleService _db;
    public SurveyReportService(OracleService db) { _db = db; }

    // ═══════════════════════════════════════════════════════════════
    //  OVERVIEW: recipient counts + per-question aggregation
    // ═══════════════════════════════════════════════════════════════
    public async Task<SurveyReportOverviewModel?> GetOverviewAsync(int surveyId)
    {
        // Survey meta
        const string sqlS = @"
            SELECT ID, TITLE, SURVEY_TYPE, LANG, STATUS, START_DATE, END_DATE, PASS_SCORE
              FROM HRMS.HR_SURVEY WHERE ID = :ID";
        var survey = (await _db.ExecuteQueryAsync(sqlS, r => new
        {
            Id = Convert.ToInt32(r["ID"]),
            Title = r["TITLE"]?.ToString() ?? "",
            Type = r["SURVEY_TYPE"]?.ToString() ?? "POLL",
            Lang = r["LANG"]?.ToString() ?? "VI",
            Status = r["STATUS"]?.ToString() ?? "",
            Start = r["START_DATE"] as DateTime?,
            End   = r["END_DATE"]   as DateTime?,
            Pass  = r["PASS_SCORE"] is DBNull ? null : (decimal?)Convert.ToDecimal(r["PASS_SCORE"]),
        }, new OracleParameter("ID", surveyId))).FirstOrDefault();
        if (survey == null) return null;

        // Recipient count + response counts by status
        const string sqlCounts = @"
            SELECT
              (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RECIPIENT WHERE SURVEY_ID = :ID) AS TOTAL,
              (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RESPONSE  WHERE SURVEY_ID = :ID AND STATUS = 'SUBMITTED')      AS SUBMITTED,
              (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RESPONSE  WHERE SURVEY_ID = :ID AND STATUS = 'AUTO_SUBMITTED') AS AUTO_SUB,
              (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RESPONSE  WHERE SURVEY_ID = :ID AND STATUS = 'ILLITERATE_SKIP')AS ILLITERATE,
              (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RESPONSE  WHERE SURVEY_ID = :ID AND STATUS = 'IN_PROGRESS')    AS IN_PROG
            FROM DUAL";
        var counts = (await _db.ExecuteQueryAsync(sqlCounts, r => new
        {
            Total       = Convert.ToInt32(r["TOTAL"]),
            Submitted   = Convert.ToInt32(r["SUBMITTED"]),
            AutoSub     = Convert.ToInt32(r["AUTO_SUB"]),
            Illiterate  = Convert.ToInt32(r["ILLITERATE"]),
            InProgress  = Convert.ToInt32(r["IN_PROG"]),
        }, new OracleParameter("ID", surveyId))).First();

        var overview = new SurveyReportOverviewModel
        {
            SURVEY_ID          = survey.Id,
            TITLE              = survey.Title,
            SURVEY_TYPE        = survey.Type,
            LANG               = survey.Lang,
            STATUS             = survey.Status,
            START_DATE         = survey.Start,
            END_DATE           = survey.End,
            TOTAL_RECIPIENTS   = counts.Total,
            SUBMITTED_COUNT    = counts.Submitted,
            AUTO_SUBMIT_COUNT  = counts.AutoSub,
            ILLITERATE_COUNT   = counts.Illiterate,
            IN_PROGRESS_COUNT  = counts.InProgress,
            NOT_STARTED_COUNT  = Math.Max(0,
                counts.Total - counts.Submitted - counts.AutoSub - counts.Illiterate - counts.InProgress),
        };

        // Per-question aggregation
        const string sqlQ = @"
            SELECT ID, QUESTION_TEXT, QUESTION_TYPE, DISPLAY_ORDER, POINTS
              FROM HRMS.HR_SURVEY_QUESTION
             WHERE SURVEY_ID = :ID
             ORDER BY DISPLAY_ORDER, ID";
        var questions = await _db.ExecuteQueryAsync(sqlQ, r => new SurveyReportQuestionModel
        {
            QUESTION_ID   = Convert.ToInt32(r["ID"]),
            QUESTION_TEXT = r["QUESTION_TEXT"]?.ToString() ?? "",
            QUESTION_TYPE = r["QUESTION_TYPE"]?.ToString() ?? "",
            DISPLAY_ORDER = Convert.ToInt32(r["DISPLAY_ORDER"]),
            POINTS        = Convert.ToDecimal(r["POINTS"] is DBNull ? 0 : r["POINTS"]),
        }, new OracleParameter("ID", surveyId));

        foreach (var q in questions)
        {
            if (q.QUESTION_TYPE is "SINGLE" or "MULTI" or "YESNO" or "DROPDOWN")
                q.OPTIONS = await GetOptionAggregateAsync(q.QUESTION_ID);
            else if (q.QUESTION_TYPE == "RATING")
                q.RATING_DIST = await GetRatingDistributionAsync(q.QUESTION_ID);
            else if (q.QUESTION_TYPE == "TEXT")
                q.TEXT_COUNT = await GetTextCountAsync(q.QUESTION_ID);
        }

        overview.QUESTIONS = questions;
        return overview;
    }

    private async Task<List<SurveyReportOptionModel>> GetOptionAggregateAsync(int questionId)
    {
        // Đếm số lần chọn theo option (parse CSV ANSWER_OPTION_IDS)
        // Đơn giản: load answers + options, aggregate ở app.
        const string sqlO = @"
            SELECT ID, OPTION_TEXT
              FROM HRMS.HR_SURVEY_OPTION
             WHERE QUESTION_ID = :QID
             ORDER BY DISPLAY_ORDER, ID";
        var opts = await _db.ExecuteQueryAsync(sqlO, r => new
        {
            Id   = Convert.ToInt32(r["ID"]),
            Text = r["OPTION_TEXT"]?.ToString() ?? "",
        }, new OracleParameter("QID", questionId));

        const string sqlA = @"
            SELECT ANSWER_OPTION_IDS
              FROM HRMS.HR_SURVEY_ANSWER
             WHERE QUESTION_ID = :QID AND ANSWER_OPTION_IDS IS NOT NULL";
        var answers = await _db.ExecuteQueryAsync(sqlA,
            r => r["ANSWER_OPTION_IDS"]?.ToString() ?? "",
            new OracleParameter("QID", questionId));

        // Đếm hit per option
        var counts = new Dictionary<int, int>();
        foreach (var csv in answers)
        {
            foreach (var s in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(s, out var id))
                    counts[id] = counts.GetValueOrDefault(id) + 1;
            }
        }

        return opts.Select(o => new SurveyReportOptionModel
        {
            OPTION_ID   = o.Id,
            OPTION_TEXT = o.Text,
            COUNT       = counts.GetValueOrDefault(o.Id),
        }).ToList();
    }

    private async Task<int[]> GetRatingDistributionAsync(int questionId)
    {
        const string sql = @"
            SELECT ANSWER_NUMBER
              FROM HRMS.HR_SURVEY_ANSWER
             WHERE QUESTION_ID = :QID AND ANSWER_NUMBER IS NOT NULL";
        var values = await _db.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["ANSWER_NUMBER"]),
            new OracleParameter("QID", questionId));

        var dist = new int[5];   // index 0 = 1 sao, index 4 = 5 sao
        foreach (var v in values)
            if (v >= 1 && v <= 5) dist[v - 1]++;
        return dist;
    }

    private async Task<int> GetTextCountAsync(int questionId)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) AS C FROM HRMS.HR_SURVEY_ANSWER WHERE QUESTION_ID = :QID AND ANSWER_TEXT IS NOT NULL",
            r => Convert.ToInt32(r["C"]),
            new OracleParameter("QID", questionId));
        return rows.First();
    }

    // Trả về list câu trả lời TEXT cho HR đọc chi tiết
    public async Task<List<SurveyReportTextAnswerModel>> GetTextAnswersAsync(int questionId, int page, int pageSize)
    {
        int offset = (page - 1) * pageSize;
        const string sql = @"
            SELECT * FROM (
                SELECT A.ID, A.RESPONSE_ID, R.EMPCD, EC.CNAME AS FULL_NAME,
                       DBMS_LOB.SUBSTR(A.ANSWER_TEXT, 4000, 1) AS ANSWER_TEXT, A.INST_DT,
                       ROW_NUMBER() OVER (ORDER BY A.INST_DT DESC) AS RN
                  FROM HRMS.HR_SURVEY_ANSWER A
                  JOIN HRMS.HR_SURVEY_RESPONSE R ON R.ID = A.RESPONSE_ID
                  LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = R.EMPCD
                 WHERE A.QUESTION_ID = :QID AND A.ANSWER_TEXT IS NOT NULL
            ) WHERE RN > :OFFSET AND RN <= :OFFSET + :PSIZE";
        return await _db.ExecuteQueryAsync(sql, r => new SurveyReportTextAnswerModel
        {
            RESPONSE_ID = Convert.ToInt32(r["RESPONSE_ID"]),
            EMPCD       = r["EMPCD"]?.ToString() ?? "",
            FULL_NAME   = r["FULL_NAME"] as string,
            ANSWER_TEXT = r["ANSWER_TEXT"] is DBNull ? null : r["ANSWER_TEXT"].ToString(),
            INST_DT     = r["INST_DT"] as DateTime?,
        },
            new OracleParameter("QID",    questionId),
            new OracleParameter("OFFSET", offset),
            new OracleParameter("PSIZE",  pageSize));
    }

    // ═══════════════════════════════════════════════════════════════
    //  QUIZ report: score per user + distribution
    // ═══════════════════════════════════════════════════════════════
    public async Task<SurveyReportQuizModel?> GetQuizReportAsync(int surveyId)
    {
        // Verify QUIZ
        var type = (await _db.ExecuteQueryAsync(
            "SELECT SURVEY_TYPE FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => r["SURVEY_TYPE"]?.ToString() ?? "POLL",
            new OracleParameter("ID", surveyId))).FirstOrDefault();
        if (type != "QUIZ") return null;

        // Load per-user
        const string sqlU = @"
            SELECT R.EMPCD, EC.CNAME AS FULL_NAME, EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   R.STATUS, R.SCORE, R.MAX_SCORE, R.IS_PASS, R.SUBMIT_DT
              FROM HRMS.HR_SURVEY_RESPONSE R
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = R.EMPCD
             WHERE R.SURVEY_ID = :ID
               AND R.STATUS IN ('SUBMITTED','AUTO_SUBMITTED')
             ORDER BY R.SCORE DESC NULLS LAST, R.EMPCD";
        var users = await _db.ExecuteQueryAsync(sqlU, r => new SurveyReportQuizUserModel
        {
            EMPCD     = r["EMPCD"]?.ToString() ?? "",
            FULL_NAME = r["FULL_NAME"] as string,
            DEPTCD    = r["DEPTCD"] as string,
            LINECD    = r["LINECD"] as string,
            WORKCD    = r["WORKCD"] as string,
            STATUS    = r["STATUS"]?.ToString() ?? "",
            SCORE     = r["SCORE"]     is DBNull ? null : (decimal?)Convert.ToDecimal(r["SCORE"]),
            MAX_SCORE = r["MAX_SCORE"] is DBNull ? null : (decimal?)Convert.ToDecimal(r["MAX_SCORE"]),
            IS_PASS   = r["IS_PASS"]   is DBNull ? null : (int?)Convert.ToInt32(r["IS_PASS"]),
            SUBMIT_DT = r["SUBMIT_DT"] as DateTime?,
        }, new OracleParameter("ID", surveyId));

        var scored   = users.Where(u => u.SCORE.HasValue).ToList();
        var passCnt  = users.Count(u => u.IS_PASS == 1);
        var failCnt  = users.Count(u => u.IS_PASS == 0);
        var avgScore = scored.Count > 0 ? scored.Average(u => u.SCORE!.Value) : 0;

        return new SurveyReportQuizModel
        {
            SURVEY_ID  = surveyId,
            USERS      = users,
            AVG_SCORE  = Math.Round(avgScore, 2),
            PASS_COUNT = passCnt,
            FAIL_COUNT = failCnt,
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  ILLITERATE list — global (không theo survey) hoặc theo survey
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<SurveyReportIlliterateModel>> GetIlliterateListAsync(int? surveyId, string? deptcd, string? linecd, string? workcd)
    {
        var sql = @"
            SELECT EX.EMPCD, EC.CNAME AS FULL_NAME, EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   EX.EFFECTIVE_DATE, EX.NOTE, EX.INST_DT
              FROM HRMS.HR_SURVEY_EXEMPT EX
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = EX.EMPCD
             WHERE EX.IS_ACTIVE = 1
               AND EX.EXEMPT_TYPE IN ('ILLITERATE','BOTH')
               AND (:P_DEPT IS NULL OR EC.DEPTCD = :P_DEPT)
               AND (:P_LINE IS NULL OR EC.LINECD = :P_LINE)
               AND (:P_WORK IS NULL OR EC.WORKCD = :P_WORK)";
        if (surveyId.HasValue)
        {
            sql += @"
               AND EXISTS (SELECT 1 FROM HRMS.HR_SURVEY_RESPONSE R
                            WHERE R.EMPCD = EX.EMPCD AND R.SURVEY_ID = :SID
                              AND R.STATUS = 'ILLITERATE_SKIP')";
        }
        sql += " ORDER BY EX.INST_DT DESC";

        var pars = new List<OracleParameter>
        {
            new("P_DEPT", (object?)deptcd ?? DBNull.Value),
            new("P_LINE", (object?)linecd ?? DBNull.Value),
            new("P_WORK", (object?)workcd ?? DBNull.Value),
        };
        if (surveyId.HasValue) pars.Add(new OracleParameter("SID", surveyId.Value));

        return await _db.ExecuteQueryAsync(sql, r => new SurveyReportIlliterateModel
        {
            EMPCD          = r["EMPCD"]?.ToString() ?? "",
            FULL_NAME      = r["FULL_NAME"] as string,
            DEPTCD         = r["DEPTCD"] as string,
            LINECD         = r["LINECD"] as string,
            WORKCD         = r["WORKCD"] as string,
            EFFECTIVE_DATE = Convert.ToDateTime(r["EFFECTIVE_DATE"]),
            NOTE           = r["NOTE"] as string,
            INST_DT        = r["INST_DT"] as DateTime?,
        }, pars.ToArray());
    }
}
