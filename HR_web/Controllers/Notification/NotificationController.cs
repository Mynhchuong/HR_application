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
    [HttpGet]
    public async Task<IActionResult> Go(string? action, string? target, decimal? noti)
    {
        // Bấm vào thông báo = đã đọc, giống hệt khi bấm trong hộp thư.
        if (noti.HasValue && noti.Value > 0 && CurrentUser?.EmpCd is string emp && emp.Length > 0)
        {
            try { await _notiSvc.MarkReadAsync(noti.Value, emp); } catch { }
        }

        return Redirect(Url.Content(ResolveLink(action, target)));
    }

    // Giữ ĐÚNG thứ tự nhánh như JS trong Notification/Index.cshtml — sửa 1 bên phải sửa cả 2.
    private static string ResolveLink(string? action, string? target)
    {
        var a = action ?? "";

        if (a.StartsWith("BULLETIN_CMT:"))
            return "~/Bulletin/Detail/" + Uri.EscapeDataString(a["BULLETIN_CMT:".Length..]);

        if (a == "BULLETIN" && !string.IsNullOrEmpty(target))
            return "~/Bulletin/Detail/" + Uri.EscapeDataString(target);

        if (a == "SURVEY" && !string.IsNullOrEmpty(target))
            return "~/Survey/Do/" + Uri.EscapeDataString(target);

        if (a.StartsWith("inquiry/"))
            return "~/EmployeeInquiry/Chat?id=" + Uri.EscapeDataString(a["inquiry/".Length..]);

        // Link ngoài: KHÔNG chuyển tiếp thẳng (hở lỗ chuyển hướng mở — ai gửi được thông báo là
        // dắt người dùng đi đâu cũng được). Đưa về hộp thư, người dùng tự bấm như trên web.
        if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
         || a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "~/Notification/Index";

        return a switch
        {
            "GP_MY"               => "~/GatePass/GpMyRequests",
            "GP_MANAGE"           => "~/GatePass/GpListForSupervisor",
            "LEAVE_MY"            => "~/Leave/LeaveMyRequests",
            "LEAVE_MANAGE"        => "~/Leave/LeaveApprovalList",
            "LEAVE_TEAM"          => "~/Leave/TeamSchedule",
            "LEAVE_ASSIGNED"      => "~/Leave/LeaveMyRequests",
            "OT_SIGN"             => "~/OT/OtConfirmForm",
            "ATT_CONFIRM_WORKER"  => "~/AttendanceConfirm/WorkerForm",
            "ATT_CONFIRM_MANAGER" => "~/AttendanceConfirm/Index",
            "GIFT_MY"             => "~/Gift/MyGifts",
            "PAYSLIP"             => "~/Payslip/Index",
            "AI_CSR"              => "~/AiCsr/Chat",
            _                     => "~/Notification/Index"   // mã lạ/không có -> hộp thư như cũ
        };
    }

    // GET /Notification/GetPage  (AJAX)
    [HttpGet]
    public async Task<IActionResult> GetPage(int page = 1, int page_size = 20)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
            return Json(new { success = false });

        var itemsTask  = _notiSvc.GetMyAsync(CurrentUser.EmpCd, page, page_size);
        var unreadTask = page == 1 ? _notiSvc.GetUnreadCountAsync(CurrentUser.EmpCd) : Task.FromResult(0);
        await Task.WhenAll(itemsTask, unreadTask);

        var items  = itemsTask.Result;
        var unread = page == 1 ? unreadTask.Result : 0;
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
