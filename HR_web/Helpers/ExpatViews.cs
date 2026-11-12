namespace HR_web.Helpers;

/// <summary>
/// Đường dẫn các view riêng của NV nước ngoài (role Expat) — gom 1 chỗ thay vì rải chuỗi
/// "~/Views/Expat/..." khắp controller (gõ sai chỉ lỗi lúc chạy, không bắt được lúc build).
///
/// Các view này nằm ngoài quy ước Views/{Controller}/{Action} nên BẮT BUỘC truyền đường dẫn đầy đủ.
/// Chúng dùng layout riêng (_LayoutExpat) qua Views/Expat/_ViewStart.cshtml — xem plan 2026-11-11:
/// tách hẳn giao diện Expat (Anh/Hàn, tối ưu mobile + web) khỏi giao diện tiếng Việt của NV thường.
/// </summary>
public static class ExpatViews
{
    public const string Home               = "~/Views/Expat/Home.cshtml";
    public const string OtList             = "~/Views/Expat/OtList.cshtml";
    public const string AttendanceConfirm  = "~/Views/Expat/AttendanceConfirm.cshtml";
    public const string GatePass           = "~/Views/Expat/GatePass.cshtml";
    public const string LeaveApproval      = "~/Views/Expat/LeaveApproval.cshtml";
    public const string TeamCalendar       = "~/Views/Expat/TeamCalendar.cshtml";
    public const string MyTeam             = "~/Views/Expat/MyTeam.cshtml";
    public const string Notification       = "~/Views/Expat/Notification.cshtml";
    public const string Profile            = "~/Views/Expat/Profile.cshtml";
}
