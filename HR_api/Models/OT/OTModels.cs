using System;

namespace HR_api.Models.OT;

public class OTTodayModel
{
    public string EMPCD { get; set; } = string.Empty;
    public DateTime? WORK_DATE { get; set; }
    public decimal? OT_HOURS { get; set; }
    public string? OT_BEFORE { get; set; }
    public string? OT_BEFORE_TIME { get; set; }
    public string? OT_AFTER { get; set; }
    public string? OT_AFTER_TIME { get; set; }
    public string? OT_REST { get; set; }
    public string? HAS_OT { get; set; }
    public DateTime? START_OT { get; set; }       
    public DateTime? END_OT { get; set; }         
    public string? CONFIRM_STATUS { get; set; }
    public DateTime? CONFIRM_DATE { get; set; }
    public string? REJECT_REASON { get; set; }
    public decimal SUM_WEEK { get; set; }
    public decimal SUM_MONTH { get; set; }
    public decimal SUM_YEAR { get; set; }
}

public class OTClerkModel
{
    public string EMPCD { get; set; } = string.Empty;
    public string? EMP_NAME { get; set; }
    public string? DEPT_ID { get; set; }
    public string? DEPT_NAME { get; set; }
    public string? LINE_ID { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_ID { get; set; }
    public string? WORK_NAME { get; set; }
    public decimal? OT_HOURS { get; set; }
    public string? OT_BEFORE { get; set; }         
    public string? OT_BEFORE_TIME { get; set; }    
    public string? OT_AFTER { get; set; }         
    public string? OT_AFTER_TIME { get; set; }     
    public string? CONFIRM_STATUS { get; set; }
    public DateTime? CONFIRM_DATE { get; set; }
    public string? REJECT_REASON { get; set; }
    public DateTime? START_OT { get; set; }       
    public DateTime? END_OT { get; set; }         
}

public class OTClerkSummary
{
    public int TOTAL { get; set; }
    public int CONFIRMED { get; set; }
    public int REJECTED { get; set; }
    public int PENDING { get; set; }
    public bool IS_DONE { get; set; }             
}

public class OTHRSummaryModel
{
    public string? DEPT_ID { get; set; }
    public string? DEPT_NAME { get; set; }    
    public int TOTAL { get; set; }
    public int CONFIRMED { get; set; }
    public int REJECTED { get; set; }
    public int PENDING { get; set; }
    public string? STATUS { get; set; }
}

public class OTHRDetailModel
{
    public string EMPCD { get; set; } = string.Empty;
    public string? EMP_NAME { get; set; }
    public string? DEPT_ID { get; set; }
    public string? DEPT_NAME { get; set; }
    public string? LINE_ID { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_ID { get; set; }
    public string? WORK_NAME { get; set; }
    public decimal? OT_HOURS { get; set; }
    public string? OT_BEFORE { get; set; }         
    public string? OT_BEFORE_TIME { get; set; }   
    public string? OT_AFTER { get; set; }          
    public string? OT_AFTER_TIME { get; set; }     
    public string? CONFIRM_STATUS { get; set; }
    public DateTime? CONFIRM_DATE { get; set; }
    public string? REJECT_REASON { get; set; }
    public DateTime? START_OT { get; set; }      
    public DateTime? END_OT { get; set; }       
    public int TOTAL_COUNT { get; set; }
}

public class OTConfirmRequest
{
    public string EMPCD { get; set; } = string.Empty;
    public string? CONFIRM_STATUS { get; set; }    
    public string? REJECT_REASON { get; set; }     
}

public class OTConfirmResponse
{
    public bool SUCCESS { get; set; }
    public string? MESSAGE { get; set; }
    public string? REQUEST_ID { get; set; }
}
