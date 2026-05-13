namespace HR_api.Models.UserDept;

public class UserDeptImportRow
{
    public string? EMPCD  { get; set; }
    public string? DEPTCD { get; set; }
    public string? LINECD { get; set; }
    public string? WORKCD { get; set; }
}

public class UserDeptImportRequest
{
    public string?              CreatedBy { get; set; }
    public List<UserDeptImportRow> Rows   { get; set; } = new();
}
