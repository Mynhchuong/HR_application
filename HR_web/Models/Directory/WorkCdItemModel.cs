namespace HR_web.Models.Directory;

public class WorkCdItemModel
{
    public string InterestCd { get; set; } = "";   // ECM100.INTEREST / EAM420.STT (VD Y80)
    public string? InterestName { get; set; }      // EAM420.TEN (VD QUÉT KEO)
    public bool HasImage { get; set; }

    public string ImageFileName => $"{InterestCd}.jpg";
}

public class WorkCdListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<WorkCdItemModel> Items { get; set; } = new();
}
