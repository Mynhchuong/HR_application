namespace HR_web.Models.Gift;

public class GiftComboDetailItem
{
    public int    ID             { get; set; }
    public string COMPONENT_NAME { get; set; } = "";
    public int    DISPLAY_ORDER  { get; set; }
}

public class GiftCategoryItem
{
    public int    ID            { get; set; }
    public string NAME          { get; set; } = "";
    public int    DISPLAY_ORDER { get; set; }
    public bool   IS_ACTIVE     { get; set; } = true;
}

public class GiftCatalogItem
{
    public int     ID            { get; set; }
    public int     CATEGORY_ID   { get; set; }
    public string? CATEGORY_NAME { get; set; }
    public string  ITEM_NAME     { get; set; } = "";
    public string  ITEM_TYPE     { get; set; } = "SINGLE";
    public string? IMAGE_PATH    { get; set; }
    public bool    IS_ACTIVE     { get; set; } = true;
    public List<GiftComboDetailItem> COMBO_DETAILS { get; set; } = new();
}

public class GiftCatalogSaveRequest
{
    public int     ID          { get; set; }
    public int     CATEGORY_ID { get; set; }
    public string  ITEM_NAME   { get; set; } = "";
    public string  ITEM_TYPE   { get; set; } = "SINGLE";
    public string? IMAGE_PATH  { get; set; }
    public List<string> COMBO_COMPONENTS { get; set; } = new();
    public string  ACTOR_EMPCD { get; set; } = "";
}

public class GiftBatchItem
{
    public int     ID                { get; set; }
    public string  BATCH_NAME        { get; set; } = "";
    public int     GIFT_ITEM_ID      { get; set; }
    public string? GIFT_ITEM_NAME    { get; set; }
    public string? CATEGORY_NAME     { get; set; }
    public string  RECEIVE_FROM_DATE { get; set; } = "";
    public string? RECEIVE_TO_DATE   { get; set; }
    public string? REMARK            { get; set; }
    public bool    IS_COMPANY_WIDE   { get; set; }
    public string? LOCATION          { get; set; }
    public int     TOTAL_RECIPIENT   { get; set; }
    public int     DELIVERED_COUNT   { get; set; }
    public int     CONFIRMED_COUNT   { get; set; }
    public bool    IS_CLOSED         { get; set; }
    public string? CLOSED_DT         { get; set; }
    public string? CLOSED_BY         { get; set; }
}

public class GiftBatchCreateRequest
{
    public string  BATCH_NAME        { get; set; } = "";
    public int     GIFT_ITEM_ID      { get; set; }
    public string  RECEIVE_FROM_DATE { get; set; } = "";
    public string? RECEIVE_TO_DATE   { get; set; }
    public string? REMARK            { get; set; }
    public bool    IS_COMPANY_WIDE   { get; set; }
    public string? LOCATION          { get; set; }
    public string  ACTOR_EMPCD       { get; set; } = "";
}

public class GiftRecipientImportRow
{
    public string  EMPCD        { get; set; } = "";
    public string  RECEIVE_DATE { get; set; } = "";
    public string? LOCATION     { get; set; }
}

public class GiftRecipientImportRequest
{
    public int    BATCH_ID    { get; set; }
    public string ACTOR_EMPCD { get; set; } = "";
    public List<GiftRecipientImportRow> ROWS { get; set; } = new();
}

public class GiftRecipientListItem
{
    public int     ID                 { get; set; }
    public int     BATCH_ID           { get; set; }
    public string? BATCH_NAME         { get; set; }
    public string? GIFT_ITEM_NAME     { get; set; }
    public string? CATEGORY_NAME      { get; set; }
    public string  EMPCD              { get; set; } = "";
    public string? EMP_NAME           { get; set; }
    public string? DEPT_ID            { get; set; }
    public string? DEPT_NAME          { get; set; }
    public string? LINE_ID            { get; set; }
    public string? LINE_NAME          { get; set; }
    public string? WORK_ID            { get; set; }
    public string? WORK_NAME          { get; set; }
    public string  RECEIVE_DATE       { get; set; } = "";
    public string? LOCATION           { get; set; }
    public string? DELIVERED_DT       { get; set; }
    public string? DELIVERED_LOCATION { get; set; }
    public string? DELIVERED_BY       { get; set; }
    public string? DELIVERED_BY_NAME  { get; set; }
    public string  CONFIRM_STATUS     { get; set; } = "NONE";
    public string? CONFIRMED_DT       { get; set; }
    public string  STATUS             { get; set; } = "READY";
    public string? NOTE               { get; set; }
    public int TOTAL_COUNT { get; set; }
}

public class GiftRecipientListResponse
{
    public bool    success     { get; set; }
    public string? message     { get; set; }
    public object? summary     { get; set; }
    public int     total       { get; set; }
    public int     page        { get; set; }
    public int     page_size   { get; set; }
    public int     total_pages { get; set; }
    public List<GiftRecipientListItem> data { get; set; } = new();
}

public class GiftDeliverRequest
{
    public int     RECIPIENT_ID       { get; set; }
    public string? DELIVERED_LOCATION { get; set; }
    public string  ACTOR_EMPCD        { get; set; } = "";
}

public class GiftSendConfirmBulkRequest
{
    public List<int> RECIPIENT_IDS { get; set; } = new();
    public string     ACTOR_EMPCD  { get; set; } = "";
}

public class GiftDeliverBulkRequest
{
    public List<int> RECIPIENT_IDS      { get; set; } = new();
    public string?    DELIVERED_LOCATION { get; set; }
    public string     ACTOR_EMPCD        { get; set; } = "";
}

public class GiftDeliverAllRequest
{
    public int     BATCH_ID           { get; set; }
    public string? DELIVERED_LOCATION { get; set; }
    public string  ACTOR_EMPCD        { get; set; } = "";
}

public class GiftBatchCloseRequest
{
    public int    BATCH_ID    { get; set; }
    public string ACTOR_EMPCD { get; set; } = "";
}

public class GiftConfirmReceiptRequest
{
    public int    RECIPIENT_ID { get; set; }
    public string EMPCD        { get; set; } = "";
}

public class GiftActionResult
{
    public int      RECIPIENT_ID { get; set; }
    public bool     OK           { get; set; }
    public bool     SKIPPED      { get; set; }
    public string?  MESSAGE      { get; set; }
}

public class GiftBulkActionResponse
{
    public bool    success   { get; set; }
    public string? message   { get; set; }
    public int     processed { get; set; }
    public int     skipped   { get; set; }
    public int     failed    { get; set; }
    public List<GiftActionResult> results { get; set; } = new();
}

public class GiftMyPendingItem
{
    public int     RECIPIENT_ID { get; set; }
    public int     GIFT_ITEM_ID { get; set; }
    public string  GIFT_NAME    { get; set; } = "";
    public string  ITEM_TYPE    { get; set; } = "SINGLE";
    public string? LOCATION     { get; set; }
    public string? DELIVERED_DT { get; set; }
    public List<GiftComboDetailItem> COMBO_DETAILS { get; set; } = new();
}

public class GiftMyPendingResponse
{
    public bool   success { get; set; }
    public string? message { get; set; }
    public int    count   { get; set; }
    public List<GiftMyPendingItem> data { get; set; } = new();
}

public class SimpleApiResponse
{
    public bool    success { get; set; }
    public string? message { get; set; }
}
