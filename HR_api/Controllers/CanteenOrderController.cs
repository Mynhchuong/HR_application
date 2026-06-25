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

            // Đăng ký dài hạn (NHE_TRUONG / CHAY_TRUONG): set luôn cả LUNCH + OT
            // để NV có OT khỏi vào đổi tay món tăng ca
            var isLongTerm = req.MealType == "NHE_TRUONG" || req.MealType == "CHAY_TRUONG";
            var mealCats   = isLongTerm ? new[] { "LUNCH", "OT" } : new[] { mealCat };

            var changer    = string.IsNullOrEmpty(req.LoginUser) ? "HR" : req.LoginUser;
            var isMysamho  = changer == "HR" ? "N" : "Y";

            const string sql = @"
                MERGE INTO HRMS.CANTEEN_ORDER T
                USING (SELECT :EMPCD AS EMPCD, :DAT AS DAT, :MEALCAT AS TYPE_MEAL, :TYPE AS TYPE_OF_FOOD, :CHANGER AS CHANGER, :MYSAMHO AS MYSAMHO FROM DUAL) S
                ON (T.EMPCD = S.EMPCD AND T.DAT = S.DAT AND T.TYPE_MEAL = S.TYPE_MEAL)
                WHEN MATCHED THEN UPDATE SET
                    T.TYPE_OF_FOOD = S.TYPE_OF_FOOD,
                    T.CHANGE_FROM  = S.CHANGER,
                    T.IS_MYSAMHO   = S.MYSAMHO,
                    T.UPDT_ID      = S.CHANGER,
                    T.UPDT_DT      = SYSDATE
                WHEN NOT MATCHED THEN INSERT (EMPCD, DAT, TYPE_MEAL, TYPE_OF_FOOD, CHANGE_FROM, IS_MYSAMHO, INST_ID, INST_DT, UPDT_ID, UPDT_DT)
                                      VALUES (S.EMPCD, S.DAT, S.TYPE_MEAL, S.TYPE_OF_FOOD, S.CHANGER, S.MYSAMHO, S.CHANGER, SYSDATE, S.CHANGER, SYSDATE)";

            // Pre-load các rule khoá trong khoảng [from, to]
            var locks = await _db.ExecuteQueryAsync(
                @"SELECT LOCK_DATE, LOCK_LUNCH, LOCK_OT, LOCK_MAN, LOCK_NHE, LOCK_CHAY, CUTOFF_DT
                  FROM HRMS.HR_MEAL_LOCK
                  WHERE IS_ACTIVE = 'Y'
                    AND LOCK_DATE BETWEEN TRUNC(:DF) AND TRUNC(:DT)",
                r => new {
                    date     = Convert.ToDateTime(r["LOCK_DATE"]).Date,
                    lunch    = (r["LOCK_LUNCH"]?.ToString() ?? "N") == "Y",
                    ot       = (r["LOCK_OT"]?.ToString()    ?? "N") == "Y",
                    lockMan  = (r["LOCK_MAN"]?.ToString()   ?? "N") == "Y",
                    lockNhe  = (r["LOCK_NHE"]?.ToString()   ?? "N") == "Y",
                    lockChay = (r["LOCK_CHAY"]?.ToString()  ?? "N") == "Y",
                    cutoff   = r["CUTOFF_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CUTOFF_DT"])
                },
                new OracleParameter("DF", from.Date),
                new OracleParameter("DT", to.Date));

            bool IsBlocked(DateTime day, string mealCat, string targetFood)
            {
                // Lọc rule áp dụng cho (day + mealCat)
                var applicable = locks.Where(l => l.date == day.Date
                    && ((mealCat == "OT" && l.ot) || (mealCat != "OT" && l.lunch)));

                foreach (var r in applicable)
                {
                    bool inEffect = r.cutoff == null || DateTime.Now >= r.cutoff.Value;
                    if (!inEffect) continue;

                    // Nếu rule không restrict type → khoá mọi loại
                    bool typeRestricted = r.lockMan || r.lockNhe || r.lockChay;
                    if (!typeRestricted) return true;

                    // Có restrict → khoá nếu targetFood khớp
                    if (targetFood == "M" && r.lockMan)  return true;
                    if (targetFood == "N" && r.lockNhe)  return true;
                    if (targetFood == "C" && r.lockChay) return true;
                }
                return false;
            }

            int total = 0;
            var blockedDays = new List<string>();
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Sunday) continue;
                var datStr = day.ToString("yyyyMMdd");

                // Nếu mọi mealCat đều bị khoá cho ngày này → bỏ qua ngày
                bool anyApplied = false;
                foreach (var cat in mealCats)
                {
                    if (IsBlocked(day, cat, typeDb)) continue;
                    anyApplied = true;
                    await _db.ExecuteNonQueryAsync(sql,
                        new OracleParameter("EMPCD",   req.EmpCd),
                        new OracleParameter("DAT",     datStr),
                        new OracleParameter("MEALCAT", cat),
                        new OracleParameter("TYPE",    typeDb),
                        new OracleParameter("CHANGER", changer),
                        new OracleParameter("MYSAMHO", isMysamho));
                }
                if (anyApplied) total++;
                else            blockedDays.Add(day.ToString("dd/MM"));
            }

            if (total == 0 && blockedDays.Count > 0)
                return Ok(new {
                    success = false,
                    code    = "LOCKED",
                    message = $"Các ngày sau đã bị khoá đổi món: {string.Join(", ", blockedDays)}"
                });

            var label = req.MealType switch
            {
                "NHE_TRUONG"  => "Món nhẹ",
                "CHAY_TRUONG" => "Chay trường",
                "NHE"         => "Nhẹ",
                "CHAY"        => "Chay",
                _             => "Mặn"
            };
            var baseMsg = $"Đã đổi sang {label} ({total} ngày)";
            if (blockedDays.Count > 0)
                baseMsg += $". Riêng các ngày {string.Join(", ", blockedDays)} đã bị khoá nên bỏ qua.";
            return Ok(new { success = true, message = baseMsg });
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
