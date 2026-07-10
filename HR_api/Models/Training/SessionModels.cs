namespace HR_api.Models.Training;

// HR_TRAINING_SESSION — buổi học (full CRUD ở Phase 2, khác ClassSessionLightModel chỉ read-only ở Class detail)
public class SessionModel
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
    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
    public string? UPDT_ID { get; set; }
    public DateTime? UPDT_DT { get; set; }
}

// HR_TRAINING_ATTENDANCE — điểm danh
public class AttendanceModel
{
    public int    SESSION_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public DateTime? CHECKIN_TIME { get; set; }
    public string STATUS { get; set; } = "ABSENT";
    public int    TEACHER_CONFIRMED { get; set; }
    public string? CONFIRMED_BY { get; set; }
    public DateTime? CONFIRMED_DT { get; set; }
    public string? NOTE { get; set; }
    public int?   GROUP_ID { get; set; }
    public string? GROUP_NAME { get; set; }
}

// Aggregate cho teacher xem 1 session — meta + danh sách attendance
public class SessionAttendanceView
{
    public SessionModel? SESSION { get; set; }
    public List<AttendanceModel> ATTENDANCE { get; set; } = new();
}

// ═══════════════════════════════════════════════════════════════
//  REQUESTS
// ═══════════════════════════════════════════════════════════════

public class SaveSessionRequest
{
    public int?   ID { get; set; }
    public int    CLASS_ID { get; set; }
    public int    SESSION_NO { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string? TOPIC { get; set; }
    public string? LOCATION { get; set; }
    public int?   GROUP_ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class RescheduleSessionRequest
{
    public int    ID { get; set; }
    public DateTime NEW_DATE { get; set; }
    public string NEW_START_TIME { get; set; } = "";
    public string NEW_END_TIME { get; set; } = "";
    public string LOGIN_USER { get; set; } = "";
}

public class CancelSessionRequest
{
    public int    ID { get; set; }
    public string LOGIN_USER { get; set; } = "";
}

public class CheckInRequest
{
    public int    SESSION_ID { get; set; }
    public string EMPCD { get; set; } = "";
}

public class ConfirmAttendanceRequest
{
    public int    SESSION_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string STATUS { get; set; } = "PRESENT";   // PRESENT | LATE | ABSENT | EXCUSED
    public string? NOTE { get; set; }
    public string LOGIN_USER { get; set; } = "";     // teacher EMPCD
}

public class ConfirmAttendanceBatchRequest
{
    public int    SESSION_ID { get; set; }
    public List<ConfirmAttendanceItem> ITEMS { get; set; } = new();
    public string LOGIN_USER { get; set; } = "";
}
public class ConfirmAttendanceItem
{
    public string EMPCD { get; set; } = "";
    public string STATUS { get; set; } = "PRESENT";
    public string? NOTE { get; set; }
}

public class DropStudentRequest
{
    public int    CLASS_ID { get; set; }
    public string EMPCD { get; set; } = "";
    public string DROP_REASON { get; set; } = "ABSENCE_EXCEEDED";
    public string LOGIN_USER { get; set; } = "";
}

// Aggregate per-student % vắng cho teacher xem "who to kick"
public class AbsentStatModel
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public int    COMPLETED_SESSIONS { get; set; }
    public int    ABSENT_COUNT { get; set; }
    public decimal ABSENT_PERCENT { get; set; }
}
