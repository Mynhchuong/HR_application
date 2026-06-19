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
    public string  NOTI_TYPE   { get; set; } = "EMPCD"; // COMPANY, DEPT, EMPCD
    public string  TARGET_VAL  { get; set; } = string.Empty;
    public string? LINK_ACTION { get; set; }
    public string? CREATED_BY  { get; set; }
}
