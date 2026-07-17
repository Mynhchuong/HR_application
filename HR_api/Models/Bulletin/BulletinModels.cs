namespace HR_api.Models.Bulletin;

// ─────────────────────────────────────────────────────────────────────────
// Bài đăng — dùng cho list + detail (CONTENT có thể null khi list)
// ─────────────────────────────────────────────────────────────────────────
public class BulletinModel
{
    public int       ID             { get; set; }
    public string    TITLE          { get; set; } = "";
    public string?   CONTENT        { get; set; }              // NCLOB, null khi list nhẹ
    public string?   COVER_IMG      { get; set; }
    public DateTime  PUBLISH_FROM   { get; set; }
    public DateTime  PUBLISH_TO     { get; set; }
    public int       IS_PINNED      { get; set; }
    public int       PIN_ORDER      { get; set; }
    public int       IS_PUBLISHED   { get; set; }
    public DateTime? PUBLISHED_DT   { get; set; }
    public int       IS_ACTIVE      { get; set; }
    public int       VIEW_COUNT     { get; set; }
    public int       COMMENT_COUNT  { get; set; }              // computed
    public int       REACTION_COUNT { get; set; }              // computed — feed Index
    public string?   INST_ID        { get; set; }
    public DateTime? INST_DT        { get; set; }
    public string?   UPDT_ID        { get; set; }
    public DateTime? UPDT_DT        { get; set; }
    public string?   UPDT_FULL_NAME { get; set; }
    public List<BulletinMediaModel>      MEDIA       { get; set; } = new();
    public List<BulletinReactionSummary> REACTIONS   { get; set; } = new();   // chỉ trả ở Detail
    public string?                       MY_REACTION { get; set; }            // reaction của empcd hiện tại
}

public class BulletinMediaModel
{
    public int     ID            { get; set; }
    public int     BULLETIN_ID   { get; set; }
    public string  MEDIA_TYPE    { get; set; } = "IMG";    // 'IMG' | 'VIDEO'
    public string  FILE_NAME     { get; set; } = "";
    public int     DISPLAY_ORDER { get; set; }
}

public class BulletinCommentDto
{
    public int       ID         { get; set; }
    public int       BULLETIN_ID{ get; set; }
    public int?      PARENT_ID  { get; set; }
    public string    EMPCD      { get; set; } = "";
    public string?   FULL_NAME  { get; set; }
    public string?   EMP_CNAME  { get; set; }
    public string?   DEPTCD     { get; set; }
    public string?   LINECD     { get; set; }
    public string?   WORKCD     { get; set; }
    public string?   DEPT_NAME  { get; set; }
    public string?   LINE_NAME  { get; set; }
    public string?   WORK_NAME  { get; set; }
    public string    CONTENT    { get; set; } = "";
    public DateTime  INST_DT    { get; set; }
}

public class BulletinReactionSummary
{
    public string REACTION { get; set; } = "";
    public int    CNT      { get; set; }
}

public class BulletinStatsModel
{
    public int VIEW_COUNT    { get; set; }
    public int COMMENT_COUNT { get; set; }
    public List<BulletinReactionSummary> REACTIONS { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────
// Request payloads (POST body)
// ─────────────────────────────────────────────────────────────────────────
public class SaveBulletinRequest
{
    public int?     ID            { get; set; }                // null/0 = insert
    public string   TITLE         { get; set; } = "";
    public string   CONTENT       { get; set; } = "";          // HTML
    public string?  COVER_IMG     { get; set; }
    public DateTime PUBLISH_FROM  { get; set; }
    public DateTime PUBLISH_TO    { get; set; }
    public int      IS_PINNED     { get; set; }
    public int      PIN_ORDER     { get; set; }
    public string?  LOGIN_USER    { get; set; }                // EmpCd HR
    public string?  LOGIN_NAME    { get; set; }                // FullName HR (lưu UPDT_FULL_NAME)
}

public class AddMediaRequest
{
    public int    BULLETIN_ID   { get; set; }
    public string MEDIA_TYPE    { get; set; } = "IMG";
    public string FILE_NAME     { get; set; } = "";
    public int    DISPLAY_ORDER { get; set; }
}

public class AddCommentRequest
{
    public int    BULLETIN_ID { get; set; }
    public int?   PARENT_ID   { get; set; }
    public string EMPCD       { get; set; } = "";
    public string CONTENT     { get; set; } = "";
}

public class ReactRequest
{
    public int    BULLETIN_ID { get; set; }
    public string EMPCD       { get; set; } = "";
    public string REACTION    { get; set; } = "";    // LIKE/LOVE/HAHA/WOW/SAD/ANGRY
}
