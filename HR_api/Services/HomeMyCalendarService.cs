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
    private async Task<List<HomeMyCalendarItem>> LoadLeaveApprovedAsync(string empcd, DateTime from, DateTime to)
    {
        const string sql = @"
            SELECT L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.SOURCE, L.REASON,
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
            Approver   = r["APPROVER_NAME"]?.ToString() ?? "",
            Assigner   = r["ASSIGNER_NAME"]?.ToString() ?? ""
        },
        new OracleParameter("EMPCD",  empcd),
        new OracleParameter("D_TO",   to.Date),
        new OracleParameter("D_FROM", from.Date));

        var items = new List<HomeMyCalendarItem>();
        foreach (var row in rows)
        {
            if (row.FromDate == DateTime.MinValue) continue;

            var cur = row.FromDate.Date < from.Date ? from.Date : row.FromDate.Date;
            var end = row.ToDate.Date   > to.Date   ? to.Date   : row.ToDate.Date;
            bool isAssigned = row.Source == "ASSIGNED";

            string leaveTypeLabel = row.LeaveType switch
            {
                "AL"  => "Phép năm",
                "SL"  => "Nghỉ bệnh",
                "CL"  => "Nghỉ chế độ",
                "NPL" => "Không lương",
                _     => "Nghỉ"
            };
            string label = isAssigned ? $"Quản lý xếp lịch nghỉ: {leaveTypeLabel}" : $"Nghỉ phép: {leaveTypeLabel}";
            string detail = isAssigned
                ? (string.IsNullOrEmpty(row.Assigner) ? "Quản lý xếp lịch nghỉ cho bạn" : $"Quản lý xếp lịch: {row.Assigner}")
                : (string.IsNullOrEmpty(row.Approver) ? "Đã duyệt"                      : $"Người duyệt: {row.Approver}");
            if (!string.IsNullOrEmpty(row.Reason))
                detail += $" · Lý do: {row.Reason}";

            while (cur <= end)
            {
                items.Add(new HomeMyCalendarItem
                {
                    DATE   = cur.ToString("yyyy-MM-dd"),
                    TYPE   = isAssigned ? "ASSIGN" : "LEAVE",
                    LABEL  = label,
                    DETAIL = detail
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
