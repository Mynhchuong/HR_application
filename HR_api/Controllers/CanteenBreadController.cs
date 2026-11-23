using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Services;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class CanteenBreadController : ControllerBase
{
    private readonly OracleService _db;
    private readonly CanteenSummaryService _sum;
    private readonly CanteenDailyMailService _mail;
    public CanteenBreadController(OracleService db, CanteenSummaryService sum, CanteenDailyMailService mail)
    {
        _db = db; _sum = sum; _mail = mail;
    }

    // GET /apiHR/CanteenBread/quota-list
    [HttpGet("quota-list")]
    public async Task<IActionResult> QuotaList()
    {
        try
        {
            // usedToday: đếm giống hệt điều kiện dùng để chặn quota thật ở endpoint /status,
            // để số "còn lại" hiển thị ở đây khớp đúng với cái NV thực sự bị chặn/cho phép.
            var today = DateTime.Today.ToString("yyyyMMdd");
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT q.ID, q.DEPTCD,
                       NVL(q.DEPT_NAME, e.DEPTNM) DEPT_NAME,
                       q.MAX_BREAD, q.IS_ACTIVE,
                       q.INST_ID, q.INST_DT, q.UPDT_ID, q.UPDT_DT,
                       NVL(u.USED_CNT, 0) USED_TODAY
                FROM HRMS.HR_CANTEEN_BREAD_QUOTA q
                LEFT JOIN (SELECT DEPTCD, MAX(DEPTNM) DEPTNM FROM HRMS.EAM410 GROUP BY DEPTCD) e
                       ON e.DEPTCD = q.DEPTCD
                LEFT JOIN (
                    SELECT EC.DEPTCD, COUNT(*) USED_CNT
                    FROM HRMS.CANTEEN_ORDER CO
                    JOIN HRMS.ECM100 EC ON EC.EMPCD = CO.EMPCD AND EC.JEAJIKGB = 'Y'
                    WHERE CO.DAT = :TODAY AND CO.TYPE_MEAL = 'LUNCH' AND CO.TYPE_OF_FOOD = 'B'
                      AND CO.CHANGE_FROM = CO.EMPCD
                    GROUP BY EC.DEPTCD
                ) u ON u.DEPTCD = q.DEPTCD
                ORDER BY DEPT_NAME",
                r =>
                {
                    var maxBread   = Convert.ToInt32(r["MAX_BREAD"]);
                    var usedToday  = Convert.ToInt32(r["USED_TODAY"]);
                    return new
                    {
                        id        = Convert.ToInt64(r["ID"]),
                        deptcd    = r["DEPTCD"]?.ToString(),
                        deptName  = r["DEPT_NAME"]?.ToString(),
                        maxBread,
                        isActive  = (r["IS_ACTIVE"]?.ToString() ?? "Y") == "Y",
                        instId    = r["INST_ID"]?.ToString(),
                        instDt    = r["INST_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["INST_DT"]),
                        updtId    = r["UPDT_ID"]?.ToString(),
                        updtDt    = r["UPDT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UPDT_DT"]),
                        usedToday,
                        // -1 = không giới hạn (maxBread = 0), khớp quy ước ở endpoint /status
                        remaining = maxBread == 0 ? -1 : Math.Max(0, maxBread - usedToday),
                    };
                },
                new OracleParameter("TODAY", today));

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/quota-save-all
    // Body: List<{ deptcd, deptName, maxBread, isActive, loginUser }>
    [HttpPost("quota-save-all")]
    public async Task<IActionResult> QuotaSaveAll([FromBody] List<BreadQuotaItem> items)
    {
        try
        {
            if (items == null || items.Count == 0)
                return Ok(new { success = false, message = "Không có dữ liệu" });

            int saved = 0;
            foreach (var item in items)
            {
                string actor = string.IsNullOrEmpty(item.LoginUser) ? "HR" : item.LoginUser;
                await _db.ExecuteNonQueryAsync(@"
                    MERGE INTO HRMS.HR_CANTEEN_BREAD_QUOTA T
                    USING (SELECT :DC AS DEPTCD FROM DUAL) S
                    ON (T.DEPTCD = S.DEPTCD)
                    WHEN MATCHED THEN UPDATE SET
                        T.DEPT_NAME = :DN,
                        T.MAX_BREAD = :MB,
                        T.IS_ACTIVE = :AC,
                        T.UPDT_ID   = :ACTOR,
                        T.UPDT_DT   = SYSDATE
                    WHEN NOT MATCHED THEN INSERT
                        (DEPTCD, DEPT_NAME, MAX_BREAD, IS_ACTIVE, INST_ID, INST_DT)
                    VALUES
                        (:DC, :DN, :MB, :AC, :ACTOR, SYSDATE)",
                    new OracleParameter("DC",    item.Deptcd),
                    new OracleParameter("DN",    item.DeptName ?? ""),
                    new OracleParameter("MB",    item.MaxBread),
                    new OracleParameter("AC",    item.IsActive ? "Y" : "N"),
                    new OracleParameter("ACTOR", actor));
                saved++;
            }

            return Ok(new { success = true, message = $"Đã lưu {saved} phòng ban" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/CanteenBread/dept-list
    [HttpGet("dept-list")]
    public async Task<IActionResult> DeptList()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(
                @"SELECT DEPTCD, MAX(DEPTNM) DEPT_NAME
                  FROM HRMS.EAM410
                  WHERE DEPTCD IS NOT NULL
                  GROUP BY DEPTCD
                  ORDER BY DEPT_NAME",
                r => new
                {
                    deptcd   = r["DEPTCD"]?.ToString(),
                    deptName = r["DEPT_NAME"]?.ToString()
                });

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/CanteenBread/roster-list
    // Danh sách NV cố định (công nhân dây chuyền) được cấp Bánh cả tuần.
    [HttpGet("roster-list")]
    public async Task<IActionResult> RosterList()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT r.ID, r.EMPCD, ec.CNAME EMP_NAME, ec.DEPTCD,
                       ea.DEPTNM DEPT_NAME, r.NOTE, r.UPDT_ID, r.UPDT_DT
                FROM HRMS.HR_CANTEEN_BREAD_ROSTER r
                LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = r.EMPCD AND ec.JEAJIKGB = 'Y'
                LEFT JOIN (SELECT DEPTCD, MAX(DEPTNM) DEPTNM FROM HRMS.EAM410 GROUP BY DEPTCD) ea
                       ON ea.DEPTCD = ec.DEPTCD
                WHERE r.IS_ACTIVE = 'Y'
                ORDER BY ec.CNAME, r.EMPCD",
                r => new
                {
                    id       = Convert.ToInt64(r["ID"]),
                    empcd    = r["EMPCD"]?.ToString(),
                    empName  = r["EMP_NAME"]?.ToString(),
                    deptcd   = r["DEPTCD"]?.ToString(),
                    deptName = r["DEPT_NAME"]?.ToString(),
                    note     = r["NOTE"]?.ToString(),
                    updtId   = r["UPDT_ID"]?.ToString(),
                    updtDt   = r["UPDT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UPDT_DT"]),
                });

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/roster-add
    // Body: { empCds: string[], note, loginUser }
    [HttpPost("roster-add")]
    public async Task<IActionResult> RosterAdd([FromBody] RosterAddBody body)
    {
        try
        {
            var empCds = (body.EmpCds ?? new List<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();

            if (empCds.Count == 0)
                return Ok(new { success = false, message = "Danh sách nhân viên trống" });

            var actor = string.IsNullOrEmpty(body.LoginUser) ? "HR" : body.LoginUser;

            const string sql = @"
                MERGE INTO HRMS.HR_CANTEEN_BREAD_ROSTER T
                USING (SELECT :EMPCD AS EMPCD FROM DUAL) S
                ON (T.EMPCD = S.EMPCD)
                WHEN MATCHED THEN UPDATE SET
                    T.IS_ACTIVE = 'Y',
                    T.NOTE      = :NOTE,
                    T.UPDT_ID   = :ACTOR,
                    T.UPDT_DT   = SYSDATE
                WHEN NOT MATCHED THEN INSERT (EMPCD, NOTE, IS_ACTIVE, INST_ID, INST_DT)
                                      VALUES (:EMPCD, :NOTE, 'Y', :ACTOR, SYSDATE)";

            foreach (var empcd in empCds)
            {
                await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("EMPCD", empcd),
                    new OracleParameter("NOTE", (object?)body.Note ?? DBNull.Value),
                    new OracleParameter("ACTOR", actor));
            }

            return Ok(new { success = true, message = $"Đã thêm {empCds.Count} nhân viên vào danh sách" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/roster-remove
    // Body: { empCd, loginUser }
    [HttpPost("roster-remove")]
    public async Task<IActionResult> RosterRemove([FromBody] RosterRemoveBody body)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.EmpCd))
                return Ok(new { success = false, message = "Thiếu mã NV" });

            var actor = string.IsNullOrEmpty(body.LoginUser) ? "HR" : body.LoginUser;
            var empcd = body.EmpCd.Trim();

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_CANTEEN_BREAD_ROSTER
                SET IS_ACTIVE = 'N', UPDT_ID = :ACTOR, UPDT_DT = SYSDATE
                WHERE EMPCD = :EMPCD",
                new OracleParameter("ACTOR", actor),
                new OracleParameter("EMPCD", empcd));

            // Huỷ Bánh đã gán từ hôm nay trở đi (roster-apply gán trước cho cả tuần) —
            // giữ nguyên lịch sử các ngày đã qua, chỉ NV không còn nhận bánh kể từ lúc bị xoá.
            var today = DateTime.Today.ToString("yyyyMMdd");
            int cancelled = await _db.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.CANTEEN_ORDER
                WHERE EMPCD = :EMPCD AND TYPE_MEAL = 'LUNCH' AND TYPE_OF_FOOD = 'B'
                  AND DAT >= :TODAY",
                new OracleParameter("EMPCD", empcd),
                new OracleParameter("TODAY", today));

            var msg = cancelled > 0 ? $"Đã xoá khỏi danh sách và huỷ {cancelled} suất bánh từ hôm nay" : "Đã xoá khỏi danh sách";
            return Ok(new { success = true, message = msg });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/CanteenBread/status?empcd=&dat=YYYYMMDD
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string empcd, [FromQuery] string? dat)
    {
        try
        {
            var dateStr = dat ?? DateTime.Today.ToString("yyyyMMdd");

            // Lấy DEPTCD của NV
            var deptRows = await _db.ExecuteQueryAsync(
                @"SELECT DEPTCD FROM HRMS.ECM100
                  WHERE EMPCD = :EMPCD AND JEAJIKGB = 'Y' AND ROWNUM = 1",
                r => r["DEPTCD"]?.ToString() ?? "",
                new OracleParameter("EMPCD", empcd));
            var deptcd = deptRows.FirstOrDefault() ?? "";

            // Quota dept — phân biệt "chưa có row" (không được phát bánh) với
            // "có row, MAX_BREAD = 0" (Admin cố ý set = không giới hạn)
            bool hasQuota = false;
            int maxBread = 0;
            if (!string.IsNullOrEmpty(deptcd))
            {
                var qRows = await _db.ExecuteQueryAsync(
                    @"SELECT MAX_BREAD FROM HRMS.HR_CANTEEN_BREAD_QUOTA
                      WHERE DEPTCD = :DC AND IS_ACTIVE = 'Y'",
                    r => Convert.ToInt32(r["MAX_BREAD"]),
                    new OracleParameter("DC", deptcd));
                if (qRows.Count > 0) { hasQuota = true; maxBread = qRows[0]; }
            }
            bool unlimited = hasQuota && maxBread == 0;

            // usedDept = số B đã dùng trong dept ngày đó
            int usedDept = 0;
            if (!string.IsNullOrEmpty(deptcd))
            {
                var uRows = await _db.ExecuteQueryAsync(
                    @"SELECT COUNT(*) CNT FROM HRMS.CANTEEN_ORDER co
                      WHERE co.DAT = :DAT AND co.TYPE_MEAL = 'LUNCH' AND co.TYPE_OF_FOOD = 'B'
                        AND co.CHANGE_FROM = co.EMPCD
                        AND co.EMPCD IN (
                            SELECT EMPCD FROM HRMS.ECM100
                            WHERE DEPTCD = :DC AND JEAJIKGB = 'Y'
                        )",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("DAT", dateStr),
                    new OracleParameter("DC",  deptcd));
                usedDept = uRows.FirstOrDefault();
            }

            // weekCount = số ngày đang chọn B trong tuần của ngày target
            DateTime targetDate;
            if (!DateTime.TryParseExact(dateStr, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out targetDate))
            {
                targetDate = DateTime.Today;
            }

            // Tuần tính theo lịch (Thứ 2 → Thứ 7 chứa targetDate) — KHÔNG phụ thuộc HR_MENU_WEEK
            // đã publish hay chưa, khớp với validate thật ở CanteenOrderController.Change.
            int diffToMonday = ((int)targetDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = targetDate.Date.AddDays(-diffToMonday);
            var weekEnd   = weekStart.AddDays(5);
            var weekRow   = new { wf = weekStart.ToString("yyyyMMdd"), wt = weekEnd.ToString("yyyyMMdd") };

            int weekCount = 0;
            {
                var wRows = await _db.ExecuteQueryAsync(
                    @"SELECT COUNT(DISTINCT DAT) CNT FROM HRMS.CANTEEN_ORDER
                      WHERE EMPCD = :EMPCD AND TYPE_OF_FOOD = 'B' AND TYPE_MEAL = 'LUNCH'
                        AND DAT BETWEEN :WF AND :WT",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("EMPCD", empcd),
                    new OracleParameter("WF",   weekRow.wf),
                    new OracleParameter("WT",   weekRow.wt));
                weekCount = wRows.FirstOrDefault();
            }

            // NV thuộc danh sách cố định (roster) — miễn quota dept + cap tuần, không hiện
            // số liệu quota cho họ ở FE tránh hiểu lầm (số không áp dụng cho họ).
            var rosterRows = await _db.ExecuteQueryAsync(
                @"SELECT 1 FROM HRMS.HR_CANTEEN_BREAD_ROSTER
                  WHERE EMPCD = :EMPCD AND IS_ACTIVE = 'Y' AND ROWNUM = 1",
                r => 1,
                new OracleParameter("EMPCD", empcd));
            bool isRoster = rosterRows.Count > 0;

            return Ok(new
            {
                success    = true,
                deptcd,
                allowed    = hasQuota,
                unlimited,
                quotaDept  = maxBread,
                usedDept,
                remaining  = unlimited ? -1 : Math.Max(0, maxBread - usedDept),
                weekCount,
                weekMax    = 3,
                isRoster
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/CanteenBread/order-view?from=&to=&empcd=&deptcd=&foodType=&page=1&pageSize=50
    [HttpGet("order-view")]
    public async Task<IActionResult> OrderView(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? empcd, [FromQuery] string? deptcd,
        [FromQuery] string? linecd, [FromQuery] string? workcd,
        [FromQuery] string? foodType, [FromQuery] string? typeMeal,
        [FromQuery] string? clerkEmpcd,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
            // Clerk chỉ xem log của dept/line/work mình quản lý (HR_USERS_DEPT) — Admin/HR/
            // Canteen/CSR không truyền clerkEmpcd nên xem toàn bộ như cũ.
            OTScopeFilterHelper.FilterResult? clerkScope = null;
            if (!string.IsNullOrEmpty(clerkEmpcd))
            {
                var hasScope = await _db.ExecuteQueryAsync(
                    "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :CE AND ROWNUM = 1",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("CE", clerkEmpcd));
                if (hasScope.Count == 0 || hasScope[0] == 0)
                    return Ok(new { success = true, total = 0, page, pageSize, data = new List<object>(),
                        message = "Bạn chưa được phân bộ phận quản lý" });

                clerkScope = OTScopeFilterHelper.ForScopeByTuple(clerkEmpcd, empAlias: "ec", prefix: "CK");
            }

            var dateFrom = (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var df))
                ? df.ToString("yyyyMMdd") : DateTime.Today.ToString("yyyyMMdd");
            var dateTo = (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, out var dt))
                ? dt.ToString("yyyyMMdd") : DateTime.Today.ToString("yyyyMMdd");

            var whereParts = new List<string>
            {
                "co.DAT BETWEEN :DF AND :DT"
            };
            var oraParams = new List<OracleParameter>
            {
                new OracleParameter("DF", dateFrom),
                new OracleParameter("DT", dateTo)
            };

            if (!string.IsNullOrEmpty(empcd))
            {
                whereParts.Add("co.EMPCD = :EMPCD");
                oraParams.Add(new OracleParameter("EMPCD", empcd));
            }
            if (!string.IsNullOrEmpty(deptcd))
            {
                whereParts.Add("ec.DEPTCD = :DEPTCD");
                oraParams.Add(new OracleParameter("DEPTCD", deptcd));
            }
            if (!string.IsNullOrEmpty(linecd))
            {
                whereParts.Add("ec.LINECD = :LINECD");
                oraParams.Add(new OracleParameter("LINECD", linecd));
            }
            if (!string.IsNullOrEmpty(workcd))
            {
                whereParts.Add("ec.WORKCD = :WORKCD");
                oraParams.Add(new OracleParameter("WORKCD", workcd));
            }
            if (!string.IsNullOrEmpty(foodType))
            {
                whereParts.Add("co.TYPE_OF_FOOD = :FOOD");
                oraParams.Add(new OracleParameter("FOOD", foodType.ToUpper()));
            }
            if (!string.IsNullOrEmpty(typeMeal))
            {
                whereParts.Add("co.TYPE_MEAL = :TYPEMEAL");
                oraParams.Add(new OracleParameter("TYPEMEAL", typeMeal.ToUpper()));
            }

            var where = string.Join(" AND ", whereParts);
            if (clerkScope != null) where += " " + clerkScope.SqlClause;
            int offset = (page - 1) * pageSize;

            Func<List<OracleParameter>> buildParams = () =>
            {
                var list = new List<OracleParameter>
                {
                    new OracleParameter("DF", dateFrom),
                    new OracleParameter("DT", dateTo)
                };
                if (!string.IsNullOrEmpty(empcd))
                    list.Add(new OracleParameter("EMPCD", empcd));
                if (!string.IsNullOrEmpty(deptcd))
                    list.Add(new OracleParameter("DEPTCD", deptcd));
                if (!string.IsNullOrEmpty(linecd))
                    list.Add(new OracleParameter("LINECD", linecd));
                if (!string.IsNullOrEmpty(workcd))
                    list.Add(new OracleParameter("WORKCD", workcd));
                if (!string.IsNullOrEmpty(foodType))
                    list.Add(new OracleParameter("FOOD", foodType.ToUpper()));
                if (!string.IsNullOrEmpty(typeMeal))
                    list.Add(new OracleParameter("TYPEMEAL", typeMeal.ToUpper()));
                if (!string.IsNullOrEmpty(clerkEmpcd))
                    list.AddRange(OTScopeFilterHelper.ForScopeByTuple(clerkEmpcd, empAlias: "ec", prefix: "CK").Params);
                return list;
            };

            // Chỉ join ECM100 khi có lọc theo phòng/chuyền/công đoạn/phạm vi Clerk — đếm & phân trang
            // trên CANTEEN_ORDER trước (2,4 triệu dòng), tên NV/phòng ban chỉ join cho đúng 1 trang.
            bool needEmp = !string.IsNullOrEmpty(deptcd) || !string.IsNullOrEmpty(linecd)
                        || !string.IsNullOrEmpty(workcd) || clerkScope != null;
            string empJoin = needEmp ? "JOIN HRMS.ECM100 ec ON ec.EMPCD = co.EMPCD AND ec.JEAJIKGB = 'Y'" : "";

            var cntSql = $@"
                SELECT COUNT(*) CNT
                FROM HRMS.CANTEEN_ORDER co
                {empJoin}
                WHERE {where}";

            var totalRows = await _db.ExecuteQueryAsync(cntSql,
                r => Convert.ToInt32(r["CNT"]), buildParams().ToArray());
            int total = totalRows.FirstOrDefault();

            // Phân trang kiểu ROWNUM (Oracle 10g) để Oracle dừng sớm, rồi mới gắn tên NV/phòng ban.
            var dataSql = $@"
                SELECT p.EMPCD, p.DAT, p.TYPE_MEAL, p.TYPE_OF_FOOD, p.CHANGE_FROM, p.UPDT_DT, p.IS_MYSAMHO,
                       e2.CNAME EMP_NAME, e2.DEPTCD,
                       (SELECT MAX(a.DEPTNM) FROM HRMS.EAM410 a WHERE a.DEPTCD = e2.DEPTCD) DEPT_NAME
                FROM (
                    SELECT q.*, ROWNUM RN FROM (
                        SELECT co.EMPCD, co.DAT, co.TYPE_MEAL, co.TYPE_OF_FOOD,
                               co.CHANGE_FROM, co.UPDT_DT, co.IS_MYSAMHO
                        FROM HRMS.CANTEEN_ORDER co
                        {empJoin}
                        WHERE {where}
                        ORDER BY co.DAT DESC, co.EMPCD, co.TYPE_MEAL
                    ) q WHERE ROWNUM <= :LIMIT
                ) p
                LEFT JOIN HRMS.ECM100 e2 ON e2.EMPCD = p.EMPCD AND e2.JEAJIKGB = 'Y'
                WHERE p.RN > :OFFSET
                ORDER BY p.RN";

            var dataParams = buildParams();
            dataParams.Add(new OracleParameter("LIMIT",  offset + pageSize));
            dataParams.Add(new OracleParameter("OFFSET", offset));

            var data = await _db.ExecuteQueryAsync(dataSql,
                r => new
                {
                    empcd      = r["EMPCD"]?.ToString(),
                    dat        = r["DAT"]?.ToString(),
                    typeMeal   = r["TYPE_MEAL"]?.ToString(),
                    typeOfFood = r["TYPE_OF_FOOD"]?.ToString(),
                    changeFrom = r["CHANGE_FROM"]?.ToString(),
                    updtDt     = r["UPDT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UPDT_DT"]),
                    isMysamho  = (r["IS_MYSAMHO"]?.ToString() ?? "N") == "Y",
                    empName    = r["EMP_NAME"]?.ToString(),
                    deptcd     = r["DEPTCD"]?.ToString(),
                    deptName   = r["DEPT_NAME"]?.ToString()
                },
                dataParams.ToArray());

            return Ok(new { success = true, total, page, pageSize, data });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/CanteenBread/order-summary?from=&to=&deptcd=&linecd=&workcd=&clerkEmpcd=
    // Số suất cần nấu theo món / giờ nghỉ trưa, từng ngày — cách tính xem Services/CanteenSummaryService.cs.
    // Mỗi ngày + mỗi khung giờ có thêm "expected" = dự kiến thực ăn (đăng ký × tỉ lệ ăn thật ~2 tuần gần nhất).
    [HttpGet("order-summary")]
    public async Task<IActionResult> OrderSummary(
        [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? deptcd, [FromQuery] string? linecd, [FromQuery] string? workcd,
        [FromQuery] string? clerkEmpcd)
    {
        try
        {
            var df = (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var f)) ? f.Date : DateTime.Today;
            var dt = (!string.IsNullOrEmpty(to)   && DateTime.TryParse(to,   out var t)) ? t.Date : df;
            if (dt < df) (df, dt) = (dt, df);
            if ((dt - df).TotalDays > 30)
                return Ok(new { success = false, message = "Tổng hợp tối đa 31 ngày, vui lòng thu hẹp khoảng ngày." });

            var days = await _sum.GetAsync(df, dt, deptcd, linecd, workcd, clerkEmpcd);
            var ratio = await _sum.GetEatRatioAsync();
            var buffer = await _sum.GetBufferPctAsync();
            CanteenSummaryService.ApplyExpected(days, ratio, await _sum.GetSpecialDaysAsync(df, dt), buffer);
            return Ok(new { success = true, data = days, ratio, buffer });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ── Giờ chốt + email số suất hằng ngày cho căn tin (yêu cầu 2026-11-23) ─────────────────────────
    private static readonly System.Text.RegularExpressions.Regex EmailRx =
        new(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private const string NotReadyMsg = "Chức năng chốt số lượng chưa được bật trên hệ thống (chưa chạy script tạo bảng)";

    // GET /apiHR/CanteenBread/mail/config -> giờ chốt, bật/tắt, người nhận, ngày sẽ báo kế tiếp, vài lần gửi gần nhất
    [HttpGet("mail/config")]
    public async Task<IActionResult> MailConfig()
    {
        try
        {
            var cfg = await _sum.GetCutoffConfigAsync();
            if (cfg == null) return Ok(new { success = false, available = false, message = NotReadyMsg });
            var recipients = (await _mail.GetRecipientsAsync()).Select(x => new { id = x.Id, email = x.Email }).ToList();
            var now = DateTime.Now;
            var cutToday = now.Date.AddHours(cfg.Hour).AddMinutes(cfg.Minute);
            var target = await _mail.TargetDateAsync(now);
            var logs = await _db.ExecuteQueryAsync(@"
                SELECT * FROM (SELECT TARGET_DAT, SENT_DT, STATUS, TOTAL_REG, TOTAL_EXP, ERR, SENT_BY
                                 FROM HRMS.HR_CANTEEN_MAIL_LOG ORDER BY SENT_DT DESC, ID DESC) WHERE ROWNUM <= 7",
                r => new
                {
                    targetDat = Convert.ToDateTime(r["TARGET_DAT"]),
                    sentDt = Convert.ToDateTime(r["SENT_DT"]),
                    status = r["STATUS"]?.ToString(),
                    totalReg = r["TOTAL_REG"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["TOTAL_REG"]),
                    totalExp = r["TOTAL_EXP"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["TOTAL_EXP"]),
                    err = r["ERR"]?.ToString(),
                    sentBy = r["SENT_BY"]?.ToString()
                });
            return Ok(new
            {
                success = true, available = true,
                sendTime = cfg.Time, isActive = cfg.Active, bufferPct = cfg.BufferPct,
                recipients, max = CanteenDailyMailService.MaxRecipients,
                nextSend = now < cutToday ? cutToday : cutToday.AddDays(1),
                nextTarget = target, logs
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/mail/config { SEND_TIME:"21:00", IS_ACTIVE:true, LOGIN_USER }
    [HttpPost("mail/config")]
    public async Task<IActionResult> MailConfigSave([FromBody] CanteenMailConfigBody body)
    {
        try
        {
            if (!await _sum.TablesReadyAsync()) return Ok(new { success = false, message = NotReadyMsg });
            if (!CanteenSummaryService.TryParseTime(body?.SEND_TIME, out var h, out var m))
                return Ok(new { success = false, message = "Giờ chốt không hợp lệ (HH:mm)" });
            var buf = body!.BUFFER_PCT ?? CanteenSummaryService.DefaultBufferPct;
            if (buf < 0 || buf > 20)
                return Ok(new { success = false, message = "% dự phòng phải từ 0 đến 20" });
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_CANTEEN_MAIL_CFG SET SEND_TIME = :T, IS_ACTIVE = :A, BUFFER_PCT = :B, UPDT_ID = :U, UPDT_DT = SYSDATE WHERE ID = 1",
                new OracleParameter("T", $"{h:D2}:{m:D2}"),
                new OracleParameter("A", body.IS_ACTIVE ? "Y" : "N"),
                new OracleParameter("B", Math.Round(buf, 1)),
                new OracleParameter("U", (object?)body.LOGIN_USER ?? DBNull.Value));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/mail/add { EMAIL, LOGIN_USER }
    [HttpPost("mail/add")]
    public async Task<IActionResult> MailAdd([FromBody] CanteenMailRecipientBody req)
    {
        try
        {
            if (!await _sum.TablesReadyAsync()) return Ok(new { success = false, message = NotReadyMsg });
            var email = (req?.EMAIL ?? "").Trim().ToLowerInvariant();
            if (email.Length == 0 || email.Length > 200 || !EmailRx.IsMatch(email))
                return Ok(new { success = false, message = "Địa chỉ email không hợp lệ" });
            var all = await _mail.GetRecipientsAsync();
            if (all.Any(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
                return Ok(new { success = false, message = "Email này đã có trong danh sách" });
            if (all.Count >= CanteenDailyMailService.MaxRecipients)
                return Ok(new { success = false, message = $"Tối đa {CanteenDailyMailService.MaxRecipients} email nhận" });
            await _db.ExecuteNonQueryAsync("INSERT INTO HRMS.HR_CANTEEN_MAIL_TO (EMAIL, INST_ID, INST_DT) VALUES (:E, :U, SYSDATE)",
                new OracleParameter("E", email),
                new OracleParameter("U", (object?)req?.LOGIN_USER ?? DBNull.Value));
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/mail/remove { ID }
    [HttpPost("mail/remove")]
    public async Task<IActionResult> MailRemove([FromBody] CanteenMailRecipientBody req)
    {
        try
        {
            if (!await _sum.TablesReadyAsync()) return Ok(new { success = false, message = NotReadyMsg });
            var n = await _db.ExecuteNonQueryAsync("DELETE FROM HRMS.HR_CANTEEN_MAIL_TO WHERE ID = :I", new OracleParameter("I", req?.ID ?? 0));
            return Ok(new { success = n > 0 });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/CanteenBread/mail/test { LOGIN_USER } — gửi ngay số của ngày làm việc kế tiếp tới cả danh sách
    // (log TEST, không tính là đã gửi nên tới giờ chốt vẫn gửi bản chính thức)
    [HttpPost("mail/test")]
    public async Task<IActionResult> MailTest([FromBody] CanteenMailRecipientBody req)
    {
        try
        {
            if (!await _sum.TablesReadyAsync()) return Ok(new { success = false, message = NotReadyMsg });
            var (ok, msg) = await _mail.SendTestAsync(req?.LOGIN_USER);
            return Ok(new { success = ok, message = msg });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }
}

public class CanteenMailConfigBody
{
    public string? SEND_TIME  { get; set; }
    public bool    IS_ACTIVE  { get; set; } = true;
    public double? BUFFER_PCT { get; set; }
    public string? LOGIN_USER { get; set; }
}

public class CanteenMailRecipientBody
{
    public int     ID         { get; set; }
    public string? EMAIL      { get; set; }
    public string? LOGIN_USER { get; set; }
}

public class BreadQuotaItem
{
    public string  Deptcd    { get; set; } = "";
    public string? DeptName  { get; set; }
    public int     MaxBread  { get; set; }
    public bool    IsActive  { get; set; } = true;
    public string? LoginUser { get; set; }
}

public class RosterAddBody
{
    public List<string> EmpCds    { get; set; } = new();
    public string?        Note      { get; set; }
    public string?        LoginUser { get; set; }
}

public class RosterRemoveBody
{
    public string  EmpCd     { get; set; } = "";
    public string? LoginUser { get; set; }
}
