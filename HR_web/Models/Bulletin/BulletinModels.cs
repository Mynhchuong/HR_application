using System.ComponentModel.DataAnnotations;

namespace HR_web.Models.Bulletin;

public class BulletinModel
{
    public int       ID             { get; set; }
    public string    TITLE          { get; set; } = "";
    public string?   CONTENT        { get; set; }
    /// <summary>Tiêu đề tiếng Anh — NV nước ngoài đọc bản này (null ở bài đăng trước 2026-11-12).</summary>
    public string?   TITLE_EN       { get; set; }
    /// <summary>Nội dung tiếng Anh (HTML) — xem <see cref="TITLE_EN"/>.</summary>
    public string?   CONTENT_EN     { get; set; }

    /// <summary>
    /// Tiêu đề hiển thị theo ngôn ngữ giao diện (UserInfoModel.UiLang).
    /// NV nước ngoài ("en"/"ko") đọc bản tiếng Anh — chưa có bản tiếng Hàn cho NỘI DUNG bản tin,
    /// nên tiếng Hàn cũng dùng chung bản EN. Bài đăng trước 2026-11-12 chưa có TITLE_EN thì rơi
    /// về tiếng Việt thay vì hiện ô trống.
    /// </summary>
    public string TitleFor(string? lang)
        => lang != "vi" && !string.IsNullOrWhiteSpace(TITLE_EN) ? TITLE_EN! : TITLE;

    /// <summary>Nội dung hiển thị theo ngôn ngữ — xem <see cref="TitleFor"/>.</summary>
    public string? ContentFor(string? lang)
        => lang != "vi" && !string.IsNullOrWhiteSpace(CONTENT_EN) ? CONTENT_EN : CONTENT;
    public string?   COVER_IMG      { get; set; }
    public DateTime  PUBLISH_FROM   { get; set; }
    public DateTime  PUBLISH_TO     { get; set; }
    public int       IS_PINNED      { get; set; }
    public int       PIN_ORDER      { get; set; }
    public int       IS_PUBLISHED   { get; set; }
    public DateTime? PUBLISHED_DT   { get; set; }
    public int       IS_ACTIVE      { get; set; } = 1;
    public int       VIEW_COUNT     { get; set; }
    public int       COMMENT_COUNT  { get; set; }
    public int       REACTION_COUNT { get; set; }
    public string?   INST_ID        { get; set; }
    public string?   CREATED_ROLE   { get; set; }
    public DateTime? INST_DT        { get; set; }
    public string?   UPDT_ID        { get; set; }
    public DateTime? UPDT_DT        { get; set; }
    public string?   UPDT_FULL_NAME { get; set; }
    public List<BulletinMediaModel>      MEDIA       { get; set; } = new();
    public List<BulletinReactionSummary> REACTIONS   { get; set; } = new();
    public string?                       MY_REACTION { get; set; }
}

public class BulletinMediaModel
{
    public int    ID            { get; set; }
    public int    BULLETIN_ID   { get; set; }
    public string MEDIA_TYPE    { get; set; } = "IMG";
    public string FILE_NAME     { get; set; } = "";
    public int    DISPLAY_ORDER { get; set; }
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
// Form bind (POST từ view) — chuyển thành SaveBulletinRequest gửi sang API
// ─────────────────────────────────────────────────────────────────────────
public class BulletinEditViewModel
{
    public int       ID            { get; set; }
    public string    TITLE         { get; set; } = "";
    public string    CONTENT       { get; set; } = "";
    /// <summary>Tiêu đề tiếng Anh — BẮT BUỘC khi lưu (yêu cầu 2026-11-12).</summary>
    public string?   TITLE_EN      { get; set; }
    /// <summary>Nội dung tiếng Anh (HTML từ editor TipTap thứ 2) — BẮT BUỘC khi lưu.</summary>
    public string?   CONTENT_EN    { get; set; }
    public string?   COVER_IMG     { get; set; }

    // [DataType(DataType.Date)] để asp-for + type="date" render "yyyy-MM-dd"
    [DataType(DataType.Date)]
    public DateTime? PUBLISH_FROM  { get; set; }

    [DataType(DataType.Date)]
    public DateTime? PUBLISH_TO    { get; set; }

    public int       IS_PINNED     { get; set; }
    public int       PIN_ORDER     { get; set; }
}

public class SaveBulletinRequest
{
    public int?     ID            { get; set; }
    public string   TITLE         { get; set; } = "";
    public string   CONTENT       { get; set; } = "";
    public string?  TITLE_EN      { get; set; }
    public string?  CONTENT_EN    { get; set; }
    public string?  COVER_IMG     { get; set; }
    public DateTime PUBLISH_FROM  { get; set; }
    public DateTime PUBLISH_TO    { get; set; }
    public int      IS_PINNED     { get; set; }
    public int      PIN_ORDER     { get; set; }
    public string?  LOGIN_USER    { get; set; }
    public string?  LOGIN_NAME    { get; set; }
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
    public string REACTION    { get; set; } = "";
}
