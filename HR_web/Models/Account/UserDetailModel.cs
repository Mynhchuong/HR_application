namespace HR_web.Models.Account;

public class UserDetailModel
{
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
    public bool HasImage { get; set; }
    public string? ImageUrl { get; set; }
    public bool HasSignature { get; set; }
    public string? SignatureUrl { get; set; }
    public string? EmpCd { get; set; }
}

// CreateUserModel has been moved to UserActionModels.cs

