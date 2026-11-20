namespace HR_web.Helpers;

/// <summary>
/// NGUỒN DUY NHẤT cho "bấm thông báo thì mở trang nào" (2026-11-20). Trước đây bảng này nằm ở 4 chỗ
/// (NotificationController.ResolveLink, JS chuông header, JS Notification/Index, JS Expat/Notification)
/// → thêm loại thông báo mới (vd Đào tạo) quên 1 chỗ là điện thoại và web mở khác nhau.
/// Giờ:
///   - App điện thoại (push) mở /Notification/Go → dùng helper này.
///   - Chuông + trang Thông báo nhận sẵn LINK_URL do server tính (NotificationController.GetPage).
/// Thêm loại thông báo mới: CHỈ sửa file này.
/// </summary>
public static class NotificationLinkHelper
{
    /// <summary>LINK_ACTION là link ngoài (bài báo, trang nhà nước...) — mở tab/trình duyệt mới.</summary>
    public static bool IsExternal(string? action) =>
        !string.IsNullOrEmpty(action)
        && (action.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || action.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Đường dẫn trong app ("~/...") của thông báo; <c>null</c> = không có trang đích (thông báo chung
    /// HR gửi, nhắc nhở...) → chỉ xem nội dung, không chuyển trang. Link ngoài cũng trả null (xem IsExternal).
    /// </summary>
    public static string? ResolvePath(string? action, string? target, bool isExpat)
    {
        var a = action ?? "";
        if (a.Length == 0 || IsExternal(a)) return null;

        if (a.StartsWith("BULLETIN_CMT:"))
            return "~/Bulletin/Detail/" + Uri.EscapeDataString(a["BULLETIN_CMT:".Length..]);
        if (a == "BULLETIN")
            return string.IsNullOrEmpty(target) || target == "ALL" ? "~/Bulletin/Index" : "~/Bulletin/Detail/" + Uri.EscapeDataString(target);
        if (a == "SURVEY")
            return string.IsNullOrEmpty(target) ? null : "~/Survey/Do/" + Uri.EscapeDataString(target);

        // Hội thoại: NV mở trang chat của mình; HR/CSR/GA/Admin không phải chủ hội thoại được
        // EmployeeInquiryController.Chat tự chuyển sang màn xử lý.
        if (a.StartsWith("inquiry/"))
            return "~/EmployeeInquiry/Chat?id=" + Uri.EscapeDataString(a["inquiry/".Length..]);

        // Đào tạo (HR_api TrainingNotificationWorker.LinkFor)
        if (a.StartsWith("TRAINING_CLASS_REGISTER/"))
            return "~/Training/ClassRegister/" + Uri.EscapeDataString(a["TRAINING_CLASS_REGISTER/".Length..]);
        if (a.StartsWith("TRAINING_CLASS/"))
            return "~/Training/ClassDetail/" + Uri.EscapeDataString(a["TRAINING_CLASS/".Length..]);
        if (a.StartsWith("TRAINING_"))
            return "~/Training/MyClasses";

        // NV nước ngoài có màn riêng cho phần duyệt/quản lý
        if (isExpat)
        {
            switch (a)
            {
                case "GP_MANAGE":    return "~/GatePass/GpListForExpat";
                case "LEAVE_MANAGE":
                case "LEAVE_TEAM":   return "~/Leave/LeaveApprovalForExpat";
                case "OT_SIGN":      return "~/OT/OtListForExpat";
                case "ATT_CONFIRM_MANAGER": return "~/AttendanceConfirm/IndexForExpat";
            }
        }

        return a switch
        {
            "GP_MY"               => "~/GatePass/GpMyRequests",
            "GP_MANAGE"           => "~/GatePass/GpListForSupervisor",
            "LEAVE_MY"            => "~/Leave/LeaveMyRequests",
            "LEAVE_MANAGE"        => "~/Leave/LeaveApprovalList",
            "LEAVE_TEAM"          => "~/Leave/TeamSchedule",
            "LEAVE_ASSIGNED"      => "~/Leave/LeaveMyRequests",
            "OT_SIGN"             => "~/OT/OtConfirmForm",
            "ATT_CONFIRM_WORKER"  => "~/AttendanceConfirm/WorkerForm",
            "ATT_CONFIRM_MANAGER" => "~/AttendanceConfirm/Index",
            "GIFT_MY"             => "~/Gift/MyGifts",
            "PAYSLIP"             => "~/Payslip/Index",
            "AI_CSR"              => "~/AiCsr/Chat",
            _                     => null      // mã lạ / "SYSTEM" / rỗng: không có trang đích
        };
    }
}
