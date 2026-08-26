namespace HR_api.Models.Directory;

public class WorkCdItemModel
{
    public string DeptCd { get; set; } = "";
    public string LineCd { get; set; } = "";
    public string WorkCd { get; set; } = "";
    public string? DeptName { get; set; }
    public string? LineName { get; set; }
    public string? WorkName { get; set; }
}
