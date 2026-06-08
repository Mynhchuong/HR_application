namespace HR_api.Models.Leave;

public class LeaveSubmitRequest
{
    public string  EMPCD      { get; set; } = string.Empty;
    public string  LEAVE_TYPE { get; set; } = string.Empty; // AL|CL|SL|NPL|OTH
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
}

public class LeaveSummary
{
    public int TOTAL    { get; set; }
    public int PENDING  { get; set; }
    public int APPROVED { get; set; }
    public int REJECTED { get; set; }
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
}
