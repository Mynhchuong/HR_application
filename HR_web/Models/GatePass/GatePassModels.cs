namespace HR_web.Models.GatePass;

public class GpFilterBarModel
{
    public string DeptUrl { get; set; } = "";
    public string LineUrl { get; set; } = "";
    public string WorkUrl { get; set; } = "";
    public string? DeptSelectedId { get; set; }
    public string Lang { get; set; } = "vi";   // "vi" | "en"
}

public class GpShiftInfoModel
{
    public string? SHIFTCD            { get; set; }
    public string? STIME              { get; set; }
    public string? ETIME              { get; set; }
    public string? WORK_DATE          { get; set; }
    public string? WORK_DATE_TOMORROW { get; set; }
}

public class GpMyRequestModel
{
    public string REQUEST_ID { get; set; } = string.Empty;
    public string? GP_TYPE { get; set; }
    public DateTime? OUT_TIME { get; set; }
    public DateTime? IN_TIME { get; set; }
    public string? REASON { get; set; }
    public string? STATUS { get; set; }
    public DateTime? CREATED_DATE { get; set; }
    public bool IS_EDITABLE { get; set; }
    public string? REMARK { get; set; }
}

public class GpMyRequestsPagedResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public int total { get; set; }
    public int page { get; set; }
    public int page_size { get; set; }
    public int total_pages { get; set; }
    public List<GpMyRequestModel> data { get; set; } = new();
}

public class GpListModel
{
    public string REQUEST_ID { get; set; } = string.Empty;
    public string EMPCD { get; set; } = string.Empty;
    public string? EMP_NAME { get; set; }
    public string? DEPT_ID { get; set; }
    public string? DEPT_NAME { get; set; }
    public string? LINE_ID { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_ID { get; set; }
    public string? WORK_NAME { get; set; }
    public string? GP_TYPE { get; set; }
    public DateTime? OUT_TIME { get; set; }
    public DateTime? IN_TIME { get; set; }
    public string? REASON { get; set; }
    public string? STATUS { get; set; }
    public DateTime? CREATED_DATE { get; set; }
    public string? FINAL_APPROVER { get; set; }
    public string? APPROVER_NAME { get; set; }
    public DateTime? FINAL_DATE { get; set; }
    public string? REMARK { get; set; }
    public string? REQUESTER_ROLE { get; set; }
}

public class GpSummary
{
    public int TOTAL { get; set; }
    public int PENDING { get; set; }
    public int APPROVED { get; set; }
    public int REJECTED { get; set; }
}

public class GpListPagedResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public GpSummary? summary { get; set; }
    public int total { get; set; }
    public int page { get; set; }
    public int page_size { get; set; }
    public int total_pages { get; set; }
    public List<GpListModel> data { get; set; } = new();
}

public class GpSubmitRequest
{
    public string EMPCD { get; set; } = string.Empty;
    public string REG_DATE { get; set; } = string.Empty;
    public string GP_TYPE { get; set; } = string.Empty;
    public string? OUT_TIME { get; set; }
    public string? IN_TIME { get; set; }
    public string? REASON { get; set; }
}

public class GpUpdateRequest
{
    public string REQUEST_ID { get; set; } = string.Empty;
    public string EMPCD { get; set; } = string.Empty;
    public string GP_TYPE { get; set; } = string.Empty;
    public string? OUT_TIME { get; set; }
    public string? IN_TIME { get; set; }
    public string? REASON { get; set; }
}

public class GpActionResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public string? request_id { get; set; }
}

public class GpShiftResponse
{
    public bool success { get; set; }
    public GpShiftInfoModel? data { get; set; }
}

public class AdminConfirmedGpModel
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
    public string?   GP_TYPE        { get; set; }
    public DateTime? OUT_TIME       { get; set; }
    public DateTime? IN_TIME        { get; set; }
    public string?   REASON         { get; set; }
    public string?   STATUS         { get; set; }
    public DateTime? FINAL_DATE     { get; set; }
    public string?   FINAL_APPROVER { get; set; }
    public DateTime? CREATED_DATE   { get; set; }
}

public class AdminConfirmedGpPagedResponse
{
    public bool   success     { get; set; }
    public string? message    { get; set; }
    public int    total       { get; set; }
    public int    page        { get; set; }
    public int    page_size   { get; set; }
    public int    total_pages { get; set; }
    public List<AdminConfirmedGpModel> data { get; set; } = new();
}
