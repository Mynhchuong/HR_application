using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;

// Ghi log mọi lần đổi CONFIRM_STATUS của OT (ký lần đầu / đổi ý / ký giùm / admin sửa / admin xoá).
// Fire-and-forget để không chặn request chính.
public class OtLogHelper
{
    private readonly OracleService _oracleService;

    public OtLogHelper(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    public enum LogAction { INS, UPD, DEL }

    public void Log(
        LogAction action,
        string? requestId,
        string empcd,
        DateTime workDate,
        string? oldStatus,
        string? newStatus,
        decimal? oldHours,
        decimal? newHours,
        string? actorEmpCd)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                const string sql = @"
                    INSERT INTO HRMS.HR_OT_LOG
                        (REQUEST_ID, EMPCD, WORK_DATE, ACTION,
                         OLD_STATUS, NEW_STATUS, OLD_HOURS, NEW_HOURS,
                         ACTOR_EMPCD, INST_DT)
                    VALUES
                        (:REQUEST_ID, :EMPCD, :WORK_DATE, :ACTION,
                         :OLD_STATUS, :NEW_STATUS, :OLD_HOURS, :NEW_HOURS,
                         :ACTOR_EMPCD, SYSDATE)";

                await _oracleService.ExecuteNonQueryAsync(sql,
                    new OracleParameter("REQUEST_ID",  (object?)requestId ?? DBNull.Value),
                    new OracleParameter("EMPCD",       empcd),
                    new OracleParameter("WORK_DATE",   workDate),
                    new OracleParameter("ACTION",      action.ToString()),
                    new OracleParameter("OLD_STATUS",  (object?)oldStatus ?? DBNull.Value),
                    new OracleParameter("NEW_STATUS",  (object?)newStatus ?? DBNull.Value),
                    new OracleParameter("OLD_HOURS",   (object?)oldHours  ?? DBNull.Value),
                    new OracleParameter("NEW_HOURS",   (object?)newHours  ?? DBNull.Value),
                    new OracleParameter("ACTOR_EMPCD", (object?)actorEmpCd ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OtLogHelper] Log error: {ex.Message}");
            }
        });
    }
}
