namespace HR_api.Models.Home;

// ============================================================
// BANNER
// ============================================================
public class HomeAdminBannerListItem
{
    public int      ID              { get; set; }
    public string   IMAGE_FILE      { get; set; } = "";
    public string?  OVERLAY_TEXT_VI { get; set; }
    public string?  OVERLAY_TEXT_EN { get; set; }
    public string   OVERLAY_POS     { get; set; } = "BL";
    public string?  LINK_URL        { get; set; }
    public string   LINK_TARGET     { get; set; } = "_self";
    public string?  TARGET_ROLES    { get; set; } // CSV
    public bool     IS_DISMISSIBLE  { get; set; }
    public DateTime PUBLISH_FROM    { get; set; }
    public DateTime PUBLISH_TO      { get; set; }
    public bool     IS_ACTIVE       { get; set; }
    public string?  INST_ID         { get; set; }
    public DateTime? INST_DT        { get; set; }
    public string?  UPDT_ID         { get; set; }
    public DateTime? UPDT_DT        { get; set; }
}

public class SaveBannerRequest
{
    public int?     ID              { get; set; } // null = insert, có = update
    public string   IMAGE_FILE      { get; set; } = "";
    public string?  OVERLAY_TEXT_VI { get; set; }
    public string?  OVERLAY_TEXT_EN { get; set; }
    public string   OVERLAY_POS     { get; set; } = "BL";
    public string?  LINK_URL        { get; set; }
    public string   LINK_TARGET     { get; set; } = "_self";
    public string?  TARGET_ROLES    { get; set; }
    public bool     IS_DISMISSIBLE  { get; set; } = true;
    public DateTime PUBLISH_FROM    { get; set; }
    public DateTime PUBLISH_TO      { get; set; }
    public string?  LOGIN_USER      { get; set; }
    public string?  LOGIN_NAME      { get; set; }
    public string?  LOGIN_ROLE      { get; set; } // "HR" hoặc "Admin" — HR bị giới hạn field
}

public class BannerActionRequest
{
    public int      ID         { get; set; }
    public string?  LOGIN_USER { get; set; }
    public string?  LOGIN_NAME { get; set; }
    public string?  LOGIN_ROLE { get; set; }
}

// ============================================================
// GREETING
// ============================================================
public class HomeAdminGreetingItem
{
    public int     ID         { get; set; }
    public string  TIME_SLOT  { get; set; } = "";
    public string  MESSAGE_VI { get; set; } = "";
    public string? MESSAGE_EN { get; set; }
    public string? EMOJI      { get; set; }
    public bool    IS_ACTIVE  { get; set; }
    public DateTime? UPDT_DT  { get; set; }
    public string?  UPDT_ID   { get; set; }
}

public class SaveGreetingRequest
{
    public int?    ID         { get; set; } // null = insert, có = update
    public string  TIME_SLOT  { get; set; } = ""; // whitelist ở server
    public string  MESSAGE_VI { get; set; } = "";
    public string? MESSAGE_EN { get; set; }
    public string? EMOJI      { get; set; }
    public bool    IS_ACTIVE  { get; set; } = true;
    public string? LOGIN_USER { get; set; }
    public string? LOGIN_NAME { get; set; }
    public string? LOGIN_ROLE { get; set; }
}

public class GreetingActionRequest
{
    public int      ID         { get; set; }
    public string?  LOGIN_USER { get; set; }
    public string?  LOGIN_NAME { get; set; }
    public string?  LOGIN_ROLE { get; set; }
}

// ============================================================
// TIME SLOT RULE
// ============================================================
public class HomeAdminRuleItem
{
    public string   TIME_SLOT { get; set; } = "";
    public int      FROM_HOUR { get; set; }
    public int      TO_HOUR   { get; set; }
    public bool     IS_ACTIVE { get; set; }
    public DateTime? UPDT_DT  { get; set; }
    public string?  UPDT_ID   { get; set; }
}

public class SaveRuleRequest
{
    public string  TIME_SLOT  { get; set; } = ""; // whitelist
    public int     FROM_HOUR  { get; set; }
    public int     TO_HOUR    { get; set; }
    public string? LOGIN_USER { get; set; }
    public string? LOGIN_NAME { get; set; }
}

// ============================================================
// AUDIT
// ============================================================
public class HomeAdminAuditItem
{
    public int      ID          { get; set; }
    public string   ACTOR_EMPCD { get; set; } = "";
    public string?  ACTOR_NAME  { get; set; }
    public string?  ROLE_NAME   { get; set; }
    public string   ACTION      { get; set; } = "";
    public string   ENTITY      { get; set; } = "";
    public string?  ENTITY_ID   { get; set; }
    public string?  OLD_VAL     { get; set; }
    public string?  NEW_VAL     { get; set; }
    public string?  IP_ADDRESS  { get; set; }
    public string?  USER_AGENT  { get; set; }
    public DateTime INST_DT     { get; set; }
}

public class HomeAdminAuditPage
{
    public List<HomeAdminAuditItem> DATA  { get; set; } = new();
    public int TOTAL       { get; set; }
    public int PAGE        { get; set; }
    public int PAGE_SIZE   { get; set; }
    public int TOTAL_PAGES { get; set; }
}
