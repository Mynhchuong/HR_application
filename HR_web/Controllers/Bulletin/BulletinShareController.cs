using HR_web.API.Service;
using HR_web.Models.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Bulletin;

/// <summary>
/// Gửi bản tin qua EMAIL và đăng FACEBOOK (2026-11-20) — 2 trang quản lý riêng, có xem trước + lưu nháp.
/// Mở từ BulletinAdmin/Manage. Lean/CSR chỉ gửi được bản tin của phòng mình (API kiểm). Cài đặt Fanpage
/// (token) chỉ HR/Admin.
/// </summary>
[Authorize(Roles = "Admin,HR,CSR,Lean")]
public class BulletinShareController : BaseController
{
    private readonly BulletinShareService _svc;
    public BulletinShareController(BulletinShareService svc) { _svc = svc; }

    private string Me => CurrentUser?.EmpCd ?? "";
    private bool IsHrAdmin => CurrentUser.HasAnyRole("HR", "Admin");
    private ContentResult J(string json) => Content(json, "application/json");

    // ── Trang ──
    public IActionResult Email(int id)
    {
        ViewBag.BulletinId = id;
        ViewBag.MyEmail = !string.IsNullOrWhiteSpace(CurrentUser?.WorkEmail) ? CurrentUser!.WorkEmail : CurrentUser?.PersonalEmail;
        return View();
    }
    // Đăng Facebook: TẠM TẮT (HR chốt 2026-11-20 chỉ dùng Email — kết nối Fanpage phức tạp). Code API + view
    // Facebook/FacebookSettings giữ lại; muốn bật lại: đổi FacebookEnabled = true + thêm lại nút ở Manage/Email.
    private const bool FacebookEnabled = false;
    public IActionResult Facebook(int id)
    {
        if (!FacebookEnabled) return RedirectToAction("Email", new { id });
        ViewBag.BulletinId = id; ViewBag.IsHrAdmin = IsHrAdmin; return View();
    }
    public IActionResult FacebookSettings()
    {
        if (!FacebookEnabled) return RedirectToAction("Manage", "BulletinAdmin");
        if (!IsHrAdmin) { TempData["ErrorMessage"] = "Chỉ HR/Admin được cài đặt Fanpage."; return RedirectToAction("Manage", "BulletinAdmin"); }
        return View();
    }

    // ── AJAX ──
    [HttpGet] public async Task<IActionResult> Data(int id) => J(await _svc.GetAsync(id.ToString(), "loginUser=" + Uri.EscapeDataString(Me)));
    [HttpGet] public async Task<IActionResult> EmailPreview(int id) => J(await _svc.GetAsync(id + "/email-preview", "loginUser=" + Uri.EscapeDataString(Me)));
    [HttpGet] public async Task<IActionResult> History(int id, string? channel) =>
        J(await _svc.GetAsync(id + "/history", "channel=" + Uri.EscapeDataString(channel ?? "EMAIL") + "&loginUser=" + Uri.EscapeDataString(Me)));
    [HttpGet] public async Task<IActionResult> Status(string? ids) => J(await _svc.GetAsync("status", "ids=" + Uri.EscapeDataString(ids ?? "")));

    public class SaveVm
    {
        public int BulletinId { get; set; } public string Channel { get; set; } = "";
        public string? Subject { get; set; } public string? Recipients { get; set; }
        public string? IntroVi { get; set; } public string? IntroEn { get; set; }
        public string? FbMessage { get; set; } public bool FbWithImage { get; set; } = true;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveVm vm) => J(await _svc.PostAsync("save", new
    {
        vm.BulletinId, vm.Channel, vm.Subject, vm.Recipients, vm.IntroVi, vm.IntroEn, vm.FbMessage, vm.FbWithImage, LoginUser = Me
    }));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SendEmail(int id, string? testTo = null)
    {
        // testTo có giá trị = gửi thử tới đúng 1 địa chỉ (không đánh dấu đã gửi)
        return J(await _svc.PostAsync("send-email", new { BulletinId = id, LoginUser = Me, TestTo = string.IsNullOrWhiteSpace(testTo) ? null : testTo.Trim() }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PostFb(int id) => !FacebookEnabled
        ? Json(new { success = false, message = "Chức năng đăng Facebook đang tắt" })
        : J(await _svc.PostAsync("post-fb", new { BulletinId = id, LoginUser = Me }));

    // ── Cài đặt Fanpage (HR/Admin) ──
    [HttpGet]
    public async Task<IActionResult> FbConfig() => IsHrAdmin ? J(await _svc.GetAsync("fb-config")) : Json(new { success = false, message = "Không có quyền" });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFbConfig(string? pageId, string? token) =>
        IsHrAdmin ? J(await _svc.PostAsync("fb-config", new { PageId = pageId, Token = token, LoginUser = Me })) : Json(new { success = false, message = "Không có quyền" });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TestFb() => IsHrAdmin ? J(await _svc.PostAsync("fb-test", new { })) : Json(new { success = false, message = "Không có quyền" });
}
