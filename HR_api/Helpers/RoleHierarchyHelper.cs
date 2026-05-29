namespace HR_api.Helpers;

/// <summary>
/// Phân cấp quyền duyệt trong hệ thống HR.
/// Worker/Clerk/HR(1) → Supervisor(2) → DeputyManager(3) → Manager(4) → Expat(5) → Admin(99)
/// Quy tắc: người duyệt phải ở cấp CAO HƠN người gửi.
/// Admin duyệt được tất cả. Không ai được tự duyệt phiếu của mình.
/// </summary>
public static class RoleHierarchyHelper
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cấp bậc — thêm role mới vào đây, code còn lại tự động hoạt động đúng dm ai ai ai 
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, int> _levels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Admin",         99 },
        { "Expat",          5 },   // Duyệt Manager trở xuống          (phạm vi HR_USERS_DEPT)
        { "Manager",        4 },   // Duyệt DeputyManager trở xuống    (phạm vi HR_USERS_DEPT)
        { "DeputyManager",  3 },   // Duyệt Supervisor trở xuống       (phạm vi HR_USERS_DEPT)
        { "Supervisor",     2 },   // Duyệt Worker/Clerk/HR             (phạm vi HR_USERS_DEPT)
        { "HR",             1 },   // Cần Supervisor duyệt
        { "Clerk",          1 },   // Cần Supervisor duyệt
        { "Worker",         1 },   // Cấp cơ bản
    };

    public static int GetLevel(string? roleName)
        => (!string.IsNullOrEmpty(roleName) && _levels.TryGetValue(roleName, out int lvl)) ? lvl : 1;

 
    public static bool CanApprove(string? approverRole, string? requesterRole)
        => GetLevel(approverRole) > GetLevel(requesterRole);

    public static string RequiredApproverName(string? requesterRole) => GetLevel(requesterRole) switch
    {
        >= 5 => "Admin",                                   // Expat → chỉ Admin
        4    => "Expat / Admin",                           // Manager → cần Expat/Admin
        3    => "Manager hoặc Expat",                     // DeputyManager → cần Manager+
        2    => "DeputyManager / Manager hoặc cấp trên",  // Supervisor → cần DeputyManager/Manager
        _    => "Supervisor hoặc cấp trên",                // Worker/Clerk/HR → cần Supervisor
    };

    public static bool HasApprovalPermission(string? roleName)
        => GetLevel(roleName) >= 2;
}
