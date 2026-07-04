using HR_web.Models.Survey;
using Newtonsoft.Json;

namespace HR_web.API.Service;

// Proxy gọi HR_api cho SurveyAdmin (HR/Admin).
public class SurveyAdminService
{
    private readonly ApiService _api;

    public SurveyAdminService(ApiService api) { _api = api; }

    private class R<T>
    {
        public bool    success { get; set; }
        public T?      data    { get; set; }
        public string? message { get; set; }
    }

    // ─── List / detail ──────────────────────────────────────

    public async Task<List<SurveyModel>> ListAsync(string? status, string? type, string? search)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(status)) qs.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrEmpty(type))   qs.Add($"type={Uri.EscapeDataString(type)}");
        if (!string.IsNullOrEmpty(search)) qs.Add($"search={Uri.EscapeDataString(search)}");

        var r = await _api.GetAsync<R<List<SurveyModel>>>("SurveyAdmin/list", string.Join("&", qs));
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<SurveyModel?> GetDetailAsync(int id)
    {
        var r = await _api.GetAsync<R<SurveyModel>>("SurveyAdmin/detail", $"id={id}");
        return r?.success == true ? r.data : null;
    }

    public async Task<SurveyModel?> PreviewAsync(int id)
    {
        var r = await _api.GetAsync<R<SurveyModel>>("SurveyAdmin/preview", $"id={id}");
        return r?.success == true ? r.data : null;
    }

    // ─── Save / state ──────────────────────────────────────

    public async Task<(bool ok, string? message, int? id)> SaveAsync(object payload)
    {
        var r = await PostAsync<R<SaveResponse>>("SurveyAdmin/save", payload);
        if (r == null) return (false, "Không kết nối được server", null);
        return (r.success, r.message, r.data?.id);
    }
    private class SaveResponse { public int id { get; set; } }

    public async Task<(bool ok, string? message)> ChangeStatusAsync(int id, string newStatus, string? loginUser)
    {
        var payload = new { ID = id, NEW_STATUS = newStatus, LOGIN_USER = loginUser };
        var r = await PostAsync<R<object>>("SurveyAdmin/change-status", payload);
        return (r?.success ?? false, r?.message);
    }

    // ─── TestMode ──────────────────────────────────────

    public async Task<SurveySubmitResultModel?> TestSubmitAsync(object payload)
    {
        var r = await PostAsync<R<SurveySubmitResultModel>>("SurveyAdmin/test-submit", payload);
        return r?.success == true ? r.data : null;
    }

    // ─── HTTP ──────────────────────────────────────

    private async Task<T?> PostAsync<T>(string endpoint, object payload)
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
