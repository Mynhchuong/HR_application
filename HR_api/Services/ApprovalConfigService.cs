using HR_api.Data;
using Microsoft.Extensions.Caching.Memory;

namespace HR_api.Services;

// Thay RoleHierarchyHelper (hard-code Dictionary<string,int> trong C#) + CanApproveCT()/IsAdminOrHRAsync()
// (whitelist role hard-code string) bằng dữ liệu đọc từ DB (HR_ROLES.ROLE_LEVEL, HR_APPROVAL_CONFIG) —
// đổi cấp bậc/role duyệt giờ chỉ cần UPDATE DB, không cần sửa code + deploy.
//
// Phân cấp: Worker/Clerk/HR/Canteen/CSR(1) → Supervisor(2) → DeputyManager(3) → Manager(4) → Expat(5) → Admin(99).
// Quy tắc ladder: người duyệt phải ở cấp CAO HƠN người gửi. Role lạ/NULL fallback level=1 (giữ đúng
// hành vi cũ của RoleHierarchyHelper.GetLevel khi Dictionary miss).
//
// HR_APPROVAL_CONFIG CHỈ gánh 2 whitelist đặc biệt (không phải ladder theo request type):
//   - "LEAVE_CT": Manager/Expat/Admin — ai được duyệt đơn Công tác (LEAVE_TYPE='CT').
//   - "OT": Admin/HR — ai được thao tác admin trên OT (xem OTController.IsAdminOrHRAsync cũ).
// "LEAVE_CT" là khoá tra cứu riêng trong app, KHÔNG phải giá trị thật của HR_REQUEST.REQUEST_TYPE.
//
// Cache 10 phút (IMemoryCache) — đổi ROLE_LEVEL/HR_APPROVAL_CONFIG qua UPDATE/INSERT trực tiếp trên DB
// có hiệu lực trong vòng tối đa 10 phút trên mỗi instance, KHÔNG phải ngay lập tức (đánh đổi hợp lý so
// với cách cũ: sửa Dictionary hard-code trong C# còn chậm hơn nhiều vì phải build + deploy lại).
public class ApprovalConfigService
{
    private readonly OracleService _db;
    private readonly IMemoryCache  _cache;

    private record RoleInfo(int Id, int Level);

    private const string RolesCacheKey  = "approval:roles";
    private const string ConfigCacheKey = "approval:config";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public ApprovalConfigService(OracleService db, IMemoryCache cache)
    {
        _db    = db;
        _cache = cache;
    }

    // 1 query lấy cả ID + LEVEL theo tên role — dùng chung cho ladder (CanApprove) VÀ whitelist
    // (IsRequestTypeRoleAllowedAsync), tránh round-trip DB riêng cho mỗi loại check.
    // Build bằng vòng lặp (không dùng ToDictionary) để 1 dòng dữ liệu trùng tên role (lỗi nhập liệu)
    // không làm throw ArgumentException và sập toàn bộ cổng duyệt của cả app — giữ dòng đầu tiên gặp,
    // bỏ qua dòng trùng.
    private async Task<Dictionary<string, RoleInfo>> GetRolesAsync()
    {
        var cached = await _cache.GetOrCreateAsync(RolesCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var rows = await _db.ExecuteQueryAsync(
                "SELECT ID, ROLE_NAME, ROLE_LEVEL FROM HRMS.HR_ROLES WHERE ROLE_NAME IS NOT NULL",
                r => new
                {
                    Id    = Convert.ToInt32(r["ID"]),
                    Name  = r["ROLE_NAME"]!.ToString()!,
                    Level = r["ROLE_LEVEL"] == DBNull.Value ? 1 : Convert.ToInt32(r["ROLE_LEVEL"]),
                });
            var map = new Dictionary<string, RoleInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                map.TryAdd(r.Name, new RoleInfo(r.Id, r.Level));
            return map;
        });
        return cached ?? new Dictionary<string, RoleInfo>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<(string RequestType, int RoleId)>> GetConfigAsync()
    {
        var cached = await _cache.GetOrCreateAsync(ConfigCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var rows = await _db.ExecuteQueryAsync(
                "SELECT REQUEST_TYPE, ROLE_ID FROM HRMS.HR_APPROVAL_CONFIG WHERE IS_ACTIVE = 'Y'",
                r => (RequestType: r["REQUEST_TYPE"]?.ToString() ?? "", RoleId: Convert.ToInt32(r["ROLE_ID"])));
            return rows.ToHashSet();
        });
        return cached ?? new HashSet<(string RequestType, int RoleId)>();
    }

    public async Task<int> GetLevelAsync(string? roleName)
    {
        if (string.IsNullOrEmpty(roleName)) return 1;
        var roles = await GetRolesAsync();
        return roles.TryGetValue(roleName, out var info) ? info.Level : 1;
    }

    public async Task<bool> HasApprovalPermissionAsync(string? roleName)
        => await GetLevelAsync(roleName) >= 2;

    public async Task<bool> CanApproveAsync(string? approverRole, string? requesterRole)
        => await GetLevelAsync(approverRole) > await GetLevelAsync(requesterRole);

    public async Task<string> RequiredApproverNameAsync(string? requesterRole)
    {
        int level = await GetLevelAsync(requesterRole);
        return level switch
        {
            >= 5 => "Admin",                                   // Expat → chỉ Admin
            4    => "Expat / Admin",                           // Manager → cần Expat/Admin
            3    => "Manager hoặc Expat",                      // DeputyManager → cần Manager+
            2    => "DeputyManager / Manager hoặc cấp trên",   // Supervisor → cần DeputyManager/Manager
            _    => "Supervisor hoặc cấp trên",                // Worker/Clerk/HR → cần Supervisor
        };
    }

    // Check whitelist riêng theo REQUEST_TYPE trong HR_APPROVAL_CONFIG (CT, OT) — KHÔNG so level,
    // chỉ check role có nằm trong danh sách được cấu hình cho request_type đó hay không.
    public async Task<bool> IsRequestTypeRoleAllowedAsync(string requestType, string? roleName)
    {
        if (string.IsNullOrEmpty(roleName)) return false;
        var roles = await GetRolesAsync();
        if (!roles.TryGetValue(roleName, out var info)) return false;

        var config = await GetConfigAsync();
        return config.Contains((requestType, info.Id));
    }
}
