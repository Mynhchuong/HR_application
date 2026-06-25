using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Menu;

[Authorize(Roles = "Admin,HR,Canteen")]
public class MealLockController : BaseController
{
    private readonly MealLockService _svc;
    public MealLockController(MealLockService svc) { _svc = svc; }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> List(string? from = null, string? to = null)
    {
        var raw = await _svc.ListRawAsync(from, to);
        return Content(raw, "application/json");
    }

    public class SaveBody
    {
        public long    Id        { get; set; }
        public string  LockDate  { get; set; } = "";
        public bool    LockLunch { get; set; }
        public bool    LockOt    { get; set; }
        public string? CutoffDt  { get; set; }
        public string? Note      { get; set; }
        public bool    IsActive  { get; set; } = true;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveBody body)
    {
        if (body == null) return Json(new { success = false, message = "Thiếu dữ liệu" });
        var payload = new
        {
            id        = body.Id,
            lockDate  = body.LockDate,
            lockLunch = body.LockLunch,
            lockOt    = body.LockOt,
            cutoffDt  = body.CutoffDt,
            note      = body.Note,
            isActive  = body.IsActive,
            loginUser = CurrentUser?.EmpCd
        };
        var raw = await _svc.SaveRawAsync(payload);
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] IdRequest req)
    {
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu ID" });
        var raw = await _svc.DeleteRawAsync(req.Id);
        return Content(raw, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> Check(string date, string typeMeal = "LUNCH")
    {
        var raw = await _svc.CheckRawAsync(date, typeMeal);
        return Content(raw, "application/json");
    }

    public class IdRequest { public long Id { get; set; } }
}
