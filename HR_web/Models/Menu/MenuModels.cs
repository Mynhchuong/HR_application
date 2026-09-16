namespace HR_web.Models.Menu;

public class MenuWeekModel
{
    public int       ID        { get; set; }
    public string?   WEEK_NAME { get; set; }
    public DateTime  FROM_DATE { get; set; }
    public DateTime  TO_DATE   { get; set; }
    public string    STATUS    { get; set; } = string.Empty;
    public string?   REMARK    { get; set; }
    public string?   INST_ID   { get; set; }
    public DateTime? INST_DT   { get; set; }
    public string?   UPDT_ID   { get; set; }
    public DateTime? UPDT_DT   { get; set; }
}

public class MenuFoodModel
{
    public int       ID         { get; set; }
    public string    FOOD_NAME  { get; set; } = string.Empty;
    public string?   FOOD_TYPE  { get; set; }
    public string    IS_IMAGE   { get; set; } = "N";
    public int       IS_ACTIVE  { get; set; }
    public string?   INST_ID    { get; set; }
    public DateTime? INST_DT    { get; set; }
    public string?   UPDT_ID    { get; set; }
    public DateTime? UPDT_DT    { get; set; }
}

public class MenuBanhFixedSlot
{
    public int     SlotNo   { get; set; }
    public int?    FoodId   { get; set; }
    public string? FoodName { get; set; }
    public string  IsImage  { get; set; } = "N";
}

public class MenuDetailModel
{
    public int     ID            { get; set; }
    public int     WEEK_ID       { get; set; }
    public int     DAY_NO        { get; set; }
    public string  SHIFT         { get; set; } = string.Empty;
    public string  MEAL_TYPE     { get; set; } = string.Empty;
    public int?    FOOD_ID       { get; set; }
    public string? FOOD_NAME     { get; set; }
    public int     DISPLAY_ORDER { get; set; }
}

public class SaveDetailItem
{
    public int     DAY_NO        { get; set; }
    public string  SHIFT         { get; set; } = string.Empty;
    public string  MEAL_TYPE     { get; set; } = string.Empty;
    public int?    FOOD_ID       { get; set; }
    public int     DISPLAY_ORDER { get; set; } = 1;
}

public class SaveDetailRequest
{
    public int                  WEEK_ID    { get; set; }
    public List<SaveDetailItem> ITEMS      { get; set; } = new();
    public string?              LOGIN_USER { get; set; }
}

public class SaveWeekRequest
{
    public int?     ID         { get; set; }
    public string?  WEEK_NAME  { get; set; }
    public DateTime FROM_DATE  { get; set; }
    public DateTime TO_DATE    { get; set; }
    public string?  REMARK     { get; set; }
    public string?  LOGIN_USER { get; set; }
}

public class SaveFoodRequest
{
    public int?    ID              { get; set; }
    public string  FOOD_NAME       { get; set; } = string.Empty;
    public string? FOOD_TYPE       { get; set; }
    public int     IS_ACTIVE       { get; set; } = 1;
    public string? LOGIN_USER      { get; set; }
    public bool    Bypass          { get; set; } = false;
    public int?    CopyImageFromId { get; set; }
}

// ── Popup "món tên giống nhau" — import danh mục món ăn (bulk) ─────────────────
public class FoodImportPendingItem
{
    public string Name            { get; set; } = "";
    public string Type            { get; set; } = "";
    public int    Active          { get; set; } = 1;
    public int    MatchedId       { get; set; }
    public string MatchedName     { get; set; } = "";
    public bool   MatchedHasImage { get; set; }
}

public class FoodImportResolveItem
{
    public string Name      { get; set; } = "";
    public string Type      { get; set; } = "";
    public int    Active    { get; set; } = 1;
    public string Decision  { get; set; } = ""; // useExisting|createNew|createNewCopyImage
    public int    MatchedId { get; set; }
}

// ── Popup "món tên giống nhau" — import thực đơn tuần ──────────────────────────
public class WeekImportPendingItem
{
    public string Shift           { get; set; } = "";
    public int    DayNo           { get; set; }
    public string MealType        { get; set; } = "";
    public int    DisplayOrder    { get; set; } = 1;
    public string TypedName       { get; set; } = "";
    public int    MatchedId       { get; set; }
    public string MatchedName     { get; set; } = "";
    public bool   MatchedHasImage { get; set; }
}

public class WeekImportResolvedItem : WeekImportPendingItem
{
    public string Decision { get; set; } = ""; // useExisting|createNew|createNewCopyImage
}

public class FinalizeWeekImportRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate   { get; set; }
    public List<SaveDetailItem>        Items    { get; set; } = new();
    public List<WeekImportResolvedItem> Resolved { get; set; } = new();
}

public class UserTodayMealModel
{
    public string? FOOD_NAME { get; set; }
    public string? FOOD_TYPE { get; set; }
    public string? SHIFT     { get; set; }
}

public class ChangeMealViewModel
{
    public string? EmpCd           { get; set; }
    public string? FullName        { get; set; }
    public string? CurrentFoodType { get; set; } // MAN, NHE, CHAY
    public string? CurrentFoodName { get; set; }
}

public class ChangeMealRequest
{
    public string  MealType  { get; set; } = string.Empty; // MAN, NHE, CHAY, CHAY_TRUONG, NUOC_TRUONG
    public string? TypeMeal  { get; set; } = "LUNCH";      // LUNCH or OT
    public string? FromDate  { get; set; }
    public string? ToDate    { get; set; }
    public string? LoginUser { get; set; }
}

public class ImportRowError
{
    public string Location { get; set; } = string.Empty;
    public string Message  { get; set; } = string.Empty;
}

public class MenuWeekDetailViewModel
{
    public MenuWeekModel         Week    { get; set; } = new();
    public List<MenuDetailModel> Details { get; set; } = new();
    public List<MenuFoodModel>   Foods   { get; set; } = new();
}

public class MenuDayViewModel
{
    public int     DAY_NO        { get; set; }
    public string  DAY_LABEL     { get; set; } = string.Empty;
    public string  SHIFT         { get; set; } = string.Empty;
    public string  MEAL_TYPE     { get; set; } = string.Empty;
    public int?    FOOD_ID       { get; set; }
    public string? FOOD_NAME     { get; set; }
    public string  IS_IMAGE      { get; set; } = "N";
    public int     DISPLAY_ORDER { get; set; }
}
