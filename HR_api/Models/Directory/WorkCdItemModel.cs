namespace HR_api.Models.Directory;

public class WorkCdItemModel
{
    public string InterestCd { get; set; } = "";   // ECM100.INTEREST / EAM420.STT (VD Y80)
    public string? InterestName { get; set; }      // EAM420.TEN (VD QUÉT KEO)
}
