namespace HR_web.Models.Training;

// Mirror của HR_api Models/Training/ReportModels.cs — nhận từ API

public class ReportClassModel
{
    public int    CLASS_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string COURSE_TITLE { get; set; } = "";
    public string? CLASS_STATUS { get; set; }
    public int    ENROLLED_COUNT { get; set; }
    public int    ASSIGNED_COUNT { get; set; }
    public int    SELF_REGISTER_COUNT { get; set; }
    public int    DROPPED_COUNT { get; set; }
    public int    COMPLETED_COUNT { get; set; }
    public int    FAILED_COUNT { get; set; }
    public int    CERTIFIED_COUNT { get; set; }
    public decimal? AVG_ATTENDANCE_PERCENT { get; set; }
    public decimal? AVG_FINAL_SCORE { get; set; }
    public List<ScoreBucket> SCORE_HISTOGRAM { get; set; } = new();
    public List<GroupBreakdown> GROUP_BREAKDOWN { get; set; } = new();
}
public class ScoreBucket { public string LABEL { get; set; } = ""; public int COUNT { get; set; } }
public class GroupBreakdown
{
    public int?   GROUP_ID { get; set; }
    public string GROUP_NAME { get; set; } = "";
    public int    ENROLLED { get; set; }
    public int    COMPLETED { get; set; }
    public int    CERTIFIED { get; set; }
    public decimal? AVG_ATTENDANCE { get; set; }
}

public class ReportAttendanceMatrix
{
    public List<AttendanceMatrixSession> SESSIONS { get; set; } = new();
    public List<AttendanceMatrixStudent> STUDENTS { get; set; } = new();
}
public class AttendanceMatrixSession
{
    public int    SESSION_ID { get; set; }
    public int    SESSION_NO { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string? TOPIC { get; set; }
    public string SESSION_STATUS { get; set; } = "";
    public int?   GROUP_ID { get; set; }
    public string? GROUP_NAME { get; set; }
}
public class AttendanceMatrixStudent
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public int?   GROUP_ID { get; set; }
    public string? GROUP_NAME { get; set; }
    public decimal ATTENDANCE_PERCENT { get; set; }
    public Dictionary<string, string> STATUS_PER_SESSION { get; set; } = new();
    // JSON deserialize sẽ decode int keys → string. Convert khi query.
}

public class ReportTestModel
{
    public int    TEST_ID { get; set; }
    public string TEST_TITLE { get; set; } = "";
    public decimal? PASS_SCORE { get; set; }
    public int    ATTEMPT_COUNT { get; set; }
    public int    PASS_COUNT { get; set; }
    public int    FAIL_COUNT { get; set; }
    public decimal? AVG_SCORE { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public decimal? MIN_SCORE { get; set; }
    public List<TestScoreItem>  SCORES { get; set; } = new();
    public List<TestWrongItem>  TOP_WRONG_QUESTIONS { get; set; } = new();
}
public class TestScoreItem
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public decimal? SCORE { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public int?   IS_PASS { get; set; }
    public string STATUS { get; set; } = "";
    public DateTime? SUBMIT_DT { get; set; }
}
public class TestWrongItem
{
    public int    QUESTION_ID { get; set; }
    public string QUESTION_TEXT { get; set; } = "";
    public string QUESTION_TYPE { get; set; } = "";
    public int    ATTEMPT_COUNT { get; set; }
    public int    WRONG_COUNT { get; set; }
    public decimal WRONG_PERCENT { get; set; }
}

// §14.4 Satisfaction
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

// Response wrappers
public class ReportClassResponse   { public bool success { get; set; } public string? message { get; set; } public ReportClassModel? data { get; set; } }
public class AttendanceResponse    { public bool success { get; set; } public string? message { get; set; } public ReportAttendanceMatrix? data { get; set; } }
public class ReportTestResponse    { public bool success { get; set; } public string? message { get; set; } public ReportTestModel? data { get; set; } }
public class ReviewReportResponse  { public bool success { get; set; } public string? message { get; set; } public ReviewReportModel? data { get; set; } }

// Class list dropdown (reuse)
public class ClassListItem
{
    public int    ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string? STATUS { get; set; }
    public string? COURSE_TITLE { get; set; }
    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }
}
public class ClassListResponse { public bool success { get; set; } public List<ClassListItem>? data { get; set; } }
