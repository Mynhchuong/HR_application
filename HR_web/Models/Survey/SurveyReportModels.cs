namespace HR_web.Models.Survey;

// View models — mirror HR_api DTOs cho Report.

public class SurveyReportOverviewModel
{
    public int       SURVEY_ID   { get; set; }
    public string    TITLE       { get; set; } = "";
    public string    SURVEY_TYPE { get; set; } = "";
    public string    LANG        { get; set; } = "";
    public string    STATUS      { get; set; } = "";
    public DateTime? START_DATE  { get; set; }
    public DateTime? END_DATE    { get; set; }

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
    public int[]?   RATING_DIST   { get; set; }
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

public class SurveyOptionRespondentModel
{
    public string    EMPCD     { get; set; } = "";
    public string?   FULL_NAME { get; set; }
    public string?   DEPTCD    { get; set; }
    public string?   LINECD    { get; set; }
    public string?   WORKCD    { get; set; }
    public DateTime? SUBMIT_DT { get; set; }
}

public class SurveyReportQuizModel
{
    public int     SURVEY_ID  { get; set; }
    public decimal AVG_SCORE  { get; set; }
    public int     PASS_COUNT { get; set; }
    public int     FAIL_COUNT { get; set; }
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

// Exempt (mirror HR_api SurveyExemptModel — cho page /SurveyExempt)
public class SurveyExemptModel
{
    public string    EMPCD          { get; set; } = "";
    public string    EXEMPT_TYPE    { get; set; } = "";
    public string?   NOTE           { get; set; }
    public DateTime  EFFECTIVE_DATE { get; set; }
    public int       IS_ACTIVE      { get; set; } = 1;
    public string?   INST_ID        { get; set; }
    public DateTime? INST_DT        { get; set; }
    public string?   UPDT_ID        { get; set; }
    public DateTime? UPDT_DT        { get; set; }
    public string?   FULL_NAME      { get; set; }
    public string?   DEPTCD         { get; set; }
    public string?   LINECD         { get; set; }
    public string?   WORKCD         { get; set; }
}

public class SurveyReportViewModel
{
    public SurveyReportOverviewModel? Overview     { get; set; }
    public SurveyReportQuizModel?     Quiz         { get; set; }
    public string?                    Error        { get; set; }
}

public class SurveyParticipantModel
{
    public string    EMPCD     { get; set; } = "";
    public string?   FULL_NAME { get; set; }
    public string?   DEPTCD    { get; set; }
    public string?   LINECD    { get; set; }
    public string?   WORKCD    { get; set; }
    public string?   DEPT_NAME { get; set; }
    public string?   LINE_NAME { get; set; }
    public string?   WORK_NAME { get; set; }
    public string    STATUS    { get; set; } = "NOT_STARTED";
    public decimal?  SCORE     { get; set; }
    public decimal?  MAX_SCORE { get; set; }
    public int?      IS_PASS   { get; set; }
    public DateTime? SUBMIT_DT { get; set; }
    public DateTime? START_DT  { get; set; }
}

public class SurveyParticipantPageModel
{
    public List<SurveyParticipantModel> ITEMS     { get; set; } = new();
    public int                          TOTAL     { get; set; }
    public int                          PAGE      { get; set; }
    public int                          PAGE_SIZE { get; set; }
}

public class SurveyParticipantDetailModel
{
    public SurveyParticipantModel        INFO      { get; set; } = new();
    public List<SurveyParticipantQModel> QUESTIONS { get; set; } = new();
    public List<SurveyParticipantAnswer> ANSWERS   { get; set; } = new();
}

public class SurveyParticipantQModel
{
    public int     ID            { get; set; }
    public string  QUESTION_TEXT { get; set; } = "";
    public string  QUESTION_TYPE { get; set; } = "SINGLE";
    public int     DISPLAY_ORDER { get; set; }
    public decimal POINTS        { get; set; }
    public int     IS_REQUIRED   { get; set; } = 1;
    public List<SurveyParticipantOptionModel> OPTIONS { get; set; } = new();
}

public class SurveyParticipantOptionModel
{
    public int    ID            { get; set; }
    public string OPTION_TEXT   { get; set; } = "";
    public int    DISPLAY_ORDER { get; set; }
    public int?   IS_CORRECT    { get; set; }
}

public class SurveyParticipantAnswer
{
    public int      QUESTION_ID       { get; set; }
    public string?  ANSWER_OPTION_IDS { get; set; }
    public string?  ANSWER_TEXT       { get; set; }
    public decimal? ANSWER_NUMBER     { get; set; }
}

public class SurveyParticipantPageViewModel
{
    public int                   SURVEY_ID    { get; set; }
    public string                TITLE        { get; set; } = "";
    public string                SURVEY_TYPE  { get; set; } = "POLL";
    public SurveyParticipantPageModel Page    { get; set; } = new();
    public string?               FilterDept   { get; set; }
    public string?               FilterLine   { get; set; }
    public string?               FilterWork   { get; set; }
    public string?               FilterEmpcd  { get; set; }
    public string?               FilterStatus { get; set; }
    public List<HR_web.Models.DropdownModel> Depts { get; set; } = new();
}

public class SurveyFullAnswerRecordModel
{
    public string    EMPCD       { get; set; } = "";
    public string?   FULL_NAME   { get; set; }
    public string?   DEPTCD      { get; set; }
    public string?   DEPT_NAME   { get; set; }
    public string?   LINECD      { get; set; }
    public string?   LINE_NAME   { get; set; }
    public string?   WORKCD      { get; set; }
    public string?   WORK_NAME   { get; set; }
    public DateTime? SUBMIT_DT   { get; set; }
    public int       QUESTION_ID { get; set; }
    public string?   ANSWER_TEXT { get; set; }
}
