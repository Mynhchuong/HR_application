using HR_web.API.Service;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;


public class DropdownController : Controller
{
    private readonly DropdownService _dropdownService;

    public DropdownController(DropdownService dropdownService)
    {
        _dropdownService = dropdownService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDept(string? term)
    {
        var data = await _dropdownService.GetDeptAsync();
        var result = data
            .Where(x => string.IsNullOrEmpty(term) ||
                        (x.text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
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
}
