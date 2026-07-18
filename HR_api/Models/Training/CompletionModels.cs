namespace HR_api.Models.Training;

// Kết quả compute completion cho 1 enrollment (§7)
public class CompletionResultModel
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public decimal? ATTENDANCE_PERCENT { get; set; }
    public decimal? FINAL_SCORE { get; set; }
    public int    PASSED { get; set; }                // 0 | 1
    public string? FAIL_REASON { get; set; }
}

public class FinalizeClassRequest
{
    public int    CLASS_ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class FinalizeClassResult
{
    public int    CLASS_ID { get; set; }
    public int    TOTAL_ENROLLED { get; set; }
    public int    COMPLETED_COUNT { get; set; }
    public int    FAILED_COUNT { get; set; }
    public int    CERTIFIED_COUNT { get; set; }
    public List<CompletionResultModel> DETAILS { get; set; } = new();
}

// Certificate list (§12 v1 record-only, không PDF)
public class CertificateItem
{
    public int    CLASS_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string COURSE_TITLE { get; set; } = "";
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public string? DEPTCD { get; set; }
    public string? LINECD { get; set; }
    public string? WORKCD { get; set; }
    public string? DEPT_NAME { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_NAME { get; set; }
    public DateTime? COMPLETION_DATE { get; set; }
    public decimal? FINAL_SCORE { get; set; }
    public decimal? MAX_SCORE { get; set; }
    public decimal? ATTENDANCE_PERCENT { get; set; }
}
