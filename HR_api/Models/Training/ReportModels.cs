namespace HR_api.Models.Training;

// §14.1 Report Class overview
public class ReportClassModel
{
    public int    CLASS_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string COURSE_TITLE { get; set; } = "";
    public string? CLASS_STATUS { get; set; }
    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }
    public string? PRIMARY_TEACHER_NAME { get; set; }
    public int    TOTAL_SESSIONS { get; set; }
    public int    COMPLETED_SESSIONS { get; set; }
    public int    TOTAL_TESTS { get; set; }

    public int    ENROLLED_COUNT { get; set; }
    public int    ASSIGNED_COUNT { get; set; }         // SOURCE='ASSIGNED' (§3.3 mandatory)
    public int    SELF_REGISTER_COUNT { get; set; }
    public int    DROPPED_COUNT { get; set; }
    public int    COMPLETED_COUNT { get; set; }
    public int    FAILED_COUNT { get; set; }
    public int    CERTIFIED_COUNT { get; set; }
    public int    RETAKE_1_COUNT { get; set; }        // Thi lại lần 1 (Lượt 2)
    public int    RETAKE_2_COUNT { get; set; }        // Thi lại lần 2 (Lượt 3)
    public int    RETAKE_3_COUNT { get; set; }        // Thi lại lần 3+ (Lượt 4+)
    public decimal? AVG_ATTENDANCE_PERCENT { get; set; }
    public decimal? AVG_FINAL_SCORE { get; set; }
    public int    EXCELLENT_ATTENDANCE_COUNT { get; set; }
    public int    AT_RISK_ATTENDANCE_COUNT { get; set; }

    // Histogram điểm final test (§14.1)
    public List<ScoreBucket> SCORE_HISTOGRAM { get; set; } = new();
    // Per-group breakdown (nếu Class có group §5b)
    public List<GroupBreakdown> GROUP_BREAKDOWN { get; set; } = new();
}

public class ScoreBucket
{
    public string LABEL { get; set; } = "";     // "0-2", "2-4", "4-6", "6-8", "8-10"
    public int    COUNT { get; set; }
}

// Report cấp Khóa học — bảng liệt kê từng lớp thuộc khóa + 1 dòng tổng cộng cộng dồn cả khóa.
public class ReportCourseModel
{
    public int    COURSE_ID { get; set; }
    public string COURSE_TITLE { get; set; } = "";
    public List<ReportCourseClassRow> CLASSES { get; set; } = new();
    public ReportCourseClassRow TOTAL { get; set; } = new();
    public List<ReportCourseStudentRow> PASSED_STUDENTS { get; set; } = new();
    public List<ReportCourseStudentRow> FAILED_STUDENTS { get; set; } = new();
}

// Danh sách học viên đậu/rớt cả khóa (cộng dồn nhiều lớp) — EMPCD, tên, dept/line/work, học lớp nào
public class ReportCourseStudentRow
{
    public string  EMPCD      { get; set; } = "";
    public string? EMP_NAME   { get; set; }
    public string? DEPT_NAME  { get; set; }
    public string? LINE_NAME  { get; set; }
    public string? WORK_NAME  { get; set; }
    public string  CLASS_NAME { get; set; } = "";
}

public class ReportCourseClassRow
{
    public int?    CLASS_ID { get; set; }        // null cho dòng TOTAL
    public string  CLASS_NAME { get; set; } = "";
    public string? CLASS_STATUS { get; set; }
    public int     ENROLLED_COUNT { get; set; }
    public int     ASSIGNED_COUNT { get; set; }
    public int     SELF_REGISTER_COUNT { get; set; }
    public int     DROPPED_COUNT { get; set; }
    public int     COMPLETED_COUNT { get; set; }
    public int     FAILED_COUNT { get; set; }
    public int     CERTIFIED_COUNT { get; set; }
    public decimal? AVG_ATTENDANCE_PERCENT { get; set; }
    public decimal? AVG_FINAL_SCORE { get; set; }
}

public class GroupBreakdown
{
    public int?   GROUP_ID { get; set; }
    public string GROUP_NAME { get; set; } = "";
    public int    ENROLLED { get; set; }
    public int    COMPLETED { get; set; }
    public int    CERTIFIED { get; set; }
    public decimal? AVG_ATTENDANCE { get; set; }
}

// §14.2 Report Attendance — matrix EMPCD × Session
public class ReportAttendanceMatrix
{
    public List<AttendanceMatrixSession> SESSIONS { get; set; } = new();
    public List<AttendanceMatrixStudent> STUDENTS { get; set; } = new();
}

public class AttendanceMatrixSession
{
    public int SESSION_ID { get; set; }
    public int SESSION_NO { get; set; }
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
    public Dictionary<int, string> STATUS_PER_SESSION { get; set; } = new();
    // key = SESSION_ID, value = PRESENT|LATE|ABSENT|EXCUSED|"" (không thuộc group session này)
    public Dictionary<int, bool> SELF_CHECKIN_PER_SESSION { get; set; } = new();
    // key = SESSION_ID, value = true nếu học viên TỰ bấm điểm danh qua app (CHECKIN_TIME NOT NULL) —
    // để phân biệt với dòng do giáo viên tự chọn giùm, phục vụ minh bạch/công bằng khi tra soát.
}

// §14.3 Report Test — điểm từng học viên + trung bình + top 5 câu sai
public class ReportTestModel
{
    public int    TEST_ID { get; set; }
    public string TEST_TITLE { get; set; } = "";
    public decimal? PASS_SCORE { get; set; }
    public int    ATTEMPT_COUNT { get; set; }
    public int    PASS_COUNT { get; set; }
    public int    FAIL_COUNT { get; set; }
    public int    RETAKE_1_COUNT { get; set; }        // Thi lại lần 1 (Lượt 2)
    public int    RETAKE_2_COUNT { get; set; }        // Thi lại lần 2 (Lượt 3)
    public int    RETAKE_3_COUNT { get; set; }        // Thi lại lần 3+ (Lượt 4+)
    public decimal? AVG_SCORE { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public decimal? MIN_SCORE { get; set; }

    public List<TestScoreItem>  SCORES { get; set; } = new();
    public List<TestWrongItem>  TOP_WRONG_QUESTIONS { get; set; } = new();  // Mọi câu có người sai, sắp theo sai nhiều nhất trước
}

public class TestScoreItem
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public int    ATTEMPT_NO { get; set; } = 1;
    public decimal? SCORE { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public int?   IS_PASS { get; set; }
    public string STATUS { get; set; } = "";
    public DateTime? SUBMIT_DT { get; set; }
    public bool   HAS_PENDING_GRANT { get; set; }
}

public class TestWrongItem
{
    public int    QUESTION_ID { get; set; }
    public string QUESTION_TEXT { get; set; } = "";
    public string QUESTION_TYPE { get; set; } = "";
    public int    ATTEMPT_COUNT { get; set; }
    public int    WRONG_COUNT { get; set; }
    public decimal WRONG_PERCENT { get; set; }
    public List<WrongStudentItem> WRONG_STUDENTS { get; set; } = new();  // Ai sai câu này, cụ thể
}

public class WrongStudentItem
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public int    ATTEMPT_NO { get; set; }
}
