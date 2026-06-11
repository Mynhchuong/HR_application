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
    // GET /Policy/Detail?ids=8,9,10
    // ─────────────────────────────────────────────
    public async Task<IActionResult> Detail(string ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return RedirectToAction("Index");

        var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                        .Where(n => n > 0).ToList();

        if (!idList.Any())
            return RedirectToAction("Index");

        var items = new List<CompanyPolicyModel>();
        foreach (var id in idList)
        {
            var item = await _service.GetByIdAsync(id);
            if (item != null) items.Add(item);
        }

        if (!items.Any())
            return RedirectToAction("Index");

        // Build prev/next navigation within same category
        var allPolicies = await _service.GetListAsync();
        var category = items.First().CATEGORY;
        var titleGroups = allPolicies
            .Where(p => p.CATEGORY == category)
            .GroupBy(p => p.TITLE)
            .OrderBy(tg => tg.Min(p => p.DISPLAY_ORDER))
            .ToList();

        var currentTitle = items.First().TITLE;
        var currentIdx = titleGroups.FindIndex(tg => tg.Key == currentTitle);

        if (currentIdx >= 0 && currentIdx < titleGroups.Count - 1)
        {
            var next = titleGroups[currentIdx + 1];
            ViewBag.NextIds   = string.Join(",", next.Select(p => p.ID));
            ViewBag.NextTitle = next.Key;
        }
        if (currentIdx > 0)
        {
            var prev = titleGroups[currentIdx - 1];
            ViewBag.PrevIds   = string.Join(",", prev.Select(p => p.ID));
            ViewBag.PrevTitle = prev.Key;
        }

        return View(items);
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

    // ─────────────────────────────────────────────
    // POST /Policy/Delete?id=
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var (success, message) = await _service.DeleteAsync(id);
        TempData[success ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToAction("Manage");
    }
}
