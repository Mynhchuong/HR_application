namespace HR_web.Models.Notification;

public class NotificationItem
{
    public decimal ID           { get; set; }
    public string  TITLE        { get; set; } = "";
    public string  BODY         { get; set; } = "";
    public string? LINK_ACTION  { get; set; }
    public DateTime CREATED_DATE { get; set; }
    public int     IS_READ      { get; set; }
    public string? SENDER_NAME  { get; set; }
}

public class NotificationPagedResponse
{
    public bool              success    { get; set; }
    public List<NotificationItem> data  { get; set; } = new();
    public int               unread     { get; set; }
}
