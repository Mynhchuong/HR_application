namespace HR_api.Models.Notification;

public class NotificationModel
{
    public decimal ID { get; set; }
    public string TITLE { get; set; } = string.Empty;
    public string BODY { get; set; } = string.Empty;
    public string? TITLE_EN { get; set; }
    public string? BODY_EN  { get; set; }
    public string? NOTI_TYPE { get; set; }
    public string? TARGET_VAL { get; set; }
    public string? LINK_ACTION { get; set; }
    public DateTime CREATED_DATE { get; set; }
    public int IS_READ { get; set; } // 0 or 1
    public string? SENDER_NAME { get; set; }
    public string? SENDER_EMPCD { get; set; }
    public string? PRIORITY { get; set; } = "NORMAL";
    public string? SOURCE   { get; set; } = "SYSTEM";
}

public class TokenRegistrationRequest
{
    public string EMPCD { get; set; } = string.Empty;
    public string TOKEN { get; set; } = string.Empty;
    public string? OS_TYPE { get; set; }
    public string? DEVICE_MODEL { get; set; }
}

public class SendNotificationRequest
{
    public string  TITLE       { get; set; } = string.Empty;
    public string  BODY        { get; set; } = string.Empty;
    public string? TITLE_EN    { get; set; }
    public string? BODY_EN     { get; set; }
    public string  NOTI_TYPE   { get; set; } = "EMPCD"; // COMPANY, DEPT, LINE, WORK, EMPCD, MULTI
    public string  TARGET_VAL  { get; set; } = string.Empty;
    public string? LINK_ACTION { get; set; }
    public string? CREATED_BY  { get; set; }
    public string? PRIORITY    { get; set; } = "NORMAL"; // HIGH, NORMAL
    public string? SOURCE      { get; set; } = "SYSTEM"; // ADMIN, HR, SYSTEM
}

public class NotificationTarget
{
    public string TYPE { get; set; } = "EMPCD"; // DEPT, LINE, WORK, EMPCD
    public string VAL  { get; set; } = string.Empty;
}

public class SendMultiNotificationRequest
{
    public string  TITLE       { get; set; } = string.Empty;
    public string  BODY        { get; set; } = string.Empty;
    public string? TITLE_EN    { get; set; }
    public string? BODY_EN     { get; set; }
    public string? LINK_ACTION { get; set; }
    public string? CREATED_BY  { get; set; }
    public string  PRIORITY    { get; set; } = "NORMAL";
    public string  SOURCE      { get; set; } = "ADMIN";
    public bool    SEND_ALL_COMPANY { get; set; } = false; // override -> NOTI_TYPE=COMPANY
    public List<NotificationTarget> TARGETS { get; set; } = new();
}

public class EmployeeLookupItem
{
    public string EMPCD     { get; set; } = string.Empty;
    public string EMP_NAME  { get; set; } = string.Empty;
    public string? DEPT_NAME { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_NAME { get; set; }
}

public class OrgLookupItem
{
    public string CODE { get; set; } = string.Empty;
    public string NAME { get; set; } = string.Empty;
    public int    EMP_COUNT { get; set; }
}
