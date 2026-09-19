using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;

// Ghi log mọi thao tác lên HR_GIFT_RECIPIENT — mirror AttendanceConfirmLogHelper.
// Fire-and-forget để không chặn request chính.
public class GiftLogHelper
{
    private readonly OracleService _oracleService;

    public GiftLogHelper(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    public enum LogAction { IMPORTED, DELIVERED, CONFIRM_REQUESTED, CONFIRMED, REMINDED }

    public void Log(LogAction action, int recipientId, string? actorEmpCd, string? detail)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                const string sql = @"
                    INSERT INTO HRMS.HR_GIFT_LOG (RECIPIENT_ID, ACTION, ACTOR_EMPCD, DETAIL, INST_DT)
                    VALUES (:RECIPIENT_ID, :ACTION, :ACTOR_EMPCD, :DETAIL, SYSDATE)";

                await _oracleService.ExecuteNonQueryAsync(sql,
                    new OracleParameter("RECIPIENT_ID", recipientId),
                    new OracleParameter("ACTION",       action.ToString()),
                    new OracleParameter("ACTOR_EMPCD",  (object?)actorEmpCd ?? DBNull.Value),
                    new OracleParameter("DETAIL",       (object?)detail     ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GiftLogHelper] Log error: {ex.Message}");
            }
        });
    }
}
