namespace HR_api.Models.Training;

// HR_TRAINING_TEST + QUESTION + OPTION (§6, DDL §10-12)
public class TestModel
{
    public int    ID { get; set; }
    public int?   CLASS_ID { get; set; }
    public int    IS_TEMPLATE { get; set; }
    public int?   TEMPLATE_COURSE_ID { get; set; }
    public string TITLE { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public string STATUS { get; set; } = "DRAFT";        // DRAFT | PUBLISHED | OPEN | CLOSED | GRADING | COMPLETED
    public int    DURATION_MINUTES { get; set; } = 20;
    public DateTime? AVAILABLE_FROM { get; set; }
    public DateTime? AVAILABLE_TO { get; set; }
    public decimal? PASS_SCORE { get; set; }
    public int    MAX_ATTEMPTS { get; set; } = 1;
    public string CREATED_BY { get; set; } = "";
    public DateTime? INST_DT { get; set; }
    public DateTime? UPDT_DT { get; set; }

    // Denorm for UI
    public string?   CLASS_NAME { get; set; }
    public string?   COURSE_TITLE { get; set; }
    public int?      QUESTION_COUNT { get; set; }
    public int?      ATTEMPT_COUNT { get; set; }

    // Student attempt info (§6)
    public string?   ATTEMPT_STATUS { get; set; }
    public decimal?  SCORE { get; set; }
    public decimal?  MAX_SCORE { get; set; }
}

public class TestQuestionModel
{
    public int?   ID { get; set; }
    public int    TEST_ID { get; set; }
    public string QUESTION_TEXT { get; set; } = "";
    public string QUESTION_TYPE { get; set; } = "SINGLE"; // SINGLE | MULTI | YESNO | DROPDOWN | TEXT
    public int    IS_REQUIRED { get; set; } = 1;
    public int    DISPLAY_ORDER { get; set; }
    public decimal POINTS { get; set; }
    public List<TestOptionModel> OPTIONS { get; set; } = new();
}

public class TestOptionModel
{
    public int?   ID { get; set; }
    public int    QUESTION_ID { get; set; }
    public string OPTION_TEXT { get; set; } = "";
    public int    DISPLAY_ORDER { get; set; }
    public int    IS_CORRECT { get; set; }        // KHÔNG gửi về student view (§6.5)
}

// ═══════════════════════════════════════════════════════════════
//  REQUESTS
// ═══════════════════════════════════════════════════════════════

public class SaveTestRequest
{
    public int?   ID { get; set; }
    public int?   CLASS_ID { get; set; }
    public int    IS_TEMPLATE { get; set; }
    public int?   TEMPLATE_COURSE_ID { get; set; }
    public string TITLE { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public int    DURATION_MINUTES { get; set; } = 20;
    public DateTime? AVAILABLE_FROM { get; set; }
    public DateTime? AVAILABLE_TO { get; set; }
    public decimal? PASS_SCORE { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

// Bulk replace toàn bộ Q + Options của 1 test (§6.1 teacher soạn nguyên bộ).
public class SaveTestQuestionsRequest
{
    public int    TEST_ID { get; set; }
    public List<TestQuestionModel> QUESTIONS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class ChangeTestStatusRequest
{
    public int       ID             { get; set; }
    public string    LOGIN_USER     { get; set; } = "";
    public DateTime? AVAILABLE_FROM { get; set; }
    public DateTime? AVAILABLE_TO   { get; set; }
}
