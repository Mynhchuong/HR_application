using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Menu;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class MenuFoodController : ControllerBase
{
    private readonly OracleService _db;

    public MenuFoodController(OracleService db)
    {
        _db = db;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/MenuFood/list  — toàn bộ (HR admin)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        try
        {
            const string sql = @"
                SELECT ID, FOOD_NAME, FOOD_TYPE, IS_IMAGE, IS_ACTIVE,
                       INST_ID, INST_DT, UPDT_ID, UPDT_DT
                FROM HRMS.HR_MENU_FOOD
                ORDER BY IS_ACTIVE DESC, FOOD_TYPE, FOOD_NAME";

            var result = await _db.ExecuteQueryAsync(sql, Map);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/MenuFood/active  — chỉ active (dùng khi dropdown chọn món)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        try
        {
            const string sql = @"
                SELECT ID, FOOD_NAME, FOOD_TYPE, IS_IMAGE, IS_ACTIVE,
                       INST_ID, INST_DT, UPDT_ID, UPDT_DT
                FROM HRMS.HR_MENU_FOOD
                WHERE IS_ACTIVE = 1
                ORDER BY FOOD_TYPE, FOOD_NAME";

            var result = await _db.ExecuteQueryAsync(sql, Map);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuFood/save  — tạo mới hoặc cập nhật
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveFoodRequest model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.FOOD_NAME))
                return Ok(new { success = false, message = "Vui lòng nhập tên món ăn" });

            if (model.ID == null || model.ID == 0)
            {
                const string sql = @"
                    INSERT INTO HRMS.HR_MENU_FOOD
                        (FOOD_NAME, FOOD_TYPE, IS_ACTIVE, INST_ID, INST_DT)
                    VALUES
                        (:FOOD_NAME, :FOOD_TYPE, :IS_ACTIVE, :INST_ID, SYSDATE)
                    RETURNING ID INTO :newId";

                var newIdParam = new OracleParameter("newId", Oracle.ManagedDataAccess.Client.OracleDbType.Int32)
                {
                    Direction = System.Data.ParameterDirection.Output
                };
                await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("FOOD_NAME",  model.FOOD_NAME),
                    new OracleParameter("FOOD_TYPE",  (object?)model.FOOD_TYPE  ?? DBNull.Value),
                    new OracleParameter("IS_ACTIVE",  model.IS_ACTIVE),
                    new OracleParameter("INST_ID",    (object?)model.LOGIN_USER ?? DBNull.Value),
                    newIdParam);

                int newId = Convert.ToInt32(newIdParam.Value.ToString());
                return Ok(new { success = true, message = "Thêm món ăn thành công", newId });
            }
            else
            {
                const string sql = @"
                    UPDATE HRMS.HR_MENU_FOOD
                    SET FOOD_NAME  = :FOOD_NAME,
                        FOOD_TYPE  = :FOOD_TYPE,
                        IS_ACTIVE  = :IS_ACTIVE,
                        UPDT_ID    = :UPDT_ID,
                        UPDT_DT    = SYSDATE
                    WHERE ID = :ID";

                int rows = await _db.ExecuteNonQueryAsync(sql,
                    new OracleParameter("FOOD_NAME",  model.FOOD_NAME),
                    new OracleParameter("FOOD_TYPE",  (object?)model.FOOD_TYPE  ?? DBNull.Value),
                    new OracleParameter("IS_ACTIVE",  model.IS_ACTIVE),
                    new OracleParameter("UPDT_ID",    (object?)model.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("ID",         model.ID));

                return Ok(rows > 0
                    ? new { success = true,  message = "Cập nhật thành công" }
                    : new { success = false, message = "Không tìm thấy món ăn" });
            }
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuFood/toggle?id=&loginUser=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("toggle")]
    public async Task<IActionResult> Toggle(int id, string loginUser)
    {
        try
        {
            const string sql = @"
                UPDATE HRMS.HR_MENU_FOOD
                SET IS_ACTIVE = CASE WHEN IS_ACTIVE = 1 THEN 0 ELSE 1 END,
                    UPDT_ID   = :UPDT_ID,
                    UPDT_DT   = SYSDATE
                WHERE ID = :ID";

            int rows = await _db.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", loginUser),
                new OracleParameter("ID",      id));

            return Ok(new { success = rows > 0, message = rows > 0 ? "Cập nhật trạng thái thành công" : "Không tìm thấy" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuFood/delete?id=  — chỉ xóa khi không có detail nào dùng
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var used = await _db.ExecuteQueryAsync(
                "SELECT COUNT(1) AS CNT FROM HRMS.HR_MENU_DETAIL WHERE FOOD_ID = :ID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("ID", id));

            if (used.Count > 0 && used[0] > 0)
                return Ok(new { success = false, message = "Món ăn đang được dùng trong thực đơn, không thể xóa" });

            await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_MENU_FOOD WHERE ID = :ID",
                new OracleParameter("ID", id));

            return Ok(new { success = true, message = "Đã xóa món ăn" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/MenuFood/set-image?id=&value=Y|N
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("set-image")]
    public async Task<IActionResult> SetImage(int id, string value)
    {
        try
        {
            var v = (value == "Y") ? "Y" : "N";
            int rows = await _db.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_MENU_FOOD SET IS_IMAGE = :IS_IMAGE WHERE ID = :ID",
                new OracleParameter("IS_IMAGE", v),
                new OracleParameter("ID", id));
            return Ok(new { success = rows > 0 });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static MenuFoodModel Map(OracleDataReader r) => new()
    {
        ID         = Convert.ToInt32(r["ID"]),
        FOOD_NAME  = r["FOOD_NAME"]?.ToString()  ?? string.Empty,
        FOOD_TYPE  = r["FOOD_TYPE"]?.ToString(),
        IS_IMAGE   = r["IS_IMAGE"]?.ToString()   ?? "N",
        IS_ACTIVE  = r["IS_ACTIVE"]  == DBNull.Value ? 0 : Convert.ToInt32(r["IS_ACTIVE"]),
        INST_ID    = r["INST_ID"]?.ToString(),
        INST_DT    = r["INST_DT"]   == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID    = r["UPDT_ID"]?.ToString(),
        UPDT_DT    = r["UPDT_DT"]   == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
    };
}
