namespace HR_web.ViewModels.Home;

// VM cho trang /Home/Index — dùng chung cho 4 view (Employee/Manager/Expat/Admin).
// Được HomeController dispatcher build sau khi gọi /apiHR/Home/init.
public class HomeIndexViewModel
{
    public string EmpCd     { get; set; } = "";
    public string FullName  { get; set; } = "";
    public string RoleName  { get; set; } = "Employee";
    public string Lang      => string.Equals(RoleName, "Expat", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
    public bool   ShowSummary => RoleName is "Supervisor" or "DeputyManager" or "Manager" or "Assistant" or "Expat" or "HR" or "Admin";
    public bool   ShowShift   => RoleName is not ("HR" or "Admin");

    // Data từ /Home/init
    public HomeGreetingVm?           Greeting  { get; set; }
    public HomeBirthdayVm?           Birthday  { get; set; }
    public HomePaydayVm?             Payday    { get; set; }
    public HomeShiftVm?              Shift     { get; set; }
    public HomeBannerVm?             Banner    { get; set; }
    public List<HomePinnedItemVm>    Pinned    { get; set; } = new();
}

public class HomeGreetingVm
{
    public string  Message { get; set; } = "";
    public string? Emoji   { get; set; }
}

public class HomeBirthdayVm
{
    public string  Type    { get; set; } = "BIRTHDAY"; // BIRTHDAY | ANNIVERSARY | BOTH
    public string  EmpName { get; set; } = "";
    public string  Message { get; set; } = "";
    public string? Emoji   { get; set; }
    public int?    Years   { get; set; }
}

public class HomePaydayVm
{
    public string ActualDate { get; set; } = "";
    public string Message    { get; set; } = "";
    public string Emoji      { get; set; } = "💰";
    public string CtaUrl     { get; set; } = "/Payslip/Index";
}

public class HomeShiftVm
{
    public string? ShiftCd   { get; set; }
    public string? StartTime { get; set; } // HH:mm
    public string? EndTime   { get; set; } // HH:mm
    public string? WorkDate  { get; set; } // yyyy-MM-dd (có thể là hôm qua nếu ca đêm)
    public bool    IsNightShift { get; set; } // STIME > ETIME (ca wrap qua nửa đêm)
}

public class HomeBannerVm
{
    public int     Id             { get; set; }
    public string  ImageUrl       { get; set; } = ""; // URL trả qua /Image/HomeBanner/{file}
    public string? OverlayText    { get; set; }
    public string  OverlayPos     { get; set; } = "BL";
    public string? LinkUrl        { get; set; }
    public string  LinkTarget     { get; set; } = "_self";
    public bool    IsDismissible  { get; set; } = true;
}

public class HomePinnedItemVm
{
    public int     Id       { get; set; }
    public string  Title    { get; set; } = "";
    public string? CoverImg { get; set; }
}
