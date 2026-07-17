namespace HR_web.Models.Shared;

// Model cho partial _AdminHeroBar.cshtml — gradient hero đỏ mận dùng chung cho các trang
// quản trị Inquiry/Notification (AdminInquiry, AdminNoti) thay vì mỗi trang tự khai báo CSS riêng.
public class AdminHeroBarVM
{
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SubtitleHtml { get; set; } // HTML thô, cho phép gắn id để JS cập nhật động
    public string? ActionHtml { get; set; } // HTML thô cho nút hành động bên phải (export, thêm mới...)
}
