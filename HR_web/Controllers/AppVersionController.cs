using HR_web.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// GET /AppVersion/Check?platform=ios&version=1.2 — app Android/iOS hỏi có phải ép cập nhật không.
//
// Bảo mật 2026-11-21: trước đây app gọi THẲNG HR_api kèm API key gắn cứng trong bản cài → ai giải nén
// APK/IPA là có key gọi được toàn bộ HR_api. Bản app mới gọi endpoint này thay thế, KHÔNG cần key: HR_web
// tự gọi HR_api bằng key nằm trên server. Mã NV lấy từ cookie phiên (app gửi kèm cookie của WebView), không
// nhận từ tham số — nên chỉ ra đúng link cập nhật iOS riêng của người đang đăng nhập. Chưa đăng nhập thì
// vẫn trả kết quả, chỉ là link chung.
[AllowAnonymous]
public class AppVersionController : BaseController
{
    private readonly ApiService _api;
    public AppVersionController(ApiService api) => _api = api;

    [HttpGet]
    public async Task<IActionResult> Check(string platform = "android", string? version = null)
    {
        var q = $"platform={Uri.EscapeDataString(platform ?? "android")}&version={Uri.EscapeDataString(version ?? "")}";
        var empcd = User.Identity?.IsAuthenticated == true ? CurrentUser?.EmpCd : null;
        if (!string.IsNullOrEmpty(empcd)) q += "&empcd=" + Uri.EscapeDataString(empcd);

        var res = await _api.GetAsync_Raw("AppVersion/check", q);
        if (res == null || !res.IsSuccessStatusCode)
            return Json(new { success = false });
        return Content(await res.Content.ReadAsStringAsync(), "application/json");
    }
}
