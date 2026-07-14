namespace HR_api.Models.Training;

// HR_TRAINING_CLASS — 1 lớp cụ thể chạy trên 1 Course (xem create_training.sql + training_rules.md §2, §4.2)
public class ClassModel
{
    public int    ID { get; set; }
    public int    COURSE_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string? DESCRIPTION { get; set; }

    // DRAFT | OPEN_FOR_REGISTRATION | SCHEDULED | IN_PROGRESS | COMPLETED | CLOSED | CANCELLED
    public string STATUS { get; set; } = "DRAFT";

    // ASSIGNED | OPEN (§3)
    public string REGISTRATION_MODE { get; set; } = "ASSIGNED";
    public int?   MAX_STUDENTS { get; set; }                // OPEN: NULL = unlimited
    public DateTime? REGISTRATION_DEADLINE { get; set; }

    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }

    // Completion criteria
    public decimal MIN_ATTENDANCE_PERCENT { get; set; } = 75;
    public int?   FINAL_TEST_ID { get; set; }
    public int    REQUIRE_POST_REVIEW { get; set; }

    // Express + Recurring tracking
    public int    IS_EXPRESS { get; set; }
    public int?   CLONED_FROM_CLASS_ID { get; set; }
    public string? CLONED_FROM_TYPE { get; set; }

    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
    public string? UPDT_ID { get; set; }
    public DateTime? UPDT_DT { get; set; }

    // Denormalised for UI
    public string? COURSE_TITLE { get; set; }
    public string? COURSE_MODE { get; set; }
    public int? ENROLLMENT_COUNT { get; set; }
    public int? SESSION_COUNT { get; set; }
    public int? TOTAL_SESSIONS { get; set; }
    public int? COMPLETED_SESSIONS { get; set; }
}

// Bảng phụ HR_TRAINING_CLASS_TEACHER
public class ClassTeacherModel
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }       // JOIN ECM100 for display
    public int    IS_PRIMARY { get; set; }
}

// Bảng HR_TRAINING_CLASS_GROUP (§5b)
public class ClassGroupModel
{
    public int    ID { get; set; }
    public int    CLASS_ID { get; set; }
    public string GROUP_NAME { get; set; } = "";
    public int?   MAX_STUDENTS { get; set; }
    public int?   ENROLLMENT_COUNT { get; set; }        // denormalised
}

// Session view (HR_TRAINING_SESSION) — full CRUD ở Phase 2, đây là read-only cho Class detail.
public class ClassSessionLightModel
{
    public int    ID { get; set; }
    public int    CLASS_ID { get; set; }
    public int    SESSION_NO { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string? TOPIC { get; set; }
    public string? LOCATION { get; set; }
    public string STATUS { get; set; } = "UPCOMING";
    public int?   GROUP_ID { get; set; }
    public string? GROUP_NAME { get; set; }
    public string? ATTENDANCE_STATUS { get; set; }
}

// ═══════════════════════════════════════════════════════════════
//  REQUESTS
// ═══════════════════════════════════════════════════════════════

public class SaveClassRequest
{
    public int?   ID { get; set; }
    public int    COURSE_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string? DESCRIPTION { get; set; }
    public string REGISTRATION_MODE { get; set; } = "ASSIGNED";
    public int?   MAX_STUDENTS { get; set; }
    public DateTime? REGISTRATION_DEADLINE { get; set; }
    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }
    public decimal? MIN_ATTENDANCE_PERCENT { get; set; }
    public int?   FINAL_TEST_ID { get; set; }
    public int?   REQUIRE_POST_REVIEW { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class ChangeClassStatusRequest
{
    public int    ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class AssignTeacherRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public int    IS_PRIMARY { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class RemoveTeacherRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class SaveGroupRequest
{
    public int?   ID { get; set; }
    public int    CLASS_ID { get; set; }
    public string GROUP_NAME { get; set; } = "";
    public int?   MAX_STUDENTS { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class DeleteGroupRequest
{
    public int    ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class AutoSplitGroupRequest
{
    public int    CLASS_ID { get; set; }
    public List<int> GROUP_IDS { get; set; } = new();   // Group IDs muốn chia đều
    public string LOGIN_USER { get; set; } = "";
}

// §15b Cách 1 — Clone Class từ Course template
public class CloneFromCourseRequest
{
    public int      COURSE_ID { get; set; }
    public string   CLASS_NAME { get; set; } = "";
    public DateTime START_DATE { get; set; }
    public string?  DESCRIPTION { get; set; }
    public string?  PRIMARY_TEACHER_EMPCD { get; set; }
    public List<string> EMPCDS { get; set; } = new();    // optional — có thể bỏ trống, HR nhập sau
    public string   LOGIN_USER { get; set; } = "";
}

// §4.2 Express Create — 1-form
public class ExpressCreateRequest
{
    public int      COURSE_ID { get; set; }             // Course EXPRESS
    public string   CLASS_NAME { get; set; } = "";
    public DateTime SESSION_DATE { get; set; }
    public string   START_TIME { get; set; } = "";      // HHMM
    public string   END_TIME { get; set; } = "";
    public string?  LOCATION { get; set; }
    public string?  TOPIC { get; set; }
    public string   PRIMARY_TEACHER_EMPCD { get; set; } = "";
    public List<string> EMPCDS { get; set; } = new();
    public int?     FINAL_TEST_ID { get; set; }         // optional (§2.1)
    public string   LOGIN_USER { get; set; } = "";
}

// §16 Recover DROPPED
public class RecoverEnrollmentRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string REASON { get; set; } = "";           // Lý do recover (audit)
    public string LOGIN_USER { get; set; } = "";
}

// Kết quả Publish/Finalize — idempotent §4.2. ALREADY_PUBLISHED=true nghĩa là bấm lại lần 2+
// (Class đã SCHEDULED/IN_PROGRESS), NOTIFIED_COUNT = số học viên vừa được re-enqueue noti.
public class PublishResult
{
    public bool ALREADY_PUBLISHED { get; set; }
    public int  NOTIFIED_COUNT { get; set; }
}

// §15b Cách 2 — Clone Class từ Class trước ("Sao chép sang đợt mới").
// Giữ nguyên mọi setting khác của Class nguồn (mode, min_attendance, final_test, teachers, group
// structure, test — deep copy riêng cho đợt mới) — chỉ đổi tên + ngày bắt đầu + DS học viên.
public class CloneFromClassRequest
{
    public int      SOURCE_CLASS_ID { get; set; }
    public string   CLASS_NAME { get; set; } = "";
    public DateTime START_DATE { get; set; }
    public List<string> EMPCDS { get; set; } = new();    // optional — có thể bỏ trống, HR nhập sau
    public string   LOGIN_USER { get; set; } = "";
}
