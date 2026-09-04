namespace HR_web.Models.Directory;

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

public class EmployeeChangeHistoryListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<EmployeeChangeHistoryModel> Items { get; set; } = new();
}
