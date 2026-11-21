using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Helpers;

/// <summary>
/// Tuỳ chọn "Hiện ảnh nhân viên ở các trang duyệt" (yêu cầu 2026-11-21): HRMS.HR_USERS.SHOW_EMP_PHOTO
/// (1 = hiện, 0 = tắt, NULL = chưa chọn → theo role: Expat hiện, còn lại tắt — tính ở HR_web UserInfoModel.EmpPhotoOn).
/// Đọc 1 lần lúc đăng nhập rồi nằm trong cookie phiên, các trang không phải hỏi DB.
/// An toàn khi DB chưa có cột (chưa chạy ALTER): Get trả null, Set trả false, trang Cài đặt ẩn công tắc.
/// Cùng khuôn với PushPrefHelper.
/// </summary>
public static class EmpPhotoPrefHelper
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
                   WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_USERS' AND COLUMN_NAME = 'SHOW_EMP_PHOTO'",
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

    /// <summary>true/false = NV đã tự chọn; null = chưa chọn (theo mặc định role) hoặc DB chưa có cột.</summary>
    public static async Task<bool?> GetAsync(OracleService db, string? empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return null;
        try
        {
            if (!await IsAvailableAsync(db)) return null;
            var rows = await db.ExecuteQueryAsync(
                "SELECT SHOW_EMP_PHOTO AS V FROM HRMS.HR_USERS WHERE EMPCD = :E AND ROWNUM = 1",
                r => r["V"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["V"]),
                new OracleParameter("E", empcd.Trim()));
            var v = rows.FirstOrDefault();
            return v.HasValue ? v.Value == 1 : null;
        }
        catch { return null; }   // lỗi đọc tuỳ chọn không được làm hỏng đăng nhập
    }

    /// <summary>Lưu lựa chọn. false nếu DB chưa có cột hoặc không có người dùng này.</summary>
    public static async Task<bool> SetAsync(OracleService db, string? empcd, bool show)
    {
        if (string.IsNullOrWhiteSpace(empcd) || !await IsAvailableAsync(db)) return false;
        var n = await db.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_USERS SET SHOW_EMP_PHOTO = :V WHERE EMPCD = :E",
            new OracleParameter("V", show ? 1 : 0),
            new OracleParameter("E", empcd.Trim()));
        return n > 0;
    }
}
