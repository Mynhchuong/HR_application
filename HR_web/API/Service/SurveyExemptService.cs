using HR_web.Models.Survey;
using Newtonsoft.Json;

namespace HR_web.API.Service;

// Proxy Exempt CRUD + Import bulk
public class SurveyExemptService
{
    private readonly ApiService _api;
    public SurveyExemptService(ApiService api) { _api = api; }

    private class R<T>
    {
        public bool    success { get; set; }
        public T?      data    { get; set; }
        public string? message { get; set; }
    }

    public async Task<List<SurveyExemptModel>> ListAsync(string? empcd, string? type, int? isActive,
        string? name, string? deptcd, string? linecd, string? workcd)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(empcd))  qs.Add($"empcd={Uri.EscapeDataString(empcd)}");
        if (!string.IsNullOrEmpty(type))   qs.Add($"type={Uri.EscapeDataString(type)}");
        if (isActive.HasValue)             qs.Add($"isActive={isActive.Value}");
        if (!string.IsNullOrEmpty(name))   qs.Add($"name={Uri.EscapeDataString(name)}");
        if (!string.IsNullOrEmpty(deptcd)) qs.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd)) qs.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd)) qs.Add($"workcd={Uri.EscapeDataString(workcd)}");

        var r = await _api.GetAsync<R<List<SurveyExemptModel>>>("SurveyExempt/list", string.Join("&", qs));
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<(bool ok, string? msg)> SaveAsync(object payload)
    {
        var r = await Post<R<object>>("SurveyExempt/save", payload);
        return (r?.success ?? false, r?.message);
    }

    public async Task<(bool ok, string? msg)> DeleteAsync(string empcd, string type, string? actor)
    {
        var payload = new { EMPCD = empcd, EXEMPT_TYPE = type, LOGIN_USER = actor };
        var r = await Post<R<object>>("SurveyExempt/delete", payload);
        return (r?.success ?? false, r?.message);
    }

    public async Task<(bool ok, int count, string? msg)> ImportAsync(List<object> items, string? actor)
    {
        var payload = new { LOGIN_USER = actor, ITEMS = items };
        var r = await Post<R<ImportResult>>("SurveyExempt/import", payload);
        return (r?.success ?? false, r?.data?.count ?? 0, r?.message);
    }
    private class ImportResult { public int count { get; set; } }

    private async Task<T?> Post<T>(string endpoint, object payload)
    {
        try
        {
            var raw = await _api.PostAsync(endpoint, payload);
            if (raw == null || !raw.IsSuccessStatusCode) return default;
            var json = await raw.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
        }
        catch { return default; }
    }
}
