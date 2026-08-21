using HR_api.Data;
using HR_api.Models.Survey;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using System.Data;

namespace HR_api.Services;

// State machine cho HR: DRAFT → SCHEDULED → ACTIVE ↔ PAUSED → EXPIRED/CANCELLED → DELETED
// SCHEDULED → ACTIVE và ACTIVE/PAUSED → EXPIRED do batch job tự chuyển, không expose ở HTTP.
public class SurveyAdminService
{
    private readonly OracleService _db;

    public SurveyAdminService(OracleService db) { _db = db; }

    // ═══════════════════════════════════════════════════════════════
    //  LIST + DETAIL
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<SurveyModel>> ListAsync(string? status, string? type, string? search)
    {
        var sql = @"
            SELECT S.ID, S.TITLE, S.DESCRIPTION, S.SURVEY_TYPE, S.LANG, S.STATUS,
                   S.START_DATE, S.END_DATE, S.RECIPIENT_MODE, S.PASS_SCORE,
                   S.PUBLISHED_DT, S.PUBLISHED_BY,
                   S.INST_ID, S.INST_DT, S.UPDT_ID, S.UPDT_DT,
                   (SELECT COUNT(*) FROM HRMS.HR_SURVEY_QUESTION Q  WHERE Q.SURVEY_ID = S.ID) AS QUESTION_COUNT,
                   CASE WHEN S.RECIPIENT_MODE = 'ALL' THEN
                        (SELECT COUNT(*) FROM HRMS.ECM100 EC
                          WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                            AND NOT EXISTS (
                                SELECT 1 FROM HRMS.HR_SURVEY_EXEMPT EX
                                 WHERE EX.EMPCD = EC.EMPCD
                                   AND EX.IS_ACTIVE = 1
                                   AND EX.EFFECTIVE_DATE <= TRUNC(SYSDATE)))
                        ELSE
                        (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RECIPIENT R WHERE R.SURVEY_ID = S.ID)
                   END AS RECIPIENT_COUNT,
                   (SELECT COUNT(*) FROM HRMS.HR_SURVEY_RESPONSE X
                     WHERE X.SURVEY_ID = S.ID
                       AND X.STATUS IN ('SUBMITTED','AUTO_SUBMITTED')) AS SUBMITTED_COUNT
              FROM HRMS.HR_SURVEY S
             WHERE S.STATUS <> 'DELETED'
               AND (:P_STATUS IS NULL OR S.STATUS = :P_STATUS)
               AND (:P_TYPE   IS NULL OR S.SURVEY_TYPE = :P_TYPE)
               AND (:P_SEARCH IS NULL OR UPPER(S.TITLE) LIKE '%' || UPPER(:P_SEARCH) || '%')
             ORDER BY S.ID DESC";

        return await _db.ExecuteQueryAsync(sql, MapSurveyLight,
            new OracleParameter("P_STATUS", (object?)status  ?? DBNull.Value),
            new OracleParameter("P_TYPE",   (object?)type    ?? DBNull.Value),
            new OracleParameter("P_SEARCH", (object?)search  ?? DBNull.Value));
    }

    public async Task<SurveyModel?> GetDetailAsync(int id)
    {
        const string sqlSurvey = @"
            SELECT ID, TITLE, DESCRIPTION, SURVEY_TYPE, LANG, STATUS,
                   START_DATE, END_DATE, RECIPIENT_MODE, PASS_SCORE,
                   PUBLISHED_DT, PUBLISHED_BY,
                   INST_ID, INST_DT, UPDT_ID, UPDT_DT
              FROM HRMS.HR_SURVEY
             WHERE ID = :ID";

        var list = await _db.ExecuteQueryAsync(sqlSurvey, MapSurveyLight,
            new OracleParameter("ID", id));
        var survey = list.FirstOrDefault();
        if (survey == null) return null;

        // Questions + options
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
        }, new OracleParameter("SID", id));

        if (questions.Count > 0)
        {
            const string sqlO = @"
                SELECT O.ID, O.QUESTION_ID, O.OPTION_TEXT, O.DISPLAY_ORDER, O.IS_CORRECT
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
                IS_CORRECT    = Convert.ToInt32(r["IS_CORRECT"]),
            }, new OracleParameter("SID", id));

            var byQuestion = options.GroupBy(o => o.QUESTION_ID).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var q in questions)
                if (byQuestion.TryGetValue(q.ID, out var opts)) q.OPTIONS = opts;
        }

        // Scopes
        const string sqlSc = @"
            SELECT ID, SURVEY_ID, SCOPE_TYPE, DEPTCD, LINECD, WORKCD, EMPCD
              FROM HRMS.HR_SURVEY_SCOPE
             WHERE SURVEY_ID = :SID
             ORDER BY ID";
        var scopes = await _db.ExecuteQueryAsync(sqlSc, r => new SurveyScopeModel
        {
            ID         = Convert.ToInt32(r["ID"]),
            SURVEY_ID  = Convert.ToInt32(r["SURVEY_ID"]),
            SCOPE_TYPE = r["SCOPE_TYPE"]?.ToString() ?? "",
            DEPTCD     = r["DEPTCD"]  as string,
            LINECD     = r["LINECD"]  as string,
            WORKCD     = r["WORKCD"]  as string,
            EMPCD      = r["EMPCD"]   as string,
        }, new OracleParameter("SID", id));

        survey.QUESTIONS = questions;
        survey.SCOPES    = scopes;
        return survey;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAVE (create / update DRAFT hoặc SCHEDULED chưa activate)
    //  Chiến lược: replace toàn bộ questions/options/scopes theo payload.
    //  Chỉ cho phép SAVE khi STATUS in ('DRAFT','SCHEDULED').
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> SaveAsync(SaveSurveyRequest req)
    {
        int surveyId;

        if (req.ID.HasValue && req.ID.Value > 0)
        {
            // UPDATE existing. DRAFT/SCHEDULED: sửa toàn bộ như cũ.
            // ACTIVE/PAUSED: cho sửa GIỚI HẠN (tiêu đề/mô tả/ngày kết thúc/đối tượng nhận) để HR
            // tự fix lỗi nhập liệu mà không cần đụng DB tay — KHÔNG đụng câu hỏi (HR_SURVEY_ANSWER
            // có FK vào HR_SURVEY_QUESTION, xoá câu hỏi cũ sẽ vỡ FK nếu đã có người trả lời) và
            // không đổi SURVEY_TYPE/START_DATE/PASS_SCORE (đã có response chấm theo cấu hình cũ).
            var existing = await GetStatusAndLangAsync(req.ID.Value);
            if (existing == null)
                throw new InvalidOperationException("Không tìm thấy survey");

            bool isLimitedEdit = existing.Status == "ACTIVE" || existing.Status == "PAUSED";
            if (existing.Status != "DRAFT" && existing.Status != "SCHEDULED" && !isLimitedEdit)
                throw new InvalidOperationException($"Không sửa được survey ở trạng thái {existing.Status}");
            if (!string.Equals(existing.Lang, req.LANG, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Không được đổi LANG sau khi tạo survey");

            if (isLimitedEdit)
            {
                // UPDATE + replace scope chạy chung 1 transaction — tránh trường hợp UPDATE
                // HR_SURVEY thành công nhưng ReplaceScopesAsync lỗi giữa chừng (đã DELETE scope
                // cũ, chưa INSERT hết scope mới), để lại survey ACTIVE với scope dở dang.
                await using var scope = await _db.BeginTransactionAsync();
                try
                {
                    const string sqlUpdLimited = @"
                        UPDATE HRMS.HR_SURVEY
                           SET TITLE          = :TITLE,
                               DESCRIPTION    = :DESCRIPTION,
                               END_DATE       = :END_DATE,
                               RECIPIENT_MODE = :RECIPIENT_MODE,
                               UPDT_ID        = :UPDT_ID
                         WHERE ID = :ID";

                    await _db.ExecuteNonQueryAsync(scope, sqlUpdLimited,
                        new OracleParameter("TITLE",          req.TITLE),
                        new OracleParameter("DESCRIPTION",    (object?)req.DESCRIPTION ?? DBNull.Value),
                        new OracleParameter("END_DATE",       (object?)req.END_DATE   ?? DBNull.Value),
                        new OracleParameter("RECIPIENT_MODE", req.RECIPIENT_MODE),
                        new OracleParameter("UPDT_ID",        (object?)req.LOGIN_USER ?? DBNull.Value),
                        new OracleParameter("ID",             req.ID.Value));

                    await ReplaceScopesAsync(scope, req.ID.Value, req.SCOPES);
                    await scope.CommitAsync();
                    return req.ID.Value; // KHÔNG đụng câu hỏi
                }
                catch
                {
                    await scope.RollbackAsync();
                    throw;
                }
            }

            const string sqlUpd = @"
                UPDATE HRMS.HR_SURVEY
                   SET TITLE          = :TITLE,
                       DESCRIPTION    = :DESCRIPTION,
                       SURVEY_TYPE    = :SURVEY_TYPE,
                       START_DATE     = :START_DATE,
                       END_DATE       = :END_DATE,
                       RECIPIENT_MODE = :RECIPIENT_MODE,
                       PASS_SCORE     = :PASS_SCORE,
                       UPDT_ID        = :UPDT_ID
                 WHERE ID = :ID";

            await _db.ExecuteNonQueryAsync(sqlUpd,
                new OracleParameter("TITLE",          req.TITLE),
                new OracleParameter("DESCRIPTION",    (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("SURVEY_TYPE",    req.SURVEY_TYPE),
                new OracleParameter("START_DATE",     (object?)req.START_DATE ?? DBNull.Value),
                new OracleParameter("END_DATE",       (object?)req.END_DATE   ?? DBNull.Value),
                new OracleParameter("RECIPIENT_MODE", req.RECIPIENT_MODE),
                new OracleParameter("PASS_SCORE",     (object?)req.PASS_SCORE ?? DBNull.Value),
                new OracleParameter("UPDT_ID",        (object?)req.LOGIN_USER ?? DBNull.Value),
                new OracleParameter("ID",             req.ID.Value));

            surveyId = req.ID.Value;
        }
        else
        {
            // INSERT new
            const string sqlIns = @"
                INSERT INTO HRMS.HR_SURVEY
                    (TITLE, DESCRIPTION, SURVEY_TYPE, LANG, STATUS,
                     START_DATE, END_DATE, RECIPIENT_MODE, PASS_SCORE, INST_ID, INST_DT)
                VALUES
                    (:TITLE, :DESCRIPTION, :SURVEY_TYPE, :LANG, 'DRAFT',
                     :START_DATE, :END_DATE, :RECIPIENT_MODE, :PASS_SCORE, :INST_ID, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outId = new OracleParameter("OUT_ID", OracleDbType.Decimal, ParameterDirection.Output);

            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("TITLE",          req.TITLE),
                new OracleParameter("DESCRIPTION",    (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("SURVEY_TYPE",    req.SURVEY_TYPE),
                new OracleParameter("LANG",           req.LANG),
                new OracleParameter("START_DATE",     (object?)req.START_DATE ?? DBNull.Value),
                new OracleParameter("END_DATE",       (object?)req.END_DATE   ?? DBNull.Value),
                new OracleParameter("RECIPIENT_MODE", req.RECIPIENT_MODE),
                new OracleParameter("PASS_SCORE",     (object?)req.PASS_SCORE ?? DBNull.Value),
                new OracleParameter("INST_ID",        (object?)req.LOGIN_USER ?? DBNull.Value),
                outId);

            surveyId = outId.Value is OracleDecimal od && !od.IsNull ? (int)od.Value : 0;
        }

        await ReplaceQuestionsAsync(surveyId, req.QUESTIONS);
        await ReplaceScopesAsync(surveyId, req.SCOPES);

        return surveyId;
    }

    private async Task ReplaceQuestionsAsync(int surveyId, List<SaveQuestionRequest> questions)
    {
        // Xoá cũ (option xoá theo cascade thủ công vì không có ON DELETE CASCADE)
        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM HRMS.HR_SURVEY_OPTION
             WHERE QUESTION_ID IN (SELECT ID FROM HRMS.HR_SURVEY_QUESTION WHERE SURVEY_ID = :SID)",
            new OracleParameter("SID", surveyId));

        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_SURVEY_QUESTION WHERE SURVEY_ID = :SID",
            new OracleParameter("SID", surveyId));

        int qOrder = 0;
        foreach (var q in questions)
        {
            // Skip câu hỏi trống — DDL CHECK QUESTION_TEXT NOT NULL, Oracle treat '' = NULL
            var qText = q.QUESTION_TEXT?.Trim();
            if (string.IsNullOrEmpty(qText)) continue;
            qOrder++;

            const string sqlQ = @"
                INSERT INTO HRMS.HR_SURVEY_QUESTION
                    (SURVEY_ID, QUESTION_TEXT, QUESTION_TYPE, IS_REQUIRED, DISPLAY_ORDER, POINTS, INST_DT)
                VALUES
                    (:SID, :QTEXT, :QTYPE, :REQ, :ORD, :PTS, SYSDATE)
                RETURNING ID INTO :OUT_ID";
            var outQid = new OracleParameter("OUT_ID", OracleDbType.Decimal, ParameterDirection.Output);
            await _db.ExecuteNonQueryAsync(sqlQ,
                new OracleParameter("SID",   surveyId),
                new OracleParameter("QTEXT", qText),
                new OracleParameter("QTYPE", q.QUESTION_TYPE),
                new OracleParameter("REQ",   q.IS_REQUIRED),
                new OracleParameter("ORD",   qOrder),
                new OracleParameter("PTS",   q.POINTS),
                outQid);

            int qid = outQid.Value is OracleDecimal od && !od.IsNull ? (int)od.Value : 0;

            int oOrder = 0;
            foreach (var o in q.OPTIONS)
            {
                var oText = o.OPTION_TEXT?.Trim();
                if (string.IsNullOrEmpty(oText)) continue;
                oOrder++;
                const string sqlO = @"
                    INSERT INTO HRMS.HR_SURVEY_OPTION
                        (QUESTION_ID, OPTION_TEXT, DISPLAY_ORDER, IS_CORRECT)
                    VALUES
                        (:QID, :TEXT, :ORD, :CORR)";
                await _db.ExecuteNonQueryAsync(sqlO,
                    new OracleParameter("QID",  qid),
                    new OracleParameter("TEXT", oText),
                    new OracleParameter("ORD",  oOrder),
                    new OracleParameter("CORR", o.IS_CORRECT));
            }
        }
    }

    private async Task ReplaceScopesAsync(int surveyId, List<SaveScopeRequest> scopes)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_SURVEY_SCOPE WHERE SURVEY_ID = :SID",
            new OracleParameter("SID", surveyId));

        foreach (var sc in scopes)
        {
            const string sqlSc = @"
                INSERT INTO HRMS.HR_SURVEY_SCOPE
                    (SURVEY_ID, SCOPE_TYPE, DEPTCD, LINECD, WORKCD, EMPCD, INST_DT)
                VALUES
                    (:SID, :STYPE, :DEPT, :LINE, :WORK, :EMP, SYSDATE)";
            await _db.ExecuteNonQueryAsync(sqlSc,
                new OracleParameter("SID",   surveyId),
                new OracleParameter("STYPE", sc.SCOPE_TYPE),
                new OracleParameter("DEPT",  (object?)sc.DEPTCD ?? DBNull.Value),
                new OracleParameter("LINE",  (object?)sc.LINECD ?? DBNull.Value),
                new OracleParameter("WORK",  (object?)sc.WORKCD ?? DBNull.Value),
                new OracleParameter("EMP",   (object?)sc.EMPCD  ?? DBNull.Value));
        }
    }

    // Bản dùng chung transaction (scope) — cho nhánh isLimitedEdit cần UPDATE + replace scope
    // atomic với nhau. Logic giống hệt overload trên, chỉ khác route DB call qua scope.
    private async Task ReplaceScopesAsync(OracleTxScope scope, int surveyId, List<SaveScopeRequest> scopes)
    {
        await _db.ExecuteNonQueryAsync(scope,
            "DELETE FROM HRMS.HR_SURVEY_SCOPE WHERE SURVEY_ID = :SID",
            new OracleParameter("SID", surveyId));

        foreach (var sc in scopes)
        {
            const string sqlSc = @"
                INSERT INTO HRMS.HR_SURVEY_SCOPE
                    (SURVEY_ID, SCOPE_TYPE, DEPTCD, LINECD, WORKCD, EMPCD, INST_DT)
                VALUES
                    (:SID, :STYPE, :DEPT, :LINE, :WORK, :EMP, SYSDATE)";
            await _db.ExecuteNonQueryAsync(scope, sqlSc,
                new OracleParameter("SID",   surveyId),
                new OracleParameter("STYPE", sc.SCOPE_TYPE),
                new OracleParameter("DEPT",  (object?)sc.DEPTCD ?? DBNull.Value),
                new OracleParameter("LINE",  (object?)sc.LINECD ?? DBNull.Value),
                new OracleParameter("WORK",  (object?)sc.WORKCD ?? DBNull.Value),
                new OracleParameter("EMP",   (object?)sc.EMPCD  ?? DBNull.Value));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  STATE MACHINE
    // ═══════════════════════════════════════════════════════════════

    // Transitions do HR bấm nút: chỉ những chuyển này expose HTTP.
    // (SCHEDULED→ACTIVE, ACTIVE→EXPIRED, PAUSED→EXPIRED do batch job)
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions =
        new()
        {
            ["DRAFT"]     = new() { "SCHEDULED", "DELETED" },
            ["SCHEDULED"] = new() { "PAUSED", "CANCELLED", "DELETED" },
            ["ACTIVE"]    = new() { "PAUSED", "CANCELLED" },
            ["PAUSED"]    = new() { "ACTIVE", "CANCELLED" },
            ["EXPIRED"]   = new() { "DELETED" },
            ["CANCELLED"] = new() { "DELETED" },
        };

    public async Task<(bool ok, string? error)> ChangeStatusAsync(int id, string newStatus, string? actorEmpCd)
    {
        var current = await GetStatusAsync(id);
        if (current == null) return (false, "Không tìm thấy survey");

        if (!AllowedTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(newStatus))
            return (false, $"Không được chuyển {current} → {newStatus}");

        // Publish DRAFT → SCHEDULED cần validate
        if (current == "DRAFT" && newStatus == "SCHEDULED")
        {
            var err = await ValidateBeforePublishAsync(id);
            if (err != null) return (false, err);
        }

        const string sql = @"
            UPDATE HRMS.HR_SURVEY
               SET STATUS       = :NEW_STATUS,
                   UPDT_ID      = :ACTOR,
                   PUBLISHED_DT = CASE WHEN :NEW_STATUS = 'SCHEDULED' THEN SYSDATE ELSE PUBLISHED_DT END,
                   PUBLISHED_BY = CASE WHEN :NEW_STATUS = 'SCHEDULED' THEN :ACTOR  ELSE PUBLISHED_BY END
             WHERE ID = :ID";
        await _db.ExecuteNonQueryAsync(sql,
            new OracleParameter("NEW_STATUS", newStatus),
            new OracleParameter("ACTOR",      (object?)actorEmpCd ?? DBNull.Value),
            new OracleParameter("ID",         id));

        return (true, null);
    }

    private async Task<string?> ValidateBeforePublishAsync(int surveyId)
    {
        const string sql = @"
            SELECT S.SURVEY_TYPE, S.START_DATE, S.END_DATE, S.PASS_SCORE, S.RECIPIENT_MODE,
                   (SELECT COUNT(*) FROM HRMS.HR_SURVEY_QUESTION Q WHERE Q.SURVEY_ID = S.ID) AS Q_COUNT,
                   (SELECT COUNT(*) FROM HRMS.HR_SURVEY_SCOPE   SC WHERE SC.SURVEY_ID = S.ID) AS SC_COUNT
              FROM HRMS.HR_SURVEY S
             WHERE S.ID = :ID";
        var rows = await _db.ExecuteQueryAsync(sql, r => new
        {
            Type       = r["SURVEY_TYPE"]?.ToString() ?? "POLL",
            StartDate  = r["START_DATE"] as DateTime?,
            EndDate    = r["END_DATE"]   as DateTime?,
            PassScore  = r["PASS_SCORE"] is DBNull ? (decimal?)null : Convert.ToDecimal(r["PASS_SCORE"]),
            Mode       = r["RECIPIENT_MODE"]?.ToString() ?? "ALL",
            QCount     = Convert.ToInt32(r["Q_COUNT"]),
            ScCount    = Convert.ToInt32(r["SC_COUNT"]),
        }, new OracleParameter("ID", surveyId));

        var s = rows.FirstOrDefault();
        if (s == null) return "Không tìm thấy survey";
        if (s.QCount == 0) return "Chưa có câu hỏi nào";
        if (s.StartDate == null || s.EndDate == null) return "Chưa nhập START_DATE / END_DATE";
        if (s.EndDate < s.StartDate) return "END_DATE phải >= START_DATE";
        if (s.Type == "QUIZ" && s.PassScore == null) return "QUIZ cần nhập PASS_SCORE";
        if (s.Mode != "ALL" && s.ScCount == 0) return "Mode " + s.Mode + " nhưng chưa có scope row";

        // QUIZ không được có câu hỏi tự luận (TEXT) hoặc rating vì không chấm điểm được.
        if (s.Type == "QUIZ")
        {
            var badRows = await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) C FROM HRMS.HR_SURVEY_QUESTION WHERE SURVEY_ID = :ID AND QUESTION_TYPE IN ('TEXT','RATING')",
                r => Convert.ToInt32(r["C"]),
                new OracleParameter("ID", surveyId));
            if (badRows.FirstOrDefault() > 0)
                return "QUIZ không cho phép câu hỏi loại TEXT (tự luận) hoặc RATING — vì không chấm điểm được";
        }
        return null;
    }

    // Xoá 1 response (kèm answer) của 1 EMPCD trong 1 survey — dùng khi HR ghi nhận
    // response bị nhập nhầm (vd quên logout tài khoản dùng chung → người sau làm survey
    // bị gán nhầm EMPCD). Không đổi HR_SURVEY_RECIPIENT nên nhân viên có thể làm lại.
    public async Task<(bool ok, string? error)> DeleteResponseAsync(int surveyId, string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return (false, "Thiếu EMPCD");

        await using var scope = await _db.BeginTransactionAsync();

        var respIds = await _db.ExecuteQueryAsync(scope,
            "SELECT ID FROM HRMS.HR_SURVEY_RESPONSE WHERE SURVEY_ID = :SID AND EMPCD = :EMPCD",
            r => Convert.ToInt32(r["ID"]),
            new OracleParameter("SID", surveyId), new OracleParameter("EMPCD", empcd));

        if (respIds.Count == 0) return (false, "Không tìm thấy response của nhân viên này trong survey");

        await _db.ExecuteNonQueryAsync(scope,
            "DELETE FROM HRMS.HR_SURVEY_ANSWER WHERE RESPONSE_ID = :RID",
            new OracleParameter("RID", respIds[0]));

        await _db.ExecuteNonQueryAsync(scope,
            "DELETE FROM HRMS.HR_SURVEY_RESPONSE WHERE ID = :RID",
            new OracleParameter("RID", respIds[0]));

        await scope.CommitAsync();
        return (true, null);
    }

    // Reset hàng loạt: HR dán danh sách EMPCD, xoá response để những người đó làm lại survey từ đầu.
    public async Task<(int deletedCount, List<string> notFound)> BulkDeleteResponseAsync(int surveyId, List<string> empcds)
    {
        var distinctEmpcds = empcds.Where(e => !string.IsNullOrWhiteSpace(e))
                                    .Select(e => e.Trim())
                                    .Distinct()
                                    .ToList();
        if (distinctEmpcds.Count == 0) return (0, new List<string>());

        await using var scope = await _db.BeginTransactionAsync();

        const int chunkSize = 500;
        var found = new List<(string Empcd, int RespId)>();
        foreach (var chunk in distinctEmpcds.Chunk(chunkSize))
        {
            var inClause = string.Join(",", chunk.Select((_, idx) => $":E{idx}"));
            var pars = new List<OracleParameter> { new OracleParameter("SID", surveyId) };
            pars.AddRange(chunk.Select((e, idx) => new OracleParameter($"E{idx}", e)));

            var rows = await _db.ExecuteQueryAsync(scope,
                $"SELECT EMPCD, ID FROM HRMS.HR_SURVEY_RESPONSE WHERE SURVEY_ID = :SID AND EMPCD IN ({inClause})",
                r => (Convert.ToString(r["EMPCD"]) ?? "", Convert.ToInt32(r["ID"])),
                pars.ToArray());
            found.AddRange(rows);
        }

        foreach (var chunk in found.Select(f => f.RespId).Chunk(chunkSize))
        {
            var inClause = string.Join(",", chunk.Select((_, idx) => $":R{idx}"));
            // OracleParameter không dùng lại được giữa 2 OracleCommand khác nhau (kể cả command trước đã Dispose)
            // → phải tạo mảng param riêng cho mỗi lệnh DELETE.
            OracleParameter[] MakeParams() => chunk.Select((id, idx) => new OracleParameter($"R{idx}", id)).ToArray();
            await _db.ExecuteNonQueryAsync(scope, $"DELETE FROM HRMS.HR_SURVEY_ANSWER WHERE RESPONSE_ID IN ({inClause})", MakeParams());
            await _db.ExecuteNonQueryAsync(scope, $"DELETE FROM HRMS.HR_SURVEY_RESPONSE WHERE ID IN ({inClause})", MakeParams());
        }

        await scope.CommitAsync();

        var foundEmpcds = found.Select(f => f.Empcd).ToHashSet();
        var notFound = distinctEmpcds.Where(e => !foundEmpcds.Contains(e)).ToList();
        return (found.Count, notFound);
    }

    private async Task<string?> GetStatusAsync(int id)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => r["STATUS"]?.ToString(),
            new OracleParameter("ID", id));
        return rows.FirstOrDefault();
    }

    private record SurveyStatusLang(string Status, string Lang);

    private async Task<SurveyStatusLang?> GetStatusAndLangAsync(int id)
    {
        var rows = await _db.ExecuteQueryAsync(
            "SELECT STATUS, LANG FROM HRMS.HR_SURVEY WHERE ID = :ID",
            r => new SurveyStatusLang(
                r["STATUS"]?.ToString() ?? "",
                r["LANG"]?.ToString()   ?? "VI"),
            new OracleParameter("ID", id));
        return rows.FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════
    //  MAPPERS
    // ═══════════════════════════════════════════════════════════════

    private static SurveyModel MapSurveyLight(OracleDataReader r)
    {
        var m = new SurveyModel
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
        // Optional aggregate columns (list query)
        if (HasCol(r, "QUESTION_COUNT"))  m.QUESTION_COUNT  = Convert.ToInt32(r["QUESTION_COUNT"]);
        if (HasCol(r, "RECIPIENT_COUNT")) m.RECIPIENT_COUNT = Convert.ToInt32(r["RECIPIENT_COUNT"]);
        if (HasCol(r, "SUBMITTED_COUNT")) m.SUBMITTED_COUNT = Convert.ToInt32(r["SUBMITTED_COUNT"]);
        return m;
    }

    private static bool HasCol(OracleDataReader r, string name)
    {
        for (int i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
