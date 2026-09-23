namespace HR_web.Models.AppLink;

public class AppLinkModel
{
    public int       ID { get; set; }
    public string    LINK_URL { get; set; } = "";
    public string    STATUS { get; set; } = "AVAILABLE";
    public string?   ASSIGNED_EMPCD { get; set; }
    public string?   ASSIGNED_EMP_NAME { get; set; }
    public DateTime? ASSIGNED_DT { get; set; }
    public string?   INST_ID { get; set; }
    public DateTime? INST_DT { get; set; }
}

public class AppLinkStatsModel
{
    public int TOTAL { get; set; }
    public int AVAILABLE { get; set; }
    public int ASSIGNED { get; set; }
    public int REPLACED { get; set; }
}

public class ImportAppLinkRequest
{
    public List<string> LINKS { get; set; } = new();
    public string? LOGIN_USER { get; set; }
}

public class ReassignAppLinkRequest
{
    public string EMPCD { get; set; } = "";
    public string? ACTOR_EMPCD { get; set; }
}

public class AppLinkResult
{
    public bool    success { get; set; }
    public string? message { get; set; }
    public bool    alreadyAssigned { get; set; }
    public string? linkUrl { get; set; }
    public DateTime? assignedDt { get; set; }
}
