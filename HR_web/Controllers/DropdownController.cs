using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;


public class DropdownController : BaseController
{
    private readonly DropdownService _dropdownService;

    public DropdownController(DropdownService dropdownService)
    {
        _dropdownService = dropdownService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDept(string? term, string? id)
    {
        var data = await _dropdownService.GetDeptAsync();
        var result = data
            .Where(x => (!string.IsNullOrEmpty(id)  && x.id == id) ||
                        ( string.IsNullOrEmpty(id)   &&
                         (string.IsNullOrEmpty(term) || x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)))
            .Select(x => new { id = x.id, text = x.text });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetRole(string? term)
    {
        var data = await _dropdownService.GetRoleAsync();
        var result = data
            .Where(x => string.IsNullOrEmpty(term) ||
                        (x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(x => new { id = x.id, text = x.text });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetRoleNoAdmin(string? term)
    {
        var data = await _dropdownService.GetRoleAsync();
        var result = data
            .Where(x => x.text != "Admin" &&
                        (string.IsNullOrEmpty(term) ||
                         x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(x => new { id = x.id, text = x.text });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetLine(string? term)
    {
        var data = await _dropdownService.GetLineAsync();
        var result = data
            .Where(x => string.IsNullOrEmpty(term) ||
                        (x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(x => new { id = x.id, text = x.text });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetWork(string? term)
    {
        var data = await _dropdownService.GetWorkAsync();
        var result = data
            .Where(x => string.IsNullOrEmpty(term) ||
                        (x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(x => new { id = x.id, text = x.text });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetLineByDept(string deptCd)
    {
        var data = await _dropdownService.GetLineByDeptAsync(deptCd);
        return Json(data.Select(x => new { id = x.id, text = x.text }));
    }

    [HttpGet]
    public async Task<IActionResult> GetEmp(string? term)
    {
        if (string.IsNullOrEmpty(term) || term.Length < 2) return Json(new List<object>());
        var data = await _dropdownService.GetEmpAsync(term);
        return Json(data.Select(x => new { id = x.id, text = x.text }));
    }

    // Scoped dropdowns — chỉ trả về options trong phạm vi HR_USERS_DEPT của user đang đăng nhập
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetDeptByScope(string? term)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new List<object>());
        var data = await _dropdownService.GetDeptByScopeAsync(empcd);
        return Json(data
            .Where(x => string.IsNullOrEmpty(term) || x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(x => new { id = x.id, text = x.text }));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetLineByScope(string? term)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new List<object>());
        var data = await _dropdownService.GetLineByScopeAsync(empcd);
        return Json(data
            .Where(x => string.IsNullOrEmpty(term) || x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(x => new { id = x.id, text = x.text }));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetWorkByScope(string? term)
    {
        var empcd = CurrentUser?.EmpCd;
        if (string.IsNullOrEmpty(empcd)) return Json(new List<object>());
        var data = await _dropdownService.GetWorkByScopeAsync(empcd);
        return Json(data
            .Where(x => string.IsNullOrEmpty(term) || x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(x => new { id = x.id, text = x.text }));
    }

}
