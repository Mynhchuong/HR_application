namespace HR_api.Models.Policy;

public class CompanyPolicyModel
{
    public int ID { get; set; }
    public string CATEGORY { get; set; } = string.Empty;
    public string TITLE { get; set; } = string.Empty;
    public string CONTENT { get; set; } = string.Empty;
    public int DISPLAY_ORDER { get; set; }
    public int IS_ACTIVE { get; set; }
    public string? INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
    public string? UPDT_ID { get; set; }
    public DateTime? UPDT_DT { get; set; }
    public string? UPDT_FULL_NAME { get; set; }
}

public class SavePolicyRequest
{
    public int? ID { get; set; }
    public string CATEGORY { get; set; } = string.Empty;
    public string TITLE { get; set; } = string.Empty;
    public string CONTENT { get; set; } = string.Empty;
    public int DISPLAY_ORDER { get; set; }
    public int IS_ACTIVE { get; set; } = 1;
    public string? LOGIN_USER { get; set; }
}
