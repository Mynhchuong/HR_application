namespace HR_api.Models.GatePass;

public class GpShiftInfoModel
{
    public string? SHIFTCD           { get; set; }
    public string? STIME             { get; set; }
    public string? ETIME             { get; set; }
    public string? WORK_DATE         { get; set; }  // yyyy-MM-dd — ngày ca (có thể là hôm qua nếu ca đêm)
    public string? WORK_DATE_TOMORROW { get; set; } // yyyy-MM-dd — ngày kế tiếp
}

public class GpSubmitRequest
{
    public string EMPCD { get; set; } = string.Empty;
    public string REG_DATE { get; set; } = string.Empty;  // yyyy-MM-dd
    public string GP_TYPE { get; set; } = string.Empty;   // IN | OUT | MID
    public string? OUT_TIME { get; set; }                  // HH:mm
    public string? IN_TIME { get; set; }                   // HH:mm
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

public class GpApproveRequest
{
    public string REQUEST_ID { get; set; } = string.Empty;
    public string APPROVER_EMPCD { get; set; } = string.Empty;
    public string? COMMENT { get; set; }
}

public class GpMyRequestModel
{
    public string REQUEST_ID { get; set; } = string.Empty;
    public string? GP_TYPE { get; set; }
    public DateTime? OUT_TIME { get; set; }
    public DateTime? IN_TIME { get; set; }
    public string? REASON { get; set; }
    public string? REMARK { get; set; }
    public string? STATUS { get; set; }
    public DateTime? CREATED_DATE { get; set; }
    public bool IS_EDITABLE { get; set; }
    public string? FINAL_APPROVER { get; set; }
    public string? APPROVER_NAME  { get; set; }
    public DateTime? FINAL_DATE   { get; set; }
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
    public string? REQUESTER_ROLE { get; set; }  // Role của người gửi đơn (dùng để kiểm tra phân cấp ở frontend)
}

public class GpSummary
{
    public int TOTAL { get; set; }
    public int PENDING { get; set; }
    public int APPROVED { get; set; }
    public int REJECTED { get; set; }
}
