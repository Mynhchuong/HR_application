using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.AppLink;

namespace HR_api.Services;

// Kho link cài app iOS — Admin import 1 danh sách link (mỗi link chỉ phát 1 lần cho 1 mã thẻ),
// nhân viên tự nhập mã thẻ ở Guide/Index để nhận link (xem AppLinkController + create_app_link.sql).
public class AppLinkService
{
    private readonly OracleService _oracleService;

    public AppLinkService(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    public async Task<List<AppLinkModel>> GetListAsync(string? status)
    {
        var sql = @"
            SELECT L.ID, L.LINK_URL, L.STATUS, L.ASSIGNED_EMPCD, U.FULL_NAME AS ASSIGNED_EMP_NAME,
                   L.ASSIGNED_DT, L.INST_ID, L.INST_DT
            FROM HRMS.HR_APP_LINK L
            LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = L.ASSIGNED_EMPCD
            WHERE (:STATUS IS NULL OR L.STATUS = :STATUS)
            ORDER BY L.ID DESC";

        return await _oracleService.ExecuteQueryAsync(sql, Map,
            new OracleParameter("STATUS", (object?)status ?? DBNull.Value));
    }

    public async Task<AppLinkStatsModel> GetStatsAsync()
    {
        const string sql = @"
            SELECT COUNT(*) AS TOTAL,
                   SUM(CASE WHEN STATUS = 'AVAILABLE' THEN 1 ELSE 0 END) AS AVAILABLE,
                   SUM(CASE WHEN STATUS = 'ASSIGNED'  THEN 1 ELSE 0 END) AS ASSIGNED,
                   SUM(CASE WHEN STATUS = 'REPLACED'  THEN 1 ELSE 0 END) AS REPLACED
            FROM HRMS.HR_APP_LINK";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new AppLinkStatsModel
        {
            TOTAL     = r["TOTAL"]     == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
            AVAILABLE = r["AVAILABLE"] == DBNull.Value ? 0 : Convert.ToInt32(r["AVAILABLE"]),
            ASSIGNED  = r["ASSIGNED"]  == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED"]),
            REPLACED  = r["REPLACED"]  == DBNull.Value ? 0 : Convert.ToInt32(r["REPLACED"]),
        });
        return rows.Count > 0 ? rows[0] : new AppLinkStatsModel();
    }

    public async Task<(bool success, string message, int inserted, int skipped)> ImportAsync(ImportAppLinkRequest req)
    {
        var links = (req.LINKS ?? new())
            .Select(l => l?.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();

        if (links.Count == 0)
            return (false, "Vui lòng nhập ít nhất 1 link", 0, 0);

        int inserted = 0;
        foreach (var link in links)
        {
            // Bỏ qua link đã tồn tại (tránh phát trùng 1 link cho 2 người nếu Admin lỡ paste lại)
            var exists = await _oracleService.ExecuteQueryAsync(
                "SELECT 1 FROM HRMS.HR_APP_LINK WHERE LINK_URL = :L",
                r => 1, new OracleParameter("L", link!));
            if (exists.Count > 0) continue;

            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_APP_LINK (LINK_URL, STATUS, INST_ID)
                VALUES (:LINK_URL, 'AVAILABLE', :INST_ID)",
                new OracleParameter("LINK_URL", link!),
                new OracleParameter("INST_ID", (object?)req.LOGIN_USER ?? DBNull.Value));
            inserted++;
        }

        int skipped = links.Count - inserted;
        return (true, $"Đã thêm {inserted} link mới" + (skipped > 0 ? $" (bỏ qua {skipped} link đã có)" : ""), inserted, skipped);
    }

    public async Task<AppLinkResult> RequestLinkAsync(string empcd)
    {
        empcd = empcd?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(empcd))
            return new AppLinkResult { success = false, message = "Vui lòng nhập mã thẻ" };

        var emp = await _oracleService.ExecuteQueryAsync(
            "SELECT 1 FROM HRMS.HR_USERS WHERE EMPCD = :E",
            r => 1, new OracleParameter("E", empcd));
        if (emp.Count == 0)
            return new AppLinkResult { success = false, message = "Không tìm thấy mã thẻ này" };

        // Đã có link đang gán rồi -> trả về ALREADY_ASSIGNED, web sẽ hỏi Cập nhật app / Đổi máy mới
        var current = await _oracleService.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT LINK_URL, ASSIGNED_DT FROM HRMS.HR_APP_LINK
                WHERE ASSIGNED_EMPCD = :E AND STATUS = 'ASSIGNED'
                ORDER BY ASSIGNED_DT DESC
            ) WHERE ROWNUM = 1",
            r => new AppLinkResult
            {
                success = true,
                alreadyAssigned = true,
                linkUrl = r["LINK_URL"].ToString(),
                assignedDt = r["ASSIGNED_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["ASSIGNED_DT"])
            },
            new OracleParameter("E", empcd));

        if (current.Count > 0) return current[0];

        return await AssignNewLinkAsync(empcd);
    }

    public async Task<AppLinkResult> ReassignAsync(ReassignAppLinkRequest req)
    {
        var empcd = req.EMPCD?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(empcd))
            return new AppLinkResult { success = false, message = "Vui lòng nhập mã thẻ" };

        // Link cũ đã tải rồi, đổi máy mới thì link cũ không sài lại được -> đóng lại, không trả về pool
        await _oracleService.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_APP_LINK
            SET STATUS = 'REPLACED'
            WHERE ASSIGNED_EMPCD = :E AND STATUS = 'ASSIGNED'",
            new OracleParameter("E", empcd));

        return await AssignNewLinkAsync(empcd);
    }

    // Retry ngắn để tránh race khi 2 người bấm nhận link cùng lúc trúng cùng 1 dòng AVAILABLE
    // (Oracle 10g không có SKIP LOCKED nên dùng optimistic UPDATE ... WHERE STATUS='AVAILABLE').
    private async Task<AppLinkResult> AssignNewLinkAsync(string empcd)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var candidate = await _oracleService.ExecuteQueryAsync(@"
                SELECT ID, LINK_URL FROM (
                    SELECT ID, LINK_URL FROM HRMS.HR_APP_LINK
                    WHERE STATUS = 'AVAILABLE'
                    ORDER BY ID
                ) WHERE ROWNUM = 1",
                r => new { ID = Convert.ToInt32(r["ID"]), LINK_URL = r["LINK_URL"].ToString() });

            if (candidate.Count == 0)
                return new AppLinkResult { success = false, message = "Đã hết link trong kho, vui lòng liên hệ Admin để bổ sung" };

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_APP_LINK
                SET STATUS = 'ASSIGNED', ASSIGNED_EMPCD = :E, ASSIGNED_DT = SYSDATE
                WHERE ID = :ID AND STATUS = 'AVAILABLE'",
                new OracleParameter("E", empcd),
                new OracleParameter("ID", candidate[0].ID));

            if (rows > 0)
                return new AppLinkResult { success = true, alreadyAssigned = false, linkUrl = candidate[0].LINK_URL };
        }

        return new AppLinkResult { success = false, message = "Hệ thống đang bận, vui lòng thử lại" };
    }

    private static AppLinkModel Map(OracleDataReader r) => new()
    {
        ID                = Convert.ToInt32(r["ID"]),
        LINK_URL          = r["LINK_URL"]?.ToString() ?? "",
        STATUS            = r["STATUS"]?.ToString() ?? "AVAILABLE",
        ASSIGNED_EMPCD    = r["ASSIGNED_EMPCD"] == DBNull.Value ? null : r["ASSIGNED_EMPCD"].ToString(),
        ASSIGNED_EMP_NAME = r["ASSIGNED_EMP_NAME"] == DBNull.Value ? null : r["ASSIGNED_EMP_NAME"].ToString(),
        ASSIGNED_DT       = r["ASSIGNED_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["ASSIGNED_DT"]),
        INST_ID           = r["INST_ID"]?.ToString(),
        INST_DT           = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
    };
}
