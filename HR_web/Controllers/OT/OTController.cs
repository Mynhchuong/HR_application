using System.Globalization;
using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.OT;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.OT;

[Authorize]
public class OTController : BaseController
{
    private readonly OtService _otService;

    public OTController(OtService otService)
    {
        _otService = otService;
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtConfirmForm
    // ─────────────────────────────────────────────
    public async Task<IActionResult> OtConfirmForm(string? work_date = null)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return RedirectToAction("Login", "Account");

            string selectedDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
            var data = await _otService.GetOTTodayAsync(CurrentUser.EmpCd, selectedDate);
            if (data == null)
                ViewBag.Error = "Không có dữ liệu OT hoặc không thể kết nối máy chủ.";

            ViewBag.WorkDate = selectedDate;
            ViewBag.HasSignature = CurrentUser?.SIGNATUREBLOB == "Y";
            return View(data);
        }
        catch (Exception ex)
        {
            ViewBag.Error = ex.Message;
            ViewBag.HasSignature = CurrentUser?.SIGNATUREBLOB == "Y";
            return View((OTTodayModel?)null);
        }
    }

    // ─────────────────────────────────────────────
    // POST: /OT/OtConfirmForm
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> OtConfirmForm(string empcd, string confirmStatus, string work_date, decimal ot_hours)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(work_date) || !DateTime.TryParse(work_date, out _))
                return Json(new { success = false, message = "Ngày tăng ca không hợp lệ" });

            if (ot_hours <= 0)
                return Json(new { success = false, message = "Số giờ tăng ca không hợp lệ" });

            var result = await _otService.ConfirmOTAsync(empcd, confirmStatus, work_date, ot_hours);
            return Json(new { success = result.success, message = result.message ?? (result.success ? "Thành công" : "Có lỗi xảy ra") });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForHR
    // ─────────────────────────────────────────────
    [Authorize(Roles = "Admin,HR")]
    public IActionResult OtListForHR(string? work_date = null, string? dept_id = null)
    {
        ViewBag.Summary  = new List<OTHRSummaryModel>();
        ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        ViewBag.DeptId   = dept_id;
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTHRDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetOTHRDetailPage(
        string? work_date = null, string? dept_id = null,
        string? search = null, string? status = null,
        string? dept_name = null, string? line_name = null,
        string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 50, int admin = 0)
    {
        try
        {
            var result = await _otService.GetOTHRDetailAsync(
                work_date, dept_id, search, status, dept_name, line_name, line_id, work_id, page, page_size, admin,
                CurrentUser?.EmpCd);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForClerk
    // ─────────────────────────────────────────────
    public IActionResult OtListForClerk(string? work_date = null    )
    {
        ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        return View();
    }
    // ─────────────────────────────────────────────
    // GET: /OT/OtListForSupervisor
    // ─────────────────────────────────────────────
    public IActionResult OtListForSupervisor(string? work_date = null)
    {
        ViewBag.WorkDate        = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        ViewBag.FilterType      = CurrentUser?.FilterType      ?? "";
        ViewBag.FilterCodes     = CurrentUser?.FilterCodes     ?? new List<string>();
        ViewBag.FilterLineCodes = CurrentUser?.FilterLineCodes ?? new List<string>();
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /OT/OtListForExpat
    // ─────────────────────────────────────────────
    public IActionResult OtListForExpat(string? work_date = null)
    {
        ViewBag.WorkDate        = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        ViewBag.FilterType      = CurrentUser?.FilterType      ?? "";
        ViewBag.FilterCodes     = CurrentUser?.FilterCodes     ?? new List<string>();
        ViewBag.FilterLineCodes = CurrentUser?.FilterLineCodes ?? new List<string>();
        return View();
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTSupervisorDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTSupervisorDetailPage(
        string? work_date = null, string? status = null,
        string? search    = null,
        string? dept_id   = null,
        string? line_id   = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (CurrentUser == null || string.IsNullOrEmpty(CurrentUser.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            // filterType có thể empty với session cũ — API tự check HR_USERS_DEPT bằng empCd
            var filterType = CurrentUser.FilterType ?? "dept";

            var result = await _otService.GetOTSupervisorDetailAsync(
                CurrentUser.EmpCd, filterType, work_date, status, search, dept_id, line_id, work_id, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /OT/RemindPendingOTSign (AJAX)
    // ─────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> RemindPendingOTSign(
        string? work_date, string? dept_id, string? line_id, string? work_id)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var (ok, msg, sent) = await _otService.RemindPendingOTSignAsync(
                CurrentUser.EmpCd, work_date ?? DateTime.Today.ToString("yyyy-MM-dd"),
                dept_id, line_id, work_id);
            return Json(new { success = ok, message = msg, sent });
        }
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOTClerkDetailPage (AJAX / JSON)
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOTClerkDetailPage(
        string? work_date = null, string? status = null,
        string? search = null, string? dept_id = null, string? line_id = null, string? work_id = null,
        int page = 1, int page_size = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });
            var result = await _otService.GetOTClerkDetailAsync(CurrentUser.EmpCd, work_date, status, search, dept_id, line_id, work_id, page, page_size);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // GET: /OT/GetOtLog — lịch sử đổi ý OT (popup) dùng chung Admin/HR/Clerk
    // ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetOtLog(string empcd, string work_date)
    {
        try
        {
            if (string.IsNullOrEmpty(CurrentUser?.EmpCd))
                return Json(new { success = false, message = "Chưa đăng nhập" });

            var result = await _otService.GetOtLogAsync(empcd, work_date, CurrentUser.EmpCd);
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────
    // POST: /OT/NotifyPending — HR broadcast nhắc kí OT
    // ─────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> NotifyPending([FromBody] NotifyPendingBody body)
    {
        if (body == null || string.IsNullOrEmpty(body.work_date))
            return Json(new { success = false, message = "Thiếu ngày làm việc" });

        var raw = await _otService.HrNotifyPendingRawAsync(body.work_date, body.dept_id, CurrentUser?.EmpCd);
        return Content(raw, "application/json");
    }

    public class NotifyPendingBody
    {
        public string? work_date { get; set; }
        public string? dept_id   { get; set; }
    }

    // ═════════════════════════════════════════════════════════════
    // ADMIN — trang quản lý OT (chỉ Admin/HR)
    // ═════════════════════════════════════════════════════════════

    // GET: /OT/OtListForAdmin
    [Authorize(Roles = "Admin,HR")]
    public IActionResult OtListForAdmin(string? work_date = null)
    {
        ViewBag.WorkDate = string.IsNullOrEmpty(work_date) ? DateTime.Today.ToString("yyyy-MM-dd") : work_date;
        return View();
    }

    // POST: /OT/AdminBulkSignFor
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AdminBulkSignFor([FromBody] OTAdminBulkSignForRequest body)
    {
        if (body == null) return Json(new { success = false, message = "Body rỗng" });
        body.ACTOR_EMPCD = CurrentUser?.EmpCd;
        var result = await _otService.AdminBulkSignForAsync(body);
        return Json(result);
    }

    // ═════════════════════════════════════════════════════════════
    // Import Excel — Ký giùm NHIỀU NV × NHIỀU NGÀY
    // ═════════════════════════════════════════════════════════════

    // GET: /OT/DownloadSignForTemplate
    [HttpGet]
    [Authorize(Roles = "Admin,HR")]
    public IActionResult DownloadSignForTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("KyGiumOT");
        ws.Cell(1, 1).Value = "Mã NV";
        ws.Cell(1, 2).Value = "Ngày tăng ca (yyyy-mm-dd)";
        var hdr = ws.Range(1, 1, 1, 2);
        hdr.Style.Font.Bold = true;
        hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // 2 dòng ví dụ
        ws.Cell(2, 1).Value = "24092522"; ws.Cell(2, 2).Value = "2026-09-04";
        ws.Cell(3, 1).Value = "25081801"; ws.Cell(3, 2).Value = "2026-09-05";
        ws.Range(2, 1, 3, 2).Style.Font.FontColor = XLColor.Gray;

        ws.Cell(5, 1).Value = "* Mỗi dòng = 1 nhân viên + 1 ngày. Được phép nhiều ngày khác nhau.";
        ws.Cell(6, 1).Value = "* Ngày: yyyy-mm-dd (vd 2026-09-04) hoặc dd/MM/yyyy. Xoá 2 dòng ví dụ trước khi nhập.";
        ws.Range(5, 1, 6, 1).Style.Font.Italic = true;
        ws.Range(5, 1, 6, 1).Style.Font.FontColor = XLColor.Gray;
        ws.Column(1).Width = 16;
        ws.Column(2).Width = 28;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "MauImport_KyGiumOT.xlsx");
    }

    public class ImportRowResult
    {
        public int row { get; set; }
        public string empcd { get; set; } = "";
        public string work_date { get; set; } = "";
        public bool ok { get; set; }
        public bool skipped { get; set; }
        public string message { get; set; } = "";
    }

    // POST: /OT/ImportSignForExcel  (multipart)
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> ImportSignForExcel(IFormFile? file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Vui lòng chọn file Excel (.xlsx)" });

        var rows = new List<ImportRowResult>();
        var apiItems = new List<OTAdminSignForMultiItem>();
        var apiRowRef = new List<ImportRowResult>();          // song song apiItems — để gắn kết quả về đúng dòng
        var seen = new HashSet<string>();

        string[] dateFormats = { "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-M-d" };

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheet(1);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            if (lastRow - 1 > 5000)
                return Json(new { success = false, message = "File quá nhiều dòng (tối đa 5000)" });

            for (int r = 2; r <= lastRow; r++)
            {
                var empRaw   = ws.Cell(r, 1).GetString()?.Trim() ?? "";
                var dateCell = ws.Cell(r, 2);
                var dateRaw  = dateCell.GetString()?.Trim() ?? "";
                if (empRaw.Length == 0 && dateRaw.Length == 0) continue;   // dòng trống

                string? wd = null;
                if (dateCell.TryGetValue<DateTime>(out var dtv) && dtv.Year > 2000)
                    wd = dtv.ToString("yyyy-MM-dd");
                else
                    foreach (var f in dateFormats)
                        if (DateTime.TryParseExact(dateRaw, f, null, DateTimeStyles.None, out var dt)) { wd = dt.ToString("yyyy-MM-dd"); break; }

                if (empRaw.Length == 0)
                { rows.Add(new ImportRowResult { row = r, empcd = "", work_date = dateRaw, message = "Thiếu mã NV" }); continue; }
                if (wd == null)
                { rows.Add(new ImportRowResult { row = r, empcd = empRaw, work_date = dateRaw, message = "Ngày không hợp lệ (cần yyyy-mm-dd hoặc dd/MM/yyyy)" }); continue; }

                if (!seen.Add(empRaw + "|" + wd))
                { rows.Add(new ImportRowResult { row = r, empcd = empRaw, work_date = wd, skipped = true, message = "Dòng trùng trong file — bỏ qua" }); continue; }

                var rr = new ImportRowResult { row = r, empcd = empRaw, work_date = wd };
                rows.Add(rr);
                apiItems.Add(new OTAdminSignForMultiItem { EMPCD = empRaw, WORK_DATE = wd });
                apiRowRef.Add(rr);
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Lỗi đọc file Excel: " + ex.Message });
        }

        if (rows.Count == 0)
            return Json(new { success = false, message = "File không có dữ liệu" });

        // Gọi API theo batch 50 để không vượt timeout 60s của HttpClient
        const int CHUNK = 50;
        for (int i = 0; i < apiItems.Count; i += CHUNK)
        {
            var slice    = apiItems.GetRange(i, Math.Min(CHUNK, apiItems.Count - i));
            var sliceRef  = apiRowRef.GetRange(i, slice.Count);
            var partRes = await _otService.AdminBulkSignForMultiAsync(new OTAdminBulkSignForMultiRequest
            {
                ACTOR_EMPCD = CurrentUser?.EmpCd,
                ITEMS = slice
            });
            if (!partRes.success)
                return Json(new { success = false, message = partRes.message ?? "Lỗi API ký giùm" });

            var byKey = new Dictionary<string, OTAdminBulkResult>();
            foreach (var ar in partRes.results ?? new())
                byKey[(ar.EMPCD ?? "") + "|" + (ar.WORK_DATE ?? "")] = ar;

            foreach (var rr in sliceRef)
            {
                if (byKey.TryGetValue(rr.empcd + "|" + rr.work_date, out var ar))
                {
                    rr.ok = ar.OK;
                    rr.skipped = ar.SKIPPED;
                    rr.message = ar.MESSAGE ?? (ar.OK ? "Ký thành công" : "");
                }
                else
                {
                    rr.message = "Không có kết quả trả về";
                }
            }
        }

        int okC   = rows.Count(x => x.ok);
        int skipC = rows.Count(x => !x.ok && x.skipped);
        int errC  = rows.Count(x => !x.ok && !x.skipped);
        return Json(new
        {
            success = true,
            total = rows.Count,
            ok = okC,
            skipped = skipC,
            failed = errC,
            message = $"Tổng {rows.Count} dòng — Ký thành công {okC}, Bỏ qua {skipC}, Lỗi {errC}",
            rows = rows.OrderBy(x => x.row)
        });
    }

    // POST: /OT/AdminBulkUpdate
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AdminBulkUpdate([FromBody] OTAdminBulkUpdateRequest body)
    {
        if (body == null) return Json(new { success = false, message = "Body rỗng" });
        body.ACTOR_EMPCD = CurrentUser?.EmpCd;
        var result = await _otService.AdminBulkUpdateAsync(body);
        return Json(result);
    }

    // POST: /OT/AdminBulkDelete
    [HttpPost]
    [Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> AdminBulkDelete([FromBody] OTAdminBulkDeleteRequest body)
    {
        if (body == null) return Json(new { success = false, message = "Body rỗng" });
        body.ACTOR_EMPCD = CurrentUser?.EmpCd;
        var result = await _otService.AdminBulkDeleteAsync(body);
        return Json(result);
    }
}
