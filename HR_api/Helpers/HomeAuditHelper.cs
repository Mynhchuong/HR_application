using HR_api.Data;
using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;

// Ghi log mọi thay đổi cấu hình Home (Banner / Greeting / Rule).
// Fire-and-forget để không chặn request chính.
public class HomeAuditHelper
{
    private readonly OracleService _oracleService;
    private readonly IHttpContextAccessor _http;

    public HomeAuditHelper(OracleService oracleService, IHttpContextAccessor http)
    {
        _oracleService = oracleService;
        _http          = http;
    }

    public enum AuditEntity { BANNER, GREETING, RULE }
    public enum AuditAction { INS, UPD, DEL }

    public void Log(
        AuditAction action,
        AuditEntity entity,
        string? entityId,
        object? oldValue,
        object? newValue,
        string actorEmpCd,
        string? actorName = null,
        string? roleName  = null)
    {
        // Snapshot HttpContext-derived values NGAY LẬP TỨC — trong Task.Run
        // HttpContext có thể đã null vì request đã hoàn tất trước khi task chạy.
        var ctxSnap   = _http.HttpContext;
        var ipSnap    = ctxSnap?.Connection.RemoteIpAddress?.ToString();
        var uaSnap    = ctxSnap?.Request.Headers.UserAgent.ToString();

        _ = Task.Run(async () =>
        {
            try
            {
                var ip        = ipSnap;
                var userAgent = uaSnap;

                const string sql = @"
                    INSERT INTO HRMS.HR_HOME_AUDIT
                        (ACTOR_EMPCD, ACTOR_NAME, ROLE_NAME,
                         ACTION, ENTITY, ENTITY_ID,
                         OLD_VAL, NEW_VAL,
                         IP_ADDRESS, USER_AGENT, INST_DT)
                    VALUES
                        (:ACTOR_EMPCD, :ACTOR_NAME, :ROLE_NAME,
                         :ACTION, :ENTITY, :ENTITY_ID,
                         :OLD_VAL, :NEW_VAL,
                         :IP_ADDRESS, :USER_AGENT, SYSDATE)";

                string? oldJson = oldValue == null ? null
                    : JsonConvert.SerializeObject(oldValue);
                string? newJson = newValue == null ? null
                    : JsonConvert.SerializeObject(newValue);

                await _oracleService.ExecuteNonQueryAsync(sql,
                    new OracleParameter("ACTOR_EMPCD", actorEmpCd),
                    new OracleParameter("ACTOR_NAME",  (object?)actorName ?? DBNull.Value),
                    new OracleParameter("ROLE_NAME",   (object?)roleName  ?? DBNull.Value),
                    new OracleParameter("ACTION",      action.ToString()),
                    new OracleParameter("ENTITY",      entity.ToString()),
                    new OracleParameter("ENTITY_ID",   (object?)entityId ?? DBNull.Value),
                    new OracleParameter("OLD_VAL",     OracleDbType.Clob) { Value = (object?)oldJson ?? DBNull.Value },
                    new OracleParameter("NEW_VAL",     OracleDbType.Clob) { Value = (object?)newJson ?? DBNull.Value },
                    new OracleParameter("IP_ADDRESS",  (object?)ip        ?? DBNull.Value),
                    new OracleParameter("USER_AGENT",  (object?)userAgent ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HomeAuditHelper] Log error: {ex.Message}");
            }
        });
    }
}
