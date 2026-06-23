using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Notification;

[Authorize]
public class AdminNotiController : BaseController
{
    private readonly NotificationService _notiSvc;

    public AdminNotiController(NotificationService notiSvc)
    {
        _notiSvc = notiSvc;
    }

    private bool IsAdmin() => CurrentUser?.RoleName == "Admin";

    private IActionResult Forbid403() =>
        Json(new { success = false, message = "Bạn không có quyền sử dụng chức năng này" });

    // ─── Pages ───────────────────────────────────────────────────────
    public IActionResult Index()
    {
        if (!IsAdmin()) return RedirectToAction("Index", "Home");
        return View();
    }

    public IActionResult Create()
    {
        if (!IsAdmin()) return RedirectToAction("Index", "Home");
        return View();
    }

    // ─── AJAX: Send multi notification ───────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send([FromBody] AdminSendRequest req)
    {
        if (!IsAdmin()) return Forbid403();
        if (string.IsNullOrWhiteSpace(req?.Title) || string.IsNullOrWhiteSpace(req?.Body))
            return Json(new { success = false, message = "Vui lòng nhập tiêu đề và nội dung" });

        var targets = (req.Targets ?? new()).Where(t => !string.IsNullOrWhiteSpace(t.Val)).ToList();

        var payload = new
        {
            TITLE       = req.Title.Trim(),
            BODY        = req.Body.Trim(),
            TITLE_EN    = (string?)null,
            BODY_EN     = (string?)null,
            LINK_ACTION = string.IsNullOrWhiteSpace(req.LinkAction) ? null : req.LinkAction!.Trim(),
            CREATED_BY  = CurrentUser?.EmpCd,
            PRIORITY    = (req.Priority == "HIGH") ? "HIGH" : "NORMAL",
            SOURCE      = "ADMIN",
            SEND_ALL_COMPANY = req.SendAllCompany,
            TARGETS     = targets.Select(t => new { TYPE = t.Type, VAL = t.Val }).ToList()
        };

        var raw = await _notiSvc.SendMultiRawAsync(payload);
        return Content(raw, "application/json");
    }

    // ─── AJAX: Search employees ──────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> SearchEmp(string? q = null, string? dept = null, string? line = null, string? work = null, int page = 1, int pageSize = 50)
    {
        if (!IsAdmin()) return Forbid403();
        var raw = await _notiSvc.SearchEmpRawAsync(q, dept, line, work, page, pageSize);
        return Content(raw, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> SearchEmpCodes(string? q = null, string? dept = null, string? line = null, string? work = null)
    {
        if (!IsAdmin()) return Forbid403();
        var raw = await _notiSvc.SearchEmpCodesRawAsync(q, dept, line, work);
        return Content(raw, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> Lookup(string type = "DEPT")
    {
        if (!IsAdmin()) return Forbid403();
        var raw = await _notiSvc.LookupRawAsync(type);
        return Content(raw, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> Sent(int page = 1, int pageSize = 30)
    {
        if (!IsAdmin()) return Forbid403();
        var raw = await _notiSvc.AdminSentRawAsync(page, pageSize);
        return Content(raw, "application/json");
    }

    // ─── Inner request models ────────────────────────────────────────
    public class AdminSendRequest
    {
        public string  Title          { get; set; } = "";
        public string  Body           { get; set; } = "";
        public string? LinkAction     { get; set; }
        public string? Priority       { get; set; } = "NORMAL";  // HIGH / NORMAL
        public bool    SendAllCompany { get; set; }
        public List<TargetItem>? Targets { get; set; }

        public class TargetItem
        {
            public string Type { get; set; } = "EMPCD";
            public string Val  { get; set; } = "";
        }
    }
}
