namespace HR_api.Models.Training;

// HR_TRAINING_REVIEW (§10) — 1 review / student / class. Anonymous v1 skip — feedback lưu thô.
public class ReviewModel
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public int    CONTENT_RATING { get; set; }       // 1..5
    public int    ORGANIZATION_RATING { get; set; }  // 1..5
    public string? FEEDBACK_TEXT { get; set; }
    public DateTime INST_DT { get; set; }
    public List<TeacherRatingModel> TEACHER_RATINGS { get; set; } = new();
}

public class TeacherRatingModel
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";              // student
    public string TEACHER_EMPCD { get; set; } = "";
    public string? TEACHER_NAME { get; set; }
    public int    RATING { get; set; }                    // 1..5
    public string? FEEDBACK_TEXT { get; set; }
}

// Submit review: student gửi 1 lần cho cả class review + list teacher ratings
public class SubmitReviewRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public int    CONTENT_RATING { get; set; }
    public int    ORGANIZATION_RATING { get; set; }
    public string? FEEDBACK_TEXT { get; set; }
    public List<TeacherRatingItem> TEACHER_RATINGS { get; set; } = new();
}
public class TeacherRatingItem
{
    public string TEACHER_EMPCD { get; set; } = "";
    public int    RATING { get; set; }
    public string? FEEDBACK_TEXT { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  REPORT AGGREGATE (§14.4 satisfaction)
// ═══════════════════════════════════════════════════════════════

public class ReviewReportModel
{
    public int    CLASS_ID { get; set; }
    public int    RESPONSE_COUNT { get; set; }
    public decimal? AVG_CONTENT { get; set; }
    public decimal? AVG_ORGANIZATION { get; set; }
    public List<TeacherAggregateModel> TEACHER_AGGREGATES { get; set; } = new();
    public List<string> FEEDBACK_LIST { get; set; } = new();
}

public class TeacherAggregateModel
{
    public string TEACHER_EMPCD { get; set; } = "";
    public string? TEACHER_NAME { get; set; }
    public decimal AVG_RATING { get; set; }
    public int COUNT { get; set; }
}
