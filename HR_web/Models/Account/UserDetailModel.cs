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
    public string? Seniority { get; set; }
    public string? HomeTown { get; set; }
    public string? ContractType { get; set; }
    public DateTime? ContractDate { get; set; }
    public string? Address { get; set; }
    public string? Juminno { get; set; }        // CCCD
    public string? JuminnoDate { get; set; }    // Ngày cấp CCCD (YYYYMMDD)
    public DateTime? HireDate { get; set; }     // Ngày đầu tiên làm ở công ty (IGENTDAT)
    public bool HasImage { get; set; }
    public string? ImageUrl { get; set; }
    public bool HasSignature { get; set; }
    public string? SignatureUrl { get; set; }
    public string? EmpCd { get; set; }
}

// CreateUserModel has been moved to UserActionModels.cs

