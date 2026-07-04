namespace HR_web.Models.Survey;

// View models — mirror HR_api DTOs, chỉ giữ field cần cho UI.

public class SurveyModel
{
    public int       ID              { get; set; }
    public string    TITLE           { get; set; } = "";
    public string?   DESCRIPTION     { get; set; }
    public string    SURVEY_TYPE     { get; set; } = "POLL";
    public string    LANG            { get; set; } = "VI";
    public string    STATUS          { get; set; } = "DRAFT";
    public DateTime? START_DATE      { get; set; }
    public DateTime? END_DATE        { get; set; }
    public string    RECIPIENT_MODE  { get; set; } = "ALL";
    public decimal?  PASS_SCORE      { get; set; }
    // Aggregate (list view)
    public int       RECIPIENT_COUNT { get; set; }
    public int       SUBMITTED_COUNT { get; set; }
    public int       QUESTION_COUNT  { get; set; }
    public List<SurveyQuestionModel> QUESTIONS { get; set; } = new();
    public List<SurveyScopeModel>    SCOPES    { get; set; } = new();
}

public class SurveyScopeModel
{
    public int     ID          { get; set; }
    public int     SURVEY_ID   { get; set; }
    public string  SCOPE_TYPE  { get; set; } = "";
    public string? DEPTCD      { get; set; }
    public string? LINECD      { get; set; }
    public string? WORKCD      { get; set; }
    public string? EMPCD       { get; set; }
}

public class SurveyQuestionModel
{
    public int     ID            { get; set; }
    public int     SURVEY_ID     { get; set; }
    public string  QUESTION_TEXT { get; set; } = "";
    public string  QUESTION_TYPE { get; set; } = "SINGLE";
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
    public int?   IS_CORRECT    { get; set; }
}

public class SurveyResponseModel
{
    public int       ID        { get; set; }
    public int       SURVEY_ID { get; set; }
    public string    EMPCD     { get; set; } = "";
    public string    STATUS    { get; set; } = "IN_PROGRESS";
    public DateTime  START_DT  { get; set; }
    public DateTime? SUBMIT_DT { get; set; }
    public decimal?  SCORE     { get; set; }
    public decimal?  MAX_SCORE { get; set; }
    public int?      IS_PASS   { get; set; }
    public List<SurveyAnswerModel> ANSWERS { get; set; } = new();
}

public class SurveyAnswerModel
{
    public int      ID                { get; set; }
    public int      RESPONSE_ID       { get; set; }
    public int      QUESTION_ID       { get; set; }
    public string?  ANSWER_OPTION_IDS { get; set; }
    public string?  ANSWER_TEXT       { get; set; }
    public decimal? ANSWER_NUMBER     { get; set; }
}

public class SurveySubmitResultModel
{
    public bool     SUCCESS    { get; set; }
    public decimal? SCORE      { get; set; }
    public decimal? MAX_SCORE  { get; set; }
    public int?     IS_PASS    { get; set; }
    public string   SURVEY_TYPE { get; set; } = "POLL";
}

// Container cho action Do.cshtml
public class SurveyDoViewModel
{
    public SurveyModel?          Survey   { get; set; }
    public SurveyResponseModel?  Response { get; set; }
    public string?               Error    { get; set; }   // Message hiện nếu Paused / expired / không được phép
}
