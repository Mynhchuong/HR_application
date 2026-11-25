using System.Text;
using System.Text.Json;
using HR_api.Data;
using HR_api.Helpers;
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
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;            // địa chỉ NỘI BỘ (Tpm:BaseUrl)
    private readonly string _baseUrlExternal;    // địa chỉ BÊN NGOÀI (Tpm:BaseUrlExternal) — để trống = chỉ dùng nội bộ

    // Giống app My Samho (2 địa chỉ nội bộ/ngoài): thử nội bộ trước, không tới được (hoặc 502/503/504) thì thử ngoài. Nhớ địa chỉ vừa chạy được
    // 5 phút để không phải chờ nội bộ hỏng mỗi lời gọi; hết hạn thì thử lại nội bộ. ConnectTimeout ngắn (3s) để chuyển địa chỉ nhanh.
    private static volatile int _preferred;           // 0 = nội bộ, 1 = ngoài
    private static DateTime _preferredUntil = DateTime.MinValue;
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(3),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _key;
    private readonly int _timeoutSec;

    public TpmService(OracleService db, IHttpClientFactory httpFactory, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
        _httpFactory = httpFactory;
        _baseUrl = (cfg["Tpm:BaseUrl"] ?? "").TrimEnd('/');
        _baseUrlExternal = (cfg["Tpm:BaseUrlExternal"] ?? "").TrimEnd('/');
        _key = cfg["Tpm:InternalKey"] ?? "";
        _timeoutSec = int.TryParse(cfg["Tpm:TimeoutSec"], out var t) && t > 0 ? t : 20;
    }

    public bool IsConfigured => (_baseUrl.Length > 0 || _baseUrlExternal.Length > 0) && _key.Length > 0;

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

        // Phòng ban bị chặn (mặc định OFFICE STAFF = A01001, khối văn phòng) — xem TpmAccess.cs (2026-10-27).
        // Công nhân bảo trì (danh sách HR_TPM_MAINT) được miễn dù thẻ thuộc khối văn phòng.
        if (TpmAccess.IsDeptBlocked(_cfg, u.DeptCd) && !await TpmAccess.IsMaintenanceAsync(_db, empcd))
            return (null, "Chức năng này không dành cho khối văn phòng.");

        // Role dept HR (người thuộc phòng nhân sự dù role chính là Manager/Supervisor/Worker...) cũng bị chặn
        // (chốt 2026-10-27, xem Helpers/DeptRoleHelper.cs).
        if (string.Equals(await DeptRoleHelper.GetDeptRoleNameAsync(_db, empcd), "HR", StringComparison.OrdinalIgnoreCase))
            return (null, "Chức năng này dành cho công nhân sản xuất.");

        return (new TpmEmployee
        {
            EmpCd = u.EmpCd, Name = u.Name,
            DeptCd = u.DeptCd, DeptNm = u.DeptNm,
            LineCd = u.LineCd, LineNm = u.LineNm,
            WorkCd = u.WorkCd, WorkNm = u.WorkNm,
        }, null);
    }

    /// <summary>Như ResolveEmployeeAsync nhưng bắt buộc thẻ nằm trong danh sách công nhân bảo trì (HR_TPM_MAINT).</summary>
    public async Task<(TpmEmployee? emp, string? error)> ResolveMaintenanceAsync(string? empcd)
    {
        var (emp, error) = await ResolveEmployeeAsync(empcd);
        if (emp == null) return (null, error);
        if (!await TpmAccess.IsMaintenanceAsync(_db, emp.EmpCd)) return (null, "Chức năng này dành cho công nhân bảo trì.");
        return (emp, null);
    }

    /// <summary>Gọi TPM.API. Trả (true, status, bodyJson) nếu TPM trả lời (kể cả 4xx — body có
    /// success/code/message để app dịch); (false, 0, "") nếu chưa cấu hình/không kết nối được.</summary>
    public async Task<(bool reached, int status, string body)> CallAsync(HttpMethod method, string path, object? payload = null)
    {
        if (!IsConfigured) return (false, 0, "");

        // Danh sách địa chỉ theo thứ tự thử (bỏ địa chỉ trống); địa chỉ đang được ưu tiên đứng đầu
        var bases = new List<(int idx, string url)>();
        if (_baseUrl.Length > 0) bases.Add((0, _baseUrl));
        if (_baseUrlExternal.Length > 0) bases.Add((1, _baseUrlExternal));
        if (bases.Count > 1 && DateTime.Now < _preferredUntil && _preferred == 1) bases.Reverse();

        var json = payload != null ? JsonSerializer.Serialize(payload) : null;
        for (int i = 0; i < bases.Count; i++)
        {
            var (idx, url) = bases[i];
            bool last = i == bases.Count - 1;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSec));
                using var req = new HttpRequestMessage(method, $"{url}{path}");
                req.Headers.Add("X-MySamho-Key", _key);
                if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var resp = await Http.SendAsync(req, cts.Token);
                var status = (int)resp.StatusCode;
                if (!last && (status == 502 || status == 503 || status == 504)) continue;   // máy chủ đứng sau lỗi cổng → thử địa chỉ kia

                var body = await resp.Content.ReadAsStringAsync();
                if (bases.Count > 1) { _preferred = idx; _preferredUntil = DateTime.Now.AddMinutes(5); }
                return (true, status, body);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TpmService] {method} {url}{path} error: {ex.Message}");
                if (last) return (false, 0, "");
            }
        }
        return (false, 0, "");
    }
}
