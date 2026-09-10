namespace HR_web.Models.Leave;

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

public class LeaveMyRequestsPagedResponse
{
    public bool   success     { get; set; }
    public string? message    { get; set; }
    public int    total       { get; set; }
    public int    page        { get; set; }
    public int    page_size   { get; set; }
    public int    total_pages { get; set; }
    public List<LeaveMyRequestModel> data { get; set; } = new();
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

public class LeaveListPagedResponse
{
    public bool         success     { get; set; }
    public string?      message     { get; set; }
    public LeaveSummary? summary    { get; set; }
    public int          total       { get; set; }
    public int          page        { get; set; }
    public int          page_size   { get; set; }
    public int          total_pages { get; set; }
    public List<LeaveListModel> data { get; set; } = new();
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

public class LeaveAssignPagedResponse
{
    public bool              success     { get; set; }
    public string?           message     { get; set; }
    public LeaveAssignSummary? summary   { get; set; }
    public int               total       { get; set; }
    public int               page        { get; set; }
    public int               page_size   { get; set; }
    public int               total_pages { get; set; }
    public List<LeaveAssignmentModel> data { get; set; } = new();
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

public class LeaveAssignmentLogPagedResponse
{
    public bool      success     { get; set; }
    public string?   message     { get; set; }
    public int       total       { get; set; }
    public int       page        { get; set; }
    public int       page_size   { get; set; }
    public int       total_pages { get; set; }
    public List<LeaveAssignmentLogModel> data { get; set; } = new();
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

public class LeaveScheduleResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public int    month   { get; set; }
    public int    year    { get; set; }
    public int    total   { get; set; }
    public List<LeaveScheduleModel> data { get; set; } = new();
}

public class LeaveActionResponse
{
    public bool   success    { get; set; }
    public string? message   { get; set; }
    public string? request_id { get; set; }
    // Kết quả từng nhân viên khi sắp lịch hàng loạt (Assign) — HR_api đã trả field này kèm lý do
    // (VD "Không thể tự sắp lịch nghỉ cho chính mình"), nhưng thiếu field ở model này nên bị rớt
    // mất khi qua HR_web, JS (TeamSchedule.cshtml) đọc res.results nhưng luôn rỗng — phát hiện khi
    // test lại bằng tài khoản quản lý thật (2026-09-10).
    public List<LeaveAssignResultItem> results { get; set; } = new();
}

public class LeaveAssignResultItem
{
    public string  empcd      { get; set; } = string.Empty;
    public string? emp_name   { get; set; }
    public bool    success    { get; set; }
    public string? message    { get; set; }
    public string? request_id { get; set; }
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

public class LeaveCreateRequest
{
    public string  EMPCD      { get; set; } = string.Empty;
    public string  LEAVE_TYPE { get; set; } = string.Empty;
    public string  FROM_DATE  { get; set; } = string.Empty;
    public string  TO_DATE    { get; set; } = string.Empty;
    public decimal TOTAL_DAYS { get; set; }
    public string? REASON     { get; set; }
}

public class AdminEmpModel
{
    public string  EMPCD       { get; set; } = string.Empty;
    public string? EMP_NAME    { get; set; }
    public string? DEPT_ID     { get; set; }
    public string? DEPT_NAME   { get; set; }
    public string? LINE_ID     { get; set; }
    public string? LINE_NAME   { get; set; }
    public string? WORK_ID     { get; set; }
    public string? WORK_NAME   { get; set; }
    public int     RECEIVE_NUM { get; set; }
    public int     USED_NUM    { get; set; }
    public int     LEFT_NUM    { get; set; }
}

public class AdminEmpListResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public int    total   { get; set; }
    public List<AdminEmpModel> data { get; set; } = new();
}

public class AdminConfirmedLeaveModel
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
    public DateTime? FINAL_DATE     { get; set; }
    public string?   FINAL_APPROVER { get; set; }
    public DateTime? CREATED_DATE   { get; set; }
}

public class AdminConfirmedLeavePagedResponse
{
    public bool   success     { get; set; }
    public string? message    { get; set; }
    public int    total       { get; set; }
    public int    page        { get; set; }
    public int    page_size   { get; set; }
    public int    total_pages { get; set; }
    public List<AdminConfirmedLeaveModel> data { get; set; } = new();
}

public class AdminBulkDeleteRequest
{
    public string       ACTOR_EMPCD { get; set; } = string.Empty;
    public List<string> REQUEST_IDS { get; set; } = new();
}

// HR xác nhận nộp giấy theo từng ngày, tự gõ Absent Code/remark (không đoán tự động) — app ghi
// thẳng sang ERP (EFM410) cho đúng những ngày đó.
public class LeaveDocConfirmDayItem
{
    public string  Date    { get; set; } = string.Empty; // yyyy-MM-dd
    public string  Leavecd { get; set; } = string.Empty; // Absent Code HR tự gõ
    public string? Remark  { get; set; }
}

public class LeaveDocConfirmDaysBody
{
    public string RequestId { get; set; } = string.Empty;
    public List<LeaveDocConfirmDayItem> Days { get; set; } = new();
}

// Trang sửa Absent Code ERP trực tiếp (thay cho gõ tay trong đơn nghỉ MySamho) — chỉ sửa Leavecd/Remark.
public class ErpAbsentUpdateBody
{
    public string  Empcd   { get; set; } = string.Empty;
    public string  FrDate  { get; set; } = string.Empty; // yyyy-MM-dd
    public string  Leavecd { get; set; } = string.Empty;
    public string? Remark  { get; set; }
}

public class AdminBulkDeleteResponse
{
    public bool   success       { get; set; }
    public string? message      { get; set; }
    public int    total_deleted { get; set; }
}

public class AdminAssignWarning
{
    public string? empcd    { get; set; }
    public string? emp_name { get; set; }
    public int     left_num { get; set; }
}

public class AdminAssignResponse
{
    public bool   success        { get; set; }
    public string? message       { get; set; }
    public int    total_inserted { get; set; }
    public List<AdminAssignWarning> warnings { get; set; } = new();
    // Kết quả từng nhân viên (kể cả lý do bị chặn, VD "Không thể tự sắp lịch nghỉ cho chính mình") —
    // HR_api đã trả field này, AdminAssignLeave.cshtml (showResults) đã đọc data.results nhưng thiếu
    // field ở model này nên luôn rỗng — phát hiện khi test lại bằng tài khoản quản lý thật (2026-09-10).
    public List<LeaveAssignResultItem> results { get; set; } = new();
}

public class SundayEmpModel
{
    public string? EMPCD     { get; set; }
    public string? EMP_NAME  { get; set; }
    public string? DEPT_NAME { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_NAME { get; set; }
    public string? DEPT_ID   { get; set; }
    public string? LINE_ID   { get; set; }
    public string? WORK_ID   { get; set; }
    public string? INST_DT   { get; set; }
}
public class SundayListResponse   { public bool success { get; set; } public List<SundayEmpModel> data { get; set; } = new(); }
public class SundayActionResponse { public bool success { get; set; } public string? message { get; set; } }
public class SundayImportResult   { public string? empCd { get; set; } public bool success { get; set; } public string? message { get; set; } }
public class SundayImportResponse { public bool success { get; set; } public string? message { get; set; } public List<SundayImportResult> results { get; set; } = new(); }
