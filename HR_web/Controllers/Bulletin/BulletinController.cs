using HR_web.API.Service;
using HR_web.Models.Bulletin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Bulletin;

[Authorize]
public class BulletinController : BaseController
{
    private readonly BulletinService _service;

    public BulletinController(BulletinService service)
    {
        _service = service;
    }

    // Codebase chuẩn dùng RoleName ("Admin"/"HR"/"Expat") — không phải RoleId.
    // [Authorize(Roles="Admin,HR")] cũng so với RoleName.
    private bool IsHrAdmin() =>
        CurrentUser?.RoleName == "HR" || CurrentUser?.RoleName == "Admin";

    // ─────────────────────────────────────────────
    // GET /Bulletin  — list bản tin đang hiển thị
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        var list = await _service.GetListAsync();
        return View(list);
    }

    // ─────────────────────────────────────────────
    // GET /Bulletin/Detail/{id}
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Detail(int id)
    {
        var empcd = CurrentUser?.EmpCd;
        var item  = await _service.GetByIdAsync(id, empcd);

        if (item == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy bản tin";
            return RedirectToAction("Index");
        }

        // Fix G10: worker không được xem DRAFT — HR vẫn cho qua (Preview riêng).
        if (item.IS_PUBLISHED == 0 && !IsHrAdmin())
        {
            TempData["ErrorMessage"] = "Bản tin chưa được đăng";
            return RedirectToAction("Index");
        }

        if (item.IS_ACTIVE == 0)
        {
            TempData["ErrorMessage"] = "Bản tin đã bị ẩn";
            return RedirectToAction("Index");
        }

        ViewBag.IsHrAdmin = IsHrAdmin();
        return View(item);
    }

    // ─────────────────────────────────────────────
    // GET /Bulletin/Comments/{id}  — partial reload comment list
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Comments(int id)
    {
        var list = await _service.GetCommentsAsync(id);
        return Json(new { success = true, data = list });
    }

    // ─────────────────────────────────────────────
    // POST /Bulletin/AddComment
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int bulletinId, string content)
    {
        if (CurrentUser == null)
            return Json(new { success = false, message = "Bạn cần đăng nhập" });

        var req = new AddCommentRequest
        {
            BULLETIN_ID = bulletinId,
            EMPCD       = CurrentUser.EmpCd,
            CONTENT     = content ?? ""
        };
        var (ok, msg, id) = await _service.AddCommentAsync(req);
        return Json(new { success = ok, message = msg, id });
    }

    // ─────────────────────────────────────────────
    // POST /Bulletin/DeleteComment
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteComment(int id)
    {
        if (CurrentUser == null)
            return Json(new { success = false, message = "Bạn cần đăng nhập" });

        var (ok, msg) = await _service.DeleteCommentAsync(id, CurrentUser.EmpCd, IsHrAdmin());
        return Json(new { success = ok, message = msg });
    }

    // ─────────────────────────────────────────────
    // POST /Bulletin/React
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> React(int bulletinId, string reaction)
    {
        if (CurrentUser == null)
            return Json(new { success = false, message = "Bạn cần đăng nhập" });

        var req = new ReactRequest
        {
            BULLETIN_ID = bulletinId,
            EMPCD       = CurrentUser.EmpCd,
            REACTION    = reaction ?? ""
        };
        var (ok, msg, action) = await _service.ReactAsync(req);
        return Json(new { success = ok, message = msg, action });
    }
}
