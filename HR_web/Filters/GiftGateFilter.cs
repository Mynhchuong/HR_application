using HR_web.API.Service;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace HR_web.Filters;

/// <summary>
/// Chặn app khi NV có quà ở trạng thái PENDING_CONFIRM (HR đã phát, đang chờ NV xác nhận đã nhận).
/// Controller riêng (gift/giftadmin) đăng ký vào AppGateRegistry để các gate khác (Survey, ...) tự
/// exempt — xem AppGateFilterBase. KHÔNG có ngoại lệ role: Admin/HR cũng là NV nên vẫn phải nhận
/// quà và xác nhận như mọi người (quyết định 2026-09-22, không được thêm role bypass).
/// </summary>
public class GiftGateFilter : AppGateFilterBase<GiftGateFilter.PendingGift>
{
    public record PendingGift(int RecipientId);

    private const string CACHE_PREFIX = "gift_pending_";

    static GiftGateFilter()
    {
        AppGateRegistry.RegisterDestination("gift");
        AppGateRegistry.RegisterOwnAdmin("gift", "giftadmin");
    }

    private readonly GiftService _gift;

    public GiftGateFilter(GiftService gift, IMemoryCache cache) : base(cache)
    {
        _gift = gift;
    }

    protected override string GateName => "gift";
    protected override string CachePrefix => CACHE_PREFIX;

    protected override async Task<PendingGift?> LoadPendingAsync(string empcd, UserInfoModel user)
    {
        var id = await _gift.GetOldestPendingIdAsync(empcd);
        return id.HasValue && id.Value > 0 ? new PendingGift(id.Value) : null;
    }

    protected override RedirectToActionResult BuildRedirect(PendingGift pending) =>
        new("MyGifts", "Gift", new { id = pending.RecipientId });

    /// <summary>Invalidate cache sau khi NV xác nhận nhận quà.</summary>
    public static void InvalidateUser(IMemoryCache cache, string empcd) =>
        AppGateRegistry.InvalidateCache(cache, CACHE_PREFIX, empcd);
}
