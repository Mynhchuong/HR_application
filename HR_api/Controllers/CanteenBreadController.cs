using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class CanteenBreadController : ControllerBase
{
    private readonly OracleService _db;
    public CanteenBreadController(OracleService db) { _db = db; }

    // GET /apiHR/CanteenBread/quota-list
    [HttpGet("quota-list")]
    public async Task<IActionResult> QuotaList()
    {
        try
        {
            var rows = await _db.ExecuteQueryAsync(@"
                SELECT q.ID, q.DEPTCD,
                       NVL(q.DEPT_NAME, e.DEPTNM) DEPT_NAME,
                       q.MAX_BREAD, q.IS_ACTIVE,
                       q.INST_ID, q.INST_DT, q.UPDT_ID, q.UPDT_DT
                FROM HRMS.HR_CANTEEN_BREAD_QUOTA q
                LEFT JOIN (SELECT DEPTCD, MAX(DEPTNM) DEPTNM FROM HRMS.EAM410 GROUP BY DEPTCD) e
                       ON e.DEPTCD = q.DEPTCD
                ORDER BY DEPT_NAME",
                r => new
                {
                    id        = Convert.ToInt64(r["ID"]),
                    deptcd    = r["DEPTCD"]?.ToString(),
                    deptName  = r["DEPT_NAME"]?.ToString(),
                    maxBread  = Convert.ToInt32(r["MAX_BREAD"]),
                    isActive  = (r["IS_ACTIVE"]?.ToString() ?? "Y") == "Y",
                    instId    = r["INST_ID"]?.ToString(),
                    instDt    = r["INST_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["INST_DT"]),
                    updtId    = r["UPDT_ID"]?.ToString(),
                    updtDt    = r["UPDT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UPDT_DT"]),
                });

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

            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_CANTEEN_BREAD_ROSTER
                SET IS_ACTIVE = 'N', UPDT_ID = :ACTOR, UPDT_DT = SYSDATE
                WHERE EMPCD = :EMPCD",
                new OracleParameter("ACTOR", actor),
                new OracleParameter("EMPCD", body.EmpCd.Trim()));

            return Ok(new { success = true, message = "Đã xoá khỏi danh sách" });
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
        [FromQuery] string? foodType,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        try
        {
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
            if (!string.IsNullOrEmpty(foodType))
            {
                whereParts.Add("co.TYPE_OF_FOOD = :FOOD");
                oraParams.Add(new OracleParameter("FOOD", foodType.ToUpper()));
            }

            var where = string.Join(" AND ", whereParts);
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
                if (!string.IsNullOrEmpty(foodType))
                    list.Add(new OracleParameter("FOOD", foodType.ToUpper()));
                return list;
            };

            // Count
            var cntSql = $@"
                SELECT COUNT(*) CNT
                FROM HRMS.CANTEEN_ORDER co
                LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = co.EMPCD AND ec.JEAJIKGB = 'Y'
                LEFT JOIN (SELECT DEPTCD, MAX(DEPTNM) DEPTNM FROM HRMS.EAM410 GROUP BY DEPTCD) ea
                       ON ea.DEPTCD = ec.DEPTCD
                WHERE {where}";

            var totalRows = await _db.ExecuteQueryAsync(cntSql,
                r => Convert.ToInt32(r["CNT"]), buildParams().ToArray());
            int total = totalRows.FirstOrDefault();

            // Data with pagination
            var dataSql = $@"
                SELECT * FROM (
                    SELECT co.EMPCD, co.DAT, co.TYPE_MEAL, co.TYPE_OF_FOOD,
                           co.CHANGE_FROM, co.UPDT_DT, co.IS_MYSAMHO,
                           ec.CNAME EMP_NAME, ec.DEPTCD,
                           ea.DEPTNM DEPT_NAME,
                           ROW_NUMBER() OVER (ORDER BY co.DAT DESC, co.EMPCD) RN
                    FROM HRMS.CANTEEN_ORDER co
                    LEFT JOIN HRMS.ECM100 ec ON ec.EMPCD = co.EMPCD AND ec.JEAJIKGB = 'Y'
                    LEFT JOIN (SELECT DEPTCD, MAX(DEPTNM) DEPTNM FROM HRMS.EAM410 GROUP BY DEPTCD) ea
                           ON ea.DEPTCD = ec.DEPTCD
                    WHERE {where}
                ) WHERE RN > :OFFSET AND RN <= :LIMIT";

            var dataParams = buildParams();
            dataParams.Add(new OracleParameter("OFFSET", offset));
            dataParams.Add(new OracleParameter("LIMIT",  offset + pageSize));

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
