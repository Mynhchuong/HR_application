namespace HR_api.Models.Leave;

public class LeaveSubmitRequest
{
    public string  EMPCD      { get; set; } = string.Empty;
    public string  LEAVE_TYPE { get; set; } = string.Empty; // AL|DT|DC|CT|VS|KT (form tạo mới); CL/SL/NPL/OTH chỉ còn trong dữ liệu lịch sử
    public string  FROM_DATE  { get; set; } = string.Empty; // yyyy-MM-dd
    public string  TO_DATE    { get; set; } = string.Empty; // yyyy-MM-dd
    public decimal TOTAL_DAYS { get; set; }
    public string? REASON     { get; set; }
}

public class LeaveUpdateRequest
{
    public string  REQUEST_ID { get; set; } = string.Empty;
    public string  EMPCD      { get; set; } = string.Empty;
    public string  LEAVE_TYPE { get; set; } = string.Empty;
    public string  FROM_DATE  { get; set; } = string.Empty;
    public string  TO_DATE    { get; set; } = string.Empty;
    public decimal TOTAL_DAYS { get; set; }
    public string? REASON     { get; set; }
}

public class LeaveApproveRequest
{
    public string  REQUEST_ID     { get; set; } = string.Empty;
    public string  APPROVER_EMPCD { get; set; } = string.Empty;
    public string? COMMENT        { get; set; }
}

public class LeaveConfirmRequest
{
    public string REQUEST_ID { get; set; } = string.Empty;
    public string EMPCD      { get; set; } = string.Empty;
}

public class LeaveAssignRequest
{
    public string       ASSIGNER_EMPCD { get; set; } = string.Empty;
    public List<string> TARGET_EMPCDS  { get; set; } = new();
    public string       FROM_DATE      { get; set; } = string.Empty;
    public string       TO_DATE        { get; set; } = string.Empty;
    public decimal      TOTAL_DAYS     { get; set; }
    public string?      REASON         { get; set; }
    public string       LEAVE_TYPE     { get; set; } = "AL";
}

public class AdminBulkDeleteRequest
{
    public string       ACTOR_EMPCD  { get; set; } = string.Empty;
    public List<string> REQUEST_IDS  { get; set; } = new();
}

public class LeaveDocStatusRequest
{
    public string  REQUEST_ID  { get; set; } = string.Empty;
    public string  ACTOR_EMPCD { get; set; } = string.Empty;
    public string? REMARK      { get; set; }   // dùng khi yêu cầu nộp lại
}

// HR xác nhận nộp giấy theo TỪNG NGÀY, tự gõ Absent Code/remark — app ghi thẳng sang ERP (EFM410),
// không giới hạn theo danh sách mã cố định (HR có thể gõ mã khác chưa từng dùng qua).
public class LeaveDocConfirmDayInput
{
    public string  DATE    { get; set; } = string.Empty; // yyyy-MM-dd
    public string  LEAVECD { get; set; } = string.Empty; // Absent Code HR tự gõ
    public string? REMARK  { get; set; }
}

public class LeaveDocConfirmDaysRequest
{
    public string REQUEST_ID  { get; set; } = string.Empty;
    public string ACTOR_EMPCD { get; set; } = string.Empty;
    public List<LeaveDocConfirmDayInput> DAYS { get; set; } = new();
}

// HR đổi ý (2026-09-10): thay vì gõ tay trong đơn nghỉ MySamho, sửa trực tiếp trên màn hình y chang
// ERP (EFM410) — chỉ được sửa Leavecd + Remark, các cột còn lại chỉ xem. Sửa xong tự map ngược lại
// đơn nghỉ MySamho tương ứng (nếu có) để cập nhật DOC_STATUS luôn, khỏi phải làm 2 bước riêng.
public class ErpAbsentUpdateRequest
{
    public string  EMPCD       { get; set; } = string.Empty;
    public string  FR_DATE     { get; set; } = string.Empty; // yyyy-MM-dd
    public string  LEAVECD     { get; set; } = string.Empty;
    public string? REMARK      { get; set; }
    public string  ACTOR_EMPCD { get; set; } = string.Empty;
}

public class LeaveMyRequestModel
{
    public string    REQUEST_ID     { get; set; } = string.Empty;
    public string?   LEAVE_TYPE     { get; set; }
    public DateTime? FROM_DATE      { get; set; }
    public DateTime? TO_DATE        { get; set; }
    public decimal?  TOTAL_DAYS     { get; set; }
    public string?   REASON         { get; set; }
    public string?   SOURCE         { get; set; }
    public string?   CONFIRM_STATUS { get; set; }
    public DateTime? CONFIRM_DATE   { get; set; }
    public string?   STATUS         { get; set; }
    public string?   REMARK         { get; set; }
    public DateTime? CREATED_DATE   { get; set; }
    public bool      IS_EDITABLE    { get; set; }
    public string?   FINAL_APPROVER { get; set; }
    public string?   APPROVER_NAME  { get; set; }
    public DateTime? FINAL_DATE     { get; set; }
    public string?   ASSIGNED_BY    { get; set; }
    public string?   ASSIGNER_NAME  { get; set; }
    public string?   DOC_STATUS     { get; set; }
}

public class LeaveListModel
{
    public string    REQUEST_ID     { get; set; } = string.Empty;
    public string    EMPCD          { get; set; } = string.Empty;
    public string?   EMP_NAME       { get; set; }
    public string?   DEPT_ID        { get; set; }
    public string?   DEPT_NAME      { get; set; }
    public string?   LINE_ID        { get; set; }
    public string?   LINE_NAME      { get; set; }
    public string?   WORK_ID        { get; set; }
    public string?   WORK_NAME      { get; set; }
    public string?   LEAVE_TYPE     { get; set; }
    public string?   SOURCE         { get; set; }
    public DateTime? FROM_DATE      { get; set; }
    public DateTime? TO_DATE        { get; set; }
    public decimal?  TOTAL_DAYS     { get; set; }
    public string?   REASON         { get; set; }
    public string?   STATUS         { get; set; }
    public string?   CONFIRM_STATUS { get; set; }
    public DateTime? CREATED_DATE   { get; set; }
    public string?   FINAL_APPROVER { get; set; }
    public string?   APPROVER_NAME  { get; set; }
    public DateTime? FINAL_DATE     { get; set; }
    public string?   REMARK         { get; set; }
    public string?   REQUESTER_ROLE { get; set; }
    public string?   ASSIGNED_BY    { get; set; }
    public string?   ASSIGNER_NAME  { get; set; }
    public string?   DOC_STATUS         { get; set; }
    public DateTime? DOC_SUBMITTED_DATE { get; set; }
    public string?   DOC_SUBMITTED_BY   { get; set; }
    public string?   DOC_REMARK         { get; set; }
}

public class LeaveSummary
{
    public int TOTAL              { get; set; }
    public int PENDING            { get; set; }
    public int APPROVED           { get; set; }
    public int REJECTED           { get; set; }
    public int ASSIGNED_PENDING   { get; set; }
    public int ASSIGNED_CONFIRMED { get; set; }
}

public class LeaveAssignSummary
{
    public int TOTAL           { get; set; }
    public int PENDING_CONFIRM { get; set; }
    public int CONFIRMED       { get; set; }
}

public class LeaveAssignmentModel
{
    public string    REQUEST_ID     { get; set; } = string.Empty;
    public string    EMPCD          { get; set; } = string.Empty;
    public string?   EMP_NAME       { get; set; }
    public string?   DEPT_NAME      { get; set; }
    public string?   LINE_NAME      { get; set; }
    public string?   LEAVE_TYPE     { get; set; }
    public DateTime? FROM_DATE      { get; set; }
    public DateTime? TO_DATE        { get; set; }
    public decimal?  TOTAL_DAYS     { get; set; }
    public string?   REASON         { get; set; }
    public string?   STATUS         { get; set; }
    public string?   CONFIRM_STATUS { get; set; }
    public DateTime? CONFIRM_DATE   { get; set; }
    public DateTime? ASSIGN_DATE    { get; set; }
}

public class LeaveAssignmentLogModel
{
    public string    REQUEST_ID    { get; set; } = string.Empty;
    public string    EMPCD         { get; set; } = string.Empty;
    public string?   EMP_NAME      { get; set; }
    public string?   DEPT_ID       { get; set; }
    public string?   DEPT_NAME     { get; set; }
    public string?   LINE_ID       { get; set; }
    public string?   LINE_NAME     { get; set; }
    public string?   WORK_ID       { get; set; }
    public string?   WORK_NAME     { get; set; }
    public string?   LEAVE_TYPE    { get; set; }
    public DateTime? FROM_DATE     { get; set; }
    public DateTime? TO_DATE       { get; set; }
    public decimal?  TOTAL_DAYS    { get; set; }
    public string?   REASON        { get; set; }
    public string?   STATUS         { get; set; }
    public string?   CONFIRM_STATUS { get; set; }
    public string?   CONFIRM_DATE   { get; set; }
    public string?   ASSIGNED_BY    { get; set; }
    public string?   ASSIGNER_NAME  { get; set; }
    public DateTime? ASSIGN_DATE    { get; set; }
}

public class LeaveScheduleModel
{
    public string    REQUEST_ID     { get; set; } = string.Empty;
    public string    EMPCD          { get; set; } = string.Empty;
    public string?   EMP_NAME       { get; set; }
    public string?   LEAVE_TYPE     { get; set; }
    public string?   SOURCE         { get; set; }
    public DateTime? FROM_DATE      { get; set; }
    public DateTime? TO_DATE        { get; set; }
    public decimal?  TOTAL_DAYS     { get; set; }
    public string?   STATUS         { get; set; }
    public string?   CONFIRM_STATUS { get; set; }
    public string?   DEPT_NAME      { get; set; }
    public string?   LINE_NAME      { get; set; }
    public string?   WORK_NAME      { get; set; }
    public string?   APPROVED_BY    { get; set; }
    public DateTime? APPROVED_DATE  { get; set; }
}
