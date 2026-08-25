using ClosedXML.Excel;
using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Menu;

[Authorize]
public class CanteenBreadController : BaseController
{
    private readonly CanteenBreadService _svc;
    public CanteenBreadController(CanteenBreadService svc) { _svc = svc; }

    private bool CanManage =>
           CurrentUser?.RoleName == "Admin"
        || CurrentUser?.RoleName == "HR"
        || CurrentUser?.RoleName == "Canteen";

    // Danh sách cố định (roster) cấp Bánh cả tuần — chỉ Admin thao tác, không mở cho HR/Canteen.
    private bool IsAdmin => CurrentUser?.RoleName == "Admin";

    private bool CanViewLog =>
           CurrentUser?.RoleName == "Admin"
        || CurrentUser?.RoleName == "HR"
        || CurrentUser?.RoleName == "Canteen"
        || CurrentUser?.RoleName == "Clerk"
        || CurrentUser?.RoleName == "CSR";

    private IActionResult ForbidJson() =>
        Json(new { success = false, message = "Bạn không có quyền thao tác chức năng này" });

    // ── BREAD QUOTA ─────────────────────────────────────────
    public IActionResult BreadQuota()
    {
        if (!CanManage) return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetQuotaList()
    {
        if (!CanManage) return ForbidJson();
        var raw = await _svc.QuotaListRawAsync();
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveQuotaAll([FromBody] List<QuotaItem> items)
    {
        if (!CanManage) return ForbidJson();
        if (items == null || items.Count == 0)
            return Json(new { success = false, message = "Không có dữ liệu" });

        var payload = items.Select(x => new
        {
            deptcd   = x.Deptcd,
            deptName = x.DeptName,
            maxBread = x.MaxBread,
            isActive = x.IsActive,
            loginUser = CurrentUser?.EmpCd
        }).ToList();

        var raw = await _svc.QuotaSaveAllRawAsync(payload);
        return Content(raw, "application/json");
    }

    // Admin gán Bánh hàng loạt cho danh sách NV (công nhân dây chuyền được cấp
    // phiếu cố định cả tuần), bypass quota dept + cap 3 ngày/tuần.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssignBread([FromBody] BulkAssignBreadBody body)
    {
        if (!IsAdmin) return ForbidJson();

        var empCds = (body.EmpCds ?? new List<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToList();

        if (empCds.Count == 0)
            return Json(new { success = false, message = "Danh sách nhân viên trống" });
        if (string.IsNullOrEmpty(body.FromDate) || string.IsNullOrEmpty(body.ToDate))
            return Json(new { success = false, message = "Vui lòng chọn khoảng ngày" });

        var payload = new
        {
            empCds    = empCds,
            fromDate  = body.FromDate,
            toDate    = body.ToDate,
            loginUser = CurrentUser?.EmpCd
        };

        var raw = await _svc.BulkAssignRawAsync(payload);
        return Content(raw, "application/json");
    }

    // ── ROSTER CỐ ĐỊNH (công nhân dây chuyền) ────────────────
    [HttpGet]
    public async Task<IActionResult> GetRosterList()
    {
        if (!IsAdmin) return ForbidJson();
        var raw = await _svc.RosterListRawAsync();
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RosterAdd([FromBody] RosterAddBody body)
    {
        if (!IsAdmin) return ForbidJson();

        var empCds = (body.EmpCds ?? new List<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .ToList();

        if (empCds.Count == 0)
            return Json(new { success = false, message = "Danh sách nhân viên trống" });

        var payload = new { empCds, note = body.Note, loginUser = CurrentUser?.EmpCd };
        var raw = await _svc.RosterAddRawAsync(payload);
        return Content(raw, "application/json");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RosterRemove([FromBody] RosterRemoveBody body)
    {
        if (!IsAdmin) return ForbidJson();
        if (string.IsNullOrWhiteSpace(body.EmpCd))
            return Json(new { success = false, message = "Thiếu mã NV" });

        var payload = new { empCd = body.EmpCd.Trim(), loginUser = CurrentUser?.EmpCd };
        var raw = await _svc.RosterRemoveRawAsync(payload);
        return Content(raw, "application/json");
    }

    // Áp dụng Bánh cho cả danh sách roster đã lưu, trong 1 khoảng ngày — không cần
    // dán lại mã NV mỗi tuần. Dùng lại đúng cơ chế BulkAssignBread (bypass quota).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyRosterBread([FromBody] ApplyRosterBreadBody body)
    {
        if (!IsAdmin) return ForbidJson();
        if (string.IsNullOrEmpty(body.FromDate) || string.IsNullOrEmpty(body.ToDate))
            return Json(new { success = false, message = "Vui lòng chọn khoảng ngày" });

        var empCds = await _svc.GetRosterEmpCdsAsync();
        if (empCds.Count == 0)
            return Json(new { success = false, message = "Danh sách cố định đang trống — thêm nhân viên trước" });

        var payload = new
        {
            empCds,
            fromDate  = body.FromDate,
            toDate    = body.ToDate,
            loginUser = CurrentUser?.EmpCd
        };

        var raw = await _svc.BulkAssignRawAsync(payload);
        return Content(raw, "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> GetDeptList()
    {
        if (!CanManage && !CanViewLog) return ForbidJson();
        var raw = await _svc.DeptListRawAsync();
        return Content(raw, "application/json");
    }

    // ── BREAD STATUS (NV dùng khi load ChangeMeal) ──────────
    // Luôn dùng EMPCD của người đang đăng nhập — không tin tưởng empcd do client gửi lên,
    // tránh lộ quota/lịch sử đổi bánh của người khác.
    [HttpGet]
    public async Task<IActionResult> GetBreadStatus(string dat)
    {
        var empcd = CurrentUser?.EmpCd ?? "";
        var raw = await _svc.StatusRawAsync(empcd, dat);
        return Content(raw, "application/json");
    }

    // ── CHANGE LOG ──────────────────────────────────────────
    public IActionResult ChangeLog()
    {
        if (!CanViewLog) return RedirectToAction("Index", "Home");
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetChangeLog(
        string? from, string? to, string? empcd,
        string? deptcd, string? linecd, string? workcd, string? foodType, string? typeMeal,
        int page = 1, int pageSize = 50)
    {
        if (!CanViewLog) return ForbidJson();
        // Clerk chỉ xem log của dept/line/work mình quản lý (HR_USERS_DEPT); Admin/HR/Canteen/CSR
        // xem toàn bộ như cũ.
        var clerkEmpcd = CurrentUser?.RoleName == "Clerk" ? CurrentUser?.EmpCd : null;
        var raw = await _svc.OrderViewRawAsync(from, to, empcd, deptcd, foodType, page, pageSize, clerkEmpcd, linecd, workcd, typeMeal);
        return Content(raw, "application/json");
    }

    private static readonly Dictionary<string, string> FoodLabel  = new() { ["M"] = "Mặn", ["N"] = "Nhẹ", ["C"] = "Chay", ["B"] = "Bánh" };
    private static readonly Dictionary<string, string> ShiftLabel = new() { ["LUNCH"] = "Bữa ca", ["OT"] = "Tăng ca" };

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        string? from, string? to, string? empcd,
        string? deptcd, string? linecd, string? workcd, string? foodType, string? typeMeal)
    {
        if (!CanViewLog) return ForbidJson();

        var clerkEmpcd = CurrentUser?.RoleName == "Clerk" ? CurrentUser?.EmpCd : null;
        var rows = await _svc.OrderViewAllAsync(from, to, empcd, deptcd, foodType, clerkEmpcd, linecd, workcd, typeMeal);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Log Đổi Món");

        string[] headers = { "STT", "Ngày", "Mã NV", "Họ & Tên", "Phòng ban", "Ca", "Món hiện tại", "Đổi bởi", "Nguồn", "Lần cuối" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var r in rows)
        {
            var dat = (r.dat?.Length == 8) ? $"{r.dat.Substring(6, 2)}/{r.dat.Substring(4, 2)}/{r.dat.Substring(0, 4)}" : (r.dat ?? "");
            var food  = r.typeOfFood != null && FoodLabel.TryGetValue(r.typeOfFood, out var fl) ? fl : (r.typeOfFood ?? "");
            var shift = r.typeMeal  != null && ShiftLabel.TryGetValue(r.typeMeal, out var sl) ? sl : (r.typeMeal ?? "");
            var source = r.isMysamho ? "App" : (string.IsNullOrEmpty(r.changeFrom) ? "HT" : r.changeFrom);

            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = dat;
            ws.Cell(row, 3).Value = r.empcd ?? "";
            ws.Cell(row, 4).Value = r.empName ?? "";
            ws.Cell(row, 5).Value = r.deptName ?? r.deptcd ?? "";
            ws.Cell(row, 6).Value = shift;
            ws.Cell(row, 7).Value = food;
            ws.Cell(row, 8).Value = r.changeFrom ?? "";
            ws.Cell(row, 9).Value = source;
            ws.Cell(row, 10).Value = r.updtDt.HasValue ? r.updtDt.Value.ToString("dd/MM/yyyy HH:mm") : "";
            row++;
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"LogDoiMon_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    public class QuotaItem
    {
        public string  Deptcd   { get; set; } = "";
        public string? DeptName { get; set; }
        public int     MaxBread { get; set; }
        public bool    IsActive { get; set; } = true;
    }

    public class BulkAssignBreadBody
    {
        public List<string> EmpCds   { get; set; } = new();
        public string        FromDate { get; set; } = "";
        public string        ToDate   { get; set; } = "";
    }

    public class RosterAddBody
    {
        public List<string> EmpCds { get; set; } = new();
        public string?        Note   { get; set; }
    }

    public class RosterRemoveBody
    {
        public string EmpCd { get; set; } = "";
    }

    public class ApplyRosterBreadBody
    {
        public string FromDate { get; set; } = "";
        public string ToDate   { get; set; } = "";
    }
}
