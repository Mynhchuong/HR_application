using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

public class PopupGateItem
{
    public string TYPE { get; set; } = "";
    public string REF_ID { get; set; } = "";
    public Dictionary<string, string?> DATA { get; set; } = new();
}

/// <summary>
/// Popup nhắc việc khi NV mở app — mỗi lần chỉ trả ĐÚNG 1 popup ưu tiên cao nhất để NV không bị dồn
/// nhiều popup cùng lúc. Thứ tự ưu tiên = thứ tự kiểm tra trong GetNextAsync; thêm loại mới chỉ cần
/// viết 1 hàm Get...Async và chèn vào đúng vị trí.
/// </summary>
public class PopupGateService
{
    // Vừa xác nhận 1 popup ưu tiên cao xong thì chờ 4 tiếng mới nhắc popup thấp hơn (yêu cầu 2026-11-17).
    private const double CooldownHours = 4;

    private readonly OracleService _db;

    public PopupGateService(OracleService db) => _db = db;

    public async Task<PopupGateItem?> GetNextAsync(string empcd)
    {
        var leave = await GetPendingLeaveAsync(empcd);
        if (leave != null) return leave;

        if (await AcknowledgedRecentlyAsync(empcd)) return null;

        return await GetUnratedChatAsync(empcd);
    }

    // Chỉ lịch được sắp trong tháng hiện tại — lịch cũ chưa xác nhận bỏ qua, tránh NV dính popup hàng loạt lúc mới update.
    private async Task<PopupGateItem?> GetPendingLeaveAsync(string empcd)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT L.REQUEST_ID, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, ASN.CNAME ASSIGNER_NAME
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST R    ON R.REQUEST_ID = L.REQUEST_ID
                LEFT JOIN HRMS.ECM100 ASN ON ASN.EMPCD    = R.CREATED_BY
                WHERE L.EMPCD = :EMPCD
                  AND L.SOURCE = 'ASSIGNED'
                  AND R.STATUS = 'ASSIGNED'
                  AND NVL(L.CONFIRM_STATUS, 'ASSIGNED') NOT IN ('CONFIRMED', 'WORKER_REJECTED')
                  AND R.CREATED_DATE >= TRUNC(SYSDATE, 'MM')
                ORDER BY R.CREATED_DATE
            ) WHERE ROWNUM = 1",
            r => new PopupGateItem
            {
                TYPE   = "LEAVE_ASSIGNED",
                REF_ID = r["REQUEST_ID"]?.ToString() ?? "",
                DATA   = new()
                {
                    ["leaveType"]    = HR_api.Controllers.LeaveController.NewLeaveTypeNames
                                           .GetValueOrDefault(r["LEAVE_TYPE"]?.ToString() ?? "", r["LEAVE_TYPE"]?.ToString() ?? ""),
                    ["fromDate"]     = r["FROM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]).ToString("dd/MM/yyyy"),
                    ["toDate"]       = r["TO_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]).ToString("dd/MM/yyyy"),
                    ["totalDays"]    = r["TOTAL_DAYS"] == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]).ToString("0.##"),
                    ["assignerName"] = r["ASSIGNER_NAME"] == DBNull.Value ? null : r["ASSIGNER_NAME"].ToString(),
                }
            },
            new OracleParameter("EMPCD", empcd));
        return rows.FirstOrDefault();
    }

    private async Task<bool> AcknowledgedRecentlyAsync(string empcd)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) C FROM HRMS.HR_LEAVE_REQUEST
            WHERE EMPCD = :EMPCD AND SOURCE = 'ASSIGNED'
              AND CONFIRM_DATE > SYSDATE - :HRS / 24",
            r => Convert.ToInt32(r["C"]),
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("HRS", CooldownHours));
        return rows.FirstOrDefault() > 0;
    }

    private async Task<PopupGateItem?> GetUnratedChatAsync(string empcd)
    {
        var rows = await _db.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT i.ID, i.INQUIRY_NO, i.SUBJECT, t.TOPIC_NAME
                FROM HRMS.HR_INQUIRY i
                LEFT JOIN HRMS.HR_INQUIRY_TOPIC t ON t.TOPIC_CD = i.TOPIC_CD
                WHERE i.EMPCD = :EMPCD AND i.STATUS = 'CLOSED' AND i.RATING IS NULL AND NVL(i.MSG_COUNT, 0) > 0
                ORDER BY i.CLOSED_DT DESC
            ) WHERE ROWNUM = 1",
            r => new PopupGateItem
            {
                TYPE   = "CHAT_RATING",
                REF_ID = r["ID"]?.ToString() ?? "",
                DATA   = new()
                {
                    ["inquiryNo"] = r["INQUIRY_NO"] == DBNull.Value ? null : r["INQUIRY_NO"].ToString(),
                    ["subject"]   = r["SUBJECT"]    == DBNull.Value ? null : r["SUBJECT"].ToString(),
                    ["topicName"] = r["TOPIC_NAME"] == DBNull.Value ? null : r["TOPIC_NAME"].ToString(),
                }
            },
            new OracleParameter("EMPCD", empcd));
        return rows.FirstOrDefault();
    }
}
