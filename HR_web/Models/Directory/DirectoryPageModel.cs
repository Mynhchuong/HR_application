namespace HR_web.Models.Directory;

public class DirectoryPageModel
{
    public EmployeeDirectoryModel Employee { get; set; } = new();
    public EmployeeChangeHistoryListResult ChangeHistory { get; set; } = new();
}
