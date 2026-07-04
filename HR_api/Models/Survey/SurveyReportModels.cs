namespace HR_api.Models.Survey;

public class SurveyReportOverviewModel
{
    public int       SURVEY_ID          { get; set; }
    public string    TITLE              { get; set; } = "";
    public string    SURVEY_TYPE        { get; set; } = "";
    public string    LANG               { get; set; } = "";
    public string    STATUS             { get; set; } = "";
    public DateTime? START_DATE         { get; set; }
    public DateTime? END_DATE           { get; set; }

    public int TOTAL_RECIPIENTS   { get; set; }
    public int SUBMITTED_COUNT    { get; set; }
    public int AUTO_SUBMIT_COUNT  { get; set; }
    public int ILLITERATE_COUNT   { get; set; }
    public int IN_PROGRESS_COUNT  { get; set; }
    public int NOT_STARTED_COUNT  { get; set; }

    public List<SurveyReportQuestionModel> QUESTIONS { get; set; } = new();
}

public class SurveyReportQuestionModel
{
    public int      QUESTION_ID   { get; set; }
    public string   QUESTION_TEXT { get; set; } = "";
    public string   QUESTION_TYPE { get; set; } = "";
    public int      DISPLAY_ORDER { get; set; }
    public decimal  POINTS        { get; set; }
    public List<SurveyReportOptionModel> OPTIONS { get; set; } = new();
    public int[]?   RATING_DIST   { get; set; }   // Rating: 5 buckets [1sao..5sao]
    public int      TEXT_COUNT    { get; set; }
}

public class SurveyReportOptionModel
{
    public int    OPTION_ID   { get; set; }
    public string OPTION_TEXT { get; set; } = "";
    public int    COUNT       { get; set; }
}

public class SurveyReportTextAnswerModel
{
    public int       RESPONSE_ID { get; set; }
    public string    EMPCD       { get; set; } = "";
    public string?   FULL_NAME   { get; set; }
    public string?   ANSWER_TEXT { get; set; }
    public DateTime? INST_DT     { get; set; }
}

public class SurveyReportQuizModel
{
    public int      SURVEY_ID  { get; set; }
    public decimal  AVG_SCORE  { get; set; }
    public int      PASS_COUNT { get; set; }
    public int      FAIL_COUNT { get; set; }
    public List<SurveyReportQuizUserModel> USERS { get; set; } = new();
}

public class SurveyReportQuizUserModel
{
    public string    EMPCD     { get; set; } = "";
    public string?   FULL_NAME { get; set; }
    public string?   DEPTCD    { get; set; }
    public string?   LINECD    { get; set; }
    public string?   WORKCD    { get; set; }
    public string    STATUS    { get; set; } = "";
    public decimal?  SCORE     { get; set; }
    public decimal?  MAX_SCORE { get; set; }
    public int?      IS_PASS   { get; set; }
    public DateTime? SUBMIT_DT { get; set; }
}

public class SurveyReportIlliterateModel
{
    public string    EMPCD          { get; set; } = "";
    public string?   FULL_NAME      { get; set; }
    public string?   DEPTCD         { get; set; }
    public string?   LINECD         { get; set; }
    public string?   WORKCD         { get; set; }
    public DateTime  EFFECTIVE_DATE { get; set; }
    public string?   NOTE           { get; set; }
    public DateTime? INST_DT        { get; set; }
}
