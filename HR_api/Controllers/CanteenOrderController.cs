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
                "B" => ("BANH", "Suất 2 Bánh"),
                _   => ("MAN",  "Mặn")
            };

            if (typeDb == "B")
            {
                DateTime targetDate;
                if (!DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out targetDate))
                    targetDate = DateTime.Today;

                int dow = (int)targetDate.DayOfWeek;
                int dayNo = dow == 0 ? 0 : dow + 1;

                var breadFoods = await _db.ExecuteQueryAsync(@"
                    SELECT F.FOOD_NAME
                    FROM HRMS.HR_MENU_WEEK W
                    JOIN HRMS.HR_MENU_DETAIL D ON D.WEEK_ID = W.ID
                    JOIN HRMS.HR_MENU_FOOD F ON F.ID = D.FOOD_ID
                    WHERE TRUNC(:TGT) BETWEEN TRUNC(W.FROM_DATE) AND TRUNC(W.TO_DATE)
                      AND W.STATUS = 'PUBLISHED'
                      AND D.DAY_NO = :DAY_NO
                      AND D.MEAL_TYPE = 'BANH'
                    ORDER BY D.DISPLAY_ORDER",
                    r => r["FOOD_NAME"]?.ToString() ?? "",
                    new OracleParameter("TGT", targetDate.Date),
                    new OracleParameter("DAY_NO", dayNo));

                if (breadFoods.Any())
                {
                    nameApp = "Suất 2 Bánh: " + string.Join(" + ", breadFoods);
                }
            }

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
                "BANH"                       => "B",
                _                            => "M"
            };

            var from = DateTime.Parse(req.FromDate);
            var to   = DateTime.Parse(req.ToDate);

            var mealCat = string.IsNullOrEmpty(req.TypeMeal) ? "LUNCH" : req.TypeMeal.ToUpper();

            if (typeDb == "B" && mealCat == "OT")
            {
                return Ok(new { success = false, message = "Phiếu bánh chỉ áp dụng cho bữa ăn giữa ca, không áp dụng cho tăng ca." });
            }

            // NV thuộc danh sách cố định (roster) chỉ được ăn Bánh — không cho tự đổi sang
            // Mặn/Nhẹ/Chay (kể cả đăng ký dài hạn) cho bữa giữa ca, tránh vừa được admin gán
            // Bánh cả tuần vừa tự đổi món khác (sài 2 lần).
            if (mealCat == "LUNCH" && typeDb != "B")
            {
                var rosterRows = await _db.ExecuteQueryAsync(
                    @"SELECT 1 FROM HRMS.HR_CANTEEN_BREAD_ROSTER
                      WHERE EMPCD = :EMPCD AND IS_ACTIVE = 'Y' AND ROWNUM = 1",
                    r => 1,
                    new OracleParameter("EMPCD", req.EmpCd));

                if (rosterRows.Count > 0)
                    return Ok(new { success = false, code = "BREAD_ROSTER_LOCKED",
                        message = "Bạn thuộc danh sách nhận Bánh cố định, không thể tự đổi sang món khác. Liên hệ HR nếu cần thay đổi." });
            }

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
                @"SELECT LOCK_DATE, LOCK_LUNCH, LOCK_OT, LOCK_MAN, LOCK_NHE, LOCK_CHAY, LOCK_BANH, CUTOFF_DT
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
                    lockBanh = (r["LOCK_BANH"]?.ToString()  ?? "N") == "Y",
                    cutoff   = r["CUTOFF_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CUTOFF_DT"])
                },
                new OracleParameter("DF", from.Date),
                new OracleParameter("DT", to.Date));

            // -- VALIDATE BÁNH (chỉ LUNCH, không long-term) --
            if (typeDb == "B" && mealCat == "LUNCH" && !isLongTerm)
            {
                // 0. NV thuộc danh sách cố định (roster) — miễn hoàn toàn quota dept + cap
                //    tuần kể cả khi tự chọn Bánh qua app (không chỉ khi admin gán hộ), khớp
                //    với badge "không giới hạn" hiện cho họ ở FE.
                var rosterRows = await _db.ExecuteQueryAsync(
                    @"SELECT 1 FROM HRMS.HR_CANTEEN_BREAD_ROSTER
                      WHERE EMPCD = :EMPCD AND IS_ACTIVE = 'Y' AND ROWNUM = 1",
                    r => 1,
                    new OracleParameter("EMPCD", req.EmpCd));
                bool isRosterMember = rosterRows.Count > 0;

                if (!isRosterMember)
                {
                // Khoá theo dept trong 1 transaction thật để tránh race: 2 request cùng dept
                // đọc quota gần như đồng thời, cùng pass check rồi cùng ghi, vượt quota thật.
                // SELECT ... FOR UPDATE khoá row quota của dept đó, các request cùng dept phải
                // xếp hàng qua nhau; validate + ghi luôn chạy chung transaction này rồi mới
                // commit, nên không có khoảng hở giữa lúc check và lúc ghi.
                await using var scope = await _db.BeginTransactionAsync();

                // 1. Lấy DEPTCD của NV từ ECM100
                var deptRows = await _db.ExecuteQueryAsync(scope,
                    @"SELECT DEPTCD FROM HRMS.ECM100
                      WHERE EMPCD = :EMPCD AND JEAJIKGB = 'Y' AND ROWNUM = 1",
                    r => r["DEPTCD"]?.ToString() ?? "",
                    new OracleParameter("EMPCD", req.EmpCd));
                var deptcd = deptRows.FirstOrDefault() ?? "";

                // 2. Quota dept — phải có row active thì mới được phát bánh.
                //    Không có row (dept chưa cấu hình) KHÁC với có row MAX_BREAD=0 (không giới hạn).
                bool hasQuota = false;
                int maxBread = 0;
                if (!string.IsNullOrEmpty(deptcd))
                {
                    var quotaRows = await _db.ExecuteQueryAsync(scope,
                        @"SELECT MAX_BREAD FROM HRMS.HR_CANTEEN_BREAD_QUOTA
                          WHERE DEPTCD = :DC AND IS_ACTIVE = 'Y'
                          FOR UPDATE WAIT 10",
                        r => Convert.ToInt32(r["MAX_BREAD"]),
                        new OracleParameter("DC", deptcd));
                    if (quotaRows.Count > 0) { hasQuota = true; maxBread = quotaRows[0]; }
                }

                if (!hasQuota)
                    return Ok(new { success = false, code = "BREAD_NOT_ALLOWED",
                        message = "Phòng ban của bạn chưa được cấp phiếu bánh." });

                // 3. Validate TỪNG NGÀY trong [from, to] — request có thể phủ nhiều ngày,
                //    nếu chỉ check ngày `from` thì các ngày sau trong cùng request có thể
                //    vượt quota tuần / quota dept mà không bị chặn. Cộng dồn qua dictionary
                //    để 1 request chọn Bánh nhiều ngày liên tiếp cũng bị tính đúng.
                var weekCountCache = new Dictionary<string, int>();
                var deptUsedCache  = new Dictionary<string, int>();
                var validDays      = new List<DateTime>();
                var banhBlockedDays = new List<string>();

                for (var vday = from; vday <= to; vday = vday.AddDays(1))
                {
                    if (vday.DayOfWeek == DayOfWeek.Sunday) continue;
                    // Ngày bị khoá thì không ghi, nên cũng không tính vào quota tuần/dept —
                    // nếu không, 1 ngày khoá giữa range có thể "ăn" mất 1 slot quota, khiến
                    // ngày hợp lệ sau đó bị từ chối oan.
                    if (IsBlocked(vday, mealCat, "B")) { banhBlockedDays.Add(vday.ToString("dd/MM")); continue; }
                    var vdatStr = vday.ToString("yyyyMMdd");

                    // Tuần tính theo lịch (Thứ 2 → Thứ 7 chứa vday) — KHÔNG phụ thuộc HR_MENU_WEEK
                    // đã publish hay chưa. Trước đây fallback về "NOWEEK-{ngày}" khi chưa có tuần
                    // thực đơn PUBLISHED nào phủ ngày đó → mỗi ngày thành 1 "tuần" riêng, cap 3
                    // ngày/tuần bị vô hiệu hoàn toàn cho các ngày chưa publish thực đơn.
                    int diffToMonday = ((int)vday.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                    var weekStart = vday.Date.AddDays(-diffToMonday);
                    var weekEnd   = weekStart.AddDays(5);
                    var weekRow   = new { wf = weekStart.ToString("yyyyMMdd"), wt = weekEnd.ToString("yyyyMMdd") };

                    var weekKey = $"{weekRow.wf}|{weekRow.wt}";
                    if (!weekCountCache.TryGetValue(weekKey, out int weekCount))
                    {
                        var cntRows = await _db.ExecuteQueryAsync(scope,
                            @"SELECT COUNT(DISTINCT DAT) CNT FROM HRMS.CANTEEN_ORDER
                              WHERE EMPCD = :EMPCD AND TYPE_OF_FOOD = 'B' AND TYPE_MEAL = 'LUNCH'
                                AND DAT BETWEEN :WF AND :WT",
                            r => Convert.ToInt32(r["CNT"]),
                            new OracleParameter("EMPCD", req.EmpCd),
                            new OracleParameter("WF",   weekRow.wf),
                            new OracleParameter("WT",   weekRow.wt));
                        weekCount = cntRows.FirstOrDefault();
                        weekCountCache[weekKey] = weekCount;
                    }

                    // Kiểm tra NV đã có B ngày này chưa
                    var alreadyBRows = await _db.ExecuteQueryAsync(scope,
                        @"SELECT TYPE_OF_FOOD FROM HRMS.CANTEEN_ORDER
                          WHERE EMPCD = :EMPCD AND DAT = :DAT AND TYPE_MEAL = 'LUNCH'",
                        r => r["TYPE_OF_FOOD"]?.ToString() ?? "",
                        new OracleParameter("EMPCD", req.EmpCd),
                        new OracleParameter("DAT", vdatStr));
                    bool isAlreadyB = (alreadyBRows.FirstOrDefault() == "B");

                    // Số phiếu B đã dùng trong dept ngày này
                    if (!deptUsedCache.TryGetValue(vdatStr, out int usedDept))
                    {
                        usedDept = 0;
                        if (!string.IsNullOrEmpty(deptcd))
                        {
                            var usedRows = await _db.ExecuteQueryAsync(scope,
                                @"SELECT COUNT(*) CNT FROM HRMS.CANTEEN_ORDER co
                                  WHERE co.DAT = :DAT AND co.TYPE_MEAL = 'LUNCH' AND co.TYPE_OF_FOOD = 'B'
                                    AND co.CHANGE_FROM = co.EMPCD
                                    AND co.EMPCD IN (
                                        SELECT EMPCD FROM HRMS.ECM100
                                        WHERE DEPTCD = :DC AND JEAJIKGB = 'Y'
                                    )",
                                r => Convert.ToInt32(r["CNT"]),
                                new OracleParameter("DAT", vdatStr),
                                new OracleParameter("DC",  deptcd));
                            usedDept = usedRows.FirstOrDefault();
                        }
                        deptUsedCache[vdatStr] = usedDept;
                    }

                    // Validate: nếu đã là B ngày này thì ko cộng dồn
                    int nextWeekCount = isAlreadyB ? weekCount : (weekCount + 1);
                    int nextUsedDept  = isAlreadyB ? usedDept  : (usedDept + 1);

                    if (nextWeekCount > 3)
                        return Ok(new { success = false, code = "BREAD_WEEK_LIMIT",
                            message = $"Bạn đã chọn Bánh 3 ngày tuần này, không thể thêm ngày {vday:dd/MM}." });

                    if (maxBread > 0 && nextUsedDept > maxBread)
                        return Ok(new { success = false, code = "BREAD_QUOTA_FULL",
                            message = $"Phòng ban đã hết phiếu bánh ngày {vday:dd/MM}." });

                    // Cộng dồn để các ngày tiếp theo trong cùng request tính đúng
                    if (!isAlreadyB)
                    {
                        weekCountCache[weekKey] = nextWeekCount;
                        deptUsedCache[vdatStr]  = nextUsedDept;
                    }
                    validDays.Add(vday);
                }

                if (validDays.Count == 0)
                    return Ok(new {
                        success = false,
                        code    = "LOCKED",
                        message = $"Các ngày sau đã bị khoá đổi món: {string.Join(", ", banhBlockedDays)}"
                    });

                // 4. Qua hết validate mới ghi — vẫn trong transaction đang giữ khoá quota,
                //    nên không request nào khác có thể chen vào giữa lúc này.
                foreach (var vday in validDays)
                {
                    await _db.ExecuteNonQueryAsync(scope, sql,
                        new OracleParameter("EMPCD",   req.EmpCd),
                        new OracleParameter("DAT",     vday.ToString("yyyyMMdd")),
                        new OracleParameter("MEALCAT", mealCat),
                        new OracleParameter("TYPE",    typeDb),
                        new OracleParameter("CHANGER", changer),
                        new OracleParameter("MYSAMHO", isMysamho));
                }
                await scope.CommitAsync();

                var banhMsg = $"Đã đổi sang Bánh ({validDays.Count} ngày)";
                if (banhBlockedDays.Count > 0)
                    banhMsg += $". Riêng các ngày {string.Join(", ", banhBlockedDays)} đã bị khoá nên bỏ qua.";
                return Ok(new { success = true, message = banhMsg });
                }
            }

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
                    bool typeRestricted = r.lockMan || r.lockNhe || r.lockChay || r.lockBanh;
                    if (!typeRestricted) return true;

                    // Có restrict → khoá nếu targetFood khớp
                    if (targetFood == "M" && r.lockMan)  return true;
                    if (targetFood == "N" && r.lockNhe)  return true;
                    if (targetFood == "C" && r.lockChay) return true;
                    if (targetFood == "B" && r.lockBanh) return true;
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
                "BANH"        => "Bánh",
                _             => "Mặn"
            };
            var baseMsg = $"Đã đổi sang {label} ({total} ngày)";
            if (blockedDays.Count > 0)
                baseMsg += $". Riêng các ngày {string.Join(", ", blockedDays)} đã bị khoá nên bỏ qua.";
            return Ok(new { success = true, message = baseMsg });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenOrder/bulk-bread
    // Admin/HR/Canteen gán Bánh hàng loạt cho 1 danh sách NV (thường là công nhân dây chuyền
    // được cấp phiếu bánh cố định cả tuần) trong 1 khoảng ngày. KHÔNG chạy qua validate quota
    // dept / cap 3 ngày-tuần / khoá đổi món — đây là override có chủ đích của admin.
    // Các dòng gán qua đây có CHANGE_FROM (người gán) khác EMPCD (người ăn) nên tự động bị
    // loại khỏi số đếm usedDept ở Change/Status (chỉ đếm CHANGE_FROM = EMPCD), tức không
    // ảnh hưởng đến quota phiếu bánh chung của phòng ban cho công nhân thường.
    [HttpPost("bulk-bread")]
    public async Task<IActionResult> BulkBread([FromBody] BulkBreadBody req)
    {
        try
        {
            var empCds = (req.EmpCds ?? new List<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();

            if (empCds.Count == 0)
                return Ok(new { success = false, message = "Danh sách nhân viên trống" });

            if (!DateTime.TryParse(req.FromDate, out var from) || !DateTime.TryParse(req.ToDate, out var to))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            if (from > to)
                return Ok(new { success = false, message = "Ngày bắt đầu không được sau ngày kết thúc" });

            var actor = string.IsNullOrEmpty(req.LoginUser) ? "HR" : req.LoginUser;

            const string sql = @"
                MERGE INTO HRMS.CANTEEN_ORDER T
                USING (SELECT :EMPCD AS EMPCD, :DAT AS DAT, 'LUNCH' AS TYPE_MEAL, 'B' AS TYPE_OF_FOOD, :CHANGER AS CHANGER FROM DUAL) S
                ON (T.EMPCD = S.EMPCD AND T.DAT = S.DAT AND T.TYPE_MEAL = S.TYPE_MEAL)
                WHEN MATCHED THEN UPDATE SET
                    T.TYPE_OF_FOOD = S.TYPE_OF_FOOD,
                    T.CHANGE_FROM  = S.CHANGER,
                    T.IS_MYSAMHO   = 'Y',
                    T.UPDT_ID      = S.CHANGER,
                    T.UPDT_DT      = SYSDATE
                WHEN NOT MATCHED THEN INSERT (EMPCD, DAT, TYPE_MEAL, TYPE_OF_FOOD, CHANGE_FROM, IS_MYSAMHO, INST_ID, INST_DT, UPDT_ID, UPDT_DT)
                                      VALUES (S.EMPCD, S.DAT, S.TYPE_MEAL, S.TYPE_OF_FOOD, S.CHANGER, 'Y', S.CHANGER, SYSDATE, S.CHANGER, SYSDATE)";

            int days = 0;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek == DayOfWeek.Sunday) continue;
                days++;
                foreach (var empcd in empCds)
                {
                    await _db.ExecuteNonQueryAsync(sql,
                        new OracleParameter("EMPCD",   empcd),
                        new OracleParameter("DAT",     day.ToString("yyyyMMdd")),
                        new OracleParameter("CHANGER", actor));
                }
            }

            return Ok(new { success = true, message = $"Đã gán Bánh cho {empCds.Count} nhân viên × {days} ngày" });
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

public class BulkBreadBody
{
    public List<string> EmpCds    { get; set; } = new();
    public string        FromDate { get; set; } = string.Empty;
    public string        ToDate   { get; set; } = string.Empty;
    public string?        LoginUser { get; set; }
}
