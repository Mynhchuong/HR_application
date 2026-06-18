using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Guide;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class GuideController : ControllerBase
{
    private readonly OracleService _oracleService;

    public GuideController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // GET /apiHR/Guide/list  — worker: chỉ active
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        try
        {
            const string sql = @"
                SELECT G.ID, G.CATEGORY, G.TITLE,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 1) AS CONTENT,
                       G.IS_VIDEO, G.DISPLAY_ORDER, G.IS_ACTIVE,
                       G.INST_ID, G.INST_DT, G.UPDT_ID, G.UPDT_DT,
                       U.FULL_NAME AS UPDT_FULL_NAME
                FROM HRMS.HR_GUIDE G
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = G.UPDT_ID
                WHERE G.IS_ACTIVE = 1
                ORDER BY G.CATEGORY NULLS LAST, G.DISPLAY_ORDER, G.ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, Map);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Guide/admin/list  — HR: tất cả kể cả inactive
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminList()
    {
        try
        {
            const string sql = @"
                SELECT G.ID, G.CATEGORY, G.TITLE,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 1) AS CONTENT,
                       G.IS_VIDEO, G.DISPLAY_ORDER, G.IS_ACTIVE,
                       G.INST_ID, G.INST_DT, G.UPDT_ID, G.UPDT_DT,
                       U.FULL_NAME AS UPDT_FULL_NAME
                FROM HRMS.HR_GUIDE G
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = G.UPDT_ID
                ORDER BY G.CATEGORY NULLS LAST, G.DISPLAY_ORDER, G.ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, Map);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Guide/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            const string sql = @"
                SELECT G.ID, G.CATEGORY, G.TITLE,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 1)    AS C1,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 2001) AS C2,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 4001) AS C3,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 6001) AS C4,
                       DBMS_LOB.SUBSTR(G.CONTENT, 2000, 8001) AS C5,
                       G.IS_VIDEO, G.DISPLAY_ORDER, G.IS_ACTIVE,
                       G.INST_ID, G.INST_DT, G.UPDT_ID, G.UPDT_DT,
                       U.FULL_NAME AS UPDT_FULL_NAME
                FROM HRMS.HR_GUIDE G
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = G.UPDT_ID
                WHERE G.ID = :ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapFull,
                new OracleParameter("ID", id));

            if (result.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy mục hướng dẫn" });

            return Ok(new { success = true, data = result[0] });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/Guide/save
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveGuideRequest model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.TITLE))
                return Ok(new { success = false, message = "Vui lòng nhập tiêu đề" });

            if (model.ID == null || model.ID == 0)
            {
                const string sqlInsert = @"
                    INSERT INTO HRMS.HR_GUIDE
                        (CATEGORY, TITLE, CONTENT, DISPLAY_ORDER, IS_ACTIVE, INST_ID, INST_DT)
                    VALUES
                        (:CATEGORY, :TITLE, :CONTENT, :DISPLAY_ORDER, :IS_ACTIVE, :INST_ID, SYSDATE)
                    RETURNING ID INTO :newId";

                var newIdParam = new OracleParameter("newId", OracleDbType.Int32)
                    { Direction = System.Data.ParameterDirection.Output };
                await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                    new OracleParameter("CATEGORY",      (object?)model.CATEGORY      ?? DBNull.Value),
                    new OracleParameter("TITLE",         model.TITLE),
                    new OracleParameter("CONTENT",       OracleDbType.NClob) { Value = (object?)(model.CONTENT?.Replace("\0", "")) ?? DBNull.Value },
                    new OracleParameter("DISPLAY_ORDER", model.DISPLAY_ORDER),
                    new OracleParameter("IS_ACTIVE",     model.IS_ACTIVE),
                    new OracleParameter("INST_ID",       (object?)model.LOGIN_USER    ?? DBNull.Value),
                    newIdParam);

                int newId = Convert.ToInt32(newIdParam.Value.ToString());
                return Ok(new { success = true, message = "Tạo mục hướng dẫn thành công", data = newId });
            }
            else
            {
                const string sqlUpdate = @"
                    UPDATE HRMS.HR_GUIDE
                    SET CATEGORY      = :CATEGORY,
                        TITLE         = :TITLE,
                        CONTENT       = :CONTENT,
                        DISPLAY_ORDER = :DISPLAY_ORDER,
                        IS_ACTIVE     = :IS_ACTIVE,
                        UPDT_ID       = :UPDT_ID
                    WHERE ID = :ID";

                int rows = await _oracleService.ExecuteNonQueryAsync(sqlUpdate,
                    new OracleParameter("CATEGORY",      (object?)model.CATEGORY   ?? DBNull.Value),
                    new OracleParameter("TITLE",         model.TITLE),
                    new OracleParameter("CONTENT",       OracleDbType.NClob) { Value = (object?)(model.CONTENT?.Replace("\0", "")) ?? DBNull.Value },
                    new OracleParameter("DISPLAY_ORDER", model.DISPLAY_ORDER),
                    new OracleParameter("IS_ACTIVE",     model.IS_ACTIVE),
                    new OracleParameter("UPDT_ID",       (object?)model.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("ID",            model.ID));

                if (rows == 0)
                    return Ok(new { success = false, message = "Không tìm thấy mục hướng dẫn để cập nhật" });

                return Ok(new { success = true, message = "Cập nhật thành công" });
            }
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/Guide/set-video?id=&value=Y|N
    [HttpPost("set-video")]
    public async Task<IActionResult> SetVideo(int id, string value, string? loginUser)
    {
        try
        {
            var isVideo = value == "Y" ? "Y" : "N";
            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_GUIDE
                SET IS_VIDEO = :IS_VIDEO,
                    UPDT_ID  = :UPDT_ID
                WHERE ID = :ID",
                new OracleParameter("IS_VIDEO", isVideo),
                new OracleParameter("UPDT_ID",  (object?)loginUser ?? DBNull.Value),
                new OracleParameter("ID",       id));

            return Ok(new { success = rows > 0 });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/Guide/toggle?id=&loginUser=
    [HttpPost("toggle")]
    public async Task<IActionResult> Toggle(int id, string loginUser)
    {
        try
        {
            const string sql = @"
                UPDATE HRMS.HR_GUIDE
                SET IS_ACTIVE = CASE WHEN IS_ACTIVE = 1 THEN 0 ELSE 1 END,
                    UPDT_ID   = :UPDT_ID
                WHERE ID = :ID";

            int rows = await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", loginUser),
                new OracleParameter("ID", id));

            return Ok(new { success = rows > 0, message = rows > 0 ? "Cập nhật trạng thái thành công" : "Không tìm thấy" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/Guide/delete?id=
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            const string checkSql = "SELECT IS_ACTIVE FROM HRMS.HR_GUIDE WHERE ID = :ID";
            var check = await _oracleService.ExecuteQueryAsync(checkSql,
                r => Convert.ToInt32(r["IS_ACTIVE"]),
                new OracleParameter("ID", id));

            if (check.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy mục hướng dẫn" });

            if (check[0] != 0)
                return Ok(new { success = false, message = "Chỉ được xóa mục đang ẩn" });

            const string deleteSql = "DELETE FROM HRMS.HR_GUIDE WHERE ID = :ID";
            await _oracleService.ExecuteNonQueryAsync(deleteSql, new OracleParameter("ID", id));

            return Ok(new { success = true, message = "Đã xóa mục hướng dẫn" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    private static GuideModel Map(OracleDataReader r) => new()
    {
        ID            = Convert.ToInt32(r["ID"]),
        CATEGORY      = r["CATEGORY"]?.ToString(),
        TITLE         = r["TITLE"]?.ToString() ?? string.Empty,
        CONTENT       = r["CONTENT"]?.ToString(),
        IS_VIDEO      = r["IS_VIDEO"]?.ToString() ?? "N",
        DISPLAY_ORDER = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"]),
        IS_ACTIVE     = r["IS_ACTIVE"]     == DBNull.Value ? 0 : Convert.ToInt32(r["IS_ACTIVE"]),
        INST_ID       = r["INST_ID"]?.ToString(),
        INST_DT       = r["INST_DT"]  == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID       = r["UPDT_ID"]?.ToString(),
        UPDT_DT       = r["UPDT_DT"]  == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
        UPDT_FULL_NAME = r["UPDT_FULL_NAME"]?.ToString(),
    };

    // Used by GetById — reads NCLOB in 5×2000-char chunks (Oracle 10g SQL limit per DBMS_LOB.SUBSTR)
    private static GuideModel MapFull(OracleDataReader r) => new()
    {
        ID            = Convert.ToInt32(r["ID"]),
        CATEGORY      = r["CATEGORY"]?.ToString(),
        TITLE         = r["TITLE"]?.ToString() ?? string.Empty,
        CONTENT       = string.IsNullOrEmpty(r["C1"]?.ToString())
                            ? null
                            : (r["C1"]?.ToString() ?? "") + (r["C2"]?.ToString() ?? "") +
                              (r["C3"]?.ToString() ?? "") + (r["C4"]?.ToString() ?? "") +
                              (r["C5"]?.ToString() ?? ""),
        IS_VIDEO      = r["IS_VIDEO"]?.ToString() ?? "N",
        DISPLAY_ORDER = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"]),
        IS_ACTIVE     = r["IS_ACTIVE"]     == DBNull.Value ? 0 : Convert.ToInt32(r["IS_ACTIVE"]),
        INST_ID       = r["INST_ID"]?.ToString(),
        INST_DT       = r["INST_DT"]  == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID       = r["UPDT_ID"]?.ToString(),
        UPDT_DT       = r["UPDT_DT"]  == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
        UPDT_FULL_NAME = r["UPDT_FULL_NAME"]?.ToString(),
    };
}
