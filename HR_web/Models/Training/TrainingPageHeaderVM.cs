namespace HR_web.Models.Training;

// Model cho partial _TrainingPageHeader.cshtml — header dạng "card-header kéo lên" chuẩn Material
// Dashboard, dùng cho mọi trang nội dung/danh sách của Training (không phải trang cổng module).
public class TrainingPageHeaderVM
{
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "bi-journal-text";
    public string Gradient { get; set; } = "primary"; // primary|success|warning|info|dark|danger
    public bool ShowBackButton { get; set; } = true;
    public string? BackUrl { get; set; } // null => history.back()
    public string? ExtraActionsHtml { get; set; } // HTML thô cho control phụ (filter, export...) bên phải tiêu đề
}
