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
        ["DT"] = "Đám tang", ["DC"] = "Đám cưới", ["CT"] = "Công tác", ["VS"] = "Vợ sanh", ["KT"] = "Khám thai",
        ["SI"] = "Bệnh có giấy"
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

    // Admin gửi yêu cầu xác nhận bổ sung cho OT ngày quá khứ NV chưa kịp tự ký (yêu cầu HR 2026-09-12).
    // Hạn 3 ngày tính từ lúc gửi (SYSDATE tại đây) — không phải từ workDate.
    public void OTSupplementRequested(string empCd, string actorEmpCd, DateTime workDate)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["workDate"] = workDate.ToString("dd/MM/yyyy"),
                ["deadline"] = DateTime.Now.AddDays(3).ToString("dd/MM/yyyy"),
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("OT_SUPP_REQUEST", ph);
            await _helper.SendNotificationAsync(Personal(empCd, actorEmpCd, title, body, "OT_SIGN", titleEn, bodyEn));
        });

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
    //  ATTENDANCE CONFIRM (xác nhận chấm công thiếu)
    // ═══════════════════════════════════════════════════════════════

    // Admin/HR/Clerk gửi yêu cầu tới NV — báo NV vào khai giờ vào/ra thực tế (bước 1).
    public void AttendanceConfirmRequested(string empCd, string actorEmpCd, DateTime workDate)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string> { ["workDate"] = workDate.ToString("dd/MM/yyyy") };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("ATT_CONFIRM_REQUEST", ph);
            await _helper.SendNotificationAsync(Personal(empCd, actorEmpCd, title, body, "ATT_CONFIRM_WORKER", titleEn, bodyEn));
        });

    // NV vừa khai giờ (bước 1) — báo quản lý theo scope dept/line/work của NV (bước 2).
    public void AttendanceConfirmSubmitted(string empCd, string empName, DateTime workDate)
        => FireAndForget(async () =>
        {
            var ph = new Dictionary<string, string>
            {
                ["empName"]  = empName,
                ["workDate"] = workDate.ToString("dd/MM/yyyy"),
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("ATT_CONFIRM_SUBMITTED", ph);
            var approvers = await _helper.GetApproverEmpCdsAsync(empCd);
            foreach (var ap in approvers)
                await _helper.SendNotificationAsync(Personal(ap, empCd, title, body, "ATT_CONFIRM_MANAGER", titleEn, bodyEn));
        });

    // Quản lý/Admin/HR đã chốt (CONFIRMED/REJECTED) — báo kết quả cho NV.
    public void AttendanceConfirmResult(string empCd, string actorEmpCd, DateTime workDate, string status)
        => FireAndForget(async () =>
        {
            string statusLabel = status == "CONFIRMED" ? "xác nhận" : "từ chối";
            var ph = new Dictionary<string, string>
            {
                ["workDate"] = workDate.ToString("dd/MM/yyyy"),
                ["status"]   = statusLabel,
            };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("ATT_CONFIRM_RESULT", ph);
            await _helper.SendNotificationAsync(Personal(empCd, actorEmpCd, title, body, "ATT_CONFIRM_WORKER", titleEn, bodyEn));
        });

    // ═══════════════════════════════════════════════════════════════
    //  GIFT (Quản lý Quà)
    // ═══════════════════════════════════════════════════════════════

    // Tới ngày dự kiến nhận quà — nhắc NV đến nhận (không chặn app, chỉ nhắc). Tắt/mở qua
    // /NotiTemplate/Index (IS_ACTIVE của key GIFT_READY) — không cần sửa code khi đang dev/test.
    public void GiftReady(string empCd, string actorEmpCd, string giftName)
        => FireAndForget(async () =>
        {
            if (!await _helper.IsTemplateActiveAsync("GIFT_READY")) return;
            var ph = new Dictionary<string, string> { ["giftName"] = giftName };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("GIFT_READY", ph);
            await _helper.SendNotificationAsync(Personal(empCd, actorEmpCd, title, body, "GIFT_MY", titleEn, bodyEn));
        });

    // Đợt quà "toàn công ty" — 1 thông báo broadcast duy nhất thay vì bắn riêng từng người
    // (mirror BulletinPublished/SurveyPublished — NOTI_TYPE=COMPANY).
    public void GiftReadyCompanyWide(string giftName, string createdBy)
        => FireAndForget(async () =>
        {
            if (!await _helper.IsTemplateActiveAsync("GIFT_READY")) return;
            var ph = new Dictionary<string, string> { ["giftName"] = giftName };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("GIFT_READY", ph);
            await _helper.SendNotificationAsync(new SendNotificationRequest
            {
                TITLE       = title,
                BODY        = body,
                TITLE_EN    = titleEn,
                BODY_EN     = bodyEn,
                NOTI_TYPE   = "COMPANY",
                TARGET_VAL  = "ALL",
                LINK_ACTION = "GIFT_MY",
                CREATED_BY  = createdBy
            });
        });

    // HR đã phát quà, gửi yêu cầu xác nhận — từ đây GiftGateFilter (HR_web) sẽ chặn app của NV
    // cho tới khi xác nhận. LƯU Ý: tắt IS_ACTIVE của GIFT_CONFIRM_REQUEST chỉ tắt THÔNG BÁO —
    // CONFIRM_STATUS vẫn chuyển PENDING_CONFIRM và gate vẫn chặn app bình thường (2 việc độc lập).
    public void GiftConfirmRequested(string empCd, string actorEmpCd, string giftName)
        => FireAndForget(async () =>
        {
            if (!await _helper.IsTemplateActiveAsync("GIFT_CONFIRM_REQUEST")) return;
            var ph = new Dictionary<string, string> { ["giftName"] = giftName };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("GIFT_CONFIRM_REQUEST", ph);
            await _helper.SendNotificationAsync(Personal(empCd, actorEmpCd, title, body, "GIFT_MY", titleEn, bodyEn));
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
    //  AI SAMHO - CSR
    // ═══════════════════════════════════════════════════════════════

    // CSR trả lời tay trong đoạn chat AI của NV -> báo NV. LINK_ACTION="AI_CSR" (web/mobile map
    // sang /AiCsr/Chat). Không báo nếu người trả lời chính là NV (không xảy ra nhưng phòng hờ).
    public void AiCsrReplied(string targetEmpCd, string csrEmpCd, string preview)
        => FireAndForget(async () =>
        {
            if (string.IsNullOrEmpty(targetEmpCd) || targetEmpCd == csrEmpCd) return;
            string shortPrev = string.IsNullOrEmpty(preview) ? "" : (preview.Length > 80 ? preview.Substring(0, 80) + "…" : preview);
            var ph = new Dictionary<string, string> { ["preview"] = shortPrev };
            var (title, body, titleEn, bodyEn) = await _helper.GetTemplateAsync("AI_CSR_REPLIED", ph);
            await _helper.SendNotificationAsync(Personal(targetEmpCd, csrEmpCd, title, body, "AI_CSR", titleEn, bodyEn));
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
