using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Policy;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class PolicyController : ControllerBase
{
    private readonly OracleService _oracleService;

    public PolicyController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Policy/list  — worker: chỉ lấy active, join tên người cập nhật
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        try
        {
            const string sql = @"
                SELECT P.ID, P.CATEGORY, P.TITLE,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 1)    AS C1,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 2001) AS C2,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 4001) AS C3,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 6001) AS C4,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 8001) AS C5,
                       P.DISPLAY_ORDER, P.IS_ACTIVE,
                       P.INST_ID, P.INST_DT, P.UPDT_ID, P.UPDT_DT,
                       U.FULL_NAME AS UPDT_FULL_NAME
                FROM HRMS.HR_COMPANY_POLICY P
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = P.UPDT_ID
                WHERE P.IS_ACTIVE = 1
                ORDER BY P.CATEGORY, P.DISPLAY_ORDER, P.ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapFull);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Policy/admin/list  — HR: lấy tất cả kể cả inactive
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminList()
    {
        try
        {
            const string sql = @"
                SELECT P.ID, P.CATEGORY, P.TITLE,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 1)    AS C1,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 2001) AS C2,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 4001) AS C3,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 6001) AS C4,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 8001) AS C5,
                       P.DISPLAY_ORDER, P.IS_ACTIVE,
                       P.INST_ID, P.INST_DT, P.UPDT_ID, P.UPDT_DT,
                       U.FULL_NAME AS UPDT_FULL_NAME
                FROM HRMS.HR_COMPANY_POLICY P
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = P.UPDT_ID
                ORDER BY P.CATEGORY, P.DISPLAY_ORDER, P.ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapFull);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Policy/{id}
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            const string sql = @"
                SELECT P.ID, P.CATEGORY, P.TITLE,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 1)    AS C1,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 2001) AS C2,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 4001) AS C3,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 6001) AS C4,
                       DBMS_LOB.SUBSTR(P.CONTENT, 2000, 8001) AS C5,
                       P.DISPLAY_ORDER, P.IS_ACTIVE,
                       P.INST_ID, P.INST_DT, P.UPDT_ID, P.UPDT_DT,
                       U.FULL_NAME AS UPDT_FULL_NAME
                FROM HRMS.HR_COMPANY_POLICY P
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = P.UPDT_ID
                WHERE P.ID = :ID";

            var result = await _oracleService.ExecuteQueryAsync(sql, MapFull,
                new OracleParameter("ID", id));

            if (result.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy quy định" });

            return Ok(new { success = true, data = result[0] });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Policy/save  — tạo mới hoặc cập nhật
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SavePolicyRequest model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.CATEGORY) ||
                string.IsNullOrWhiteSpace(model.TITLE) ||
                string.IsNullOrWhiteSpace(model.CONTENT))
                return Ok(new { success = false, message = "Vui lòng điền đầy đủ thông tin" });

            // Oracle 10g không chấp nhận null byte (\0) trong NCLOB — strip trước khi bind
            var content = model.CONTENT.Replace("\0", "");

            if (model.ID == null || model.ID == 0)
            {
                // INSERT
                const string sqlInsert = @"
                    INSERT INTO HRMS.HR_COMPANY_POLICY
                        (CATEGORY, TITLE, CONTENT, DISPLAY_ORDER, IS_ACTIVE, INST_ID, INST_DT)
                    VALUES
                        (:CATEGORY, :TITLE, :CONTENT, :DISPLAY_ORDER, :IS_ACTIVE, :INST_ID, SYSDATE)";

                await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                    new OracleParameter("CATEGORY",      model.CATEGORY),
                    new OracleParameter("TITLE",         model.TITLE),
                    new OracleParameter("CONTENT",       OracleDbType.NClob) { Value = content },
                    new OracleParameter("DISPLAY_ORDER", model.DISPLAY_ORDER),
                    new OracleParameter("IS_ACTIVE",     model.IS_ACTIVE),
                    new OracleParameter("INST_ID",       (object?)model.LOGIN_USER ?? DBNull.Value));

                return Ok(new { success = true, message = "Tạo quy định thành công" });
            }
            else
            {
                // UPDATE
                const string sqlUpdate = @"
                    UPDATE HRMS.HR_COMPANY_POLICY
                    SET CATEGORY      = :CATEGORY,
                        TITLE         = :TITLE,
                        CONTENT       = :CONTENT,
                        DISPLAY_ORDER = :DISPLAY_ORDER,
                        IS_ACTIVE     = :IS_ACTIVE,
                        UPDT_ID       = :UPDT_ID,
                        UPDT_DT       = SYSDATE
                    WHERE ID = :ID";

                int rows = await _oracleService.ExecuteNonQueryAsync(sqlUpdate,
                    new OracleParameter("CATEGORY",      model.CATEGORY),
                    new OracleParameter("TITLE",         model.TITLE),
                    new OracleParameter("CONTENT",       OracleDbType.NClob) { Value = content },
                    new OracleParameter("DISPLAY_ORDER", model.DISPLAY_ORDER),
                    new OracleParameter("IS_ACTIVE",     model.IS_ACTIVE),
                    new OracleParameter("UPDT_ID",       (object?)model.LOGIN_USER ?? DBNull.Value),
                    new OracleParameter("ID",            model.ID));

                if (rows == 0)
                    return Ok(new { success = false, message = "Không tìm thấy quy định để cập nhật" });

                return Ok(new { success = true, message = "Cập nhật thành công" });
            }
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Policy/toggle?id=&loginUser=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("toggle")]
    public async Task<IActionResult> Toggle(int id, string loginUser)
    {
        try
        {
            const string sql = @"
                UPDATE HRMS.HR_COMPANY_POLICY
                SET IS_ACTIVE = CASE WHEN IS_ACTIVE = 1 THEN 0 ELSE 1 END,
                    UPDT_ID   = :UPDT_ID,
                    UPDT_DT   = SYSDATE
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

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Policy/delete?id=  — chỉ xóa khi IS_ACTIVE = 0
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("delete")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            const string checkSql = "SELECT IS_ACTIVE FROM HRMS.HR_COMPANY_POLICY WHERE ID = :ID";
            var check = await _oracleService.ExecuteQueryAsync(checkSql,
                r => Convert.ToInt32(r["IS_ACTIVE"]),
                new OracleParameter("ID", id));

            if (check.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy quy định" });

            if (check[0] != 0)
                return Ok(new { success = false, message = "Chỉ được xóa quy định đang ẩn" });

            const string deleteSql = "DELETE FROM HRMS.HR_COMPANY_POLICY WHERE ID = :ID";
            await _oracleService.ExecuteNonQueryAsync(deleteSql, new OracleParameter("ID", id));

            return Ok(new { success = true, message = "Đã xóa quy định" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // Reads NCLOB in 5×2000-char chunks (Oracle 10g SQL limit per DBMS_LOB.SUBSTR)
    private static CompanyPolicyModel MapFull(Oracle.ManagedDataAccess.Client.OracleDataReader r) => new()
    {
        ID             = Convert.ToInt32(r["ID"]),
        CATEGORY       = r["CATEGORY"]?.ToString() ?? string.Empty,
        TITLE          = r["TITLE"]?.ToString() ?? string.Empty,
        CONTENT        = (r["C1"]?.ToString() ?? "") + (r["C2"]?.ToString() ?? "") +
                         (r["C3"]?.ToString() ?? "") + (r["C4"]?.ToString() ?? "") +
                         (r["C5"]?.ToString() ?? ""),
        DISPLAY_ORDER  = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"]),
        IS_ACTIVE      = r["IS_ACTIVE"] == DBNull.Value ? 0 : Convert.ToInt32(r["IS_ACTIVE"]),
        INST_ID        = r["INST_ID"]?.ToString(),
        INST_DT        = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
        UPDT_ID        = r["UPDT_ID"]?.ToString(),
        UPDT_DT        = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
        UPDT_FULL_NAME = r["UPDT_FULL_NAME"]?.ToString()
    };
}
