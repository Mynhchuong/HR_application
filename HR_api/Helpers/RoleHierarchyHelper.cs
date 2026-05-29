namespace HR_api.Helpers;

/// <summary>
/// Phân cấp quyền duyệt trong hệ thống HR.
/// Worker/Clerk/HR(1) → Supervisor/Assistant(2) → Manager(3) → Expat(4) → Admin(99)
/// Quy tắc: người duyệt phải ở cấp CAO HƠN người gửi.
/// Admin duyệt được tất cả. Không ai được tự duyệt phiếu của mình.
/// </summary>
public static class RoleHierarchyHelper
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cấp bậc — thêm role mới vào đây, code còn lại tự động hoạt động đúng
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, int> _levels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Admin",      99 },
        { "Expat",       4 },   // Cấp cao nhất thường (duyệt Manager)
        { "Manager",     3 },   // Duyệt Supervisor trở xuống
        { "Supervisor",  2 },   // Duyệt Worker/Clerk/HR
        { "Assistant",   1 },   // Tương đương Worker, cần Supervisor duyệt
        { "HR",          1 },   // Như Worker, cần Supervisor duyệt
        { "Clerk",       1 },   // Như Worker, cần Supervisor duyệt
        { "Worker",      1 },   // Cấp cơ bản
        { "Employee",    1 },   // Alias của Worker
    };

    public static int GetLevel(string? roleName)
        => (!string.IsNullOrEmpty(roleName) && _levels.TryGetValue(roleName, out int lvl)) ? lvl : 1;

 
    public static bool CanApprove(string? approverRole, string? requesterRole)
        => GetLevel(approverRole) > GetLevel(requesterRole);

    public static string RequiredApproverName(string? requesterRole) => GetLevel(requesterRole) switch
    {
        >= 3 => "Expat / Admin",       // Manager trở lên → cần Expat/Admin
        2    => "Manager hoặc Admin",  // Supervisor → cần Manager
        _    => "Supervisor hoặc cấp trên", // Worker/Clerk/HR → cần Supervisor
    };

    public static bool HasApprovalPermission(string? roleName)
        => GetLevel(roleName) >= 2;
}
