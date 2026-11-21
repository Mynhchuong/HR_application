using HR_web.API.Service;
using HR_web.Helpers;
using HR_web.Models.Account;
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
///   • Ảnh nhân viên  — người duyệt bật/tắt ảnh NV ở các trang duyệt (HR_USERS.SHOW_EMP_PHOTO, xem wwwroot/js/emp-photo.js)
///   • Tài khoản      — liên kết sang trang Hồ sơ (đổi mật khẩu / ảnh chữ ký), không nhân đôi chức năng
///
/// Thêm tuỳ chọn mới: thêm 1 khối trong Views/Settings/Index.cshtml + 1 action lưu ở đây.
/// </summary>
[Authorize]
public class SettingsController : BaseController
{
    private readonly NotificationService _noti;
    private readonly AccountService _account;
    private readonly Microsoft.AspNetCore.Antiforgery.IAntiforgery _antiforgery;

    public SettingsController(NotificationService noti, AccountService account,
                              Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery)
    {
        _noti = noti;
        _account = account;
        _antiforgery = antiforgery;
    }

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

        // Ảnh NV ở trang duyệt: chỉ người có duyệt/quản lý người khác. Đọc lại từ DB để đồng bộ phiên
        // (vd vừa đổi trên điện thoại, máy tính vẫn giữ cookie cũ) — mở trang Cài đặt là khớp lại.
        bool photoAvailable = false;
        if (CurrentUser.CanUseEmpPhoto() && !string.IsNullOrEmpty(empCd))
        {
            var (avail, show) = await _account.GetEmpPhotoPrefAsync(empCd);
            photoAvailable = avail;
            if (avail && show != CurrentUser!.ShowEmpPhoto)
            {
                var u = CurrentUser!;
                u.ShowEmpPhoto = show;
                await AuthHelper.UpdateUserSessionAsync(HttpContext, u);
            }
        }
        ViewBag.EmpPhotoAvailable = photoAvailable;
        ViewBag.EmpPhotoOn        = CurrentUser.EmpPhotoOn();
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

    // POST /Settings/SetEmpPhoto (AJAX) — show = true: hiện ảnh NV ở các trang duyệt.
    // Lưu DB xong mới cập nhật cookie phiên (giống Account/SetLanguage): DB không lưu được thì không giả vờ đã đổi.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEmpPhoto(bool show)
    {
        var user = CurrentUser;
        if (user == null || string.IsNullOrEmpty(user.EmpCd))
            return Json(new { success = false, message = "Chưa đăng nhập" });
        if (!user.CanUseEmpPhoto())
            return Json(new { success = false, message = "Không áp dụng cho tài khoản này" });

        if (!await _account.SetEmpPhotoPrefAsync(user.EmpCd, show))
            return Json(new { success = false });

        user.ShowEmpPhoto = show;
        await AuthHelper.UpdateUserSessionAsync(HttpContext, user);
        // Phiên vừa cấp lại → token trên trang đã cũ; trả token mới để lần gạt kế tiếp (kể cả công tắc
        // Thông báo cùng trang) không bị "Thao tác đã hết hiệu lực".
        var token = _antiforgery.GetAndStoreTokens(HttpContext).RequestToken;
        return Json(new { success = true, show, token });
    }
}
