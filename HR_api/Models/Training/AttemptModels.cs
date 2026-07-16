namespace HR_api.Models.Training;

// HR_TRAINING_TEST_ATTEMPT (§6.2-6.5)
public class AttemptModel
{
    public int    ID { get; set; }
    public int    TEST_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public int    ATTEMPT_NO { get; set; } = 1;
    public string STATUS { get; set; } = "IN_PROGRESS"; // IN_PROGRESS | SUBMITTED | AUTO_SUBMITTED
    public DateTime START_DT { get; set; }
    public DateTime? SUBMIT_DT { get; set; }
    public DateTime EFFECTIVE_DEADLINE { get; set; }
    public decimal? SCORE { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public int?   IS_PASS { get; set; }
    public int    IS_GRADED { get; set; }
    public DateTime? INST_DT { get; set; }
    public DateTime? UPDT_DT { get; set; }
}

// Câu trả lời của student
public class AttemptAnswerModel
{
    public int    ID { get; set; }
    public int    ATTEMPT_ID { get; set; }
    public int    QUESTION_ID { get; set; }
    public string? ANSWER_OPTION_IDS { get; set; }   // CSV cho MULTI; single value SINGLE/YESNO/DROPDOWN
    public string? ANSWER_TEXT { get; set; }         // ESSAY
    public decimal? POINTS_AWARDED { get; set; }
    public string? GRADER_COMMENT { get; set; }
    public string? GRADED_BY { get; set; }
    public DateTime? GRADED_DT { get; set; }
}

// Aggregate cho student làm bài — meta + questions + attempt hiện tại + saved answers
public class TestForStudentView
{
    public TestModel? TEST { get; set; }
    public List<TestQuestionModel> QUESTIONS { get; set; } = new();   // IS_CORRECT đã strip §6.5
    public AttemptModel? ATTEMPT { get; set; }
    public List<AttemptAnswerModel> ANSWERS { get; set; } = new();
    public int SECONDS_REMAINING { get; set; }                        // computed từ EFFECTIVE_DEADLINE - SYSDATE
}

// Kết quả sau submit (§6.5)
public class MyResultView
{
    public AttemptModel? ATTEMPT { get; set; }
    public string? PASS_FAIL { get; set; }        // "PASS" | "FAIL" | null (không có PASS_SCORE)
    public int?    CLASS_ID { get; set; }         // lớp của test — FE dùng cho nút "Trở lại chi tiết lớp học"
    // Câu tự luận của chính học viên: đề + bài làm + điểm/nhận xét GV (POINTS_AWARDED null = chưa chấm).
    // TEXT không có đáp án mẫu nên không vi phạm rule "không hiển đáp án đúng" §6.5.
    public List<EssayItem> ESSAYS { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
//  REQUESTS
// ═══════════════════════════════════════════════════════════════

public class StartAttemptRequest
{
    public int    TEST_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? IP_ADDRESS { get; set; }
    public string? USER_AGENT { get; set; }
}

public class SaveAnswerRequest
{
    public int    ATTEMPT_ID { get; set; }
    public int    QUESTION_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? ANSWER_OPTION_IDS { get; set; }
    public string? ANSWER_TEXT { get; set; }
}

public class SubmitAttemptRequest
{
    public int    ATTEMPT_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

// HR cấp thêm 1 lượt thi cho học viên (bận / lỗi kỹ thuật / thi rớt)
public class GrantRetakeRequest
{
    public int     TEST_ID { get; set; }
    public string  EMPCD { get; set; } = "";
    public string? REASON { get; set; }
    public string  LOGIN_USER { get; set; } = "";
}

// Teacher chấm 1 câu ESSAY (§6.4)
public class GradeAnswerRequest
{
    public int    ANSWER_ID { get; set; }
    public decimal POINTS_AWARDED { get; set; }
    public string? GRADER_COMMENT { get; set; }
    public string LOGIN_USER { get; set; } = "";      // teacher EMPCD
}

// Aggregate cho teacher grade view — attempt + ESSAY answers cần chấm
public class GradingItemModel
{
    public AttemptModel? ATTEMPT { get; set; }
    public List<EssayItem> ESSAYS { get; set; } = new();
}

public class EssayItem
{
    public int    ANSWER_ID { get; set; }
    public int    QUESTION_ID { get; set; }
    public string QUESTION_TEXT { get; set; } = "";
    public decimal QUESTION_POINTS { get; set; }
    public string? ANSWER_TEXT { get; set; }
    public decimal? POINTS_AWARDED { get; set; }
    public string? GRADER_COMMENT { get; set; }
}
