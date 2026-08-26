namespace HR_web.Models.Directory;

public class DirectoryPageModel
{
    public EmployeeDirectoryModel Employee { get; set; } = new();
    public List<EmployeeChangeHistoryModel> ChangeHistory { get; set; } = new();
}
