using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace HR_web.Controllers.Home;

// Admin panel /HomeAdmin — HR + Admin có quyền vào.
// HR chỉ thấy Banner + Greeting (bị giới hạn field), Admin thêm Rule + Audit.
[Authorize]
public class HomeAdminController : BaseController
{
    private readonly HomeAdminApiService _api;

    public HomeAdminController(HomeAdminApiService api) { _api = api; }

    private bool IsHrOrAdmin() => CurrentUser?.RoleName is "HR" or "Admin";
    private bool IsAdmin()     => CurrentUser?.RoleName == "Admin";

    private IActionResult Forbid403() =>
        Json(new { success = false, message = "Bạn không có quyền sử dụng chức năng này" });

    // ═══════════════ PAGES ═══════════════════════════════════════
    public IActionResult Index()
    {
        if (!IsHrOrAdmin()) return RedirectToAction("Index", "Home");
        ViewBag.IsAdmin = IsAdmin();
        return View();
    }

    public IActionResult Banner()
    {
        if (!IsHrOrAdmin()) return RedirectToAction("Index", "Home");
        ViewBag.IsAdmin = IsAdmin();
        return View();
    }

    public IActionResult Greeting()
    {
        if (!IsHrOrAdmin()) return RedirectToAction("Index", "Home");
        ViewBag.IsAdmin = IsAdmin();
        return View();
    }

    public IActionResult Audit()
    {
        if (!IsAdmin()) return RedirectToAction("Index", "Home");
        return View();
    }

    // ═══════════════ BANNER AJAX ═════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> BannerList()
    {
        if (!IsHrOrAdmin()) return Forbid403();
        var raw = await _api.BannerListRawAsync();
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BannerSave([FromBody] BannerSaveRequest req)
    {
        if (!IsHrOrAdmin()) return Forbid403();
        if (req == null) return Json(new { success = false, message = "Thiếu dữ liệu" });

        var payload = new
        {
            ID              = req.Id,
            IMAGE_FILE      = req.ImageFile,
            OVERLAY_TEXT_VI = req.OverlayTextVi,
            OVERLAY_TEXT_EN = req.OverlayTextEn,
            OVERLAY_POS     = req.OverlayPos ?? "BL",
            LINK_URL        = req.LinkUrl,
            LINK_TARGET     = req.LinkTarget ?? "_self",
            TARGET_ROLES    = req.TargetRoles,
            IS_DISMISSIBLE  = req.IsDismissible,
            PUBLISH_FROM    = req.PublishFrom,
            PUBLISH_TO      = req.PublishTo,
            LOGIN_USER      = CurrentUser?.EmpCd,
            LOGIN_NAME      = CurrentUser?.FullName,
            LOGIN_ROLE      = CurrentUser?.RoleName
        };
        var raw = await _api.BannerSaveRawAsync(payload);
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BannerDelete([FromBody] IdRequest req)
    {
        if (!IsAdmin()) return Forbid403(); // Chỉ Admin được xoá banner
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu ID" });

        var raw = await _api.BannerDeleteRawAsync(new
        {
            ID         = req.Id,
            LOGIN_USER = CurrentUser?.EmpCd,
            LOGIN_NAME = CurrentUser?.FullName,
            LOGIN_ROLE = CurrentUser?.RoleName
        });
        return Content(raw, "application/json");
    }

    // ═══════════════ GREETING AJAX ═══════════════════════════════
    [HttpGet]
    public async Task<IActionResult> GreetingList(string? slot = null)
    {
        if (!IsHrOrAdmin()) return Forbid403();
        var raw = await _api.GreetingListRawAsync(slot);
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GreetingSave([FromBody] GreetingSaveRequest req)
    {
        if (!IsHrOrAdmin()) return Forbid403();
        if (req == null) return Json(new { success = false, message = "Thiếu dữ liệu" });

        var raw = await _api.GreetingSaveRawAsync(new
        {
            ID         = req.Id,
            TIME_SLOT  = req.TimeSlot,
            MESSAGE_VI = req.MessageVi,
            MESSAGE_EN = req.MessageEn,
            EMOJI      = req.Emoji,
            IS_ACTIVE  = req.IsActive,
            LOGIN_USER = CurrentUser?.EmpCd,
            LOGIN_NAME = CurrentUser?.FullName,
            LOGIN_ROLE = CurrentUser?.RoleName
        });
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GreetingDelete([FromBody] IdRequest req)
    {
        if (!IsHrOrAdmin()) return Forbid403();
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu ID" });

        var raw = await _api.GreetingDeleteRawAsync(new
        {
            ID         = req.Id,
            LOGIN_USER = CurrentUser?.EmpCd,
            LOGIN_NAME = CurrentUser?.FullName,
            LOGIN_ROLE = CurrentUser?.RoleName
        });
        return Content(raw, "application/json");
    }

    // ═══════════════ RULE AJAX ═══════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> RuleList()
    {
        if (!IsAdmin()) return Forbid403();
        var raw = await _api.RuleListRawAsync();
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RuleSave([FromBody] RuleSaveRequest req)
    {
        if (!IsAdmin()) return Forbid403();
        if (req == null) return Json(new { success = false, message = "Thiếu dữ liệu" });

        var raw = await _api.RuleSaveRawAsync(new
        {
            TIME_SLOT  = req.TimeSlot,
            FROM_HOUR  = req.FromHour,
            TO_HOUR    = req.ToHour,
            LOGIN_USER = CurrentUser?.EmpCd,
            LOGIN_NAME = CurrentUser?.FullName
        });
        return Content(raw, "application/json");
    }

    // ═══════════════ AUDIT AJAX ══════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> AuditList(
        int page = 1, int pageSize = 30,
        string? entity = null, string? actor = null,
        string? dateFrom = null, string? dateTo = null)
    {
        if (!IsAdmin()) return Forbid403();
        var raw = await _api.AuditRawAsync(page, pageSize, entity, actor, dateFrom, dateTo);
        return Content(raw, "application/json");
    }

    // ═══════════════ REQUEST BODIES ══════════════════════════════
    public class IdRequest { public int Id { get; set; } }

    public class BannerSaveRequest
    {
        public int?     Id             { get; set; }
        public string   ImageFile      { get; set; } = "";
        public string?  OverlayTextVi  { get; set; }
        public string?  OverlayTextEn  { get; set; }
        public string?  OverlayPos     { get; set; }
        public string?  LinkUrl        { get; set; }
        public string?  LinkTarget     { get; set; }
        public string?  TargetRoles    { get; set; }
        public bool     IsDismissible  { get; set; } = true;
        public DateTime PublishFrom    { get; set; }
        public DateTime PublishTo      { get; set; }
    }

    public class GreetingSaveRequest
    {
        public int?    Id        { get; set; }
        public string  TimeSlot  { get; set; } = "";
        public string  MessageVi { get; set; } = "";
        public string? MessageEn { get; set; }
        public string? Emoji     { get; set; }
        public bool    IsActive  { get; set; } = true;
    }

    public class RuleSaveRequest
    {
        public string TimeSlot { get; set; } = "";
        public int    FromHour { get; set; }
        public int    ToHour   { get; set; }
    }
}
