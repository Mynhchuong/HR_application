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
    public int       IS_ACTIVE  { get; set; }
    public string?   INST_ID    { get; set; }
    public DateTime? INST_DT    { get; set; }
    public string?   UPDT_ID    { get; set; }
    public DateTime? UPDT_DT    { get; set; }
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
    public string? FOOD_TEXT     { get; set; }
    public int     DISPLAY_ORDER { get; set; }
}

public class SaveDetailItem
{
    public int     DAY_NO        { get; set; }
    public string  SHIFT         { get; set; } = string.Empty;
    public string  MEAL_TYPE     { get; set; } = string.Empty;
    public int?    FOOD_ID       { get; set; }
    public string? FOOD_TEXT     { get; set; }
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
    public int?    ID         { get; set; }
    public string  FOOD_NAME  { get; set; } = string.Empty;
    public string? FOOD_TYPE  { get; set; }
    public int     IS_ACTIVE  { get; set; } = 1;
    public string? LOGIN_USER { get; set; }
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
    public string? FOOD_TEXT     { get; set; }
    public int     DISPLAY_ORDER { get; set; }
}
