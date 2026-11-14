using HR_web.API.Service;
using HR_web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace HR_web.Controllers.Settings;

/// <summary>
/// Trang "Cài đặt" của từng nhân viên (yêu cầu 2026-11-13).
/// Chỗ gom các tuỳ chọn cá nhân, để sau này thêm công tắc mới thì bỏ vào đây chứ không rải mỗi nơi một ít.
///
/// Hiện có:
///   • Thông báo đẩy  — HR_USERS.PUSH_MUTED (backend đã có sẵn từ trước, trước đây KHÔNG có màn hình nào gọi)
///   • Ngôn ngữ       — chỉ Expat, dùng lại Account/SetLanguage (HR_USERS.LANG_PREF)
///   • Tài khoản      — liên kết sang trang Hồ sơ (đổi mật khẩu / ảnh chữ ký), không nhân đôi chức năng
///
/// Thêm tuỳ chọn mới: thêm 1 khối trong Views/Settings/Index.cshtml + 1 action lưu ở đây.
/// </summary>
[Authorize]
public class SettingsController : BaseController
{
    private readonly NotificationService _noti;

    public SettingsController(NotificationService noti) => _noti = noti;

    // GET /Settings/Index
    public async Task<IActionResult> Index()
    {
        bool available = false, muted = false;
        var empCd = CurrentUser?.EmpCd;
        if (!string.IsNullOrEmpty(empCd))
        {
            try
            {
                var j = JObject.Parse(await _noti.GetPushSettingRawAsync(empCd));
                // available = false nghĩa là cột PUSH_MUTED chưa được thêm vào DB -> ẩn hẳn công tắc,
                // không hiện ra rồi bấm không ăn.
                available = j["available"]?.Value<bool>() ?? false;
                muted     = j["muted"]?.Value<bool>() ?? false;
            }
            catch { /* API lỗi -> coi như chưa bật, ẩn công tắc */ }
        }

        ViewBag.PushAvailable = available;
        ViewBag.PushMuted     = muted;
        return View();
    }

    // POST /Settings/SetPush (AJAX) — muted = true là TẮT nhận thông báo
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPush(bool muted)
    {
        var empCd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empCd))
            return Json(new { success = false, message = "Chưa đăng nhập" });

        return Content(await _noti.SetPushSettingRawAsync(empCd, muted), "application/json");
    }
}
