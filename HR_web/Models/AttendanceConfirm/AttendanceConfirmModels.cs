namespace HR_web.Models.AttendanceConfirm;

public class AttendanceMissingItem
{
    public string  EMPCD           { get; set; } = "";
    public string? EMP_NAME        { get; set; }
    public string  WORK_DATE       { get; set; } = "";
    public string? DEPT_ID         { get; set; }
    public string? DEPT_NAME       { get; set; }
    public string? LINE_ID         { get; set; }
    public string? LINE_NAME       { get; set; }
    public string? WORK_ID         { get; set; }
    public string? WORK_NAME       { get; set; }

    public string? ERP_TIME_IN     { get; set; }
    public string? ERP_TIME_OUT    { get; set; }
    public string? SUGGESTED_TIME_IN  { get; set; }
    public string? SUGGESTED_TIME_OUT { get; set; }
    public string  MISSING_TYPE    { get; set; } = "";
    public string? SHIFT_CD        { get; set; }
    public string? SHIFT_STIME     { get; set; }
    public string? SHIFT_ETIME     { get; set; }
    public bool    IS_NIGHT_SHIFT  { get; set; }
    public string? REASON          { get; set; }
    public decimal? OVER_TIME      { get; set; }

    public int?     CONFIRM_ID       { get; set; }
    public string?  CONFIRM_STATUS   { get; set; }
    public string?  WORKER_TIME_IN   { get; set; }
    public string?  WORKER_TIME_OUT  { get; set; }
    public string?  CONFIRM_TIME_IN  { get; set; }
    public string?  CONFIRM_TIME_OUT { get; set; }
    public string?  SHIFT_TYPE       { get; set; }
    public string?  WORKER_NOTE      { get; set; }
    public string?  NOTE             { get; set; }
    public string?  REQUESTED_BY     { get; set; }
    public string?  REQUESTED_DATE   { get; set; }
    public string?  CONFIRMED_BY     { get; set; }
    public string?  CONFIRMED_BY_NAME{ get; set; }
    public string?  CONFIRMED_DATE   { get; set; }

    public int TOTAL_COUNT { get; set; }
}

public class AttendanceMissingListResponse
{
    public bool    success     { get; set; }
    public string? message     { get; set; }
    public object? summary     { get; set; }
    public int     total       { get; set; }
    public int     page        { get; set; }
    public int     page_size   { get; set; }
    public int     total_pages { get; set; }
    public bool    can_confirm { get; set; }
    public List<AttendanceMissingItem> data { get; set; } = new();
}

public class WorkerSubmitRequest
{
    public string  EMPCD      { get; set; } = "";
    public string  WORK_DATE  { get; set; } = "";
    public string? TIME_IN    { get; set; }
    public string? TIME_OUT   { get; set; }
    public string? NOTE       { get; set; }
}

public class ManagerConfirmRequest
{
    public int     CONFIRM_ID  { get; set; }
    public string  ACTOR_EMPCD { get; set; } = "";
    public string? TIME_IN     { get; set; }
    public string? TIME_OUT    { get; set; }
    public string? NOTE        { get; set; }
    public string  STATUS      { get; set; } = "CONFIRMED";
}

public class RequestConfirmItem
{
    public string EMPCD       { get; set; } = "";
    public string WORK_DATE   { get; set; } = "";
    public string ACTOR_EMPCD { get; set; } = "";
}

public class RequestConfirmBulkRequest
{
    public List<RequestConfirmItem> ITEMS { get; set; } = new();
}

public class MyPendingDayItem
{
    public string WORK_DATE      { get; set; } = "";
    public string CONFIRM_STATUS { get; set; } = "";
    public string MISSING_TYPE   { get; set; } = "";
}

public class MyPendingResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public int    count   { get; set; }
    public List<MyPendingDayItem> data { get; set; } = new();
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
    public bool    success   { get; set; }
    public string? message   { get; set; }
    public int     processed { get; set; }
    public int     skipped   { get; set; }
    public int     failed    { get; set; }
    public List<AttendanceConfirmActionResult> results { get; set; } = new();
}

public class SimpleApiResponse
{
    public bool    success { get; set; }
    public string? message { get; set; }
}

public class MyDayResponse
{
    public bool                 success { get; set; }
    public string?              message { get; set; }
    public AttendanceMissingItem? data  { get; set; }
}
