using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    public IActionResult Index() => View();

    // GET /Notification/GetPage  (AJAX)
    [HttpGet]
    public async Task<IActionResult> GetPage(int page = 1, int page_size = 20)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false });

        var items = await _notiSvc.GetMyAsync(CurrentUser.EmpCd, page, page_size);
        var unread = items.Count(n => n.IS_READ == 0);
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

    // POST /Notification/RegisterToken  (gọi từ mobile app qua JS bridge)
    [HttpPost]
    public async Task<IActionResult> RegisterToken([FromBody] TokenRegistrationBody body)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd) || string.IsNullOrEmpty(body?.Token))
            return Json(new { success = false });

        var ok = await _notiSvc.RegisterTokenAsync(CurrentUser.EmpCd, body.Token, body.OsType ?? "ANDROID");
        return Json(new { success = ok });
    }

    public class TokenRegistrationBody
    {
        public string? Token  { get; set; }
        public string? OsType { get; set; }
    }
}
