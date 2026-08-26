namespace HR_web.Models.Directory;

public class WorkCdItemModel
{
    public string DeptCd { get; set; } = "";
    public string LineCd { get; set; } = "";
    public string WorkCd { get; set; } = "";
    public string? DeptName { get; set; }
    public string? LineName { get; set; }
    public string? WorkName { get; set; }
    public bool HasImage { get; set; }

    public string ImageFileName => $"{DeptCd}_{LineCd}_{WorkCd}.jpg";
}

public class WorkCdListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<WorkCdItemModel> Items { get; set; } = new();
}

public class DeptOptionModel
{
    public string? DeptCd { get; set; }
    public string? DeptName { get; set; }
}

public class LineOptionModel
{
    public string? LineCd { get; set; }
    public string? LineName { get; set; }
}
