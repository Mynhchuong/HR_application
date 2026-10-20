namespace HR_api.Tpm;

/// <summary>Body POST apiHR/Tpm/report. Chỉ nhận mã NV + mã máy + mô tả — tên/bộ phận/line/tổ do
/// HR_api tự tra từ DB HR, không tin dữ liệu app gửi lên.</summary>
public class TpmReportBody
{
    public string? Empcd { get; set; }
    public string? ItemCode { get; set; }
    public string? Description { get; set; }
}

/// <summary>Người dùng đã tra từ DB HR (ECM100 + EAM410 + HR_USERS) để gửi sang TPM.</summary>
public class TpmEmployee
{
    public string EmpCd { get; set; } = "";
    public string Name { get; set; } = "";
    public string? DeptCd { get; set; }
    public string? DeptNm { get; set; }
    public string? LineCd { get; set; }
    public string? LineNm { get; set; }
    public string? WorkCd { get; set; }
    public string? WorkNm { get; set; }
}
