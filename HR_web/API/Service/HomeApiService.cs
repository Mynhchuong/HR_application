namespace HR_web.API.Service;

// Wrapper gọi /apiHR/Home/* — tách khỏi HomeController để dễ test/mock.
public class HomeApiService
{
    private readonly ApiService _api;

    public HomeApiService(ApiService api) { _api = api; }

    // ============================================================
    // GET /apiHR/Home/init
    // ============================================================
    public async Task<HomeInitData?> GetInitAsync(string empcd, string? roleName, string? fullName)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empcd)}" +
                    (string.IsNullOrEmpty(roleName) ? "" : $"&role_name={Uri.EscapeDataString(roleName)}") +
                    (string.IsNullOrEmpty(fullName) ? "" : $"&full_name={Uri.EscapeDataString(fullName)}");

            var resp = await _api.GetAsync<HomeInitResponse>("home/init", q);
            return resp?.success == true ? resp.data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeApiService] GetInitAsync error: {ex.Message}");
            return null;
        }
    }

    // ============================================================
    // GET /apiHR/Home/hr-summary → counts cho HR dashboard
    // ============================================================
    public async Task<HrDashboardCounts?> GetHrSummaryAsync()
    {
        try
        {
            var resp = await _api.GetAsync<HrSummaryResponse>("home/hr-summary", "");
            return resp?.success == true ? resp.data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeApiService] GetHrSummaryAsync error: {ex.Message}");
            return null;
        }
    }

    private class HrSummaryResponse
    {
        public bool success { get; set; }
        public HrDashboardCounts? data { get; set; }
    }

    // ============================================================
    // GET /apiHR/Home/summary
    // ============================================================
    public async Task<HomeSummaryData?> GetSummaryAsync(string empcd, string? roleName, bool force = false)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empcd)}" +
                    (string.IsNullOrEmpty(roleName) ? "" : $"&role_name={Uri.EscapeDataString(roleName)}") +
                    (force ? "&force=1" : "");

            var resp = await _api.GetAsync<HomeSummaryResponse>("home/summary", q);
            return resp?.success == true ? resp.data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeApiService] GetSummaryAsync error: {ex.Message}");
            return null;
        }
    }

    // ============================================================
    // GET /apiHR/Home/my-calendar
    // ============================================================
    public async Task<List<HomeMyCalendarItem>?> GetMyCalendarAsync(string empcd, int year, int month)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empcd)}&year={year}&month={month}";
            var resp = await _api.GetAsync<MyCalendarResponse>("home/my-calendar", q);
            return resp?.success == true ? resp.data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeApiService] GetMyCalendarAsync error: {ex.Message}");
            return null;
        }
    }

    // ============================================================
    // GET /apiHR/Home/team-birthday
    // ============================================================
    public async Task<List<TeamBirthdayItem>?> GetTeamBirthdayAsync(string empcd, string? roleName)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empcd)}" +
                    (string.IsNullOrEmpty(roleName) ? "" : $"&role_name={Uri.EscapeDataString(roleName)}");

            var resp = await _api.GetAsync<TeamBirthdayResponse>("home/team-birthday", q);
            return resp?.success == true ? resp.data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeApiService] GetTeamBirthdayAsync error: {ex.Message}");
            return null;
        }
    }

    // ============================================================
    // GET /apiHR/Home/leave-doc-missing
    // ============================================================
    public async Task<List<LeaveDocMissingItem>?> GetLeaveDocMissingAsync(string empcd, string? roleName)
    {
        try
        {
            var q = $"empcd={Uri.EscapeDataString(empcd)}" +
                    (string.IsNullOrEmpty(roleName) ? "" : $"&role_name={Uri.EscapeDataString(roleName)}");

            var resp = await _api.GetAsync<LeaveDocMissingResponse>("home/leave-doc-missing", q);
            return resp?.success == true ? resp.data : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomeApiService] GetLeaveDocMissingAsync error: {ex.Message}");
            return null;
        }
    }
}

// ─── Response DTO (match /apiHR/Home/* JSON structure) ───────────────
public class HomeInitResponse
{
    public bool          success { get; set; }
    public string?       message { get; set; }
    public HomeInitData? data    { get; set; }
}

public class HomeInitData
{
    public HomeGreetingDto?         Greeting  { get; set; }
    public HomeBirthdayDto?         Birthday  { get; set; }
    public HomePaydayDto?           Payday    { get; set; }
    public HomeShiftDto?            Shift     { get; set; }
    public HomeBannerDto?           Banner    { get; set; }
    public List<HomePinnedDto>      Pinned    { get; set; } = new();
}

public class HomeGreetingDto
{
    public string  MESSAGE { get; set; } = "";
    public string? EMOJI   { get; set; }
    public string  LANG    { get; set; } = "vi";
}

public class HomeBirthdayDto
{
    public string  TYPE     { get; set; } = "BIRTHDAY";
    public string  EMP_NAME { get; set; } = "";
    public string  MESSAGE  { get; set; } = "";
    public string? EMOJI    { get; set; }
    public int?    YEARS    { get; set; }
}

public class HomePaydayDto
{
    public bool   IS_PAYDAY   { get; set; }
    public string ACTUAL_DATE { get; set; } = "";
    public string MESSAGE     { get; set; } = "";
    public string EMOJI       { get; set; } = "💰";
    public string CTA_URL     { get; set; } = "/Payslip/Index";
}

public class HomeShiftDto
{
    public string? SHIFTCD            { get; set; }
    public string? STIME              { get; set; }
    public string? ETIME              { get; set; }
    public string? WORK_DATE          { get; set; }
    public string? WORK_DATE_TOMORROW { get; set; }
}

public class HomeBannerDto
{
    public int     ID              { get; set; }
    public string  IMAGE_FILE      { get; set; } = "";
    public string? OVERLAY_TEXT    { get; set; }
    public string  OVERLAY_POS     { get; set; } = "BL";
    public string? LINK_URL        { get; set; }
    public string  LINK_TARGET     { get; set; } = "_self";
    public bool    IS_DISMISSIBLE  { get; set; }
}

public class HomePinnedDto
{
    public int     ID         { get; set; }
    public string  TITLE      { get; set; } = "";
    public string? COVER_IMG  { get; set; }
    public int     PIN_ORDER  { get; set; }
}

// ─── Summary
public class HomeSummaryResponse
{
    public bool             success { get; set; }
    public string?          message { get; set; }
    public HomeSummaryData? data    { get; set; }
}

// ─── HR dashboard counts
public class HrDashboardCounts
{
    public int unreadMessages           { get; set; }
    public int openInquiries            { get; set; }
    public int bulletinsWithNewComments { get; set; }
}

public class HomeSummaryData
{
    public int      LEAVE_PENDING       { get; set; }
    public int      GP_PENDING          { get; set; }
    public int      OT_NEED_SIGN        { get; set; }
    public int      OT_SIGNED           { get; set; }
    public int      OT_TOTAL            { get; set; }
    public int      TEAM_BIRTHDAY_COUNT { get; set; }
    public int      LEAVE_TODAY_TOTAL   { get; set; }
    public int      GP_TODAY_TOTAL      { get; set; }
    public int      LEAVE_DOC_MISSING_COUNT { get; set; }
    public DateTime AS_OF               { get; set; }
}

// ─── Leave doc missing (Đám tang/Đám cưới/Vợ sanh/Khám thai chưa nộp giấy tờ)
public class LeaveDocMissingResponse
{
    public bool                       success { get; set; }
    public string?                    message { get; set; }
    public List<LeaveDocMissingItem>? data    { get; set; }
}

public class LeaveDocMissingItem
{
    public string    EMPCD      { get; set; } = "";
    public string?   CNAME      { get; set; }
    public string?   DEPT_NAME  { get; set; }
    public string?   LINE_NAME  { get; set; }
    public string?   WORK_NAME  { get; set; }
    public string?   LEAVE_TYPE { get; set; }
    public DateTime? FROM_DATE  { get; set; }
    public DateTime? TO_DATE    { get; set; }
    public string?   DOC_STATUS { get; set; }
}

// ─── Team birthday
public class TeamBirthdayResponse
{
    public bool                    success { get; set; }
    public string?                 message { get; set; }
    public List<TeamBirthdayItem>? data    { get; set; }
}

public class TeamBirthdayItem
{
    public string  EMPCD     { get; set; } = "";
    public string  CNAME     { get; set; } = "";
    public string? DEPT_NAME { get; set; }
    public string? LINE_NAME { get; set; }
    public string? WORK_NAME { get; set; }
    public string? BIRTHDAT  { get; set; }
}

// ─── My Calendar
public class MyCalendarResponse
{
    public bool                       success { get; set; }
    public string?                    message { get; set; }
    public int                        year    { get; set; }
    public int                        month   { get; set; }
    public List<HomeMyCalendarItem>?  data    { get; set; }
}

public class HomeMyCalendarItem
{
    public string DATE   { get; set; } = "";
    public string TYPE   { get; set; } = "";   // LEAVE | GP | OT | ASSIGN
    public string LABEL  { get; set; } = "";
    public string DETAIL { get; set; } = "";
}
