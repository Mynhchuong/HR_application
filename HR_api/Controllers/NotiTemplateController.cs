using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Notification;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class NotiTemplateController : ControllerBase
{
    private readonly OracleService _db;

    public NotiTemplateController(OracleService db) { _db = db; }

    // GET /NotiTemplate/list
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        try
        {
            const string sql = @"
                SELECT ID, TEMPLATE_KEY, TITLE_VI, BODY_VI, TITLE_EN, BODY_EN,
                       IS_ACTIVE, UPDATED_BY, UPDATED_DATE
                FROM HRMS.HR_NOTI_TEMPLATES
                ORDER BY TEMPLATE_KEY";

            var list = await _db.ExecuteQueryAsync(sql, Map);
            return Ok(new { success = true, data = list });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /NotiTemplate/by-key?key=PAYSLIP
    [HttpGet("by-key")]
    public async Task<IActionResult> GetByKey(string key)
    {
        try
        {
            const string sql = @"
                SELECT ID, TEMPLATE_KEY, TITLE_VI, BODY_VI, TITLE_EN, BODY_EN,
                       IS_ACTIVE, UPDATED_BY, UPDATED_DATE
                FROM HRMS.HR_NOTI_TEMPLATES
                WHERE TEMPLATE_KEY = :KEY AND IS_ACTIVE = 1";

            var list = await _db.ExecuteQueryAsync(sql, Map, new OracleParameter("KEY", key));
            return Ok(new { success = true, data = list.FirstOrDefault() });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /NotiTemplate/save
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SaveNotiTemplateRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.TEMPLATE_KEY) || string.IsNullOrWhiteSpace(req.TITLE_VI))
                return Ok(new { success = false, message = "Thiếu thông tin bắt buộc" });

            const string sql = @"
                MERGE INTO HRMS.HR_NOTI_TEMPLATES T
                USING (SELECT :KEY K FROM DUAL) S ON (T.TEMPLATE_KEY = S.K)
                WHEN MATCHED THEN UPDATE SET
                    TITLE_VI     = :TITLE_VI,
                    BODY_VI      = :BODY_VI,
                    TITLE_EN     = :TITLE_EN,
                    BODY_EN      = :BODY_EN,
                    UPDATED_BY   = :UPDATED_BY,
                    UPDATED_DATE = SYSDATE
                WHEN NOT MATCHED THEN INSERT
                    (TEMPLATE_KEY, TITLE_VI, BODY_VI, TITLE_EN, BODY_EN, IS_ACTIVE, UPDATED_BY, UPDATED_DATE)
                VALUES
                    (:KEY, :TITLE_VI, :BODY_VI, :TITLE_EN, :BODY_EN, 1, :UPDATED_BY, SYSDATE)";

            await _db.ExecuteNonQueryAsync(sql,
                new OracleParameter("KEY",        req.TEMPLATE_KEY),
                new OracleParameter("TITLE_VI",   req.TITLE_VI),
                new OracleParameter("BODY_VI",    req.BODY_VI),
                new OracleParameter("TITLE_EN",   (object?)req.TITLE_EN   ?? DBNull.Value),
                new OracleParameter("BODY_EN",    (object?)req.BODY_EN    ?? DBNull.Value),
                new OracleParameter("UPDATED_BY", (object?)req.LOGIN_USER ?? DBNull.Value));

            NotificationHelper.InvalidateTemplateCache();
            return Ok(new { success = true, message = "Lưu thành công" });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /NotiTemplate/toggle?key=PAYSLIP
    [HttpPost("toggle")]
    public async Task<IActionResult> Toggle(string key, string loginUser)
    {
        try
        {
            const string sql = @"
                UPDATE HRMS.HR_NOTI_TEMPLATES
                SET IS_ACTIVE = CASE WHEN IS_ACTIVE = 1 THEN 0 ELSE 1 END,
                    UPDATED_BY = :USER, UPDATED_DATE = SYSDATE
                WHERE TEMPLATE_KEY = :KEY";

            await _db.ExecuteNonQueryAsync(sql,
                new OracleParameter("USER", loginUser),
                new OracleParameter("KEY",  key));

            NotificationHelper.InvalidateTemplateCache();
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    private static NotiTemplateModel Map(System.Data.IDataReader r) => new()
    {
        ID           = Convert.ToInt32(r["ID"]),
        TEMPLATE_KEY = r["TEMPLATE_KEY"]?.ToString() ?? "",
        TITLE_VI     = r["TITLE_VI"]?.ToString()     ?? "",
        BODY_VI      = r["BODY_VI"]?.ToString()      ?? "",
        TITLE_EN     = r["TITLE_EN"]?.ToString(),
        BODY_EN      = r["BODY_EN"]?.ToString(),
        IS_ACTIVE    = Convert.ToInt32(r["IS_ACTIVE"]),
        UPDATED_BY   = r["UPDATED_BY"]?.ToString(),
        UPDATED_DATE = r["UPDATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDATED_DATE"])
    };
}
