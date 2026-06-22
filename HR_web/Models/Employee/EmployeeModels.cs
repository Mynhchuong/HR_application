namespace HR_web.Models.Employee;

public class TeamMemberModel
{
    public string EMPCD     { get; set; } = "";
    public string EMP_NAME  { get; set; } = "";
    public string DEPTCD    { get; set; } = "";
    public string LINECD    { get; set; } = "";
    public string WORKCD    { get; set; } = "";
    public string DEPT_NAME { get; set; } = "";
    public string LINE_NAME { get; set; } = "";
    public string WORK_NAME { get; set; } = "";
    public decimal SUM_YEAR  { get; set; } = 0;
    public decimal SUM_MONTH { get; set; } = 0;
}

public class TeamMemberResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public int    total   { get; set; }
    public List<TeamMemberModel> data { get; set; } = new();
}
