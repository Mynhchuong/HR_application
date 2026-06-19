using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class CanteenOrderController : ControllerBase
{
    private readonly OracleService _db;

    public CanteenOrderController(OracleService db) { _db = db; }

    // GET /apiHR/CanteenOrder/today?empcd=xxx&date=20260618&typeMeal=LUNCH
    [HttpGet("today")]
    public async Task<IActionResult> GetToday([FromQuery] string empcd, [FromQuery] string? date, [FromQuery] string? typeMeal)
    {
        try
        {
            var dateStr  = date ?? DateTime.Today.ToString("yyyyMMdd");
            var mealCat  = string.IsNullOrEmpty(typeMeal) ? "LUNCH" : typeMeal.ToUpper();

            var sql = $@"
                SELECT NVL(b.type_of_food, 'M') type_of_food
                FROM ecm100 a, HRMS.CANTEEN_ORDER b
                WHERE a.empcd        = b.empcd(+)
                AND   a.empcd        = :EMPCD
                AND   b.dat(+)       = :DAT
                AND   b.type_meal(+) = '{mealCat}'
                AND   a.jeajikgb     = 'Y'";

            var rows = await _db.ExecuteQueryAsync(sql,
                r => r["type_of_food"]?.ToString() ?? "M",
                new OracleParameter("EMPCD", empcd),
                new OracleParameter("DAT",   dateStr));

            var typeDb = rows.FirstOrDefault() ?? "M";
            var (typeApp, nameApp) = typeDb switch
            {
                "C" => ("CHAY", "Chay"),
                "N" => ("NHE",  "Nhẹ"),
                _   => ("MAN",  "Mặn")
            };

            return Ok(new { success = true, data = new { FOOD_TYPE = typeApp, FOOD_NAME = nameApp } });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/CanteenOrder/week?empcd=xxx
    [HttpGet("week")]
    public async Task<IActionResult> GetWeek([FromQuery] string empcd)
    {
        try
        {
            var weekRow = (await _db.ExecuteQueryAsync(
                @"SELECT TO_CHAR(FROM_DATE,'YYYYMMDD') F, TO_CHAR(TO_DATE,'YYYYMMDD') T
                  FROM HRMS.HR_MENU_WEEK
                  WHERE TRUNC(SYSDATE) BETWEEN TRUNC(FROM_DATE) AND TRUNC(TO_DATE)
                    AND STATUS = 'PUBLISHED'",
                r => new { f = r["F"]?.ToString(), t = r["T"]?.ToString() }))
                .FirstOrDefault();

            if (weekRow == null)
                return Ok(new { success = true, data = new Dictionary<string, string>() });

            var rows = await _db.ExecuteQueryAsync(
                @"SELECT DAT, TYPE_OF_FOOD FROM HRMS.CANTEEN_ORDER
                  WHERE EMPCD = :EMPCD AND TYPE_MEAL = 'LUNCH'
                    AND DAT BETWEEN :F AND :T",
                r => new { dat = r["DAT"]?.ToString()!, type = r["TYPE_OF_FOOD"]?.ToString() ?? "M" },
                new OracleParameter("EMPCD", empcd),
                new OracleParameter("F", weekRow.f),
                new OracleParameter("T", weekRow.t));

            var result = rows.ToDictionary(r => r.dat, r => r.type);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenOrder/change
    // Body: { empCd, mealType, fromDate, toDate }
    [HttpPost("change")]
    public async Task<IActionResult> Change([FromBody] CanteenChangeBody req)
    {
        try
        {
            var typeDb = req.MealType switch
            {
                "NHE"       or "NHE_TRUONG"  => "N",
                "CHAY"      or "CHAY_TRUONG" => "C",
                _                            => "M"
            };

            var from = DateTime.Parse(req.FromDate);
            var to   = DateTime.Parse(req.ToDate);

            var mealCat = string.IsNullOrEmpty(req.TypeMeal) ? "LUNCH" : req.TypeMeal.ToUpper();

            var changer    = string.IsNullOrEmpty(req.LoginUser) ? "HR" : req.LoginUser;
            var isMysamho  = req.IsMysamho ? "Y" : "N";

            var sql = $@"
                MERGE INTO HRMS.CANTEEN_ORDER T
                USING (SELECT :EMPCD AS EMPCD, :DAT AS DAT, '{mealCat}' AS TYPE_MEAL, :TYPE AS TYPE_OF_FOOD, :CHANGER AS CHANGER, :MYSAMHO AS MYSAMHO FROM DUAL) S
                ON (T.EMPCD = S.EMPCD AND T.DAT = S.DAT AND T.TYPE_MEAL = S.TYPE_MEAL)
                WHEN MATCHED THEN UPDATE SET
                    T.TYPE_OF_FOOD = S.TYPE_OF_FOOD,
                    T.CHANGE_FROM  = S.CHANGER,
                    T.IS_MYSAMHO   = S.MYSAMHO,
                    T.UPDT_ID      = S.CHANGER,
                    T.UPDT_DT      = SYSDATE
                WHEN NOT MATCHED THEN INSERT (EMPCD, DAT, TYPE_MEAL, TYPE_OF_FOOD, CHANGE_FROM, IS_MYSAMHO, INST_ID, INST_DT, UPDT_ID, UPDT_DT)
                                      VALUES (S.EMPCD, S.DAT, S.TYPE_MEAL, S.TYPE_OF_FOOD, S.CHANGER, S.MYSAMHO, S.CHANGER, SYSDATE, S.CHANGER, SYSDATE)";

            int total = 0;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Sunday) continue;
                var datStr = day.ToString("yyyyMMdd");
                await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("EMPCD",   req.EmpCd),
                    new OracleParameter("DAT",     datStr),
                    new OracleParameter("TYPE",    typeDb),
                    new OracleParameter("CHANGER", changer),
                    new OracleParameter("MYSAMHO", isMysamho));
                total++;
            }

            var label = req.MealType switch
            {
                "NHE_TRUONG"  => "Món nhẹ",
                "CHAY_TRUONG" => "Chay trường",
                "NHE"         => "Nhẹ",
                "CHAY"        => "Chay",
                _             => "Mặn"
            };
            return Ok(new { success = true, message = $"Đã đổi sang {label} ({total} ngày)" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }
}

public class CanteenChangeBody
{
    public string  EmpCd      { get; set; } = string.Empty;
    public string  MealType   { get; set; } = string.Empty;
    public string? TypeMeal   { get; set; } = "LUNCH"; // LUNCH or OT
    public string  FromDate   { get; set; } = string.Empty;
    public string  ToDate     { get; set; } = string.Empty;
    public string? LoginUser  { get; set; }
    public bool    IsMysamho  { get; set; } = false;
}
