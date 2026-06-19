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

            var changer = string.IsNullOrEmpty(req.LoginUser) ? "HR" : req.LoginUser;

            var sql = $@"
                MERGE INTO HRMS.CANTEEN_ORDER T
                USING (SELECT :EMPCD AS EMPCD, :DAT AS DAT, '{mealCat}' AS TYPE_MEAL, :TYPE AS TYPE_OF_FOOD, :CHANGER AS CHANGER FROM DUAL) S
                ON (T.EMPCD = S.EMPCD AND T.DAT = S.DAT AND T.TYPE_MEAL = S.TYPE_MEAL)
                WHEN MATCHED THEN UPDATE SET
                    T.TYPE_OF_FOOD = S.TYPE_OF_FOOD,
                    T.CHANGE_FROM  = S.CHANGER,
                    T.UPDT_ID      = S.CHANGER,
                    T.UPDT_DT      = SYSDATE
                WHEN NOT MATCHED THEN INSERT (EMPCD, DAT, TYPE_MEAL, TYPE_OF_FOOD, CHANGE_FROM, INST_ID, INST_DT, UPDT_ID, UPDT_DT)
                                      VALUES (S.EMPCD, S.DAT, S.TYPE_MEAL, S.TYPE_OF_FOOD, S.CHANGER, S.CHANGER, SYSDATE, S.CHANGER, SYSDATE)";

            int total = 0;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Sunday) continue;
                var datStr = day.ToString("yyyyMMdd");
                await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("EMPCD",   req.EmpCd),
                    new OracleParameter("DAT",     datStr),
                    new OracleParameter("TYPE",    typeDb),
                    new OracleParameter("CHANGER", changer));
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
    public string  EmpCd    { get; set; } = string.Empty;
    public string  MealType { get; set; } = string.Empty;
    public string? TypeMeal { get; set; } = "LUNCH"; // LUNCH or OT
    public string  FromDate { get; set; } = string.Empty;
    public string  ToDate   { get; set; } = string.Empty;
}
