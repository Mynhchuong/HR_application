using System;

namespace HR_api.Models.Account;

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
    public int IsActive { get; set; }
    public DateTime? LastedLogin { get; set; }
    public string? DeptCd { get; set; }
    public string? LineCd { get; set; }
    public string? SIGNATUREBLOB { get; set; }
}

public class CreateUserModel
{
    public string EmpCd { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string LoginUser { get; set; } = string.Empty;
}

public class UserDropdownModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? WorkCd { get; set; }
    public string? LineCd { get; set; }
    public string? DeptCd { get; set; }
}

public class LoginRequest
{
    public string EmpCd { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string EmpCd { get; set; } = string.Empty;
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class DisableUserRequest
{
    public string EmpCd { get; set; } = string.Empty;
    public string LoginUser { get; set; } = string.Empty;
}

public class UserDetailModel
{
    public string? DeptName { get; set; }
    public string? LineName { get; set; }
    public string? WorkName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime? BirthDate { get; set; }
    public string? Sex { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Phone { get; set; }
    public string? Seniority { get; set; }
    public string? HomeTown { get; set; }
    public string? ContractType { get; set; }
    public DateTime? ContractDate { get; set; }
}

public class UpdateSignatureRequest
{
    public string EmpCd { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;   // "Y" hoặc "N"
    public string? LoginUser { get; set; }
}

public class UserInfoPagedViewModel
{
    public List<UserInfoModel> data { get; set; } = new();
    public int total { get; set; }
    public int page { get; set; }
    public int pageSize { get; set; }
    public int totalPage { get; set; }
}
