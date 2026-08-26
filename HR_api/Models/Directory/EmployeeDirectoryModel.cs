namespace HR_api.Models.Directory;

public class EmployeeDirectoryModel
{
    public string? EmpCd { get; set; }
    public string? CName { get; set; }
    public string? JikwiCd { get; set; }
    public string? PositionNameEn { get; set; }   // E.ENGFNM (SAD100 E103 - chức danh)
    public DateTime? BirthDate { get; set; }
    public string? Juminno { get; set; }           // CCCD - dữ liệu nhạy cảm
    public string? SexGb { get; set; }
    public DateTime? HireDate { get; set; }        // IGENTDAT
    public string? DirectYn { get; set; }

    public string? Addr1 { get; set; }
    public string? Addr2 { get; set; }
    public string? Addr3 { get; set; }

    public string? DeptCd { get; set; }
    public string? LineCd { get; set; }
    public string? WorkCd { get; set; }
    public string? WorkCdCode { get; set; }
    public string? DeptName { get; set; }
    public string? LineName { get; set; }
    public string? WorkName { get; set; }
    public string? WorkCdNameEn { get; set; }      // C.ENGFNM AS WORKCD_NAME

    public string? ShiftType { get; set; }

    public string? NewDeptCd { get; set; }
    public string? NewLineCd { get; set; }
    public string? NewWorkCd { get; set; }
    public string? NewDeptName { get; set; }
    public string? NewLineName { get; set; }
    public string? NewWorkName { get; set; }
}
