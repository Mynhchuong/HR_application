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
    public string PHONE     { get; set; } = "";
    public string EMAIL     { get; set; } = "";
    public decimal SUM_YEAR  { get; set; } = 0;
    public decimal SUM_MONTH { get; set; } = 0;
    public DateTime? LAST_LOGIN { get; set; }
    public int LEAVE_LEFT { get; set; } = 0;
    public string SHIFT_CD   { get; set; } = "";
    public string SHIFT_TIME { get; set; } = "";
    public string SHIFT_TYPE { get; set; } = "";
    public string POSITION_NAME { get; set; } = "";
    public string HARDWORK_TEN  { get; set; } = "";
}

public class TeamMemberResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public int    total   { get; set; }
    public int    page    { get; set; } = 1;
    public int    page_size { get; set; } = 50;
    public int    total_pages { get; set; }
    public List<TeamMemberModel> data { get; set; } = new();
}

public class TeamScopeTupleModel
{
    public string DEPTCD    { get; set; } = "";
    public string DEPT_NAME { get; set; } = "";
    public string LINECD    { get; set; } = "";
    public string LINE_NAME { get; set; } = "";
    public string WORKCD    { get; set; } = "";
    public string WORK_NAME { get; set; } = "";
}

public class TeamShiftOptionModel
{
    public string SHIFTCD    { get; set; } = "";
    public string SHIFT_TIME { get; set; } = "";
    public string SHIFT_TYPE { get; set; } = "";
}

public class TeamPositionOptionModel
{
    public string JIKWICD       { get; set; } = "";
    public string POSITION_NAME { get; set; } = "";
}

public class TeamHardworkOptionModel
{
    public string INTEREST     { get; set; } = "";
    public string HARDWORK_TEN { get; set; } = "";
}

public class TeamScopeFiltersResponse
{
    public bool    success { get; set; }
    public string? message { get; set; }
    public List<TeamScopeTupleModel>     data       { get; set; } = new();
    public List<TeamShiftOptionModel>    shifts     { get; set; } = new();
    public List<TeamPositionOptionModel> positions  { get; set; } = new();
    public List<TeamHardworkOptionModel> hardworks  { get; set; } = new();
}
