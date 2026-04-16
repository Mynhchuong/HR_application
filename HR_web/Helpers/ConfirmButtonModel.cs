namespace HR_web.Helpers;

/// <summary>
/// Model dùng cho partial view _ConfirmButton.
/// Không thay đổi logic so với phiên bản cũ - chỉ cập nhật namespace.
/// </summary>
public class ConfirmButtonModel
{
    public string Url { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ButtonClass { get; set; } = "btn btn-sm btn-primary";
    public string ConfirmMessage { get; set; } = "Bạn có chắc chắn?";
}
