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

    // Cho Clerk theo dõi team: tổng số NV nghỉ / ra cổng / có tiết học hôm nay
    public int      LEAVE_TODAY_TOTAL    { get; set; }
    public int      GP_TODAY_TOTAL       { get; set; }
    public int      TRAINING_TODAY_TOTAL { get; set; }   // §14.5 — số NV có session hôm nay

    public DateTime AS_OF         { get; set; } = DateTime.Now;
}

// Cho team birthday list (khi user click chip)
public class TeamBirthdayItem
{
    public string  EMPCD     { get; set; } = "";
    public string  CNAME     { get; set; } = "";
    public string? DEPT_NAME { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_NAME { get; set; }
    public string? BIRTHDAT  { get; set; } // MM-DD only, không lộ năm sinh
}

// ─── My Calendar ─────────────────────────────────────────────
// 1 event = 1 ô ngày trong lịch cá nhân. TYPE quyết định emoji + màu.
// Chỉ chứa trạng thái ĐÃ HOÀN TẤT (APPROVED / CONFIRMED) — không show pending.
public class HomeMyCalendarItem
{
    public string DATE   { get; set; } = "";  // yyyy-MM-dd
    public string TYPE   { get; set; } = "";  // LEAVE | GP | OT | ASSIGN
    public string LABEL  { get; set; } = "";  // "Nghỉ phép (AL)", "Ra cổng (OUT)", …
    public string DETAIL { get; set; } = "";  // dòng chi tiết (giờ, lý do, người sắp)
}

// Context user hiện tại — được HomeController resolve từ CurrentUser
public class HomeUserContext
{
    public string  EMPCD    { get; set; } = "";
    public string  ROLENAME { get; set; } = "Employee";
    public string? FULLNAME { get; set; }
    public string  LANG     => string.Equals(ROLENAME, "Expat", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
}
