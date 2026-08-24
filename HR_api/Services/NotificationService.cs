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

    private static readonly Dictionary<string, string> DocLeaveTypeNames = new()
    {
        ["DT"] = "Đám tang", ["DC"] = "Đám cưới", ["CT"] = "Công tác", ["VS"] = "Vợ sanh", ["KT"] = "Khám thai"
    };

    public void LeaveDocReminder(string empCd, string leaveTypeCode, DateTime from, DateTime to)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["leaveType"] = DocLeaveTypeNames.GetValueOrDefault(leaveTypeCode, leaveTypeCode),
                ["fromDate"]  = from.ToString("dd/MM/yyyy"),
                ["toDate"]    = to.ToString("dd/MM/yyyy"),
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_DOC_REMINDER", ph);
            await _helper.SendNotificationAsync(Personal(empCd, "SYSTEM", title, body, "LEAVE_MY", titleEn, bodyEn));
        });

    public void LeaveDocResubmitRequested(string empCd, string actorEmpCd, string leaveTypeCode, DateTime from, DateTime to, string? remark)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["leaveType"] = DocLeaveTypeNames.GetValueOrDefault(leaveTypeCode, leaveTypeCode),
                ["fromDate"]  = from.ToString("dd/MM/yyyy"),
                ["toDate"]    = to.ToString("dd/MM/yyyy"),
                ["remark"]    = remark ?? "",
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("LEAVE_DOC_RESUBMIT", ph);
            await _helper.SendNotificationAsync(Personal(empCd, actorEmpCd, title, body, "LEAVE_MY", titleEn, bodyEn));
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
    //  BULLETIN
    // ═══════════════════════════════════════════════════════════════

    // Broadcast cho TẤT CẢ token khi HR publish bản tin LẦN ĐẦU
    public void BulletinPublished(int bulletinId, string title, string createdBy)
        => FireAndForget(async () =>
        {
            // Cắt title nếu dài quá 50 ký tự để giữ notification ngắn
            string shortTitle = title.Length > 50 ? title.Substring(0, 50) + "…" : title;
            var ph = new Dictionary<string, string> { ["title"] = shortTitle };

            var (titleVi, bodyVi, titleEn, bodyEn) =
                await _helper.GetTemplateAsync("BULLETIN_NEW", ph);

            await _helper.SendNotificationAsync(new SendNotificationRequest
            {
                TITLE       = titleVi,
                BODY        = bodyVi,
                TITLE_EN    = titleEn,
                BODY_EN     = bodyEn,
                NOTI_TYPE   = "COMPANY",
                TARGET_VAL  = bulletinId.ToString(),   // bulletinId — web/mobile dùng để build /Bulletin/Detail/{id}
                LINK_ACTION = "BULLETIN",              // code — linkMap trong Notification/Index.cshtml sẽ map sang URL
                CREATED_BY  = createdBy
            });
        });

    // Báo cho chủ bình luận khi có người trả lời bình luận của họ.
    // Tên người trả lời KHÔNG nhúng vào body (font VNI chỉ hiển thị đúng qua SENDER_NAME/CREATED_BY).
    // Noti cá nhân nên TARGET_VAL phải chứa EMPCD người nhận — bulletinId nhét vào LINK_ACTION
    // theo pattern "BULLETIN_CMT:{id}", JS trang Notification/Index parse ra URL detail.
    public void BulletinCommentReplied(int bulletinId, string targetEmpCd, string replierEmpCd)
        => FireAndForget(async () =>
        {
            if (string.IsNullOrEmpty(targetEmpCd) || targetEmpCd == replierEmpCd) return;
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("BULLETIN_REPLY");
            await _helper.SendNotificationAsync(
                Personal(targetEmpCd, replierEmpCd, title, body, "BULLETIN_CMT:" + bulletinId, titleEn, bodyEn));
        });

    // ═══════════════════════════════════════════════════════════════
    //  SURVEY
    // ═══════════════════════════════════════════════════════════════

    // Broadcast khi survey chuyển SCHEDULED → ACTIVE.
    // Client filter recipient theo HR_SURVEY_RECIPIENT (pattern giống Bulletin).
    public void SurveyPublished(int surveyId, string title, string createdBy)
        => FireAndForget(async () =>
        {
            string shortTitle = title.Length > 50 ? title.Substring(0, 50) + "…" : title;
            var ph = new Dictionary<string, string> { ["surveyTitle"] = shortTitle };

            var (titleVi, bodyVi, titleEn, bodyEn) =
                await _helper.GetTemplateAsync("SURVEY_NEW", ph);

            await _helper.SendNotificationAsync(new SendNotificationRequest
            {
                TITLE       = titleVi,
                BODY        = bodyVi,
                TITLE_EN    = titleEn,
                BODY_EN     = bodyEn,
                NOTI_TYPE   = "SURVEY",
                TARGET_VAL  = surveyId.ToString(),
                LINK_ACTION = "SURVEY",
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
