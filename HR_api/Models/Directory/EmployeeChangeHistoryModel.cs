namespace HR_api.Models.Directory;

public class EmployeeChangeHistoryModel
{
    public int Seq { get; set; }
    public DateTime? Dat { get; set; }

    public string? OldDeptName { get; set; }
    public string? OldLineName { get; set; }
    public string? OldWorkName { get; set; }

    public string? NewDeptName { get; set; }
    public string? NewLineName { get; set; }
    public string? NewWorkName { get; set; }
}
