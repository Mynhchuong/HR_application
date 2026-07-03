namespace HR_web.Models.Notification;

public class NotificationItem
{
    public decimal ID           { get; set; }
    public string  TITLE        { get; set; } = "";
    public string  BODY         { get; set; } = "";
    public string? TITLE_EN     { get; set; }
    public string? BODY_EN      { get; set; }
    public string? LINK_ACTION  { get; set; }
    public DateTime CREATED_DATE { get; set; }
    public int     IS_READ      { get; set; }
    public string? SENDER_NAME  { get; set; }
    public string? SENDER_EMPCD { get; set; }
    public string? SENDER_DEPT_NAME { get; set; }
    public string? SENDER_LINE_NAME { get; set; }
    public string? SENDER_WORK_NAME { get; set; }
    public string? PRIORITY     { get; set; }   // HIGH / NORMAL
    public string? SOURCE       { get; set; }   // ADMIN / HR / SYSTEM
    public string? NOTI_TYPE    { get; set; }
}

public class NotificationPagedResponse
{
    public bool              success    { get; set; }
    public List<NotificationItem> data  { get; set; } = new();
    public int               unread     { get; set; }
}
