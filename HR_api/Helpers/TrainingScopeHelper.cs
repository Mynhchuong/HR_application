using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Helpers;

/// <summary>
/// PHÂN QUYỀN THEO ROLE TẠO cho Quản lý Đào tạo (yêu cầu 2026-10-27): mỗi KHÓA HỌC có cột HR_TRAINING_COURSE.CREATED_ROLE
/// ('HR' | 'CSR' | 'Lean'). Lean chỉ thấy khóa học/lớp học của Lean; CSR chỉ thấy của CSR; HR/Admin thấy tất cả.
/// LỚP HỌC không có cột riêng: lớp theo role của khóa học mà nó thuộc.
/// Script DDL: HR_api/Scripts/alter_training_course_created_role.sql. An toàn khi DB CHƯA có cột (chưa chạy script):
/// mọi hàm tự rơi về "không lọc" như trước, không làm hỏng trang Quản lý Đào tạo.
/// </summary>
public static class TrainingScopeHelper
{
    public const string RoleHr = "HR", RoleCsr = "CSR", RoleLean = "Lean";
    public static readonly string[] AllRoles = { RoleHr, RoleCsr, RoleLean };

    private static volatile bool _available;
    private static DateTime _nextProbeUtc = DateTime.MinValue;
    private static readonly object ProbeLock = new();

    /// <summary>DB đã có cột CREATED_ROLE chưa (dò lại mỗi 60 giây để chạy script xong không cần khởi động lại).</summary>
    public static async Task<bool> IsAvailableAsync(OracleService db)
    {
        if (_available) return true;
        if (DateTime.UtcNow < _nextProbeUtc) return false;
        bool ok;
        try
        {
            var rows = await db.ExecuteQueryAsync(
                @"SELECT COUNT(*) AS C FROM ALL_TAB_COLUMNS
                   WHERE OWNER = 'HRMS' AND TABLE_NAME = 'HR_TRAINING_COURSE' AND COLUMN_NAME = 'CREATED_ROLE'",
                r => Convert.ToInt32(r["C"]));
            ok = rows.FirstOrDefault() > 0;
        }
        catch { ok = false; }
        lock (ProbeLock) { _available = ok; _nextProbeUtc = ok ? DateTime.MaxValue : DateTime.UtcNow + TimeSpan.FromSeconds(60); }
        return ok;
    }

    /// <summary>Role tạo của người dùng: HR/Admin -> HR, dept Lean -> Lean, role CSR -> CSR, còn lại -> HR.</summary>
    public static async Task<string> GetCreatorRoleAsync(OracleService db, string? empcd)
    {
        var roles = await DeptRoleHelper.GetRoleNamesAsync(db, empcd);
        if (roles.Contains("Admin") || roles.Contains("HR")) return RoleHr;
        if (roles.Contains("Lean")) return RoleLean;
        if (roles.Contains("CSR")) return RoleCsr;
        return RoleHr;
    }

    /// <summary>Các role tạo mà người này ĐƯỢC THẤY. null = thấy tất cả (HR/Admin). Mảng rỗng = không thấy gì.</summary>
    public static async Task<string[]?> GetViewerRolesAsync(OracleService db, string? empcd)
    {
        var roles = await DeptRoleHelper.GetRoleNamesAsync(db, empcd);
        if (roles.Contains("Admin") || roles.Contains("HR")) return null;
        var list = new List<string>();
        if (roles.Contains("CSR")) list.Add(RoleCsr);
        if (roles.Contains("Lean")) list.Add(RoleLean);
        return list.ToArray();
    }

    /// <summary>Bản đồ CourseId -> CREATED_ROLE (null/rỗng trong DB coi là HR). Rỗng nếu DB chưa có cột.</summary>
    public static async Task<Dictionary<int, string>> GetCourseRolesAsync(OracleService db)
    {
        var map = new Dictionary<int, string>();
        if (!await IsAvailableAsync(db)) return map;
        var rows = await db.ExecuteQueryAsync(
            "SELECT ID, NVL(CREATED_ROLE, 'HR') AS CREATED_ROLE FROM HRMS.HR_TRAINING_COURSE",
            r => (Id: Convert.ToInt32(r["ID"]), Role: r["CREATED_ROLE"]?.ToString() ?? RoleHr));
        foreach (var x in rows) map[x.Id] = x.Role;
        return map;
    }

    public static async Task<bool> CanAccessCourseAsync(OracleService db, string? empcd, int courseId)
    {
        if (!await IsAvailableAsync(db)) return true;
        var viewer = await GetViewerRolesAsync(db, empcd);
        if (viewer == null) return true;
        var rows = await db.ExecuteQueryAsync(
            "SELECT NVL(CREATED_ROLE, 'HR') AS CREATED_ROLE FROM HRMS.HR_TRAINING_COURSE WHERE ID = :ID",
            r => r["CREATED_ROLE"]?.ToString() ?? RoleHr, new OracleParameter("ID", courseId));
        // Không có khóa học (id sai) -> để tầng dưới tự báo "không tìm thấy", không chặn ở đây.
        return rows.Count == 0 || viewer.Contains(rows[0]);
    }

    public static async Task<bool> CanAccessClassAsync(OracleService db, string? empcd, int classId)
    {
        if (!await IsAvailableAsync(db)) return true;
        var viewer = await GetViewerRolesAsync(db, empcd);
        if (viewer == null) return true;
        var rows = await db.ExecuteQueryAsync(
            @"SELECT NVL(CO.CREATED_ROLE, 'HR') AS CREATED_ROLE FROM HRMS.HR_TRAINING_CLASS CL
                JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID WHERE CL.ID = :ID",
            r => r["CREATED_ROLE"]?.ToString() ?? RoleHr, new OracleParameter("ID", classId));
        return rows.Count == 0 || viewer.Contains(rows[0]);
    }

    /// <summary>
    /// Quyền với 1 đối tượng con tra theo ID (buổi học, nhóm, bài kiểm tra, tài liệu): suy ra khóa học chứa nó
    /// rồi xét role tạo như CanAccessCourseAsync. kind: SESSION | GROUP | TEST | MATERIAL. Không tìm thấy -> true
    /// (tầng dưới tự báo "không tìm thấy").
    /// </summary>
    public static async Task<bool> CanAccessEntityAsync(OracleService db, string? empcd, string kind, int id)
    {
        if (!await IsAvailableAsync(db)) return true;
        var viewer = await GetViewerRolesAsync(db, empcd);
        if (viewer == null) return true;

        string sql = kind switch
        {
            "SESSION" => @"SELECT NVL(CO.CREATED_ROLE,'HR') AS R FROM HRMS.HR_TRAINING_SESSION E
                             JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = E.CLASS_ID
                             JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID WHERE E.ID = :ID",
            "GROUP"   => @"SELECT NVL(CO.CREATED_ROLE,'HR') AS R FROM HRMS.HR_TRAINING_CLASS_GROUP E
                             JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = E.CLASS_ID
                             JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID WHERE E.ID = :ID",
            "TEST"    => @"SELECT NVL(CO.CREATED_ROLE,'HR') AS R FROM HRMS.HR_TRAINING_TEST E
                             JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = E.CLASS_ID
                             JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID WHERE E.ID = :ID",
            "MATERIAL" => @"SELECT NVL(CO.CREATED_ROLE,'HR') AS R FROM HRMS.HR_TRAINING_MATERIAL E
                             LEFT JOIN HRMS.HR_TRAINING_SESSION S ON S.ID = E.SESSION_ID
                             LEFT JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = NVL(E.CLASS_ID, S.CLASS_ID)
                             JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = NVL(E.COURSE_ID, CL.COURSE_ID) WHERE E.ID = :ID",
            _ => throw new ArgumentException("kind không hợp lệ: " + kind)
        };
        var rows = await db.ExecuteQueryAsync(sql, r => r["R"]?.ToString() ?? RoleHr, new OracleParameter("ID", id));
        return rows.Count == 0 || viewer.Contains(rows[0]);
    }

    /// <summary>Lọc danh sách khóa học/lớp theo người xem + ô lọc role (chỉ HR/Admin mới dùng được ô lọc).</summary>
    public static (bool visible, string role) Evaluate(string[]? viewerRoles, string? roleFilter, string courseRole)
    {
        if (viewerRoles != null && !viewerRoles.Contains(courseRole)) return (false, courseRole);
        if (viewerRoles == null && !string.IsNullOrWhiteSpace(roleFilter) && !string.Equals(roleFilter, courseRole, StringComparison.OrdinalIgnoreCase)) return (false, courseRole);
        return (true, courseRole);
    }
}
