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

    public enum LogAction { INS, UPD, DEL, SUPP_REQ, PLAN_CHG }

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

    // Kế hoạch OT (giờ/số giờ tăng ca) bị đổi bên ERP SAU KHI NV đã ký — HR nhận xét thực tế hay xảy
    // ra (2026-09-30) mà trước giờ không có dấu vết nào trong "Lịch sử đổi ý", vì đây không phải NV
    // tự đổi ý (không có ACTION nào ghi lại). Phát hiện ngay lúc ai đó MỞ trang xem (GetOTToday /
    // GetOTHRDetail chế độ admin) — không cần job chạy nền riêng. Dedup: chỉ ghi 1 lần cho đúng 1 cặp
    // (REQUEST_ID, OLD_HOURS, NEW_HOURS) — tránh spam log mỗi lần ai đó mở lại cùng trang.
    public void LogPlanChanged(string requestId, string empcd, DateTime workDate, decimal? oldHours, decimal? newHours)
    {
        if (string.IsNullOrEmpty(requestId)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var existing = await _oracleService.ExecuteQueryAsync(
                    @"SELECT 1 FROM HRMS.HR_OT_LOG
                      WHERE REQUEST_ID = :R AND ACTION = 'PLAN_CHG'
                        AND NVL(OLD_HOURS,-1) = NVL(:OH,-1) AND NVL(NEW_HOURS,-1) = NVL(:NH,-1)
                        AND ROWNUM = 1",
                    r => 1,
                    new OracleParameter("R", requestId),
                    new OracleParameter("OH", (object?)oldHours ?? DBNull.Value),
                    new OracleParameter("NH", (object?)newHours ?? DBNull.Value));
                if (existing.Count > 0) return; // đã ghi cho đúng cặp giờ này rồi, khỏi ghi trùng

                const string sql = @"
                    INSERT INTO HRMS.HR_OT_LOG
                        (REQUEST_ID, EMPCD, WORK_DATE, ACTION,
                         OLD_STATUS, NEW_STATUS, OLD_HOURS, NEW_HOURS,
                         ACTOR_EMPCD, INST_DT)
                    VALUES
                        (:REQUEST_ID, :EMPCD, :WORK_DATE, 'PLAN_CHG',
                         NULL, NULL, :OLD_HOURS, :NEW_HOURS,
                         NULL, SYSDATE)";

                await _oracleService.ExecuteNonQueryAsync(sql,
                    new OracleParameter("REQUEST_ID", requestId),
                    new OracleParameter("EMPCD",      empcd),
                    new OracleParameter("WORK_DATE",  workDate),
                    new OracleParameter("OLD_HOURS",  (object?)oldHours ?? DBNull.Value),
                    new OracleParameter("NEW_HOURS",  (object?)newHours ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OtLogHelper] LogPlanChanged error: {ex.Message}");
            }
        });
    }
}
