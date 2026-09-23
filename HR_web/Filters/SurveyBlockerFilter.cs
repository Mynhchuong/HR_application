using HR_web.API.Service;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace HR_web.Filters;

/// <summary>
/// Chặn app khi NV có survey ACTIVE chưa làm — redirect về /Survey/Do/{id}.
/// Controller riêng (survey/surveyadmin/surveyexempt) đăng ký vào AppGateRegistry để các gate khác
/// (Gift, ...) tự exempt — xem AppGateFilterBase.
/// </summary>
public class SurveyBlockerFilter : AppGateFilterBase<SurveyBlockerFilter.PendingSurvey>
{
    public record PendingSurvey(int SurveyId);

    private const string CACHE_PREFIX = "survey_pending_";

    static SurveyBlockerFilter()
    {
        AppGateRegistry.Register("survey", "surveyadmin", "surveyexempt");
    }

    private readonly SurveyService _survey;

    public SurveyBlockerFilter(SurveyService survey, IMemoryCache cache) : base(cache)
    {
        _survey = survey;
    }

    protected override string GateName => "survey";
    protected override string CachePrefix => CACHE_PREFIX;

    // Chỉ Admin (6) không cần làm survey — HR (5) vẫn phải làm.
    protected override bool AppliesTo(UserInfoModel user) => user.RoleId != 6;

    protected override async Task<PendingSurvey?> LoadPendingAsync(string empcd, UserInfoModel user)
    {
        var id = await _survey.GetOldestPendingSurveyIdAsync(empcd, user.RoleId);
        return id.HasValue && id.Value > 0 ? new PendingSurvey(id.Value) : null;
    }

    protected override RedirectToActionResult BuildRedirect(PendingSurvey pending) =>
        new("Do", "Survey", new { id = pending.SurveyId });

    /// <summary>Invalidate cache sau khi user submit / skip survey.</summary>
    public static void InvalidateUser(IMemoryCache cache, string empcd) =>
        AppGateRegistry.InvalidateCache(cache, CACHE_PREFIX, empcd);
}
