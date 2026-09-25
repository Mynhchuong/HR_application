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

    // Xoá / đổi món hàng loạt trên trang ChangeLog — CHỈ Admin/HR (yêu cầu 2026-09-10), khác
    // CanViewLog (rộng hơn, cho cả Canteen/Clerk/CSR xem log nhưng KHÔNG được sửa/xoá dữ liệu).
    private bool CanBulkEditLog =>
           CurrentUser?.RoleName == "Admin"
        || CurrentUser?.RoleName == "HR";

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

    // Xoá hàng loạt bản ghi log — chọn từng dòng (Keys) hoặc "chọn tất cả khớp bộ lọc" (Filter).
    // CHỈ Admin/HR (không cho Canteen/Clerk/CSR dù họ xem được log).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LogBulkDelete([FromBody] CanteenLogBulkRequest body)
    {
        if (!CanBulkEditLog) return ForbidJson();
        var payload = new { keys = body.Keys, filter = body.Filter, loginUser = CurrentUser?.EmpCd };
        var raw = await _svc.LogBulkDeleteRawAsync(payload);
        return Content(raw, "application/json");
    }

    // Đổi món hàng loạt (mặc định đổi về Mặn) — VD: tất cả NV đổi món Bánh 1 ngày nào đó, chuyển
    // hết về Mặn. CHỈ Admin/HR.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LogBulkChangeFood([FromBody] CanteenLogBulkRequest body)
    {
        if (!CanBulkEditLog) return ForbidJson();
        var payload = new
        {
            keys        = body.Keys,
            filter      = body.Filter,
            newFoodType = string.IsNullOrEmpty(body.NewFoodType) ? "M" : body.NewFoodType,
            loginUser   = CurrentUser?.EmpCd
        };
        var raw = await _svc.LogBulkChangeFoodRawAsync(payload);
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

    // GET /CanteenBread/ChangeLogImportTemplate — file mẫu import hàng loạt (HR cần update nhiều
    // dòng cùng lúc, yêu cầu 2026-09-24). CHỈ Admin/HR như các thao tác bulk khác trên trang này.
    [HttpGet]
    public IActionResult ChangeLogImportTemplate()
    {
        if (!CanBulkEditLog) return ForbidJson();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Import");

        string[] headers = { "Ngày (dd/mm/yyyy)", "Mã NV", "Bữa (LUNCH/OT)", "Món ăn (M/N/C/B)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            c.Style.Font.FontColor = XLColor.White;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        ws.Cell(2, 1).Value = DateTime.Today.ToString("dd/MM/yyyy");
        ws.Cell(2, 2).Value = "12345678";
        ws.Cell(2, 3).Value = "LUNCH";
        ws.Cell(2, 4).Value = "M";

        ws.Cell(4, 1).Value = "Hướng dẫn:";
        ws.Cell(4, 1).Style.Font.Italic = true;
        ws.Cell(5, 1).Value = "- Bữa: LUNCH = bữa giữa ca, OT = tăng ca";
        ws.Cell(6, 1).Value = "- Món ăn: M = Mặn, N = Nhẹ, C = Chay, B = Bánh";
        ws.Cell(7, 1).Value = "- Mỗi dòng ghi đè đúng món của 1 mã NV trong 1 ngày + 1 bữa (không cần xoá dòng cũ trước khi import lại)";
        for (int r = 5; r <= 7; r++) { ws.Cell(r, 1).Style.Font.Italic = true; ws.Cell(r, 1).Style.Font.FontColor = XLColor.Gray; }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauImportLogDoiMon.xlsx");
    }

    // POST /CanteenBread/ChangeLogImport — HR upload Excel để cập nhật hàng loạt (ngày, mã NV, món
    // ăn). Parse ở HR_web (ClosedXML), gửi rows đã parse sang HR_api MERGE từng dòng — cùng pattern
    // Training session bulk-import.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeLogImport(IFormFile file)
    {
        if (!CanBulkEditLog) return ForbidJson();
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel" });

        var rows = new List<object>();
        var parseErrors = new List<object>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int r = 2; r <= lastRow; r++)
            {
                var datCell  = ws.Cell(r, 1).GetString().Trim();
                var empcd    = ws.Cell(r, 2).GetString().Trim();
                var typeMeal = ws.Cell(r, 3).GetString().Trim();
                var typeFood = ws.Cell(r, 4).GetString().Trim();

                if (string.IsNullOrEmpty(datCell) && string.IsNullOrEmpty(empcd)) continue; // dòng trống bỏ qua

                if (string.IsNullOrEmpty(empcd))
                { parseErrors.Add(new { row = r, message = "Thiếu mã NV" }); continue; }

                rows.Add(new { empcd, dat = datCell, typeMeal, typeOfFood = typeFood });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Không đọc được file Excel: " + ex.Message });
        }

        if (rows.Count == 0)
            return Json(new { success = false, message = "File không có dòng dữ liệu hợp lệ nào", errors = parseErrors });

        var payload = new { rows, loginUser = CurrentUser?.EmpCd };
        var raw = await _svc.LogBulkImportRawAsync(payload);

        // Gộp thêm lỗi parse cục bộ (thiếu mã NV) vào cùng mảng errors trả về từ API, để FE hiện
        // chung 1 danh sách lỗi duy nhất — theo đúng convention SessionImport (TrainingAdmin).
        if (parseErrors.Count > 0)
        {
            try
            {
                var obj = System.Text.Json.Nodes.JsonNode.Parse(raw)?.AsObject();
                if (obj != null)
                {
                    var existing = obj["errors"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray();
                    foreach (var pe in parseErrors)
                        existing.Add(System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(pe)));
                    obj["errors"] = existing;
                    return Content(obj.ToJsonString(), "application/json");
                }
            }
            catch { /* nếu parse lỗi thì trả nguyên raw, không chặn kết quả import chính */ }
        }

        return Content(raw, "application/json");
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

    // ── Bulk xoá / đổi món trên ChangeLog ────────────────────
    public class CanteenLogKeyBody
    {
        public string Empcd    { get; set; } = "";
        public string Dat      { get; set; } = "";
        public string TypeMeal { get; set; } = "";
    }

    public class CanteenLogFilterBody
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Empcd { get; set; }
        public string? Deptcd { get; set; }
        public string? Linecd { get; set; }
        public string? Workcd { get; set; }
        public string? FoodType { get; set; }
        public string? TypeMeal { get; set; }
    }

    public class CanteenLogBulkRequest
    {
        // Chọn từng dòng cụ thể (checkbox trên trang hiện tại) — ưu tiên nếu có.
        public List<CanteenLogKeyBody>? Keys { get; set; }
        // "Chọn tất cả khớp bộ lọc" — áp dụng cho MỌI dòng khớp filter, kể cả ngoài trang đang xem.
        public CanteenLogFilterBody?    Filter { get; set; }
        // Chỉ dùng cho LogBulkChangeFood — mặc định "M" (Mặn) nếu không truyền.
        public string? NewFoodType { get; set; }
    }
}
