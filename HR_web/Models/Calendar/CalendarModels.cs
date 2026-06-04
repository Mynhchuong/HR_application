namespace HR_web.Models.Calendar;

public class CalendarAttendanceItem
{
    public string   date         { get; set; } = "";
    public string?  shiftCode    { get; set; }
    public string?  shiftLabel   { get; set; }
    public string?  timeIn       { get; set; }
    public string?  timeOut      { get; set; }
    public decimal  tFormal      { get; set; }
    public decimal  tOt          { get; set; }
    public decimal  tRot         { get; set; }
    public decimal  otPlan       { get; set; }
    public decimal  otBeforeTime { get; set; }
    public decimal  otAfterTime  { get; set; }
}

public class CalendarGpItem
{
    public string  date   { get; set; } = "";
    public string? type   { get; set; }
    public string? status { get; set; }
}

public class CalendarLeaveItem
{
    public string  from      { get; set; } = "";
    public string  to        { get; set; } = "";
    public string? leaveType { get; set; }
    public string? status    { get; set; }
    public decimal days      { get; set; }
}

public class CalendarMonthlyData
{
    public List<CalendarAttendanceItem> attendance { get; set; } = new();
    public List<CalendarGpItem>         gatePass   { get; set; } = new();
    public List<CalendarLeaveItem>      leave      { get; set; } = new();
}

public class CalendarMonthlyResponse
{
    public bool                 success { get; set; }
    public string?              message { get; set; }
    public CalendarMonthlyData? data    { get; set; }
}
