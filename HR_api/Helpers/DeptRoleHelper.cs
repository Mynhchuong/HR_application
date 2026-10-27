using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Helpers;

/// <summary>
/// ROLE DEPT (role phòng ban): HRMS.HR_USERS.DEPT_ROLE_ID -> HRMS.HR_DEPT_ROLES (HR, CSR, ...).
/// Tách "vai trò" (HR_USERS.ROLE_ID: Worker/Clerk/Supervisor/Manager/...) khỏi "phòng ban" (HR, CSR...):
/// trưởng bộ phận nhân sự = role chính Manager + role dept HR -> thấy cả menu Quản lý lẫn Nhân sự.
/// Script DDL: HR_api/Scripts/create_dept_role.sql.
///
/// Mọi chỗ hỏi "người này có phải HR/CSR/Admin không" phải đi qua helper này (role chính HOẶC role dept),
/// không tự tra HR_ROLES.ROLE_NAME. An toàn khi DB chưa có cột/bảng (chưa chạy script): mọi hàm tự rơi về
/// chỉ xét role chính, KHÔNG làm hỏng đăng nhập hay các chức năng khác.
/// </summary>
public static class DeptRoleHelper
{
    private static volatile bool _available;
    private static DateTime _nextProbeUtc = DateTime.MinValue;
    private static readonly TimeSpan ProbeEvery = TimeSpan.FromSeconds(60);
    private static readonly object ProbeLock = new();

    /// <summary>DB đã có cột HR_USERS.DEPT_ROLE_ID và bảng HR_DEPT_ROLES chưa. Kiểm tra lại mỗi 60 giây
    /// (để sau khi chạy script không cần khởi động lại HR_api).</summary>
    public static async Task<bool> IsAvailableAsync(OracleService db)
    {
        if (_available) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;

        bool ok;
        try
        {
            var rows = await db.ExecuteQueryAsync(
                @"SELECT COUNT(*) AS C FROM ALL_TAB_COLUMNS
                   WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_USERS' AND COLUMN_NAME = 'DEPT_ROLE_ID'",
                r => Convert.ToInt32(r["C"]));
            var hasCol = rows.FirstOrDefault() > 0;
            ok = hasCol;
            if (ok)
            {
                var t = await db.ExecuteQueryAsync(
                    @"SELECT COUNT(*) AS C FROM ALL_TABLES WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_DEPT_ROLES'",
                    r => Convert.ToInt32(r["C"]));
                ok = t.FirstOrDefault() > 0;
            }
        }
        catch { ok = false; }

        lock (ProbeLock)
        {
            _available = ok;
            _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow + ProbeEvery;
        }
        return ok;
    }

    /// <summary>Tên role dept của NV ("HR", "CSR"...), null nếu không có hoặc DB chưa hỗ trợ.</summary>
    public static async Task<string?> GetDeptRoleNameAsync(OracleService db, string? empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return null;
        if (!await IsAvailableAsync(db)) return null;
        try
        {
            var rows = await db.ExecuteQueryAsync(
                @"SELECT DR.ROLE_NAME FROM HRMS.HR_USERS U
                   JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = U.DEPT_ROLE_ID
                  WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", empCd.Trim()));
            return rows.FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Tập tên role (chính + role dept) của NV bằng 1 truy vấn — dùng khi cần xét nhiều role cùng lúc.</summary>
    public static async Task<HashSet<string>> GetRoleNamesAsync(OracleService db, string? empCd)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(empCd)) return set;

        if (await IsAvailableAsync(db))
        {
            try
            {
                var rows = await db.ExecuteQueryAsync(
                    @"SELECT R.ROLE_NAME AS MAIN_ROLE, DR.ROLE_NAME AS DEPT_ROLE FROM HRMS.HR_USERS U
                        LEFT JOIN HRMS.HR_ROLES R       ON R.ID  = U.ROLE_ID
                        LEFT JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = U.DEPT_ROLE_ID
                       WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                    r => (Main: r["MAIN_ROLE"]?.ToString(), Dept: r["DEPT_ROLE"]?.ToString()),
                    new OracleParameter("EMPCD", empCd.Trim()));
                foreach (var x in rows) { if (!string.IsNullOrEmpty(x.Main)) set.Add(x.Main); if (!string.IsNullOrEmpty(x.Dept)) set.Add(x.Dept); }
                return set;
            }
            catch { /* rơi xuống truy vấn chỉ role chính */ }
        }

        var main = await db.ExecuteQueryAsync(
            @"SELECT R.ROLE_NAME FROM HRMS.HR_USERS U JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
            r => r["ROLE_NAME"]?.ToString(), new OracleParameter("EMPCD", empCd.Trim()));
        foreach (var m in main) if (!string.IsNullOrEmpty(m)) set.Add(m);
        return set;
    }

    /// <summary>True nếu role chính HOẶC role dept của NV nằm trong danh sách <paramref name="roles"/>.</summary>
    public static async Task<bool> HasAnyRoleAsync(OracleService db, string? empCd, params string[] roles)
    {
        if (string.IsNullOrWhiteSpace(empCd) || roles == null || roles.Length == 0) return false;
        var emp = empCd.Trim();

        // Mỗi truy vấn tạo bộ OracleParameter MỚI (một OracleParameter không được gắn vào 2 lệnh khác nhau).
        OracleParameter[] Params(string prefix) =>
            new[] { new OracleParameter("EMPCD", emp) }
                .Concat(roles.Select((r, i) => new OracleParameter(prefix + i, r))).ToArray();
        string InList(string prefix) => string.Join(",", roles.Select((_, i) => ":" + prefix + i));

        if (await IsAvailableAsync(db))
        {
            try
            {
                var rows = await db.ExecuteQueryAsync($@"
                    SELECT 1 FROM HRMS.HR_USERS U
                      LEFT JOIN HRMS.HR_ROLES R       ON R.ID  = U.ROLE_ID
                      LEFT JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = U.DEPT_ROLE_ID
                     WHERE U.EMPCD = :EMPCD
                       AND (R.ROLE_NAME IN ({InList("M")}) OR DR.ROLE_NAME IN ({InList("D")}))
                       AND ROWNUM = 1",
                    r => 1,
                    Params("M").Concat(roles.Select((r, i) => new OracleParameter("D" + i, r))).ToArray());
                return rows.Count > 0;
            }
            catch { /* rơi xuống truy vấn chỉ role chính */ }
        }

        var mainOnly = await db.ExecuteQueryAsync($@"
            SELECT 1 FROM HRMS.HR_USERS U
              JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID
             WHERE U.EMPCD = :EMPCD AND R.ROLE_NAME IN ({InList("M")}) AND ROWNUM = 1",
            r => 1, Params("M"));
        return mainOnly.Count > 0;
    }

    /// <summary>
    /// Câu SELECT trả về EMPCD của mọi người có role chính HOẶC role dept thuộc <paramref name="roles"/> —
    /// để nhúng vào IN / NOT IN. <paramref name="roles"/> phải là hằng số trong code (được nối thẳng vào SQL).
    /// </summary>
    public static async Task<string> EmpcdInRolesSqlAsync(OracleService db, params string[] roles)
    {
        var lit = string.Join(",", roles.Select(r => "'" + r.Replace("'", "''") + "'"));
        var sql = $@"SELECT U.EMPCD FROM HRMS.HR_USERS U JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID WHERE R.ROLE_NAME IN ({lit})";
        if (await IsAvailableAsync(db))
        {
            sql += $@" UNION SELECT U.EMPCD FROM HRMS.HR_USERS U JOIN HRMS.HR_DEPT_ROLES DR ON DR.ID = U.DEPT_ROLE_ID WHERE DR.ROLE_NAME IN ({lit})";
        }
        return sql;
    }
}
