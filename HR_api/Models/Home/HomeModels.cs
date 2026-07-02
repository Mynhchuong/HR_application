using HR_api.Models.GatePass;

namespace HR_api.Models.Home;

// Response tổng của /Home/init — gộp toàn bộ dữ liệu cần cho trang Home.
// Trường null → frontend ẩn UI tương ứng.
public class HomeInitResponse
{
    public HomeGreetingModel?  Greeting  { get; set; }
    public HomeBirthdayModel?  Birthday  { get; set; }
    public HomePaydayModel?    Payday    { get; set; }
    public GpShiftInfoModel?   Shift     { get; set; }
    public HomeBannerModel?    Banner    { get; set; }
    public List<HomePinnedBulletinModel> Pinned { get; set; } = new();
}

public class HomeGreetingModel
{
    public string  MESSAGE { get; set; } = "";
    public string? EMOJI   { get; set; }
    public string  LANG    { get; set; } = "vi"; // vi | en
}

public class HomeBirthdayModel
{
    public string  TYPE     { get; set; } = "BIRTHDAY"; // BIRTHDAY | ANNIVERSARY | BOTH
    public string  EMP_NAME { get; set; } = "";
    public string  MESSAGE  { get; set; } = "";
    public string? EMOJI    { get; set; }
    public int?    YEARS    { get; set; } // chỉ set khi TYPE có ANNIVERSARY
}

public class HomePaydayModel
{
    public bool   IS_PAYDAY   { get; set; }
    public string ACTUAL_DATE { get; set; } = ""; // yyyy-MM-dd
    public string MESSAGE     { get; set; } = "";
    public string EMOJI       { get; set; } = "💰";
    public string CTA_URL     { get; set; } = "/Payslip/Index";
}

public class HomeBannerModel
{
    public int     ID              { get; set; }
    public string  IMAGE_FILE      { get; set; } = "";
    public string? OVERLAY_TEXT    { get; set; }
    public string  OVERLAY_POS     { get; set; } = "BL";
    public string? LINK_URL        { get; set; }
    public string  LINK_TARGET     { get; set; } = "_self";
    public bool    IS_DISMISSIBLE  { get; set; } = true;
}

public class HomePinnedBulletinModel
{
    public int     ID         { get; set; }
    public string  TITLE      { get; set; } = "";
    public string? COVER_IMG  { get; set; }
    public int     PIN_ORDER  { get; set; }
}

// Response cho GET /Home/summary
public class HomeSummaryModel
{
    public int      LEAVE_PENDING { get; set; }
    public int      GP_PENDING    { get; set; }
    public int      OT_NEED_SIGN  { get; set; }
    public int      OT_SIGNED     { get; set; }
    public int      OT_TOTAL      { get; set; }
    public int      TEAM_BIRTHDAY_COUNT { get; set; }
    public DateTime AS_OF         { get; set; } = DateTime.Now;
}

// Cho team birthday list (khi user click chip)
public class TeamBirthdayItem
{
    public string  EMPCD    { get; set; } = "";
    public string  CNAME    { get; set; } = "";
    public string? DEPTCD   { get; set; }
    public string? LINECD   { get; set; }
    public string? WORKCD   { get; set; }
    public string? BIRTHDAT { get; set; } // MM-DD only, không lộ năm sinh
}

// Context user hiện tại — được HomeController resolve từ CurrentUser
public class HomeUserContext
{
    public string  EMPCD    { get; set; } = "";
    public string  ROLENAME { get; set; } = "Employee";
    public string? FULLNAME { get; set; }
    public string  LANG     => string.Equals(ROLENAME, "Expat", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
}
