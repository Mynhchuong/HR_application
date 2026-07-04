using HR_web.Models.Survey;
using Newtonsoft.Json;

namespace HR_web.API.Service;

// Proxy gọi HR_api cho Survey.
public class SurveyService
{
    private readonly ApiService _api;

    public SurveyService(ApiService api) { _api = api; }

    private class R<T>
    {
        public bool    success { get; set; }
        public T?      data    { get; set; }
        public string? message { get; set; }
    }

    // ─── Filter dùng ────────────────────────────────────────────────

    public async Task<int?> GetOldestPendingSurveyIdAsync(string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return null;
        try
        {
            var r = await _api.GetAsync<R<int?>>("Survey/pending-id", $"empcd={Uri.EscapeDataString(empcd)}");
            return r?.success == true ? r.data : null;
        }
        catch { return null; }
    }

    // ─── User làm survey ────────────────────────────────────────────

    private class DetailData
    {
        public SurveyModel?         survey   { get; set; }
        public SurveyResponseModel? response { get; set; }
    }

    public async Task<(SurveyModel? survey, SurveyResponseModel? response, string? error)> GetDetailAsync(string empcd, int id)
    {
        var qs = $"empcd={Uri.EscapeDataString(empcd)}&id={id}";
        var r = await _api.GetAsync<R<DetailData>>("Survey/detail", qs);
        if (r == null) return (null, null, "Không kết nối được server");
        return (r.data?.survey, r.data?.response, r.message);
    }

    public async Task<List<SurveyModel>> GetPendingAsync(string empcd)
    {
        var r = await _api.GetAsync<R<List<SurveyModel>>>("Survey/pending", $"empcd={Uri.EscapeDataString(empcd)}");
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<(bool ok, string? error)> SaveAnswerAsync(object payload)
    {
        var r = await PostAsync<R<object>>("Survey/save-answer", payload);
        return (r?.success ?? false, r?.message);
    }

    public async Task<(SurveySubmitResultModel? result, string? error)> SubmitAsync(string empcd, int surveyId)
    {
        var payload = new { EMPCD = empcd, SURVEY_ID = surveyId };
        var r = await PostAsync<R<SurveySubmitResultModel>>("Survey/submit", payload);
        return (r?.data, r?.message);
    }

    public async Task<(bool ok, string? error)> SkipIlliterateAsync(string empcd, int surveyId)
    {
        var payload = new { EMPCD = empcd, SURVEY_ID = surveyId };
        var r = await PostAsync<R<object>>("Survey/skip-illiterate", payload);
        return (r?.success ?? false, r?.message);
    }

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
