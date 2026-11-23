using HR_web.API;

namespace HR_web.API.Service;

// Passthrough sang HR_api (apiHR/Tpm/*) cho tính năng "Báo máy hư" (TPM) của công nhân sản xuất.
// Trả raw JSON để Controller Content(...) thẳng ra client (pattern giống AiCsrService). Module TPM
// tự chứa: TpmService + Controllers/Tpm + Views/Tpm — xem HR_application/TPM.DOC/README.md.
public class TpmService
{
    private readonly ApiService _api;
    public TpmService(ApiService api) { _api = api; }

    private const string Err = "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";

    private static async Task<string> ReadAsync(HttpResponseMessage? res)
        => res?.IsSuccessStatusCode == true ? await res.Content.ReadAsStringAsync() : Err;

    public async Task<string> MachineRawAsync(string empcd, string itemCode)
        => await ReadAsync(await _api.GetAsync_Raw("Tpm/machine",
            $"empcd={Uri.EscapeDataString(empcd)}&itemCode={Uri.EscapeDataString(itemCode)}"));

    public async Task<string> HistoryRawAsync(string empcd, string? status)
    {
        var qs = $"empcd={Uri.EscapeDataString(empcd)}";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        return await ReadAsync(await _api.GetAsync_Raw("Tpm/history", qs));
    }

    public async Task<string> LocationsRawAsync(string empcd, long? parentId)
    {
        var qs = $"empcd={Uri.EscapeDataString(empcd)}";
        if (parentId.HasValue) qs += $"&parentId={parentId.Value}";
        return await ReadAsync(await _api.GetAsync_Raw("Tpm/locations", qs));
    }

    public async Task<string> LocationReportRawAsync(object payload)
        => await ReadAsync(await _api.PostAsync("Tpm/location-report", payload));

    // ── Bảo trì dùng My Samho (tích hợp 3) — HR_api chỉ cho thẻ trong HR_TPM_MAINT, TPM quyết quyền thật
    public async Task<string> MaintBrokenRawAsync(string empcd, bool myAreaOnly)
        => await ReadAsync(await _api.GetAsync_Raw("Tpm/maintenance/broken",
            $"empcd={Uri.EscapeDataString(empcd)}&myAreaOnly={(myAreaOnly ? "true" : "false")}"));

    public async Task<string> MaintLookupRawAsync(string kind, string empcd)   // kind: areas | issues | solve-groups
        => await ReadAsync(await _api.GetAsync_Raw($"Tpm/maintenance/{kind}", $"empcd={Uri.EscapeDataString(empcd)}"));

    public async Task<string> MaintReceiveRawAsync(object payload)
        => await ReadAsync(await _api.PutAsync_Raw("Tpm/maintenance/receive", payload));

    public async Task<string> MaintRepairRawAsync(object payload)
        => await ReadAsync(await _api.PutAsync_Raw("Tpm/maintenance/repair", payload));

    public async Task<string> ReportRawAsync(object payload)
        => await ReadAsync(await _api.PostAsync("Tpm/report", payload));
}
