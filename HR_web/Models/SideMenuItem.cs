namespace HR_web.Models;

public class SideMenuItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string? Url { get; set; }
    public List<SideMenuItem> Children { get; set; } = new();
    public Func<bool>? VisibleWhen { get; set; }

    // Cụm màu trong 1 section menu (VD "leave" = xanh lá, "chat" = vàng) — HR yêu cầu 2026-09-10 vì
    // menu HR quá dài, muốn nhóm các chức năng liên quan lại gần nhau + tô màu để lướt mắt tìm cho lẹ.
    // Chỉ set khi cần nhóm; item không set Group vẫn hiện bình thường (không nhóm/không đổi màu).
    public string? Group { get; set; }

    // ID phần tử badge số tin nhắn chưa xem hiện cạnh tên menu (VD "Hộp thư phản ánh" — công nhân hay
    // không để ý có tin mới, yêu cầu 2026-09-16). Chỉ set khi cần; JS ở _Layout.cshtml tự đổ số vào
    // đúng id này (badge ẩn mặc định, .style.display='' + textContent khi có tin chưa đọc).
    public string? BadgeId { get; set; }
}

// Bảng màu + tên nhóm dùng chung cho mọi section — thêm nhóm mới thì khai báo thêm 1 dòng ở đây,
// rồi gán Group = "key" cho các item liên quan trong SideMenuBuilder.
// LabelEn: menu Expat hiển thị toàn bộ tiêu đề bằng tiếng Anh (NV Hàn Quốc/nước ngoài) — nhãn cụm màu
// phải theo tiếng Anh ở section đó, không dùng chung nhãn Việt (bug phát hiện 2026-11-06).
public static class MenuGroupCatalog
{
    public static readonly Dictionary<string, (string Label, string Color, string LabelEn)> Styles = new()
    {
        ["leave"]      = ("Nghỉ phép",            "#10b981", "Leave"),              // xanh lá
        ["attendance"] = ("Tăng ca / Ra vào cổng", "#f97316", "Overtime / Gate Pass"), // cam
        ["gift"]       = ("Quà",                   "#db2777", "Gifts"),             // hồng
        ["canteen"]    = ("Ăn uống",               "#0ea5e9", "Canteen"),           // xanh dương
        ["training"]   = ("Đào tạo",               "#8b5cf6", "Training"),         // tím
        ["chat"]       = ("Hội thoại",             "#eab308", "Chat"),             // vàng
        ["system"]     = ("Tài khoản & Hệ thống",  "#64748b", "Account & System"), // xám
    };
}
