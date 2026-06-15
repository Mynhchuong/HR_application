using HR_api.Helpers;
using HR_api.Models.Notification;

namespace HR_api.Services;

/// <summary>
/// High-level notification service — controllers chỉ gọi method nghiệp vụ, không cần biết chi tiết.
/// Thêm loại request mới thì thêm method ở đây là xong.
/// </summary>
public class NotificationService
{
    private readonly NotificationHelper _helper;

    public NotificationService(NotificationHelper helper)
    {
        _helper = helper;
    }

    // ═══════════════════════════════════════════════════════════════
    //  GATE PASS
    // ═══════════════════════════════════════════════════════════════

    // Nhân viên gửi Gate Pass → báo tất cả quản lý cùng bộ phận
    public void GatePassSubmitted(string empCd, string empName)
        => FireAndForget(async () =>
        {
            var approvers = await _helper.GetApproverEmpCdsAsync(empCd);
            foreach (var ap in approvers)
                await _helper.SendNotificationAsync(Personal(ap, empCd,
                    "Yêu cầu Gate Pass mới",
                    $"{empName} đã gửi yêu cầu ra/vào cổng, vui lòng xem xét.",
                    "GP_MANAGE"));
        });

    // Quản lý duyệt → báo người gửi
    public void GatePassApproved(string requesterEmpCd, string approverEmpCd)
        => Fire(_helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd,
            "Gate Pass được duyệt",
            "Yêu cầu ra/vào cổng của bạn đã được phê duyệt.",
            "GP_MY")));

    // Quản lý từ chối → báo người gửi
    public void GatePassRejected(string requesterEmpCd, string approverEmpCd)
        => Fire(_helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd,
            "Gate Pass bị từ chối",
            "Yêu cầu ra/vào cổng của bạn đã bị từ chối.",
            "GP_MY")));

    // ═══════════════════════════════════════════════════════════════
    //  LEAVE (Nghỉ phép)
    // ═══════════════════════════════════════════════════════════════

    // Quản lý duyệt đơn → báo người gửi
    public void LeaveApproved(string requesterEmpCd, string approverEmpCd)
        => Fire(_helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd,
            "Đơn nghỉ phép được duyệt",
            "Đơn xin nghỉ phép của bạn đã được phê duyệt.",
            "LEAVE_MY")));

    // Quản lý từ chối → báo người gửi
    public void LeaveRejected(string requesterEmpCd, string approverEmpCd)
        => Fire(_helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd,
            "Đơn nghỉ phép bị từ chối",
            "Đơn xin nghỉ phép của bạn đã bị từ chối.",
            "LEAVE_MY")));

    // Sếp sắp lịch nghỉ → báo nhân viên được sắp
    public void LeaveAssigned(string targetEmpCd, string assignerEmpCd, string leaveTypeName, DateTime from, DateTime to)
        => Fire(_helper.SendNotificationAsync(Personal(targetEmpCd, assignerEmpCd,
            "Bạn được sắp lịch nghỉ phép",
            $"Lịch {leaveTypeName} {from:dd/MM/yyyy} – {to:dd/MM/yyyy} đã được sắp. Vui lòng xác nhận.",
            "LEAVE_ASSIGNED")));

    // Nhân viên xác nhận đã nhận lịch → báo người sắp
    public void LeaveAcknowledged(string assignerEmpCd, string workerEmpCd)
        => Fire(_helper.SendNotificationAsync(Personal(assignerEmpCd, workerEmpCd,
            "Công nhân đã nhận thông báo nghỉ",
            $"Nhân viên {workerEmpCd} đã nhận thông báo lịch nghỉ phép được sắp.",
            "LEAVE_TEAM")));

    // ═══════════════════════════════════════════════════════════════
    //  OT (Tăng ca)
    // ═══════════════════════════════════════════════════════════════

    // Nhắc ký xác nhận OT → báo cả công ty hoặc bộ phận
    public void OTSignReminder(string workDateStr, string createdBy, string? deptId = null)
        => Fire(_helper.SendNotificationAsync(new SendNotificationRequest
        {
            TITLE       = "Xác nhận tăng ca",
            BODY        = $"Vui lòng kiểm tra và ký xác nhận tăng ca ngày {workDateStr}.",
            NOTI_TYPE   = string.IsNullOrEmpty(deptId) ? "COMPANY" : "DEPT",
            TARGET_VAL  = string.IsNullOrEmpty(deptId) ? "ALL" : deptId,
            LINK_ACTION = "OT_SIGN",
            CREATED_BY  = createdBy
        }));

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS — dùng nội bộ
    // ═══════════════════════════════════════════════════════════════

    private static SendNotificationRequest Personal(
        string targetEmpCd, string createdBy,
        string title, string body, string linkAction) => new()
    {
        TITLE       = title,
        BODY        = body,
        NOTI_TYPE   = "EMPCD",
        TARGET_VAL  = targetEmpCd,
        LINK_ACTION = linkAction,
        CREATED_BY  = createdBy
    };

    private static void Fire(Task t) => _ = t;

    private static void FireAndForget(Func<Task> fn) => _ = Task.Run(fn);
}
