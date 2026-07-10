namespace HR_web.Models.Training;

// Mirror của HR_api Models/Training/TeamScheduleModels — chỉ nhận về từ API
public class TeamScheduleItem
{
    public string EMPCD { get; set; } = "";
    public string? EMP_NAME { get; set; }
    public string? DEPTCD { get; set; }
    public string? DEPT_NAME { get; set; }
    public string? LINECD { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORKCD { get; set; }
    public string? WORK_NAME { get; set; }

    public int    CLASS_ID { get; set; }
    public string CLASS_NAME { get; set; } = "";
    public string COURSE_TITLE { get; set; } = "";

    public int    SESSION_ID { get; set; }
    public int    SESSION_NO { get; set; }
    public DateTime SESSION_DATE { get; set; }
    public string START_TIME { get; set; } = "";
    public string END_TIME { get; set; } = "";
    public string? TOPIC { get; set; }
    public string? LOCATION { get; set; }
    public string SESSION_STATUS { get; set; } = "";

    public int?   GROUP_ID { get; set; }
    public string? GROUP_NAME { get; set; }
}

public class TeamScheduleResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public List<TeamScheduleItem>? data { get; set; }
}

public class HasScopeResponse
{
    public bool success { get; set; }
    public bool data { get; set; }
}
