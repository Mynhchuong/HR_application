namespace HR_web.Models;

public class SideMenuItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? Url { get; set; }
    public List<SideMenuItem> Children { get; set; } = new();
    public Func<bool>? VisibleWhen { get; set; }
}
