using HR_api.Data;
using HR_api.Helpers;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace HR_api.Services;

// Gom logic "công bố kỳ lương" dùng chung cho cả 2 nơi: HR bấm tay (PayslipController.ReleasePeriod)
// và job tự động theo giờ hẹn (PayslipLifecycleService) — tránh lặp lại y nguyên 1 đoạn insert thông
// báo dài. Trước đây logic này nằm trong PayslipController, job tự động không gọi được vì controller
// không phải nơi hợp lý để 1 HostedService resolve qua DI.
public class PayslipService
{
    private readonly OracleService _db;
    private readonly NotificationHelper _notiHelper;

    // NV phòng Office Staff nhận phiếu lương qua email riêng, không cần app nhắc nữa (yêu cầu HR
    // 2026-09-10) — loại phòng này khỏi thông báo "công bố phiếu lương".
    private const string OfficeStaffDeptName = "OFFICE STAFF";

    public PayslipService(OracleService db, NotificationHelper notiHelper)
    {
        _db = db;
        _notiHelper = notiHelper;
    }

    // Claim-style: chỉ thật sự công bố (và gửi thông báo) nếu đang IS_PUBLISHED=0 — an toàn khi HR
    // bấm tay đúng lúc job tự động cũng đang xử lý period đó (không gửi thông báo 2 lần).
    public async Task<bool> ReleasePeriodAsync(decimal id, string? updtId)
    {
        int claimed = await _db.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_PAYROLL_PERIOD SET IS_PUBLISHED = 1, UPDT_ID = :UPDT_ID, UPDT_DT = SYSDATE WHERE ID = :ID AND IS_PUBLISHED = 0",
            new OracleParameter("UPDT_ID", (object?)updtId ?? DBNull.Value),
            new OracleParameter("ID", id));

        if (claimed == 0) return false;

        _ = Task.Run(() => NotifyPayslipReleasedAsync(updtId));
        return true;
    }

    // Gửi thông báo "công bố phiếu lương" cho TOÀN CÔNG TY TRỪ phòng Office Staff. Dùng NOTI_TYPE='MULTI'
    // liệt kê mọi phòng ban CÒN LẠI (tái sử dụng cơ chế "gửi nhiều target" HR_NOTIFICATION_TARGET).
    private async Task NotifyPayslipReleasedAsync(string? updtId)
    {
        try
        {
            var (title, body, titleEn, bodyEn) = await _notiHelper.GetTemplateAsync("PAYSLIP");

            var deptRows = await _db.ExecuteQueryAsync(
                "SELECT DISTINCT DEPTCD FROM HRMS.EAM410 WHERE UPPER(TRIM(DEPTNM)) != :EXCLUDE_DEPT",
                r => r["DEPTCD"]?.ToString() ?? "",
                new OracleParameter("EXCLUDE_DEPT", OfficeStaffDeptName));
            var targets = deptRows.Where(d => !string.IsNullOrEmpty(d)).Distinct()
                .Select(d => new Models.Notification.NotificationTarget { TYPE = "DEPT", VAL = d })
                .ToList();
            if (targets.Count == 0) return; // an toàn — không dò được phòng ban nào thì thôi, khỏi gửi sai

            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS (TITLE, BODY, TITLE_EN, BODY_EN, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE)
                VALUES (:TITLE, :BODY, :TITLE_EN, :BODY_EN, 'MULTI', 'MULTI', :LINK_ACTION, :CREATED_BY, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _db.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE",       title),
                new OracleParameter("BODY",        body),
                new OracleParameter("TITLE_EN",    (object?)titleEn ?? DBNull.Value),
                new OracleParameter("BODY_EN",     (object?)bodyEn  ?? DBNull.Value),
                new OracleParameter("LINK_ACTION", "PAYSLIP"),
                new OracleParameter("CREATED_BY",  (object?)updtId  ?? DBNull.Value),
                outIdParam);

            decimal notiId = outIdParam.Value is OracleDecimal od && !od.IsNull ? od.Value : 0;
            if (notiId <= 0) return;

            foreach (var t in targets)
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_NOTIFICATION_TARGET (NOTI_ID, TARGET_TYPE, TARGET_VAL)
                    VALUES (:NID, :TT, :TV)",
                    new OracleParameter("NID", notiId),
                    new OracleParameter("TT",  t.TYPE),
                    new OracleParameter("TV",  t.VAL));
            }

            await _notiHelper.SendFcmForMultiAsync(notiId, new Models.Notification.SendMultiNotificationRequest
            {
                TITLE = title, BODY = body, TITLE_EN = titleEn, BODY_EN = bodyEn,
                LINK_ACTION = "PAYSLIP", CREATED_BY = updtId, TARGETS = targets
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PayslipService] NotifyPayslipReleasedAsync error: {ex.Message}");
        }
    }
}
