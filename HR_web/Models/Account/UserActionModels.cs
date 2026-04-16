namespace HR_web.Models.Account;

/// <summary>
/// Model hiển thị trong bảng danh sách User (UserManager).
/// Mở rộng từ UserInfoModel với thêm DisplayName để hiện thị.
/// </summary>
public class UserInfoTableModel
{
    public string  EmpCd    { get; set; } = string.Empty;
    public string? FullName  { get; set; }
    public string? DeptCd    { get; set; }
    public string? LineCd    { get; set; }
    public string? WorkCd    { get; set; }
    public int?    RoleId    { get; set; }
    public string? RoleName  { get; set; }
    public int     IsActive  { get; set; }
}

/// <summary>
/// Model tạo user mới (form trong modal UserManager).
/// </summary>
public class CreateUserModel
{
    public string  EmpCd     { get; set; } = string.Empty;
    public string? FullName   { get; set; }
    public string  Password  { get; set; } = "123456";
    public int?    RoleId    { get; set; }
    /// <summary>Người tạo - set từ CurrentUser trong controller.</summary>
    public string? LoginUser { get; set; }
}
