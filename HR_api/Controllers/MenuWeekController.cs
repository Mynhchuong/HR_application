using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Menu;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class MenuWeekController : ControllerBase
{
    private readonly OracleService _db;

    public MenuWeekController(OracleService db)
    {
        _db = db;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/MenuWeek/list  — danh sách tuần (HR admin)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        try
        {
            const string sql = @"
                SELECT ID, WEEK_NAME, FROM_DATE, TO_DATE, STATUS, REMARK,
                       INST_ID, INST_DT, UPDT_ID, UPDT_DT
                FROM HRMS.HR_MENU_WEEK
                ORDER BY FROM_DATE DESC";

            var result = await _db.ExecuteQueryAsync(sql, MapWeek);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/MenuWeek/{id}  — chi tiết 1 tuần + toàn bộ món
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var weeks = await _db.ExecuteQueryAsync(
                @"SELECT ID, WEEK_NAME, FROM_DATE, TO_DATE, STATUS, REMARK,
                         INST_ID, INST_DT, UPDT_ID, UPDT_DT
                  FROM HRMS.HR_MENU_WEEK WHERE ID = :ID",
                MapWeek, new OracleParameter("ID", id));

            if (weeks.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy tuần thực đơn" });

            var details = await GetDetails(id);
            return Ok(new { success = true, data = weeks[0], details });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuWeek/save  — tạo mới hoặc cập nhật tuần
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveWeekRequest model)
    {
        try
        {
            if (model.FROM_DATE == default || model.TO_DATE == default)
                return Ok(new { success = false, message = "Vui lòng nhập đầy đủ ngày bắt đầu và kết thúc" });

            if (model.FROM_DATE > model.TO_DATE)
                return Ok(new { success = false, message = "Ngày bắt đầu phải nhỏ hơn ngày kết thúc" });

            if (model.ID == null || model.ID == 0)
            {
                // Kiểm tra trùng tuần
                var dup = await _db.ExecuteQueryAsync(
                    @"SELECT COUNT(1) AS CNT FROM HRMS.HR_MENU_WEEK
                      WHERE TRUNC(FROM_DATE) = TRUNC(:FROM_DATE)
                        AND TRUNC(TO_DATE)   = TRUNC(:TO_DATE)",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("FROM_DATE", model.FROM_DATE),
                    new OracleParameter("TO_DATE",   model.TO_DATE));

                if (dup.Count > 0 && dup[0] > 0)
                    return Ok(new { success = false, message = "Tuần này đã tồn tại trong hệ thống" });

                var weekName = model.WEEK_NAME ?? $"Tuần {model.FROM_DATE:dd/MM} - {model.TO_DATE:dd/MM/yyyy}";

                const string sql = @"
                    INSERT INTO HRMS.HR_MENU_WEEK
                        (WEEK_NAME, FROM_DATE, TO_DATE, STATUS, REMARK, INST_ID, INST_DT)
                    VALUES
                        (:WEEK_NAME, :FROM_DATE, :TO_DATE, 'DRAFT', :REMARK, :INST_ID, SYSDATE)";

                await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("WEEK_NAME",  weekName),
                    new OracleParameter("FROM_DATE",  model.FROM_DATE),
                    new OracleParameter("TO_DATE",    model.TO_DATE),
                    new OracleParameter("REMARK",     (object?)model.REMARK    ?? DBNull.Value),
                    new OracleParameter("INST_ID",    (object?)model.LOGIN_USER ?? DBNull.Value));

                return Ok(new { success = true, message = "Tạo tuần thực đơn thành công" });
            }
            else
            {
                const string sql = @"
                    UPDATE HRMS.HR_MENU_WEEK
                    SET WEEK_NAME = :WEEK_NAME,
                        FROM_DATE = :FROM_DATE,
                        TO_DATE   = :TO_DATE,
                        REMARK    = :REMARK,
                        UPDT_ID   = :UPDT_ID,
                        UPDT_DT   = SYSDATE
                    WHERE ID = :ID AND STATUS = 'DRAFT'";

                var weekName = model.WEEK_NAME ?? $"Tuần {model.FROM_DATE:dd/MM} - {model.TO_DATE:dd/MM/yyyy}";

                int rows = await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("WEEK_NAME",  weekName),
                    new OracleParameter("FROM_DATE",  model.FROM_DATE),
                    new OracleParameter("TO_DATE",    model.TO_DATE),
                    new OracleParameter("REMARK",     (object?)model.REMARK    ?? DBNull.Value),
                    new OracleParameter("UPDT_ID",    (object?)model.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("ID",         model.ID));

                return Ok(rows > 0
                    ? new { success = true,  message = "Cập nhật thành công" }
                    : new { success = false, message = "Không tìm thấy hoặc tuần đã PUBLISHED không thể sửa" });
            }
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuWeek/detail/save
    // Lưu toàn bộ chi tiết 1 tuần (xóa cũ → insert mới)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("detail/save")]
    public async Task<IActionResult> SaveDetail([FromBody] SaveDetailRequest model)
    {
        try
        {
            if (model.WEEK_ID <= 0)
                return Ok(new { success = false, message = "WEEK_ID không hợp lệ" });

            // Kiểm tra tuần tồn tại và còn DRAFT
            var week = await _db.ExecuteQueryAsync(
                "SELECT STATUS FROM HRMS.HR_MENU_WEEK WHERE ID = :ID",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("ID", model.WEEK_ID));

            if (week.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy tuần thực đơn" });

            if (week[0] == "PUBLISHED")
                return Ok(new { success = false, message = "Tuần đã PUBLISHED không thể chỉnh sửa" });

            var items = model.ITEMS ?? new List<SaveDetailItem>();
            if (items.Any(i => !i.FOOD_ID.HasValue))
                return Ok(new { success = false, message = "Tất cả món ăn phải chọn từ danh mục (FOOD_ID required)" });

            // Xóa toàn bộ detail cũ của tuần
            await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_MENU_DETAIL WHERE WEEK_ID = :WEEK_ID",
                new OracleParameter("WEEK_ID", model.WEEK_ID));

            // Bulk insert tất cả món trong 1 lần gọi DB
            if (items.Count > 0)
            {
                const string insertSql = @"
                    INSERT INTO HRMS.HR_MENU_DETAIL
                        (WEEK_ID, DAY_NO, SHIFT, MEAL_TYPE, FOOD_ID, DISPLAY_ORDER, INST_ID, INST_DT)
                    VALUES
                        (:WEEK_ID, :DAY_NO, :SHIFT, :MEAL_TYPE, :FOOD_ID, :DISPLAY_ORDER, :INST_ID, SYSDATE)";

                var pFoodId = new OracleParameter("FOOD_ID", OracleDbType.Int32)
                {
                    Value = items.Select(i => i.FOOD_ID!.Value).ToArray()
                };
                var pInstId = new OracleParameter("INST_ID", OracleDbType.Varchar2)
                {
                    Value            = items.Select(_ => model.LOGIN_USER ?? string.Empty).ToArray(),
                    ArrayBindStatus  = items.Select(_ => !string.IsNullOrEmpty(model.LOGIN_USER) ? OracleParameterStatus.Success : OracleParameterStatus.NullInsert).ToArray()
                };

                await _db.ExecuteBulkInsertAsync(insertSql, items.Count,
                    new OracleParameter("WEEK_ID",       OracleDbType.Int32)    { Value = items.Select(_ => model.WEEK_ID).ToArray() },
                    new OracleParameter("DAY_NO",        OracleDbType.Int32)    { Value = items.Select(i => i.DAY_NO).ToArray() },
                    new OracleParameter("SHIFT",         OracleDbType.Varchar2) { Value = items.Select(i => i.SHIFT).ToArray() },
                    new OracleParameter("MEAL_TYPE",     OracleDbType.Varchar2) { Value = items.Select(i => i.MEAL_TYPE).ToArray() },
                    pFoodId,
                    new OracleParameter("DISPLAY_ORDER", OracleDbType.Int32)    { Value = items.Select(i => i.DISPLAY_ORDER).ToArray() },
                    pInstId);
            }

            return Ok(new { success = true, message = $"Đã lưu {items.Count} món cho tuần" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuWeek/publish?id=&loginUser=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(int id, string loginUser)
    {
        try
        {
            // Phải có ít nhất 1 detail mới publish
            var cnt = await _db.ExecuteQueryAsync(
                "SELECT COUNT(1) AS CNT FROM HRMS.HR_MENU_DETAIL WHERE WEEK_ID = :ID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("ID", id));

            if (cnt.Count == 0 || cnt[0] == 0)
                return Ok(new { success = false, message = "Chưa có món ăn nào, vui lòng nhập thực đơn trước khi đăng" });

            // Chỉ cho phép 1 thực đơn PUBLISHED tại 1 thời điểm:
            // ẩn (về DRAFT) tất cả tuần khác đang PUBLISHED, rồi mới publish tuần mới.
            const string sql = @"
                BEGIN
                    UPDATE HRMS.HR_MENU_WEEK
                       SET STATUS  = 'DRAFT',
                           UPDT_ID = :UPDT_ID,
                           UPDT_DT = SYSDATE
                     WHERE STATUS = 'PUBLISHED' AND ID <> :ID;

                    UPDATE HRMS.HR_MENU_WEEK
                       SET STATUS  = 'PUBLISHED',
                           UPDT_ID = :UPDT_ID,
                           UPDT_DT = SYSDATE
                     WHERE ID = :ID AND STATUS = 'DRAFT';

                    :ROWS := SQL%ROWCOUNT;
                END;";

            var pRows = new OracleParameter("ROWS", OracleDbType.Int32)
            { Direction = System.Data.ParameterDirection.Output };

            await _db.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", loginUser),
                new OracleParameter("ID",      id),
                pRows);

            int rows = pRows.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                       ? (int)od.Value : 0;

            return Ok(rows > 0
                ? new { success = true,  message = "Đã đăng thực đơn. Công nhân có thể xem ngay!" }
                : new { success = false, message = "Không tìm thấy hoặc tuần đã PUBLISHED" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuWeek/unpublish?id=&loginUser=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("unpublish")]
    public async Task<IActionResult> Unpublish(int id, string loginUser)
    {
        try
        {
            const string sql = @"
                UPDATE HRMS.HR_MENU_WEEK
                SET STATUS  = 'DRAFT',
                    UPDT_ID = :UPDT_ID,
                    UPDT_DT = SYSDATE
                WHERE ID = :ID AND STATUS = 'PUBLISHED'";

            int rows = await _db.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", loginUser),
                new OracleParameter("ID",      id));

            return Ok(rows > 0
                ? new { success = true,  message = "Đã ẩn thực đơn" }
                : new { success = false, message = "Không tìm thấy hoặc tuần chưa PUBLISHED" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuWeek/delete?id=  — chỉ xóa DRAFT
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var status = await _db.ExecuteQueryAsync(
                "SELECT STATUS FROM HRMS.HR_MENU_WEEK WHERE ID = :ID",
                r => r["STATUS"]?.ToString(),
                new OracleParameter("ID", id));

            if (status.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy tuần thực đơn" });

            if (status[0] == "PUBLISHED")
                return Ok(new { success = false, message = "Tuần đã PUBLISHED, vui lòng Unpublish trước khi xóa" });

            await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_MENU_DETAIL WHERE WEEK_ID = :ID",
                new OracleParameter("ID", id));

            await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_MENU_WEEK WHERE ID = :ID",
                new OracleParameter("ID", id));

            return Ok(new { success = true, message = "Đã xóa tuần thực đơn" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/MenuWeek/today  — công nhân xem hôm nay ăn gì
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("today")]
    public async Task<IActionResult> GetToday()
    {
        try
        {
            // Tính DAY_NO từ ngày hôm nay (C# DayOfWeek: Mon=1→DAY_NO=2, Sat=6→DAY_NO=7)
            int dow   = (int)DateTime.Today.DayOfWeek;  // 0=Sun,1=Mon...6=Sat
            int dayNo = dow == 0 ? 0 : dow + 1;         // Chủ nhật = 0 (không có thực đơn)

            if (dayNo == 0)
                return Ok(new { success = true, data = new List<object>(), message = "Hôm nay là Chủ nhật, không có thực đơn" });

            const string sql = @"
                SELECT D.DAY_NO, D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER,
                       F.FOOD_NAME, F.IS_IMAGE,
                       D.FOOD_ID
                FROM HRMS.HR_MENU_WEEK W
                JOIN HRMS.HR_MENU_DETAIL D ON D.WEEK_ID = W.ID
                JOIN HRMS.HR_MENU_FOOD F ON F.ID = D.FOOD_ID
                WHERE TRUNC(SYSDATE) BETWEEN TRUNC(W.FROM_DATE) AND TRUNC(W.TO_DATE)
                  AND W.STATUS = 'PUBLISHED'
                  AND D.DAY_NO = :DAY_NO
                ORDER BY D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER";

            var result = await _db.ExecuteQueryAsync(sql, MapDay,
                new OracleParameter("DAY_NO", dayNo));

            return Ok(new { success = true, data = result, dayNo, date = DateTime.Today.ToString("dd/MM/yyyy") });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/MenuWeek/next-week
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("next-week")]
    public async Task<IActionResult> GetNextWeek()
    {
        try
        {
            const string sql = @"
                SELECT D.DAY_NO, D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER,
                       F.FOOD_NAME, F.IS_IMAGE,
                       D.FOOD_ID
                FROM HRMS.HR_MENU_WEEK W
                JOIN HRMS.HR_MENU_DETAIL D ON D.WEEK_ID = W.ID
                JOIN HRMS.HR_MENU_FOOD F ON F.ID = D.FOOD_ID
                WHERE TRUNC(SYSDATE + 7) BETWEEN TRUNC(W.FROM_DATE) AND TRUNC(W.TO_DATE)
                  AND W.STATUS = 'PUBLISHED'
                ORDER BY D.DAY_NO, D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER";

            var result = await _db.ExecuteQueryAsync(sql, MapDay);

            var weekInfo = await _db.ExecuteQueryAsync(
                @"SELECT WEEK_NAME, FROM_DATE, TO_DATE
                  FROM HRMS.HR_MENU_WEEK
                  WHERE TRUNC(SYSDATE + 7) BETWEEN TRUNC(FROM_DATE) AND TRUNC(TO_DATE)
                    AND STATUS = 'PUBLISHED'",
                r => new
                {
                    weekName = r["WEEK_NAME"]?.ToString(),
                    fromDate = Convert.ToDateTime(r["FROM_DATE"]).ToString("dd/MM/yyyy"),
                    toDate   = Convert.ToDateTime(r["TO_DATE"]).ToString("dd/MM/yyyy"),
                });

            var week = weekInfo.FirstOrDefault();
            return Ok(new { success = true, data = result, weekInfo = week });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/MenuWeek/this-week  — công nhân xem cả tuần
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("this-week")]
    public async Task<IActionResult> GetThisWeek()
    {
        try
        {
            const string sql = @"
                SELECT D.DAY_NO, D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER,
                       F.FOOD_NAME, F.IS_IMAGE,
                       D.FOOD_ID
                FROM HRMS.HR_MENU_WEEK W
                JOIN HRMS.HR_MENU_DETAIL D ON D.WEEK_ID = W.ID
                JOIN HRMS.HR_MENU_FOOD F ON F.ID = D.FOOD_ID
                WHERE TRUNC(SYSDATE) BETWEEN TRUNC(W.FROM_DATE) AND TRUNC(W.TO_DATE)
                  AND W.STATUS = 'PUBLISHED'
                ORDER BY D.DAY_NO, D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER";

            var result = await _db.ExecuteQueryAsync(sql, MapDay);

            // Lấy thêm thông tin tuần
            var weekInfo = await _db.ExecuteQueryAsync(
                @"SELECT WEEK_NAME, FROM_DATE, TO_DATE
                  FROM HRMS.HR_MENU_WEEK
                  WHERE TRUNC(SYSDATE) BETWEEN TRUNC(FROM_DATE) AND TRUNC(TO_DATE)
                    AND STATUS = 'PUBLISHED'",
                r => new
                {
                    weekName = r["WEEK_NAME"]?.ToString(),
                    fromDate = Convert.ToDateTime(r["FROM_DATE"]).ToString("dd/MM/yyyy"),
                    toDate   = Convert.ToDateTime(r["TO_DATE"]).ToString("dd/MM/yyyy"),
                });

            var week = weekInfo.FirstOrDefault();
            return Ok(new { success = true, data = result, weekInfo = week });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<List<MenuDetailModel>> GetDetails(int weekId)
    {
        const string sql = @"
            SELECT D.ID, D.WEEK_ID, D.DAY_NO, D.SHIFT, D.MEAL_TYPE,
                   D.FOOD_ID, D.DISPLAY_ORDER,
                   F.FOOD_NAME
            FROM HRMS.HR_MENU_DETAIL D
            JOIN HRMS.HR_MENU_FOOD F ON F.ID = D.FOOD_ID
            WHERE D.WEEK_ID = :WEEK_ID
            ORDER BY D.DAY_NO, D.SHIFT, D.MEAL_TYPE, D.DISPLAY_ORDER";

        return await _db.ExecuteQueryAsync(sql, r => new MenuDetailModel
        {
            ID           = Convert.ToInt32(r["ID"]),
            WEEK_ID      = Convert.ToInt32(r["WEEK_ID"]),
            DAY_NO       = Convert.ToInt32(r["DAY_NO"]),
            SHIFT        = r["SHIFT"]?.ToString()     ?? string.Empty,
            MEAL_TYPE    = r["MEAL_TYPE"]?.ToString() ?? string.Empty,
            FOOD_ID      = r["FOOD_ID"]   == DBNull.Value ? null : Convert.ToInt32(r["FOOD_ID"]),
            FOOD_NAME    = r["FOOD_NAME"]?.ToString(),
            DISPLAY_ORDER= r["DISPLAY_ORDER"] == DBNull.Value ? 1 : Convert.ToInt32(r["DISPLAY_ORDER"]),
        }, new OracleParameter("WEEK_ID", weekId));
    }

    private static MenuWeekModel MapWeek(OracleDataReader r) => new()
    {
        ID        = Convert.ToInt32(r["ID"]),
        WEEK_NAME = r["WEEK_NAME"]?.ToString(),
        FROM_DATE = Convert.ToDateTime(r["FROM_DATE"]),
        TO_DATE   = Convert.ToDateTime(r["TO_DATE"]),
        STATUS    = r["STATUS"]?.ToString() ?? string.Empty,
        REMARK    = r["REMARK"]?.ToString(),
        INST_ID   = r["INST_ID"]?.ToString(),
        INST_DT   = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID   = r["UPDT_ID"]?.ToString(),
        UPDT_DT   = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
    };

    private static MenuDayViewModel MapDay(OracleDataReader r)
    {
        var dayNo = r["DAY_NO"] == DBNull.Value ? 0 : Convert.ToInt32(r["DAY_NO"]);
        var labels = new Dictionary<int, string> { {2,"Thứ 2"},{3,"Thứ 3"},{4,"Thứ 4"},{5,"Thứ 5"},{6,"Thứ 6"},{7,"Thứ 7"} };
        return new()
        {
            DAY_NO        = dayNo,
            DAY_LABEL     = labels.GetValueOrDefault(dayNo, $"Thứ {dayNo}"),
            SHIFT         = r["SHIFT"]?.ToString()     ?? string.Empty,
            MEAL_TYPE     = r["MEAL_TYPE"]?.ToString() ?? string.Empty,
            FOOD_NAME     = r["FOOD_NAME"]?.ToString(),
            FOOD_ID       = r["FOOD_ID"] == DBNull.Value ? null : Convert.ToInt32(r["FOOD_ID"]),
            IS_IMAGE      = r["IS_IMAGE"]?.ToString() ?? "N",
            DISPLAY_ORDER = r["DISPLAY_ORDER"] == DBNull.Value ? 1 : Convert.ToInt32(r["DISPLAY_ORDER"]),
        };
    }
}
