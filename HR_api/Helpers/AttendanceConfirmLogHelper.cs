using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Helpers;

// Ghi log mọi lần đổi CONFIRM_STATUS của Attendance Confirm — mirror OtLogHelper.
// Fire-and-forget để không chặn request chính. Actor bất kể thao tác từ web hay app Mysamho
// (Mysamho chỉ là WebView bọc HR_web, không có luồng ghi riêng).
public class AttendanceConfirmLogHelper
{
    private readonly OracleService _oracleService;

    public AttendanceConfirmLogHelper(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    public enum LogAction { REQUEST_SENT, WORKER_SUBMIT, MANAGER_CONFIRM, MANAGER_REJECT }

    public void Log(
        LogAction action,
        int confirmId,
        string empcd,
        DateTime workDate,
        string? oldStatus,
        string? newStatus,
        string? detail,
        string? actorEmpCd)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                const string sql = @"
                    INSERT INTO HRMS.HR_ATT_CONFIRM_LOG
                        (CONFIRM_ID, EMPCD, WORK_DATE, ACTION, OLD_STATUS, NEW_STATUS, DETAIL, ACTOR_EMPCD, INST_DT)
                    VALUES
                        (:CONFIRM_ID, :EMPCD, :WORK_DATE, :ACTION, :OLD_STATUS, :NEW_STATUS, :DETAIL, :ACTOR_EMPCD, SYSDATE)";

                await _oracleService.ExecuteNonQueryAsync(sql,
                    new OracleParameter("CONFIRM_ID", confirmId),
                    new OracleParameter("EMPCD",      empcd),
                    new OracleParameter("WORK_DATE",  workDate),
                    new OracleParameter("ACTION",     action.ToString()),
                    new OracleParameter("OLD_STATUS", (object?)oldStatus ?? DBNull.Value),
                    new OracleParameter("NEW_STATUS", (object?)newStatus ?? DBNull.Value),
                    new OracleParameter("DETAIL",     (object?)detail    ?? DBNull.Value),
                    new OracleParameter("ACTOR_EMPCD",(object?)actorEmpCd ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AttendanceConfirmLogHelper] Log error: {ex.Message}");
            }
        });
    }
}
