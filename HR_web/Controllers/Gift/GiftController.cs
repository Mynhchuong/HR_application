using HR_web.API.Service;
using HR_web.Filters;
using HR_web.Models.Gift;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace HR_web.Controllers.Gift;

// User-facing MVC. Route "gift" đã whitelist trong GiftGateFilter nên NV có quà pending
// không bị vòng lặp redirect (giống SurveyController với SurveyBlockerFilter).
[Authorize]
public class GiftController : BaseController
{
    private readonly GiftService _svc;
    private readonly IMemoryCache _cache;

    public GiftController(GiftService svc, IMemoryCache cache)
    {
        _svc = svc;
        _cache = cache;
    }

    // GET /Gift/MyGifts — trang xác nhận nhận quà, đích redirect của GiftGateFilter
    public IActionResult MyGifts(int? id) => View();

    // GET /Gift/GetMyPending — badge sidebar + dữ liệu hiển thị ở MyGifts
    [HttpGet]
    public async Task<IActionResult> GetMyPending()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, count = 0, data = new List<object>() });
        var result = await _svc.GetMyPendingAsync(CurrentUser.EmpCd);
        return Json(result);
    }

    // GET /Gift/CheckPending — AJAX re-check khi WebView phục hồi trang từ back-forward cache
    // (nút back cứng Android) — copy y hệt SurveyController.CheckPending.
    [HttpGet]
    public async Task<IActionResult> CheckPending()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { pendingId = (int?)null });
        var pendingId = await _svc.GetOldestPendingIdAsync(CurrentUser.EmpCd);
        return Json(new { pendingId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceipt([FromBody] GiftConfirmReceiptRequest req)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        req.EMPCD = CurrentUser.EmpCd; // NV chỉ xác nhận được cho chính mình

        var result = await _svc.ConfirmReceiptAsync(req);
        if (result.OK)
            GiftGateFilter.InvalidateUser(_cache, CurrentUser.EmpCd);

        return Json(result);
    }
}
