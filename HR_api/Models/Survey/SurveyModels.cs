namespace HR_api.Models.Survey;

// ═══════════════════════════════════════════════════════════════
//  RESPONSE MODELS
// ═══════════════════════════════════════════════════════════════

public class SurveyModel
{
    public int       ID              { get; set; }
    public string    TITLE           { get; set; } = "";
    public string?   DESCRIPTION     { get; set; }
    public string    SURVEY_TYPE     { get; set; } = "POLL";   // POLL | QUIZ
    public string    LANG            { get; set; } = "VI";     // VI | EN
    public string    STATUS          { get; set; } = "DRAFT";
    public DateTime? START_DATE      { get; set; }
    public DateTime? END_DATE        { get; set; }
    public string    RECIPIENT_MODE  { get; set; } = "ALL";
    public decimal?  PASS_SCORE      { get; set; }
    public DateTime? PUBLISHED_DT    { get; set; }
    public string?   PUBLISHED_BY    { get; set; }
    public string?   INST_ID         { get; set; }
    public DateTime? INST_DT         { get; set; }
    public string?   UPDT_ID         { get; set; }
    public DateTime? UPDT_DT         { get; set; }

    // Aggregated
    public int  QUESTION_COUNT  { get; set; }
    public int  RECIPIENT_COUNT { get; set; }
    public int  SUBMITTED_COUNT { get; set; }

    // Detail
    public List<SurveyQuestionModel> QUESTIONS { get; set; } = new();
    public List<SurveyScopeModel>    SCOPES    { get; set; } = new();
}

public class SurveyQuestionModel
{
    public int     ID            { get; set; }
    public int     SURVEY_ID     { get; set; }
    public string  QUESTION_TEXT { get; set; } = "";
    public string  QUESTION_TYPE { get; set; } = "SINGLE";  // SINGLE|MULTI|YESNO|RATING|TEXT|DROPDOWN
    public int     IS_REQUIRED   { get; set; } = 1;
    public int     DISPLAY_ORDER { get; set; }
    public decimal POINTS        { get; set; }
    public List<SurveyOptionModel> OPTIONS { get; set; } = new();
}

public class SurveyOptionModel
{
    public int    ID            { get; set; }
    public int    QUESTION_ID   { get; set; }
    public string OPTION_TEXT   { get; set; } = "";
    public int    DISPLAY_ORDER { get; set; }
    // Chỉ trả về khi HR admin xem; user không thấy IS_CORRECT
    public int?   IS_CORRECT    { get; set; }
}

public class SurveyScopeModel
{
    public int     ID          { get; set; }
    public int     SURVEY_ID   { get; set; }
    public string  SCOPE_TYPE  { get; set; } = "";      // DEPT_LINE_WORK | EMPCD
    public string? DEPTCD      { get; set; }
    public string? LINECD      { get; set; }
    public string? WORKCD      { get; set; }
    public string? EMPCD       { get; set; }
}

public class SurveyResponseModel
{
    public int      ID          { get; set; }
    public int      SURVEY_ID   { get; set; }
    public string   EMPCD       { get; set; } = "";
    public string   STATUS      { get; set; } = "IN_PROGRESS";
    public DateTime START_DT    { get; set; }
    public DateTime? SUBMIT_DT  { get; set; }
    public decimal? SCORE       { get; set; }
    public decimal? MAX_SCORE   { get; set; }
    public int?     IS_PASS     { get; set; }
    public List<SurveyAnswerModel> ANSWERS { get; set; } = new();
}

public class SurveyAnswerModel
{
    public int      ID                { get; set; }
    public int      RESPONSE_ID       { get; set; }
    public int      QUESTION_ID       { get; set; }
    public string?  ANSWER_OPTION_IDS { get; set; }    // CSV
    public string?  ANSWER_TEXT       { get; set; }
    public decimal? ANSWER_NUMBER     { get; set; }
}

public class SurveyExemptModel
{
    public string    EMPCD          { get; set; } = "";
    public string    EXEMPT_TYPE    { get; set; } = "";   // ILLITERATE|NO_PHONE|BOTH
    public string?   NOTE           { get; set; }
    public DateTime  EFFECTIVE_DATE { get; set; }
    public int       IS_ACTIVE      { get; set; } = 1;
    public string?   INST_ID        { get; set; }
    public DateTime? INST_DT        { get; set; }
    public string?   UPDT_ID        { get; set; }
    public DateTime? UPDT_DT        { get; set; }
    // Enriched (join khi list)
    public string?   FULL_NAME      { get; set; }
    public string?   DEPTCD         { get; set; }
    public string?   LINECD         { get; set; }
    public string?   WORKCD         { get; set; }
}

public class SurveySubmitResultModel
{
    public bool    SUCCESS    { get; set; }
    public decimal? SCORE     { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public int?    IS_PASS    { get; set; }
    public string  SURVEY_TYPE { get; set; } = "POLL";
}

// ═══════════════════════════════════════════════════════════════
//  REQUEST MODELS
// ═══════════════════════════════════════════════════════════════

public class SaveSurveyRequest
{
    public int?      ID             { get; set; }        // null/0 = insert
    public string    TITLE          { get; set; } = "";
    public string?   DESCRIPTION    { get; set; }
    public string    SURVEY_TYPE    { get; set; } = "POLL";
    public string    LANG           { get; set; } = "VI";
    public DateTime? START_DATE     { get; set; }
    public DateTime? END_DATE       { get; set; }
    public string    RECIPIENT_MODE { get; set; } = "ALL";
    public decimal?  PASS_SCORE     { get; set; }
    public string?   LOGIN_USER     { get; set; }        // EmpCd HR
    public List<SaveQuestionRequest> QUESTIONS { get; set; } = new();
    public List<SaveScopeRequest>    SCOPES    { get; set; } = new();
}

public class SaveQuestionRequest
{
    public int?     ID            { get; set; }
    public string   QUESTION_TEXT { get; set; } = "";
    public string   QUESTION_TYPE { get; set; } = "SINGLE";
    public int      IS_REQUIRED   { get; set; } = 1;
    public int      DISPLAY_ORDER { get; set; }
    public decimal  POINTS        { get; set; }
    public List<SaveOptionRequest> OPTIONS { get; set; } = new();
}

public class SaveOptionRequest
{
    public int?    ID            { get; set; }
    public string  OPTION_TEXT   { get; set; } = "";
    public int     DISPLAY_ORDER { get; set; }
    public int     IS_CORRECT    { get; set; }
}

public class SaveScopeRequest
{
    public string  SCOPE_TYPE { get; set; } = "";
    public string? DEPTCD     { get; set; }
    public string? LINECD     { get; set; }
    public string? WORKCD     { get; set; }
    public string? EMPCD      { get; set; }
}

public class SaveAnswerRequest
{
    public string   EMPCD             { get; set; } = "";
    public int      SURVEY_ID         { get; set; }
    public int      QUESTION_ID       { get; set; }
    public string?  ANSWER_OPTION_IDS { get; set; }
    public string?  ANSWER_TEXT       { get; set; }
    public decimal? ANSWER_NUMBER     { get; set; }
    public string?  IP_ADDRESS        { get; set; }
    public string?  USER_AGENT        { get; set; }
}

public class SubmitSurveyRequest
{
    public string EMPCD     { get; set; } = "";
    public int    SURVEY_ID { get; set; }
}

public class SkipIlliterateRequest
{
    public string EMPCD     { get; set; } = "";
    public int    SURVEY_ID { get; set; }
}

public class ChangeStatusRequest
{
    public int    ID         { get; set; }
    public string NEW_STATUS { get; set; } = "";       // SCHEDULED|PAUSED|ACTIVE|CANCELLED|DELETED
    public string? LOGIN_USER { get; set; }
}

public class SaveExemptRequest
{
    public string    EMPCD          { get; set; } = "";
    public string    EXEMPT_TYPE    { get; set; } = "";
    public string?   NOTE           { get; set; }
    public DateTime? EFFECTIVE_DATE { get; set; }
    public int       IS_ACTIVE      { get; set; } = 1;
    public string?   LOGIN_USER     { get; set; }
}

public class DeleteExemptRequest
{
    public string EMPCD       { get; set; } = "";
    public string EXEMPT_TYPE { get; set; } = "";
    public string? LOGIN_USER { get; set; }
}
