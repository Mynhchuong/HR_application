using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Compute SCORE / MAX_SCORE / IS_PASS cho QUIZ. POLL trả về (null, null, null).
// Rule: SINGLE/YESNO/DROPDOWN → chọn đúng option = full points.
//       MULTI → khớp CHÍNH XÁC tập đáp án đúng (chọn thiếu/thừa = 0 điểm).
//       RATING/TEXT → không chấm.
public class SurveyScoreService
{
    private readonly OracleService _db;
    public SurveyScoreService(OracleService db) { _db = db; }

    public async Task<(decimal? score, decimal? maxScore, int? isPass)> ComputeAsync(int responseId)
    {
        // Meta: survey type + pass_score
        const string sqlMeta = @"
            SELECT S.SURVEY_TYPE, S.PASS_SCORE
              FROM HRMS.HR_SURVEY_RESPONSE R
              JOIN HRMS.HR_SURVEY S ON S.ID = R.SURVEY_ID
             WHERE R.ID = :RID";
        var meta = (await _db.ExecuteQueryAsync(sqlMeta, r => new
        {
            Type = r["SURVEY_TYPE"]?.ToString() ?? "POLL",
            Pass = r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
        }, new OracleParameter("RID", responseId))).FirstOrDefault();

        if (meta == null || meta.Type != "QUIZ")
            return (null, null, null);

        // Question + POINTS + correct option IDs
        const string sqlQ = @"
            SELECT Q.ID AS QID, Q.QUESTION_TYPE, Q.POINTS
              FROM HRMS.HR_SURVEY_QUESTION Q
              JOIN HRMS.HR_SURVEY_RESPONSE R ON R.SURVEY_ID = Q.SURVEY_ID
             WHERE R.ID = :RID
             ORDER BY Q.DISPLAY_ORDER, Q.ID";
        var questions = await _db.ExecuteQueryAsync(sqlQ, r => new
        {
            Qid    = Convert.ToInt32(r["QID"]),
            Type   = r["QUESTION_TYPE"]?.ToString() ?? "SINGLE",
            Points = Convert.ToDecimal(r["POINTS"] is DBNull ? 0 : r["POINTS"]),
        }, new OracleParameter("RID", responseId));

        if (questions.Count == 0) return (0m, 0m, meta.Pass == null ? (int?)null : 0);

        // Correct options (theo question)
        const string sqlCorrect = @"
            SELECT O.QUESTION_ID, O.ID
              FROM HRMS.HR_SURVEY_OPTION O
              JOIN HRMS.HR_SURVEY_QUESTION Q ON Q.ID = O.QUESTION_ID
              JOIN HRMS.HR_SURVEY_RESPONSE R ON R.SURVEY_ID = Q.SURVEY_ID
             WHERE R.ID = :RID
               AND O.IS_CORRECT = 1";
        var correctRows = await _db.ExecuteQueryAsync(sqlCorrect, r => new
        {
            Qid = Convert.ToInt32(r["QUESTION_ID"]),
            Oid = Convert.ToInt32(r["ID"]),
        }, new OracleParameter("RID", responseId));
        var correctByQ = correctRows
            .GroupBy(x => x.Qid)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Oid).ToHashSet());

        // Answers (theo question)
        const string sqlA = @"
            SELECT A.QUESTION_ID, A.ANSWER_OPTION_IDS
              FROM HRMS.HR_SURVEY_ANSWER A
             WHERE A.RESPONSE_ID = :RID";
        var answerRows = await _db.ExecuteQueryAsync(sqlA, r => new
        {
            Qid = Convert.ToInt32(r["QUESTION_ID"]),
            Ids = r["ANSWER_OPTION_IDS"] as string,
        }, new OracleParameter("RID", responseId));
        var answersByQ = answerRows.ToDictionary(x => x.Qid, x => x.Ids);

        decimal score    = 0;
        decimal maxScore = 0;

        foreach (var q in questions)
        {
            switch (q.Type)
            {
                case "SINGLE":
                case "YESNO":
                case "DROPDOWN":
                case "MULTI":
                    maxScore += q.Points;
                    var answered = ParseIds(answersByQ.GetValueOrDefault(q.Qid));
                    var correct  = correctByQ.GetValueOrDefault(q.Qid) ?? new HashSet<int>();
                    if (answered.SetEquals(correct) && correct.Count > 0)
                        score += q.Points;
                    break;
                // RATING, TEXT: không cộng vào maxScore
            }
        }

        int? isPass = meta.Pass == null ? (int?)null : (score >= meta.Pass ? 1 : 0);
        return (score, maxScore, isPass);
    }

    private static HashSet<int> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new HashSet<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => int.TryParse(s, out _))
                  .Select(int.Parse)
                  .ToHashSet();
    }
}
