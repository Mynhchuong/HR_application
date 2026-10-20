using System.Text;
using System.Text.Json;
using HR_api.Data;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Tpm;

/// <summary>Cầu nối My Samho -> TPM.API (hệ thống bảo trì máy, DB riêng). Module tự chứa trong thư mục
/// Tpm/: kiểm tra ai được dùng (tra DB HR) + gọi TPM.API bằng khóa nội bộ riêng
/// (<c>X-MySamho-Key</c>). Xem HR_application/TPM.DOC/README.md.</summary>
public class TpmService
{
    // ROLE_ID: 5 = HR, 7 = Expat — không dùng chức năng này (công nhân sản xuất dùng, quyết định 2026-10-20).
    private static readonly HashSet<int> BlockedRoleIds = new() { 5, 7 };

    private readonly OracleService _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;
    private readonly string _key;
    private readonly int _timeoutSec;

    public TpmService(OracleService db, IHttpClientFactory httpFactory, IConfiguration cfg)
    {
        _db = db;
        _httpFactory = httpFactory;
        _baseUrl = (cfg["Tpm:BaseUrl"] ?? "").TrimEnd('/');
        _key = cfg["Tpm:InternalKey"] ?? "";
        _timeoutSec = int.TryParse(cfg["Tpm:TimeoutSec"], out var t) && t > 0 ? t : 20;
    }

    public bool IsConfigured => _baseUrl.Length > 0 && _key.Length > 0;

    /// <summary>Tra nhân viên + kiểm quyền. Trả (employee, null) nếu được dùng; (null, lý do) nếu không.</summary>
    public async Task<(TpmEmployee? emp, string? error)> ResolveEmployeeAsync(string? empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd)) return (null, "Thiếu mã nhân viên.");

        var rows = await _db.ExecuteQueryAsync(@"
            SELECT * FROM (
                SELECT E.EMPCD, E.CNAME, E.JEAJIKGB, U.ROLE_ID, U.IS_ACTIVE,
                       E.DEPTCD, B.DEPTNM, E.LINECD, B.TEAMNM, E.WORKCD, B.WORKNM
                FROM HRMS.ECM100 E
                LEFT JOIN HRMS.HR_USERS U ON U.EMPCD = E.EMPCD
                LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = E.DEPTCD AND B.LINECD = E.LINECD AND B.WORKCD = E.WORKCD
                WHERE E.EMPCD = :EMPCD
            ) WHERE ROWNUM = 1",
            r => new
            {
                EmpCd = r["EMPCD"]?.ToString() ?? "",
                Name = r["CNAME"]?.ToString() ?? "",
                Jeajikgb = r["JEAJIKGB"]?.ToString(),
                RoleId = r["ROLE_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["ROLE_ID"]),
                IsActive = r["IS_ACTIVE"] == DBNull.Value ? 1 : Convert.ToInt32(r["IS_ACTIVE"]),
                DeptCd = r["DEPTCD"]?.ToString(), DeptNm = r["DEPTNM"]?.ToString(),
                LineCd = r["LINECD"]?.ToString(), LineNm = r["TEAMNM"]?.ToString(),
                WorkCd = r["WORKCD"]?.ToString(), WorkNm = r["WORKNM"]?.ToString(),
            },
            new OracleParameter("EMPCD", empcd.Trim()));

        var u = rows.FirstOrDefault();
        if (u == null || u.Jeajikgb != "Y" || u.IsActive == 0) return (null, "Tài khoản không hợp lệ.");
        if (u.RoleId.HasValue && BlockedRoleIds.Contains(u.RoleId.Value))
            return (null, "Chức năng này dành cho công nhân sản xuất.");

        return (new TpmEmployee
        {
            EmpCd = u.EmpCd, Name = u.Name,
            DeptCd = u.DeptCd, DeptNm = u.DeptNm,
            LineCd = u.LineCd, LineNm = u.LineNm,
            WorkCd = u.WorkCd, WorkNm = u.WorkNm,
        }, null);
    }

    /// <summary>Gọi TPM.API. Trả (true, status, bodyJson) nếu TPM trả lời (kể cả 4xx — body có
    /// success/code/message để app dịch); (false, 0, "") nếu chưa cấu hình/không kết nối được.</summary>
    public async Task<(bool reached, int status, string body)> CallAsync(HttpMethod method, string path, object? payload = null)
    {
        if (!IsConfigured) return (false, 0, "");
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(_timeoutSec);

            using var req = new HttpRequestMessage(method, $"{_baseUrl}{path}");
            req.Headers.Add("X-MySamho-Key", _key);
            if (payload != null)
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return (true, (int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TpmService] {method} {path} error: {ex.Message}");
            return (false, 0, "");
        }
    }
}
