using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR_web.Helpers;

namespace HR_web.Controllers.Notification;

[Authorize]
public class NotificationController : BaseController
{
    private readonly NotificationService _notiSvc;

    public NotificationController(NotificationService notiSvc)
    {
        _notiSvc = notiSvc;
    }

    // GET /Notification/Index
    // Expat vào bằng đường dẫn chung "/Notification" (app mở khi bấm thông báo đẩy) -> dùng bản tiếng Anh.
    public IActionResult Index() =>
        CurrentUser != null && CurrentUser.RoleName == "Expat" ? View(ExpatViews.Notification) : View();

    // GET /Notification/IndexForExpat
    public IActionResult IndexForExpat() => View(ExpatViews.Notification);

    // GET /Notification/Go?action=...&target=...&noti=...
    //
    // Điểm chuyển hướng DUY NHẤT từ 1 thông báo đẩy tới đúng màn hình của nó.
    //
    // Trước đây app native bấm thông báo nào cũng chỉ vào hộp thư chung, người nhận phải tự đi tìm
    // đơn — quản lý nhận thông báo "có 5 đơn chờ duyệt" vẫn phải lục menu. Bảng ánh xạ mã ->
    // đường dẫn vốn nằm trong JS của Notification/Index.cshtml, nếu chép sang iOS + Android nữa là
    // thành 3 bản, sửa 1 chỗ quên 2 chỗ. Đặt ở server thì native chỉ cần mở đúng 1 URL này.
    //
    // ⚠️ Tham số URL tên "action" TRÙNG route value {action} (= "Go") — để binding mặc định thì MVC lấy route
    // value trước query string → action luôn = "Go" → mọi thông báo rơi về trang Thông báo (lỗi gốc của
    // "bấm thông báo cứ nhảy vào Notification/Index", cả app lẫn web, xác nhận bằng app thử 2026-11-20).
    // Ép đọc từ QUERY STRING; giữ nguyên tên "?action=" vì app iOS/Android đã phát hành đang gửi đúng tên này.
    [HttpGet]
    public async Task<IActionResult> Go([FromQuery(Name = "action")] string? linkAction,
                                        [FromQuery(Name = "target")] string? target,
                                        [FromQuery(Name = "noti")]   decimal? noti)
    {
        var action = linkAction;
        // Bấm vào thông báo = đã đọc, giống hệt khi bấm trong hộp thư.
        if (noti.HasValue && noti.Value > 0 && CurrentUser?.EmpCd is string emp && emp.Length > 0)
        {
            try { await _notiSvc.MarkReadAsync(noti.Value, emp); } catch { }
        }

        // Bảng mã -> trang: Helpers/NotificationLinkHelper (nguồn duy nhất cho web + app điện thoại).
        // Không có trang đích / link ngoài (không chuyển tiếp thẳng — chống chuyển hướng mở) → mở trang
        // Thông báo và tự bung đúng thông báo đó (?open=).
        var path = NotificationLinkHelper.ResolvePath(action, target, CurrentUser?.RoleName == "Expat");
        if (path == null)
            return Redirect(Url.Content("~/Notification/Index") + (noti.HasValue ? "?open=" + IdStr(noti.Value) : ""));
        return Redirect(Url.Content(path));
    }

    // Mã thông báo dạng số nguyên, KHÔNG theo văn hoá vi-VN: Oracle có lúc trả ID kèm phần lẻ (668783.0)
    // → in ra "668783,0" làm /Notification/Go không đọc được noti (bấm không đánh dấu đã đọc).
    private static string IdStr(decimal id) => decimal.Truncate(id).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    // GET /Notification/GetPage  (AJAX)
    [HttpGet]
    public async Task<IActionResult> GetPage(int page = 1, int page_size = 20, string? date_from = null, string? date_to = null)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false });

        var itemsTask  = _notiSvc.GetMyAsync(CurrentUser.EmpCd, page, page_size, date_from, date_to);
        var unreadTask = page == 1 ? _notiSvc.GetUnreadCountAsync(CurrentUser.EmpCd) : Task.FromResult(0);
        await Task.WhenAll(itemsTask, unreadTask);

        var items  = itemsTask.Result;
        var unread = page == 1 ? unreadTask.Result : 0;
        // Tính sẵn đường dẫn mở cho từng thông báo — chuông + trang Thông báo chỉ việc dùng (không tự map nữa)
        bool isExpat = CurrentUser.RoleName == "Expat";
        foreach (var n in items)
        {
            if (NotificationLinkHelper.IsExternal(n.LINK_ACTION)) { n.LINK_URL = n.LINK_ACTION; n.LINK_EXT = true; continue; }
            if (NotificationLinkHelper.ResolvePath(n.LINK_ACTION, n.TARGET_VAL, isExpat) != null)
                // Ghép tay: route value tên "action" sẽ bị Url.Action hiểu là TÊN ACTION
                n.LINK_URL = Url.Content("~/Notification/Go") + "?action=" + Uri.EscapeDataString(n.LINK_ACTION ?? "")
                           + "&target=" + Uri.EscapeDataString(n.TARGET_VAL ?? "") + "&noti=" + IdStr(n.ID);
        }
        return Json(new { success = true, data = items, unread });
    }

    // GET /Notification/UnreadCount  (AJAX — dùng cho bell badge)
    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { count = 0 });

        var count = await _notiSvc.GetUnreadCountAsync(CurrentUser.EmpCd);
        return Json(new { count });
    }

    // POST /Notification/MarkRead  (AJAX)
    [HttpPost]
    public async Task<IActionResult> MarkRead(decimal notiId)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false });

        var ok = await _notiSvc.MarkReadAsync(notiId, CurrentUser.EmpCd);
        return Json(new { success = ok });
    }

    // POST /Notification/MarkAllRead  (AJAX)
    [HttpPost]
    public async Task<IActionResult> MarkAllRead()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false });

        var ok = await _notiSvc.MarkAllReadAsync(CurrentUser.EmpCd);
        return Json(new { success = ok });
    }

    // GET /Notification/PushSetting  (AJAX) — trạng thái "tắt nhận thông báo đẩy" của chính người đăng nhập
    [HttpGet]
    public async Task<IActionResult> PushSetting()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false });
        var raw = await _notiSvc.GetPushSettingRawAsync(CurrentUser.EmpCd);
        return Content(raw, "application/json");
    }

    // POST /Notification/SetPushSetting?muted=true|false  (AJAX) — chỉ đổi được của CHÍNH MÌNH
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPushSetting(bool muted)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false });
        var raw = await _notiSvc.SetPushSettingRawAsync(CurrentUser.EmpCd, muted);
        return Content(raw, "application/json");
    }

    // POST /Notification/RegisterToken  (gọi từ mobile app qua JS bridge)
    [HttpPost]
    public async Task<IActionResult> RegisterToken([FromBody] TokenRegistrationBody body)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd) || string.IsNullOrEmpty(body?.Token))
            return Json(new { success = false });

        var ok = await _notiSvc.RegisterTokenAsync(CurrentUser.EmpCd, body.Token, body.OsType ?? "ANDROID");
        return Json(new { success = ok });
    }

    // POST /Notification/UnregisterToken  (gọi khi user logout)
    // Cho phép AllowAnonymous vì user có thể đã hết session khi logout xong mới gọi.
    [HttpPost]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> UnregisterToken([FromBody] TokenRegistrationBody body)
    {
        if (string.IsNullOrEmpty(body?.Token))
            return Json(new { success = false });

        // Nếu còn session NV thì truyền EMPCD (chính xác hơn);
        // nếu không thì chỉ xoá theo token (vẫn an toàn vì 1 token = 1 device).
        var ok = await _notiSvc.UnregisterTokenAsync(CurrentUser?.EmpCd, body.Token);
        return Json(new { success = ok });
    }

    public class TokenRegistrationBody
    {
        public string? Token  { get; set; }
        public string? OsType { get; set; }
    }
}
