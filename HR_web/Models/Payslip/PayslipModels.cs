namespace HR_web.Models.Payslip;

public class PayrollPeriodModel
{
    public decimal ID { get; set; }
    public string? PERIOD_NAME { get; set; }
    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }
    public int IS_PUBLISHED { get; set; }
    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
    public string? UPDT_ID { get; set; }
    public DateTime? UPDT_DT { get; set; }
    public DateTime? PUBLISH_DATE { get; set; }
    public int IS_AUTO_PUBLISH { get; set; }
    public string? REMARK { get; set; }
}

public class PayrollItemModel
{
    public decimal ID { get; set; }
    public string? ITEM_CODE { get; set; }
    public string? ITEM_NAME { get; set; }
    public string? ITEM_TYPE { get; set; }
    public int IS_VISIBLE { get; set; }
    public decimal DISPLAY_ORDER { get; set; }
}

public class PayrollDataModel
{
    public string? ITEM_CODE { get; set; }
    public string? ITEM_NAME { get; set; }
    public string? ITEM_TYPE { get; set; }
    public decimal? AMOUNT { get; set; }
    public string? TEXT_VALUE { get; set; }
    public int IS_VISIBLE { get; set; }
}

public class PayslipUploadRow
{
    public string? EmpCd { get; set; }
    public List<decimal?> Values { get; set; } = new();
    public List<string> TextValues { get; set; } = new();
}

public class PayslipAdminDetailModel
{
    public string? EMPCD { get; set; }
    public string? EMP_NAME { get; set; }
    public List<PayrollDataModel> Details { get; set; } = new();
}

public class PayslipApiResponse<T>
{
    public bool success { get; set; }
    public string? message { get; set; }
    public T? data { get; set; }
    public List<string>? errors { get; set; }
}

public class PayslipAdminPagedResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public int total { get; set; }
    public int page { get; set; }
    public List<PayslipAdminDetailModel>? data { get; set; }
}
