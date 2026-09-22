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
    public DateTime? LastPwdReset { get; set; }
    public string? DeptCd { get; set; }
    public string? LineCd { get; set; }
    public string? SIGNATUREBLOB { get; set; }
    // Được populate lúc login cho Supervisor/Manager/Assistant
    // FilterType: "work" (Supervisor) hoặc "dept" (Manager/Assistant)
    public string?       FilterType      { get; set; }
    public List<string>? FilterCodes     { get; set; }
    public List<string>? FilterLineCodes { get; set; }
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

public class ForgotPasswordRequest
{
    public string Empcd       { get; set; } = string.Empty;
    public string Juminno     { get; set; } = string.Empty;   // CCCD
    public string JuminnoDate { get; set; } = string.Empty;   // ngày cấp (yyyyMMdd hoặc dd/MM/yyyy)
    public string NewPassword { get; set; } = string.Empty;
}

public class UserDetailModel
{
    public string? DeptCd { get; set; }
    public string? LineCd { get; set; }
    public string? WorkCd { get; set; }
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
    public string? Address { get; set; }
    public string? Juminno { get; set; }        // CCCD
    public string? JuminnoDate { get; set; }    // Ngày cấp CCCD (YYYYMMDD)
    public DateTime? HireDate { get; set; }     // Ngày đầu tiên làm ở công ty (IGENTDAT)
    public string? HardworkStt { get; set; }    // ECM100.INTEREST - mã công việc (VD Y80)
    public string? HardworkTen { get; set; }    // EAM420.TEN tương ứng (VD QUÉT KEO)
    public List<DisciplineHistoryItem> DisciplineHistory { get; set; } = new();
    public List<LaborContractItem> LaborContracts { get; set; } = new();
}

// HRMS.EAM900 — lịch sử hợp đồng lao động (yêu cầu 2026-09-21). DEFINITE có 4 giá trị (T/F/L/G)
// nhưng chỉ lấy T=XD (có thời hạn) và F=KXD (không thời hạn) — L/G là phụ lục tăng lương
// (PLTL/PLGL), không phải hợp đồng, không hiển thị ở đây.
public class LaborContractItem
{
    public string  ContractType { get; set; } = "";   // XD | KXD
    public DateTime? StartDate  { get; set; }          // ST_DATE
    public DateTime? EndDate    { get; set; }          // ED_DATE — NULL với KXD (không thời hạn)
    public string? Remark       { get; set; }
}

// HRMS.DISCIPLINE_HISTORY — lịch sử lập biên bản kỷ luật NV (tự xem trong Hồ sơ cá nhân).
public class DisciplineHistoryItem
{
    public string  Num          { get; set; } = "";   // Số biên bản
    public string? Code         { get; set; }
    public DateTime? StartDate  { get; set; }          // Ngày hiệu lực từ
    public DateTime? EndDate    { get; set; }          // Ngày hiệu lực đến
    public string? Remark       { get; set; }          // Font giống họ tên (vni-font)
}

public class UpdateSignatureRequest
{
    public string EmpCd { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;   // "Y" hoặc "N"
    public string? LoginUser { get; set; }
}

public class UpdateRoleRequest
{
    public string EmpCd { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string LoginUser { get; set; } = string.Empty;
}

public class BulkUpdateRoleItem
{
    public string EmpCd { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string LoginUser { get; set; } = string.Empty;
}

public class BulkUpdateRoleResult
{
    public string EmpCd { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UserInfoPagedViewModel
{
    public List<UserInfoModel> data { get; set; } = new();
    public int total { get; set; }
    public int page { get; set; }
    public int pageSize { get; set; }
    public int totalPage { get; set; }
}
