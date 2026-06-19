namespace HR_web.Models.NotiTemplate;

public class NotiTemplateModel
{
    public int      ID           { get; set; }
    public string   TEMPLATE_KEY { get; set; } = string.Empty;
    public string   TITLE_VI     { get; set; } = string.Empty;
    public string   BODY_VI      { get; set; } = string.Empty;
    public string?  TITLE_EN     { get; set; }
    public string?  BODY_EN      { get; set; }
    public int      IS_ACTIVE    { get; set; } = 1;
    public string?  UPDATED_BY   { get; set; }
    public DateTime? UPDATED_DATE { get; set; }
}

public class SaveNotiTemplateRequest
{
    public string  TEMPLATE_KEY { get; set; } = string.Empty;
    public string  TITLE_VI     { get; set; } = string.Empty;
    public string  BODY_VI      { get; set; } = string.Empty;
    public string? TITLE_EN     { get; set; }
    public string? BODY_EN      { get; set; }
}
