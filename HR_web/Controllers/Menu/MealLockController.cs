using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Menu;

[Authorize]
public class MealLockController : BaseController
{
    private readonly MealLockService _svc;
    public MealLockController(MealLockService svc) { _svc = svc; }

    private bool CanManage =>
           CurrentUser?.RoleName == "Admin"
        || CurrentUser?.RoleName == "HR"
        || CurrentUser?.RoleName == "Canteen";

    private IActionResult ForbidJson() =>
        Json(new { success = false, message = "Bạn không có quyền thao tác chức năng này" });

    public IActionResult Index()
    {
        if (!CanManage) return RedirectToAction("Index", "Home");
        return View();
    }

    // LIST: ai cũng đọc được (NV cần hiện section "Sắp tới" trên Menu/Today)
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
        public bool    LockMan   { get; set; }
        public bool    LockNhe   { get; set; }
        public bool    LockChay  { get; set; }
        public bool    LockBanh  { get; set; }   // Bánh
        public string? CutoffDt  { get; set; }
        public string? Note      { get; set; }
        public bool    IsActive  { get; set; } = true;
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveBody body)
    {
        if (!CanManage) return ForbidJson();
        if (body == null) return Json(new { success = false, message = "Thiếu dữ liệu" });
        var payload = new
        {
            id        = body.Id,
            lockDate  = body.LockDate,
            lockLunch = body.LockLunch,
            lockOt    = body.LockOt,
            lockMan   = body.LockMan,
            lockNhe   = body.LockNhe,
            lockChay  = body.LockChay,
            lockBanh  = body.LockBanh,
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
        if (!CanManage) return ForbidJson();
        if (req == null || req.Id <= 0) return Json(new { success = false, message = "Thiếu ID" });
        var raw = await _svc.DeleteRawAsync(req.Id);
        return Content(raw, "application/json");
    }

    // CHECK: ai cũng gọi được (NV cần xem banner khoá)
    [HttpGet]
    public async Task<IActionResult> Check(string date, string typeMeal = "LUNCH", string? targetFood = null)
    {
        var raw = await _svc.CheckRawAsync(date, typeMeal, targetFood);
        return Content(raw, "application/json");
    }

    public class IdRequest { public long Id { get; set; } }
}
