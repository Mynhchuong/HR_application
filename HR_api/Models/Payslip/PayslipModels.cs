using System;
using System.Collections.Generic;

namespace HR_api.Models.Payslip;

public class PayrollPeriodModel
{
    public decimal ID { get; set; }
    public string PERIOD_NAME { get; set; } = string.Empty;
    public DateTime? START_DATE { get; set; }
    public DateTime? END_DATE { get; set; }
    public int IS_PUBLISHED { get; set; }
    public string INST_ID { get; set; } = string.Empty;
    public DateTime? INST_DT { get; set; }
    public string UPDT_ID { get; set; } = string.Empty;
    public DateTime? UPDT_DT { get; set; }
    public DateTime? PUBLISH_DATE { get; set; }
    public int IS_AUTO_PUBLISH { get; set; }
    public string REMARK { get; set; } = string.Empty;
}

public class PayrollItemModel
{
    public decimal ID { get; set; }
    public string ITEM_CODE { get; set; } = string.Empty;
    public string ITEM_NAME { get; set; } = string.Empty;
    public string ITEM_TYPE { get; set; } = string.Empty;
    public int IS_VISIBLE { get; set; }
    public decimal DISPLAY_ORDER { get; set; }
}

public class PayrollDataModel
{
    public string ITEM_CODE { get; set; } = string.Empty;
    public string ITEM_NAME { get; set; } = string.Empty;
    public string ITEM_TYPE { get; set; } = string.Empty;
    public decimal? AMOUNT { get; set; }
    public string TEXT_VALUE { get; set; } = string.Empty;
    public int IS_VISIBLE { get; set; }
}

public class PayslipUploadRow
{
    public string EmpCd { get; set; } = string.Empty;
    public List<decimal?> Values { get; set; } = new List<decimal?>();
    public List<string> TextValues { get; set; } = new List<string>();
}

public class PayslipAdminDetailModel
{
    public string EMPCD { get; set; } = string.Empty;
    public string EMP_NAME { get; set; } = string.Empty;
    public List<PayrollDataModel> Details { get; set; } = new List<PayrollDataModel>();
}

public class PayslipAdminPagedResponse
{
    public bool success { get; set; }
    public int total { get; set; }
    public int page { get; set; }
    public int page_size { get; set; }
    public int total_pages { get; set; }
    public List<PayslipAdminDetailModel> data { get; set; } = new();
}
