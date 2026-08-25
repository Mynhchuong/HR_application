using HR_web.API;

namespace HR_web.API.Service;

public class CanteenBreadService
{
    private readonly ApiService _api;
    public CanteenBreadService(ApiService api) { _api = api; }

    public async Task<string> QuotaListRawAsync()
    {
        var res = await _api.GetAsync_Raw("CanteenBread/quota-list", "");
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> QuotaSaveAllRawAsync(object payload)
    {
        var res = await _api.PostAsync("CanteenBread/quota-save-all", payload);
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> DeptListRawAsync()
    {
        var res = await _api.GetAsync_Raw("CanteenBread/dept-list", "");
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> StatusRawAsync(string empcd, string dat)
    {
        var qs = $"empcd={Uri.EscapeDataString(empcd)}&dat={Uri.EscapeDataString(dat)}";
        var res = await _api.GetAsync_Raw("CanteenBread/status", qs);
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    // Admin/HR/Canteen gán Bánh hàng loạt cho 1 danh sách NV (công nhân dây chuyền được
    // cấp phiếu cố định cả tuần) — bypass quota dept + cap 3 ngày/tuần ở tầng API.
    public async Task<string> BulkAssignRawAsync(object payload)
    {
        var res = await _api.PostAsync("CanteenOrder/bulk-bread", payload);
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    // ── Roster cố định (công nhân dây chuyền được cấp Bánh mỗi tuần) ────────
    public async Task<string> RosterListRawAsync()
    {
        var res = await _api.GetAsync_Raw("CanteenBread/roster-list", "");
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> RosterAddRawAsync(object payload)
    {
        var res = await _api.PostAsync("CanteenBread/roster-add", payload);
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    public async Task<string> RosterRemoveRawAsync(object payload)
    {
        var res = await _api.PostAsync("CanteenBread/roster-remove", payload);
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    // Lấy danh sách mã NV trong roster (dùng nội bộ để áp dụng bulk-bread theo roster
    // mà không cần admin dán lại danh sách mỗi tuần).
    public async Task<List<string>> GetRosterEmpCdsAsync()
    {
        var r = await _api.GetAsync<RosterResp>("CanteenBread/roster-list");
        if (r?.success != true || r.data == null) return new List<string>();
        return r.data
            .Select(x => x.empcd)
            .Where(e => !string.IsNullOrEmpty(e))
            .Select(e => e!)
            .ToList();
    }

    private class RosterResp
    {
        public bool             success { get; set; }
        public List<RosterRow>? data    { get; set; }
    }

    private class RosterRow
    {
        public string? empcd { get; set; }
    }

    public async Task<string> OrderViewRawAsync(
        string? from, string? to, string? empcd,
        string? deptcd, string? foodType, int page, int pageSize,
        string? clerkEmpcd = null, string? linecd = null, string? workcd = null,
        string? typeMeal = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(from))       q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrEmpty(to))         q.Add($"to={Uri.EscapeDataString(to)}");
        if (!string.IsNullOrEmpty(empcd))      q.Add($"empcd={Uri.EscapeDataString(empcd)}");
        if (!string.IsNullOrEmpty(deptcd))     q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd))     q.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd))     q.Add($"workcd={Uri.EscapeDataString(workcd)}");
        if (!string.IsNullOrEmpty(foodType))   q.Add($"foodType={Uri.EscapeDataString(foodType)}");
        if (!string.IsNullOrEmpty(typeMeal))   q.Add($"typeMeal={Uri.EscapeDataString(typeMeal)}");
        if (!string.IsNullOrEmpty(clerkEmpcd)) q.Add($"clerkEmpcd={Uri.EscapeDataString(clerkEmpcd)}");
        q.Add($"page={page}");
        q.Add($"pageSize={pageSize}");
        var res = await _api.GetAsync_Raw("CanteenBread/order-view", string.Join("&", q));
        return res?.IsSuccessStatusCode == true
            ? await res.Content.ReadAsStringAsync()
            : "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
    }

    // Lấy TOÀN BỘ log (không phân trang) — dùng cho Xuất Excel, cùng bộ filter với GetChangeLog.
    // pageSize lớn để lấy 1 lần thay vì loop nhiều trang.
    public async Task<List<ChangeLogRow>> OrderViewAllAsync(
        string? from, string? to, string? empcd,
        string? deptcd, string? foodType,
        string? clerkEmpcd = null, string? linecd = null, string? workcd = null,
        string? typeMeal = null)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(from))       q.Add($"from={Uri.EscapeDataString(from)}");
        if (!string.IsNullOrEmpty(to))         q.Add($"to={Uri.EscapeDataString(to)}");
        if (!string.IsNullOrEmpty(empcd))      q.Add($"empcd={Uri.EscapeDataString(empcd)}");
        if (!string.IsNullOrEmpty(deptcd))     q.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd))     q.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd))     q.Add($"workcd={Uri.EscapeDataString(workcd)}");
        if (!string.IsNullOrEmpty(foodType))   q.Add($"foodType={Uri.EscapeDataString(foodType)}");
        if (!string.IsNullOrEmpty(typeMeal))   q.Add($"typeMeal={Uri.EscapeDataString(typeMeal)}");
        if (!string.IsNullOrEmpty(clerkEmpcd)) q.Add($"clerkEmpcd={Uri.EscapeDataString(clerkEmpcd)}");
        q.Add("page=1");
        q.Add("pageSize=100000");

        var r = await _api.GetAsync<ChangeLogListResp>("CanteenBread/order-view", string.Join("&", q));
        return (r?.success == true ? r.data : null) ?? new List<ChangeLogRow>();
    }

    private class ChangeLogListResp
    {
        public bool               success { get; set; }
        public List<ChangeLogRow>? data   { get; set; }
    }
}

public class ChangeLogRow
{
    public string?   empcd      { get; set; }
    public string?   dat        { get; set; }
    public string?   typeMeal   { get; set; }
    public string?   typeOfFood { get; set; }
    public string?   changeFrom { get; set; }
    public DateTime? updtDt     { get; set; }
    public bool      isMysamho  { get; set; }
    public string?   empName    { get; set; }
    public string?   deptcd     { get; set; }
    public string?   deptName   { get; set; }
}
