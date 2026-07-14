namespace HR_api.Models.Training;

// HR_TRAINING_ENROLLMENT — 1 học viên trong 1 Class (§3, §5b)
// PK = (CLASS_ID, EMPCD). "Là mandatory?" = SOURCE='ASSIGNED' — không cột riêng (bỏ IS_MANDATORY, xem §3.3).
public class EnrollmentModel
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }        // JOIN ECM100
    public string? DEPTCD { get; set; }
    public string? LINECD { get; set; }
    public string? WORKCD { get; set; }

    // ASSIGNED | SELF_REGISTER
    public string SOURCE { get; set; } = "ASSIGNED";
    // PENDING_APPROVAL | ENROLLED | REJECTED | DROPPED | COMPLETED | FAILED
    public string STATUS { get; set; } = "ENROLLED";
    public string? DROP_REASON { get; set; }

    public int?   GROUP_ID { get; set; }
    public string? GROUP_NAME { get; set; }

    // Completion (populate khi Class COMPLETED — phase 2/4)
    public decimal? FINAL_SCORE { get; set; }
    public decimal? ATTENDANCE_PERCENT { get; set; }
    public int    IS_CERTIFIED { get; set; }
    public DateTime? COMPLETION_DATE { get; set; }

    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
    public string? UPDT_ID { get; set; }
    public DateTime? UPDT_DT { get; set; }

    // Denormalised Class info — populate ở MyClasses view (user-facing)
    public string? CLASS_NAME { get; set; }
    public string? COURSE_TITLE { get; set; }
    public string? CLASS_STATUS { get; set; }
    public DateTime? CLASS_START_DATE { get; set; }
    public DateTime? CLASS_END_DATE { get; set; }
    public string? CLASS_MODE { get; set; }
    public int? TOTAL_SESSIONS { get; set; }
    public int? COMPLETED_SESSIONS { get; set; }
    public string? PRIMARY_TEACHER_NAME { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  REQUESTS
// ═══════════════════════════════════════════════════════════════

// Bulk assign (mode ASSIGNED, hoặc thêm học viên vào Class ASSIGNED đã publish)
public class BulkAssignRequest
{
    public int    CLASS_ID { get; set; }
    public List<string> EMPCDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

// Bulk pre-assign (§3.3 — OPEN mode, đánh dấu SOURCE='ASSIGNED' để bắt buộc)
// Server-side dùng MERGE: upgrade PENDING/ENROLLED SELF_REGISTER → ASSIGNED, DROPPED giữ nguyên.
public class BulkPreAssignRequest
{
    public int    CLASS_ID { get; set; }
    public List<string> EMPCDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

public class BulkAssignResult
{
    public int NEW_INSERTED { get; set; }
    public int UPGRADED { get; set; }
    public int SKIPPED_DROPPED { get; set; }
    public int SKIPPED_EXISTED { get; set; }
    public int TOTAL_INPUT { get; set; }
    public List<string> FAILED_EMPCDS { get; set; } = new();
}

public class SelfRegisterRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

public class ApproveEnrollmentRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class RejectEnrollmentRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class AssignGroupRequest
{
    public int    CLASS_ID { get; set; }
    public int?   GROUP_ID { get; set; }         // NULL = bỏ gán group
    public List<string> EMPCDS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}

// Xóa học viên khỏi lớp
public class RemoveEnrollmentRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}
