using HR_web.API.Service;
using HR_web.Models.Policy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Policy;

[Authorize]
public class PolicyController : BaseController
{
    private readonly PolicyService _service;

    public PolicyController(PolicyService service)
    {
        _service = service;
    }

    // ─────────────────────────────────────────────
    // GET /Policy/Index  — worker xem quy định
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        var list = await _service.GetListAsync();
        return View(list);
    }

    // ─────────────────────────────────────────────
    // GET /Policy/Manage  — HR quản lý
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Manage()
    {
        var list = await _service.GetAdminListAsync();
        return View(list);
    }

    // ─────────────────────────────────────────────
    // GET /Policy/Edit?id=  — tạo mới hoặc sửa
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null || id == 0)
            return View(new CompanyPolicyModel { IS_ACTIVE = 1 });

        var model = await _service.GetByIdAsync(id.Value);
        if (model == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy quy định!";
            return RedirectToAction("Manage");
        }
        return View(model);
    }

    // ─────────────────────────────────────────────
    // POST /Policy/Edit
    // ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CompanyPolicyModel model)
    {
        if (string.IsNullOrWhiteSpace(model.CATEGORY) ||
            string.IsNullOrWhiteSpace(model.TITLE) ||
            string.IsNullOrWhiteSpace(model.CONTENT))
        {
            TempData["ErrorMessage"] = "Vui lòng điền đầy đủ thông tin!";
            return View(model);
        }

        var request = new SavePolicyRequest
        {
            ID            = model.ID == 0 ? null : model.ID,
            CATEGORY      = model.CATEGORY.Trim(),
            TITLE         = model.TITLE.Trim(),
            CONTENT       = model.CONTENT,
            DISPLAY_ORDER = model.DISPLAY_ORDER,
            IS_ACTIVE     = model.IS_ACTIVE,
            LOGIN_USER    = CurrentUser!.EmpCd
        };

        var (success, message) = await _service.SaveAsync(request);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }

    // ─────────────────────────────────────────────
    // POST /Policy/Toggle?id=
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Toggle(int id)
    {
        var (success, message) = await _service.ToggleAsync(id, CurrentUser!.EmpCd);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }
}
