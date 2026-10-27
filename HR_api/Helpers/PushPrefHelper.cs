using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Helpers;

/// <summary>
/// Tùy chọn "Tắt nhận thông báo đẩy" của từng nhân viên: HRMS.HR_USERS.PUSH_MUTED (1 = tắt). Tắt chỉ chặn việc ĐẨY
/// thông báo lên điện thoại — thông báo vẫn ghi vào danh sách trong app. Thông báo ưu tiên CAO (PRIORITY = 'HIGH',
/// admin chọn lúc soạn) vẫn đẩy bình thường, kể cả khi đã tắt (yêu cầu 2026-10-28).
/// An toàn khi DB chưa có cột (chưa chạy ALTER): mọi hàm tự coi như "không ai tắt", không làm hỏng việc gửi push.
/// </summary>
public static class PushPrefHelper
{
    private static volatile bool _available;
    private static DateTime _nextProbeUtc = DateTime.MinValue;
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(60);
    private static readonly object ProbeLock = new();

    public static async Task<bool> IsAvailableAsync(OracleService db)
    {
        if (_available) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;

        bool ok;
        try
        {
            var rows = await db.ExecuteQueryAsync(
                @"SELECT COUNT(*) AS C FROM ALL_TAB_COLUMNS
                   WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_USERS' AND COLUMN_NAME = 'PUSH_MUTED'",
                r => Convert.ToInt32(r["C"]));
            ok = rows.FirstOrDefault() > 0;
        }
        catch { ok = false; }

        lock (ProbeLock)
        {
            _available = ok;
            _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow + ProbeEvery;
        }
        return ok;
    }

    /// <summary>Câu SELECT trả EMPCD những người đã tắt nhận push (để nhúng vào NOT IN); null nếu DB chưa hỗ trợ.</summary>
    public static async Task<string?> MutedEmpcdSqlAsync(OracleService db)
        => await IsAvailableAsync(db) ? "SELECT EMPCD FROM HRMS.HR_USERS WHERE PUSH_MUTED = 1" : null;

    public static async Task<bool> IsMutedAsync(OracleService db, string? empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd) || !await IsAvailableAsync(db)) return false;
        var rows = await db.ExecuteQueryAsync(
            "SELECT NVL(PUSH_MUTED, 0) AS M FROM HRMS.HR_USERS WHERE EMPCD = :E AND ROWNUM = 1",
            r => Convert.ToInt32(r["M"]) == 1,
            new OracleParameter("E", empcd.Trim()));
        return rows.FirstOrDefault();
    }

    /// <summary>Đặt tắt/bật. false nếu DB chưa có cột hoặc không có người dùng này.</summary>
    public static async Task<bool> SetMutedAsync(OracleService db, string? empcd, bool muted)
    {
        if (string.IsNullOrWhiteSpace(empcd) || !await IsAvailableAsync(db)) return false;
        var n = await db.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_USERS SET PUSH_MUTED = :M WHERE EMPCD = :E",
            new OracleParameter("M", muted ? 1 : 0),
            new OracleParameter("E", empcd.Trim()));
        return n > 0;
    }
}
