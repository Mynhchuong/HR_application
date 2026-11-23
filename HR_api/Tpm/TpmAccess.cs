namespace HR_api.Tpm;

/// <summary>
/// Quy tắc "phòng ban nào KHÔNG dùng được Báo máy sản xuất (TPM)": cấu hình Tpm:BlockedDeptCodes trong appsettings
/// (mặc định A01001 = OFFICE STAFF, khối văn phòng — yêu cầu 2026-10-27). Là NƠI DUY NHẤT quyết định: HR_api trả cờ
/// UserInfoModel.TpmBlocked lúc đăng nhập để HR_web ẩn menu, và TpmService vẫn chặn ở phía server.
/// </summary>
internal static class TpmAccess
{
    private static readonly string[] DefaultBlocked = { "A01001" };

    /// <summary>Thẻ có trong danh sách công nhân bảo trì (bảng HRMS.HR_TPM_MAINT, xem Scripts/create_tpm_maint.sql) không.
    /// Bảng chưa tạo / lỗi DB -> false (không làm hỏng đăng nhập).</summary>
    public static async Task<bool> IsMaintenanceAsync(HR_api.Data.OracleService db, string? empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return false;
        try
        {
            var rows = await db.ExecuteQueryAsync(
                "SELECT 1 AS X FROM HRMS.HR_TPM_MAINT WHERE EMP_CD = :E AND ROWNUM = 1",
                r => 1, new Oracle.ManagedDataAccess.Client.OracleParameter("E", empCd.Trim()));
            return rows.Count > 0;
        }
        catch { return false; }
    }

    public static bool IsDeptBlocked(IConfiguration cfg, string? deptCd)
    {
        if (string.IsNullOrWhiteSpace(deptCd)) return false;
        var configured = cfg.GetSection("Tpm:BlockedDeptCodes").Get<string[]>();
        var list = configured is { Length: > 0 } ? configured : DefaultBlocked;
        return list.Any(c => string.Equals(c?.Trim(), deptCd.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
