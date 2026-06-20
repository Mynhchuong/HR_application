using HR_api.Helpers;
using HR_api.Models.Notification;

namespace HR_api.Services;

public class NotificationService
{
    private readonly NotificationHelper _helper;

    public NotificationService(NotificationHelper helper) { _helper = helper; }

    // ═══════════════════════════════════════════════════════════════
    //  GATE PASS
    // ═══════════════════════════════════════════════════════════════

    public void GatePassSubmitted(string empCd, string empName, string gpTypeName)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["empName"] = empName,
                ["gpType"]  = gpTypeName,
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("GP_SUBMITTED", ph);
            var approvers = await _helper.GetApproverEmpCdsAsync(empCd);
            foreach (var ap in approvers)
                await _helper.SendNotificationAsync(Personal(ap, empCd, title, body, "GP_MANAGE", titleEn, bodyEn));
        });

    public void GatePassApproved(string requesterEmpCd, string approverEmpCd)
        => FireAndForget(async () =>
        {
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("GP_APPROVED");
            await _helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd, title, body, "GP_MY", titleEn, bodyEn));
        });

    public void GatePassRejected(string requesterEmpCd, string approverEmpCd)
        => FireAndForget(async () =>
        {
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("GP_REJECTED");
            await _helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd, title, body, "GP_MY", titleEn, bodyEn));
        });

    // ═══════════════════════════════════════════════════════════════
    //  LEAVE
    // ═══════════════════════════════════════════════════════════════

    public void LeaveSubmitted(string empCd, string empName, string leaveTypeName)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["empName"]   = empName,
                ["leaveType"] = leaveTypeName,
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_SUBMITTED", ph);
            var approvers = await _helper.GetApproverEmpCdsAsync(empCd);
            foreach (var ap in approvers)
                await _helper.SendNotificationAsync(Personal(ap, empCd, title, body, "LEAVE_MANAGE", titleEn, bodyEn));
        });

    public void LeaveApproved(string requesterEmpCd, string approverEmpCd)
        => FireAndForget(async () =>
        {
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_APPROVED");
            await _helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd, title, body, "LEAVE_MY", titleEn, bodyEn));
        });

    public void LeaveRejected(string requesterEmpCd, string approverEmpCd)
        => FireAndForget(async () =>
        {
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_REJECTED");
            await _helper.SendNotificationAsync(Personal(requesterEmpCd, approverEmpCd, title, body, "LEAVE_MY", titleEn, bodyEn));
        });

    public void LeaveAssigned(string targetEmpCd, string assignerEmpCd, string leaveTypeName, DateTime from, DateTime to)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["leaveType"] = leaveTypeName,
                ["fromDate"]  = from.ToString("dd/MM/yyyy"),
                ["toDate"]    = to.ToString("dd/MM/yyyy"),
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_ASSIGNED", ph);
            await _helper.SendNotificationAsync(Personal(targetEmpCd, assignerEmpCd, title, body, "LEAVE_ASSIGNED", titleEn, bodyEn));
        });

    public void LeaveAcknowledged(string assignerEmpCd, string workerEmpCd)
        => FireAndForget(async () =>
        {
            var empName = await _helper.GetEmpNameAsync(workerEmpCd);
            var ph = new Dictionary<string, string> { ["empName"] = empName };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_ACKNOWLEDGED", ph);
            await _helper.SendNotificationAsync(Personal(assignerEmpCd, workerEmpCd, title, body, "LEAVE_TEAM", titleEn, bodyEn));
        });

    // ═══════════════════════════════════════════════════════════════
    //  OT
    // ═══════════════════════════════════════════════════════════════

    public void OTSignReminderToEmployees(IEnumerable<string> pendingEmpCds, string clerkEmpCd, string workDateStr)
    {
        FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string> { ["workDate"] = workDateStr };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("OT_SIGN_EMPLOYEE", ph);
            foreach (var empCd in pendingEmpCds)
                await _helper.SendNotificationAsync(Personal(empCd, clerkEmpCd, title, body, "OT_SIGN", titleEn, bodyEn));
        });
    }

    public void OTSignReminder(string workDateStr, string createdBy, string? deptId = null)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string> { ["workDate"] = workDateStr };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("OT_SIGN_GENERAL", ph);
            await _helper.SendNotificationAsync(new SendNotificationRequest
            {
                TITLE       = title,
                BODY        = body,
                TITLE_EN    = titleEn,
                BODY_EN     = bodyEn,
                NOTI_TYPE   = string.IsNullOrEmpty(deptId) ? "COMPANY" : "DEPT",
                TARGET_VAL  = string.IsNullOrEmpty(deptId) ? "ALL" : deptId,
                LINK_ACTION = "OT_SIGN",
                CREATED_BY  = createdBy
            });
        });

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static SendNotificationRequest Personal(
        string targetEmpCd, string createdBy,
        string title, string body,
        string linkAction,
        string? titleEn = null, string? bodyEn = null) => new()
    {
        TITLE       = title,
        BODY        = body,
        TITLE_EN    = titleEn,
        BODY_EN     = bodyEn,
        NOTI_TYPE   = "EMPCD",
        TARGET_VAL  = targetEmpCd,
        LINK_ACTION = linkAction,
        CREATED_BY  = createdBy
    };

    private static void FireAndForget(Func<Task> fn) => _ = Task.Run(fn);
}
