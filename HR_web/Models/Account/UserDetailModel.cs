namespace HR_web.Models.Account;

public class UserDetailModel
{
    public string? DeptCd { get; set; }
    public string? LineCd { get; set; }
    public string? WorkCd { get; set; }
    public string? DeptName { get; set; }
    public string? LineName { get; set; }
    public string? WorkName { get; set; }
    public string? FullName { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Sex { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }   // ECM100.EMAIL — yêu cầu HR 2026-10-08
    /// <summary>Thâm niên đã ghép sẵn tiếng Việt ("3 năm 2 tháng") — dùng cho giao diện tiếng Việt.</summary>
    public string? Seniority { get; set; }
    /// <summary>Số năm thâm niên (thuần số) — giao diện NV nước ngoài tự ghép câu Anh/Hàn.</summary>
    public int? SeniorityYears { get; set; }
    /// <summary>Số tháng lẻ thâm niên (0-11) — xem <see cref="SeniorityYears"/>.</summary>
    public int? SeniorityMonths { get; set; }
    public string? HomeTown { get; set; }
    public string? ContractType { get; set; }
    public DateTime? ContractDate { get; set; }
    public string? Address { get; set; }
    public string? Juminno { get; set; }        // CCCD
    public string? JuminnoDate { get; set; }    // Ngày cấp CCCD (YYYYMMDD)
    public DateTime? HireDate { get; set; }     // Ngày đầu tiên làm ở công ty (IGENTDAT)
    public string? HardworkStt { get; set; }    // ECM100.INTEREST - mã công việc (VD Y80)
    public string? HardworkTen { get; set; }    // EAM420.TEN tương ứng (VD QUÉT KEO)
    public bool HasImage { get; set; }
    public string? ImageUrl { get; set; }
    public bool HasSignature { get; set; }
    public string? SignatureUrl { get; set; }
    public string? EmpCd { get; set; }
    public List<DisciplineHistoryItem> DisciplineHistory { get; set; } = new();
    public List<LaborContractItem> LaborContracts { get; set; } = new();
}

// HRMS.DISCIPLINE_HISTORY — lịch sử lập biên bản kỷ luật NV (tự xem trong Hồ sơ cá nhân).
public class DisciplineHistoryItem
{
    public string  Num       { get; set; } = "";
    public string? Code      { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate   { get; set; }
    public string? Remark    { get; set; }   // Font giống họ tên (vni-font)
}

// HRMS.EAM900 — lịch sử hợp đồng lao động, chỉ lấy XD (có thời hạn) / KXD (không thời hạn).
public class LaborContractItem
{
    public string  ContractType { get; set; } = "";
    public DateTime? StartDate  { get; set; }
    public DateTime? EndDate    { get; set; }
    public string? Remark       { get; set; }
}

// CreateUserModel has been moved to UserActionModels.cs

