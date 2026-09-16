using HR_api.Data;
using HR_api.Models.Home;
using Microsoft.Extensions.Caching.Memory;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Lịch cá nhân cho Home — chỉ hiển thị TRẠNG THÁI HOÀN TẤT.
// - Nghỉ phép APPROVED (SOURCE=SELF)
// - Nghỉ phép sắp lịch (SOURCE=ASSIGNED, STATUS=APPROVED)
// - Ra vào cổng APPROVED
// - Tăng ca CONFIRMED
// Dữ liệu từ My Samho, không phải ERP → có disclaimer hiển thị trong view.
public class HomeMyCalendarService
{
    private readonly OracleService _oracleService;
    private readonly IMemoryCache  _cache;

    public HomeMyCalendarService(OracleService oracleService, IMemoryCache cache)
    {
        _oracleService = oracleService;
        _cache         = cache;
    }

    public async Task<List<HomeMyCalendarItem>> GetAsync(string empcd, int year, int month)
    {
        if (string.IsNullOrEmpty(empcd)) return new();

        string cacheKey = $"home:mycal:{empcd}:{year}-{month:D2}";
        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return await LoadAsync(empcd, year, month);
        });
        return cached ?? new();
    }

    private async Task<List<HomeMyCalendarItem>> LoadAsync(string empcd, int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var to   = from.AddMonths(1).AddDays(-1);

        var tasks = new[]
        {
            LoadLeaveApprovedAsync(empcd, from, to),
            LoadGpApprovedAsync(empcd, from, to),
            LoadOtConfirmedAsync(empcd, from, to)
        };
        await Task.WhenAll(tasks);
        return tasks.SelectMany(t => t.Result).OrderBy(x => x.DATE).ToList();
    }

    // ─── Nghỉ phép (SELF + ASSIGNED đều APPROVED) ────────────────
    // Loop TỪNG NGÀY trong FROM_DATE → TO_DATE để chấm màu từng ô.
    // SOURCE=SELF   → TYPE=LEAVE  🟢
    // SOURCE=ASSIGNED → TYPE=ASSIGN 🟣
    // Loại phải nộp giấy tờ chứng minh — khớp DocRequiredTypes bên LeaveController (SI/DT/DC/VS/KT,
    // DS (Dưỡng sức) bỏ khỏi danh sách theo yêu cầu 2026-09-15).
    private static readonly HashSet<string> DocRequiredTypes = new() { "SI", "DT", "DC", "VS", "KT" };

    // Tên đầy đủ 9 loại nghỉ mới + vài mã cũ còn đọc được — khớp NewLeaveTypeNames bên LeaveController
    // (trước đây switch chỉ có AL/SL/CL/NPL, 9 loại mới đều rớt vào default "Nghỉ" chung chung).
    private static readonly Dictionary<string, string> LeaveTypeNames = new()
    {
        ["AL"] = "Phép năm", ["NL"] = "Không lương", ["SI"] = "Bệnh có giấy",
        ["DT"] = "Đám tang", ["DC"] = "Đám cưới",     ["CT"] = "Công tác",
        ["VS"] = "Vợ sanh",  ["DS"] = "Dưỡng sức",    ["KT"] = "Khám thai",
        ["SL"] = "Nghỉ bệnh", ["CL"] = "Nghỉ chế độ", ["NPL"] = "Không lương"
    };

    private async Task<List<HomeMyCalendarItem>> LoadLeaveApprovedAsync(string empcd, DateTime from, DateTime to)
    {
        const string sql = @"
            SELECT L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.SOURCE, L.REASON, L.DOC_STATUS,
                   AP.CNAME APPROVER_NAME, ASN.CNAME ASSIGNER_NAME
            FROM HRMS.HR_LEAVE_REQUEST L
            JOIN HRMS.HR_REQUEST R    ON R.REQUEST_ID = L.REQUEST_ID
            LEFT JOIN HRMS.ECM100 AP  ON AP.EMPCD    = R.FINAL_APPROVER
            LEFT JOIN HRMS.ECM100 ASN ON ASN.EMPCD   = R.CREATED_BY
            WHERE L.EMPCD = :EMPCD
              AND R.REQUEST_TYPE = 'LEAVE'
              AND R.STATUS = 'APPROVED'
              AND L.FROM_DATE <= :D_TO
              AND L.TO_DATE   >= :D_FROM";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            LeaveType  = r["LEAVE_TYPE"]?.ToString() ?? "AL",
            FromDate   = r["FROM_DATE"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r["FROM_DATE"]),
            ToDate     = r["TO_DATE"]   == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r["TO_DATE"]),
            Source     = r["SOURCE"]?.ToString() ?? "SELF",
            Reason     = r["REASON"]?.ToString() ?? "",
            DocStatus  = r["DOC_STATUS"] == DBNull.Value ? null : r["DOC_STATUS"]?.ToString(),
            Approver   = r["APPROVER_NAME"]?.ToString() ?? "",
            Assigner   = r["ASSIGNER_NAME"]?.ToString() ?? ""
        },
        new OracleParameter("EMPCD",  empcd),
        new OracleParameter("D_TO",   to.Date),
        new OracleParameter("D_FROM", from.Date));

        // Ngày nào đã nộp giấy tờ THẬT SỰ (theo HR_LEAVE_DOC_DAY, nguồn xác định "đã nộp" chính xác
        // của tính năng nộp giấy tờ) — để chấm đúng từng ngày cụ thể thay vì lặp lại 1 trạng thái
        // chung cho cả đơn (yêu cầu 2026-09-10: "cụ thể là ngày nào hiện luôn").
        var confirmedRows = await _oracleService.ExecuteQueryAsync(
            "SELECT TO_CHAR(LEAVE_DATE,'YYYY-MM-DD') D FROM HRMS.HR_LEAVE_DOC_DAY WHERE EMPCD = :EMPCD AND LEAVE_DATE BETWEEN :D_FROM AND :D_TO",
            r => r["D"]?.ToString() ?? "",
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("D_FROM", from.Date),
            new OracleParameter("D_TO", to.Date));
        var confirmedDates = confirmedRows.Where(d => d.Length > 0).ToHashSet();

        var items = new List<HomeMyCalendarItem>();
        foreach (var row in rows)
        {
            if (row.FromDate == DateTime.MinValue) continue;

            var cur = row.FromDate.Date < from.Date ? from.Date : row.FromDate.Date;
            var end = row.ToDate.Date   > to.Date   ? to.Date   : row.ToDate.Date;
            bool isAssigned = row.Source == "ASSIGNED";
            bool docRequired = DocRequiredTypes.Contains(row.LeaveType);

            string leaveTypeLabel = LeaveTypeNames.GetValueOrDefault(row.LeaveType, "Nghỉ");
            string label = isAssigned ? $"Quản lý xếp lịch nghỉ: {leaveTypeLabel}" : $"Nghỉ phép: {leaveTypeLabel}";
            // Tên người duyệt/người sắp lịch (CNAME) đọc theo font VNI-Windows — tách riêng khỏi
            // DETAIL để frontend bọc đúng font vni-font, không lẫn với chữ Unicode thường của Lý do
            // (trước đây gộp chung 1 chuỗi DETAIL khiến tên hiện lỗi phông trên popup lịch cá nhân).
            string signerLabel = isAssigned ? "Người sắp lịch" : "Người duyệt";
            string? signerName = isAssigned ? row.Assigner : row.Approver;
            if (string.IsNullOrEmpty(signerName)) signerName = null;
            string detail = string.IsNullOrEmpty(row.Reason) ? "" : $"Lý do: {row.Reason}";

            while (cur <= end)
            {
                var dateKey = cur.ToString("yyyy-MM-dd");
                // Cả đơn đã SUBMITTED đủ (kể cả do job tự dò ERP đánh dấu — job đó KHÔNG ghi từng
                // dòng vào HR_LEAVE_DOC_DAY, chỉ set thẳng DOC_STATUS='SUBMITTED') -> coi mọi ngày
                // trong đơn là xong, khỏi cần tra HR_LEAVE_DOC_DAY. Còn PARTIALLY_SUBMITTED thì mới
                // cần tra đúng ngày nào đã xác nhận (đây là lúc HR_LEAVE_DOC_DAY có dữ liệu chính xác
                // theo từng ngày — "cụ thể là ngày nào" theo yêu cầu 2026-09-10).
                string? dayDocStatus = !docRequired ? null
                    : row.DocStatus == "SUBMITTED" ? "SUBMITTED"
                    : confirmedDates.Contains(dateKey) ? "SUBMITTED"
                    : row.DocStatus == "RESUBMIT_REQUESTED" ? "RESUBMIT_REQUESTED"
                    : null;

                items.Add(new HomeMyCalendarItem
                {
                    DATE         = dateKey,
                    TYPE         = isAssigned ? "ASSIGN" : "LEAVE",
                    LABEL        = label,
                    DETAIL       = detail,
                    SIGNER_LABEL = signerLabel,
                    SIGNER_NAME  = signerName,
                    DOC_REQUIRED = docRequired,
                    DOC_STATUS   = dayDocStatus
                });
                cur = cur.AddDays(1);
            }
        }
        return items;
    }

    // ─── Ra cổng APPROVED 🟡 ─────────────────────────────────────
    private async Task<List<HomeMyCalendarItem>> LoadGpApprovedAsync(string empcd, DateTime from, DateTime to)
    {
        const string sql = @"
            SELECT GP.GP_TYPE, GP.OUT_TIME, GP.IN_TIME, GP.REASON,
                   AP.CNAME APPROVER_NAME
            FROM HRMS.HR_GATEPASS_REQUEST GP
            JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = GP.REQUEST_ID
            LEFT JOIN HRMS.ECM100 AP ON AP.EMPCD  = R.FINAL_APPROVER
            WHERE GP.EMPCD = :EMPCD
              AND R.STATUS = 'APPROVED'
              AND TRUNC(NVL(GP.OUT_TIME, GP.IN_TIME)) BETWEEN :D_FROM AND :D_TO";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            GpType   = r["GP_TYPE"]?.ToString() ?? "OUT",
            OutTime  = r["OUT_TIME"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["OUT_TIME"]),
            InTime   = r["IN_TIME"]  == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["IN_TIME"]),
            Reason   = r["REASON"]?.ToString() ?? "",
            Approver = r["APPROVER_NAME"]?.ToString() ?? ""
        },
        new OracleParameter("EMPCD",  empcd),
        new OracleParameter("D_FROM", from.Date),
        new OracleParameter("D_TO",   to.Date));

        var items = new List<HomeMyCalendarItem>();
        foreach (var row in rows)
        {
            var refDate = row.OutTime ?? row.InTime;
            if (refDate == null) continue;

            string typeLabel = row.GpType switch
            {
                "OUT" => "Ra cổng",
                "IN"  => "Vào cổng",
                "MID" => "Ra rồi vào lại",
                _     => "Ra vào cổng"
            };
            string timeStr;
            if (row.GpType == "MID" && row.OutTime.HasValue && row.InTime.HasValue)
                timeStr = $"Ra {row.OutTime:HH:mm} → Vào {row.InTime:HH:mm}";
            else if (row.GpType == "IN" && row.InTime.HasValue)
                timeStr = $"Vào lúc {row.InTime:HH:mm}";
            else if (row.OutTime.HasValue)
                timeStr = $"Ra lúc {row.OutTime:HH:mm}";
            else
                timeStr = "";

            string detail = timeStr;
            if (!string.IsNullOrEmpty(row.Approver))
                detail += string.IsNullOrEmpty(detail) ? $"Người duyệt: {row.Approver}" : $" · Người duyệt: {row.Approver}";
            if (!string.IsNullOrEmpty(row.Reason))
                detail += string.IsNullOrEmpty(detail) ? $"Lý do: {row.Reason}" : $" · Lý do: {row.Reason}";

            items.Add(new HomeMyCalendarItem
            {
                DATE   = refDate.Value.ToString("yyyy-MM-dd"),
                TYPE   = "GP",
                LABEL  = typeLabel,
                DETAIL = detail
            });
        }
        return items;
    }

    // ─── Tăng ca CONFIRMED 🔵 ────────────────────────────────────
    // Ưu tiên HR_OT_REQUEST (app), fallback qua EBM300/EBM300_WAIT.SIGNED_STATUS (ERP, chỉ đọc)
    // khi app thiếu bản ghi — tránh lặp lại case ERP đã ký mà app mất data (vd bị admin xoá
    // nhưng không reset lại ERP) khiến lịch cá nhân "mất" ngày tăng ca nhân viên đã ký.
    private async Task<List<HomeMyCalendarItem>> LoadOtConfirmedAsync(string empcd, DateTime from, DateTime to)
    {
        const string sql = @"
            SELECT WORK_DATE, OT_HOURS, CONFIRM_DATE
              FROM HRMS.HR_OT_REQUEST
             WHERE EMPCD = :EMPCD
               AND CONFIRM_STATUS = 'CONFIRMED'
               AND TRUNC(WORK_DATE) BETWEEN :D_FROM AND :D_TO

            UNION ALL

            SELECT TRUNC(E.DAT) WORK_DATE, E.OVER_TIME OT_HOURS, E.SIGNED_LOG CONFIRM_DATE
              FROM HRMS.EBM300 E
             WHERE E.EMPCD = :EMPCD2
               AND E.SIGNED_STATUS = 'Y'
               AND NVL(E.OVER_TIME, 0) > 0
               AND TRUNC(E.DAT) BETWEEN :D_FROM2 AND :D_TO2
               AND NOT EXISTS (
                       SELECT 1 FROM HRMS.HR_OT_REQUEST R
                        WHERE R.EMPCD = E.EMPCD AND R.WORK_DATE = TRUNC(E.DAT))

            UNION ALL

            SELECT TRUNC(W.DAT) WORK_DATE, W.OVER_TIME OT_HOURS, W.SIGNED_LOG CONFIRM_DATE
              FROM HRMS.EBM300_WAIT W
             WHERE W.EMPCD = :EMPCD3
               AND W.SIGNED_STATUS = 'Y'
               AND NVL(W.OVER_TIME, 0) > 0
               AND TRUNC(W.DAT) BETWEEN :D_FROM3 AND :D_TO3
               AND NOT EXISTS (
                       SELECT 1 FROM HRMS.HR_OT_REQUEST R2
                        WHERE R2.EMPCD = W.EMPCD AND R2.WORK_DATE = TRUNC(W.DAT))
               AND NOT EXISTS (
                       SELECT 1 FROM HRMS.EBM300 E2
                        WHERE E2.EMPCD = W.EMPCD AND TRUNC(E2.DAT) = TRUNC(W.DAT))";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            WorkDate    = r["WORK_DATE"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(r["WORK_DATE"]),
            OtHours     = r["OT_HOURS"]  == DBNull.Value ? 0m : Convert.ToDecimal(r["OT_HOURS"]),
            ConfirmDate = r["CONFIRM_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CONFIRM_DATE"])
        },
        new OracleParameter("EMPCD",  empcd),
        new OracleParameter("D_FROM", from.Date),
        new OracleParameter("D_TO",   to.Date),
        new OracleParameter("EMPCD2",  empcd),
        new OracleParameter("D_FROM2", from.Date),
        new OracleParameter("D_TO2",   to.Date),
        new OracleParameter("EMPCD3",  empcd),
        new OracleParameter("D_FROM3", from.Date),
        new OracleParameter("D_TO3",   to.Date));

        var items = new List<HomeMyCalendarItem>();
        foreach (var row in rows)
        {
            if (row.WorkDate == DateTime.MinValue) continue;

            string detail = $"Đồng ý tăng ca {row.OtHours:0.##} giờ";
            if (row.ConfirmDate.HasValue)
                detail += $" · Ký ngày {row.ConfirmDate:dd/MM/yyyy}";

            items.Add(new HomeMyCalendarItem
            {
                DATE   = row.WorkDate.ToString("yyyy-MM-dd"),
                TYPE   = "OT",
                LABEL  = "Tăng ca",
                DETAIL = detail
            });
        }
        return items;
    }
}
