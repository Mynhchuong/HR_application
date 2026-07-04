using HR_web.Models.Survey;

namespace HR_web.API.Service;

// Proxy Report + Illiterate + Text answers
public class SurveyReportService
{
    private readonly ApiService _api;
    public SurveyReportService(ApiService api) { _api = api; }

    private class R<T>
    {
        public bool    success { get; set; }
        public T?      data    { get; set; }
        public string? message { get; set; }
    }

    public async Task<SurveyReportOverviewModel?> GetOverviewAsync(int surveyId)
    {
        var r = await _api.GetAsync<R<SurveyReportOverviewModel>>("SurveyAdmin/report", $"id={surveyId}");
        return r?.success == true ? r.data : null;
    }

    public async Task<SurveyReportQuizModel?> GetQuizAsync(int surveyId)
    {
        var r = await _api.GetAsync<R<SurveyReportQuizModel>>("SurveyAdmin/report/quiz", $"id={surveyId}");
        return r?.success == true ? r.data : null;
    }

    public async Task<List<SurveyReportIlliterateModel>> GetIlliterateAsync(int? surveyId, string? deptcd, string? linecd, string? workcd)
    {
        var qs = new List<string>();
        if (surveyId.HasValue)         qs.Add($"id={surveyId.Value}");
        if (!string.IsNullOrEmpty(deptcd)) qs.Add($"deptcd={Uri.EscapeDataString(deptcd)}");
        if (!string.IsNullOrEmpty(linecd)) qs.Add($"linecd={Uri.EscapeDataString(linecd)}");
        if (!string.IsNullOrEmpty(workcd)) qs.Add($"workcd={Uri.EscapeDataString(workcd)}");
        var r = await _api.GetAsync<R<List<SurveyReportIlliterateModel>>>("SurveyAdmin/report/illiterate", string.Join("&", qs));
        return r?.success == true ? r.data ?? new() : new();
    }

    public async Task<List<SurveyReportTextAnswerModel>> GetTextAnswersAsync(int questionId, int page, int pageSize)
    {
        var r = await _api.GetAsync<R<List<SurveyReportTextAnswerModel>>>(
            "SurveyAdmin/report/text-answers",
            $"qid={questionId}&page={page}&pageSize={pageSize}");
        return r?.success == true ? r.data ?? new() : new();
    }
}
