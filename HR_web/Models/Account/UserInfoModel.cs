using System.ComponentModel.DataAnnotations;

namespace HR_web.Models.Account;

public class UserInfoModel
{
    public int Id { get; set; }
    public string EmpCd { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PersonalEmail { get; set; }
    public string? WorkEmail { get; set; }
    public string? MobilePhone { get; set; }
    public string? WorkCd { get; set; }
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }
    /// <summary>Role phòng ban (HR/CSR/...) — tách khỏi role chính (vai trò). Xem HR_api/Scripts/create_dept_role.sql.
    /// null = không có. Trưởng bộ phận nhân sự: RoleName = Manager, DeptRoleName = HR.</summary>
    public string? DeptRoleName { get; set; }
    /// <summary>true = phòng ban bị chặn khỏi "Báo máy sản xuất" (TPM) — HR_api tính lúc đăng nhập (Tpm:BlockedDeptCodes, mặc định OFFICE STAFF).</summary>
    public bool TpmBlocked { get; set; }
    public int IsActive { get; set; }
    public DateTime? LastedLogin { get; set; }
    public DateTime? LastPwdReset { get; set; }
    public string? DeptCd { get; set; }
    public string? LineCd { get; set; }
    public string? SIGNATUREBLOB { get; set; }
    public bool RequirePasswordChange { get; set; }
    // Populate 1 lần lúc login cho Supervisor/Manager/Assistant
    // FilterType: "work" (Supervisor) | "dept" (Manager/Assistant)
    public string?       FilterType      { get; set; }
    public List<string>? FilterCodes     { get; set; }
    public List<string>? FilterLineCodes { get; set; }
}

/// <summary>
/// Kiểm tra role theo CẢ role chính (RoleName) LẪN role phòng ban (DeptRoleName). Mọi chỗ hỏi "người này có phải
/// HR/CSR/Admin..." phải dùng các hàm này thay vì so sánh RoleName trực tiếp — nếu không, người có role chính
/// là Manager/Supervisor/Worker nhưng role dept là HR sẽ bị coi như không phải HR.
/// Null-safe: gọi được trên CurrentUser khi chưa đăng nhập (trả false).
/// </summary>
public static class UserRoleExtensions
{
    public static bool HasRole(this UserInfoModel? user, string role) =>
        user != null && (string.Equals(user.RoleName, role, StringComparison.Ordinal)
                      || string.Equals(user.DeptRoleName, role, StringComparison.Ordinal));

    public static bool HasAnyRole(this UserInfoModel? user, params string[] roles)
    {
        if (user == null) return false;
        foreach (var r in roles) if (user.HasRole(r)) return true;
        return false;
    }
}
