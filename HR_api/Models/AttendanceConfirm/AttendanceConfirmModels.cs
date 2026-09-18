namespace HR_api.Models.AttendanceConfirm;

// 1 dòng danh sách "cần xác nhận" — join live ADD_TIME + ECM100/EAM410 + LEFT JOIN HR_ATT_CONFIRM.
public class AttendanceMissingItem
{
    public string  EMPCD           { get; set; } = "";
    public string? EMP_NAME        { get; set; }
    public string  WORK_DATE       { get; set; } = "";   // yyyy-MM-dd
    public string? DEPT_ID         { get; set; }
    public string? DEPT_NAME       { get; set; }
    public string? LINE_ID         { get; set; }
    public string? LINE_NAME       { get; set; }
    public string? WORK_ID         { get; set; }
    public string? WORK_NAME       { get; set; }

    public string? ERP_TIME_IN     { get; set; }         // HH:mm hoặc null nếu thiếu
    public string? ERP_TIME_OUT    { get; set; }
    // Giờ gợi ý mặc định khi ERP_TIME_IN/OUT null — lấy theo giờ ca làm việc (STIME/ETIME), cộng
    // thêm giờ tăng ca trước/sau nếu ERP có log — để NV chỉ cần xác nhận, khỏi gõ tay từ đầu.
    public string? SUGGESTED_TIME_IN  { get; set; }
    public string? SUGGESTED_TIME_OUT { get; set; }
    public string  MISSING_TYPE    { get; set; } = "";    // IN | OUT | BOTH
    public string? SHIFT_CD        { get; set; }
    public string? SHIFT_STIME     { get; set; }          // HH:mm — giờ bắt đầu ca chính (EBM100.STIME)
    public string? SHIFT_ETIME     { get; set; }          // HH:mm — giờ kết thúc ca chính (EBM100.ETIME)
    public bool    IS_NIGHT_SHIFT  { get; set; }          // STIME > ETIME (ca qua đêm)
    public string? REASON          { get; set; }
    public decimal? OVER_TIME      { get; set; }

    // Trạng thái xác nhận (null nếu chưa từng có ai khởi tạo)
    public int?     CONFIRM_ID       { get; set; }
    public string?  CONFIRM_STATUS   { get; set; }
    public string?  WORKER_TIME_IN   { get; set; }
    public string?  WORKER_TIME_OUT  { get; set; }
    public string?  CONFIRM_TIME_IN  { get; set; }
    public string?  CONFIRM_TIME_OUT { get; set; }
    public string?  SHIFT_TYPE       { get; set; }
    public string?  WORKER_NOTE      { get; set; }   // ghi chú của công nhân (bước 1)
    public string?  NOTE             { get; set; }   // ghi chú của quản lý (bước 2)
    public string?  REQUESTED_BY     { get; set; }
    public string?  REQUESTED_DATE   { get; set; }
    public string?  CONFIRMED_BY     { get; set; }
    public string?  CONFIRMED_BY_NAME{ get; set; }   // họ tên người xác nhận (CNAME, đọc theo font VNI)
    public string?  CONFIRMED_DATE   { get; set; }

    public int TOTAL_COUNT { get; set; }
}

public class AttendanceMissingListResponse
{
    public bool    success     { get; set; } = true;
    public string? message     { get; set; }
    public object? summary     { get; set; }
    public int     total       { get; set; }
    public int     page        { get; set; }
    public int     page_size   { get; set; }
    public int     total_pages { get; set; }
    // Chỉ true khi caller là quản lý thật (Supervisor+) trong đúng scope của danh sách đang xem —
    // Admin/HR luôn false, không được bấm Xác nhận thay (yêu cầu HR 2026-09-18).
    public bool    can_confirm { get; set; }
    public List<AttendanceMissingItem> data { get; set; } = new();
}

// Bước 1 — công nhân khai giờ vào/ra thực tế cho 1 ngày
public class WorkerSubmitRequest
{
    public string  EMPCD      { get; set; } = "";
    public string  WORK_DATE  { get; set; } = "";   // yyyy-MM-dd
    public string? TIME_IN    { get; set; }         // HH:mm
    public string? TIME_OUT   { get; set; }         // HH:mm
    public string? NOTE       { get; set; }         // ghi chú của công nhân (vd lý do quên bấm giờ)
    // SHIFT_TYPE KHÔNG do NV chọn — server tự detect từ log tăng ca ERP (xem DetermineShiftTypeAsync).
}

// Bước 2 — quản lý/Admin/HR xem lại, có thể sửa, ghi chú, chốt
public class ManagerConfirmRequest
{
    public int     CONFIRM_ID    { get; set; }
    public string  ACTOR_EMPCD   { get; set; } = "";
    public string? TIME_IN       { get; set; }
    public string? TIME_OUT      { get; set; }
    public string? NOTE          { get; set; }
    public string  STATUS        { get; set; } = "CONFIRMED"; // CONFIRMED | REJECTED
}

// Admin/HR/Clerk chọn record ERP (chưa có row app) để gửi thông báo "yêu cầu xác nhận"
public class RequestConfirmRequest
{
    public string  EMPCD       { get; set; } = "";
    public string  WORK_DATE   { get; set; } = "";
    public string  ACTOR_EMPCD { get; set; } = "";
}

public class RequestConfirmBulkRequest
{
    public List<RequestConfirmRequest> ITEMS { get; set; } = new();
}

// 1 ngày công nhân cần tự khai/khai lại — dùng cho badge menu + list gợi ý ngày trên WorkerForm.
public class MyPendingDayItem
{
    public string WORK_DATE     { get; set; } = "";   // yyyy-MM-dd
    public string CONFIRM_STATUS{ get; set; } = "";   // MISSING | PENDING_WORKER | REJECTED
    public string MISSING_TYPE  { get; set; } = "";   // IN | OUT | BOTH
}

public class AttendanceConfirmActionResult
{
    public string  EMPCD     { get; set; } = "";
    public string? WORK_DATE { get; set; }
    public bool    OK        { get; set; }
    public bool    SKIPPED   { get; set; }
    public string? MESSAGE   { get; set; }
}

public class AttendanceConfirmResponse
{
    public bool    success { get; set; }
    public string? message { get; set; }
    public int     processed { get; set; }
    public int     skipped   { get; set; }
    public int     failed    { get; set; }
    public List<AttendanceConfirmActionResult> results { get; set; } = new();
}
