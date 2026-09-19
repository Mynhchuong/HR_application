using ClosedXML.Excel;
using HR_web.API.Service;
using HR_web.Models.Gift;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace HR_web.Controllers.Gift;

// Trang quản trị Quà. Route "giftadmin" whitelist trong GiftGateFilter (giống "surveyadmin"
// whitelist trong SurveyBlockerFilter) — tránh HR/Clerk tự khóa mình khi đang xử lý quà cho người
// khác nếu chính họ cũng có quà đang PENDING_CONFIRM.
// Phân quyền (chốt 2026-09-19, HR yêu cầu): Clerk CHỈ xem báo cáo + nhắc công nhân đi lãnh quà —
// KHÔNG có quyền như HR (không tạo danh mục/đợt quà, không import, không đánh dấu đã phát, không
// gửi yêu cầu xác nhận). Class-level cho phép cả 3 role, action nào chỉ-HR thì tự thêm
// [Authorize(Roles="Admin,HR")] đè lên (2 attribute stack lại = phải thỏa cả 2 = thực chất chỉ
// Admin/HR) — Report/GetRecipients/ExportExcel/RemindRecipients KHÔNG đè, giữ nguyên cho cả Clerk.
[Authorize(Roles = "Admin,HR,Clerk")]
public class GiftAdminController : BaseController
{
    private readonly GiftService _svc;

    public GiftAdminController(GiftService svc)
    {
        _svc = svc;
    }

    [Authorize(Roles = "Admin,HR")]
    public IActionResult Catalog() => View();

    [Authorize(Roles = "Admin,HR")]
    public IActionResult BatchList() => View();

    [Authorize(Roles = "Admin,HR")]
    public IActionResult BatchDetail(int id)
    {
        ViewBag.BatchId = id;
        return View();
    }

    public IActionResult Report()
    {
        ViewBag.DateFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd");
        ViewBag.DateTo   = DateTime.Today.AddMonths(2).ToString("yyyy-MM-dd");
        return View();
    }

    // ── Danh mục (chỉ Admin/HR) ───────────────────────────────
    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetCategories(bool active_only = false)
        => Json(new { success = true, data = await _svc.GetCategoriesAsync(active_only) });

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveCategory([FromBody] GiftCategoryItem req)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        return Json(await _svc.SaveCategoryAsync(req, CurrentUser.EmpCd));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> DeleteCategory(int id)
        => Json(await _svc.DeleteCategoryAsync(id));

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> GetCatalog(int? category_id = null, bool active_only = false)
        => Json(new { success = true, data = await _svc.GetCatalogAsync(category_id, active_only) });

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SaveCatalogItem([FromBody] GiftCatalogSaveRequest req)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        req.ACTOR_EMPCD = CurrentUser.EmpCd;
        return Json(await _svc.SaveCatalogItemAsync(req));
    }

    // ── Đợt quà ───────────────────────────────────────────────
    // KHÔNG khoá Admin/HR — Clerk cần danh sách đợt quà để lọc trang Báo cáo (chỉ đọc, không
    // phải thao tác quản trị). Trang quản trị BatchList/BatchDetail vẫn khoá riêng ở trên.
    [HttpGet]
    public async Task<IActionResult> GetBatchList()
        => Json(new { success = true, data = await _svc.GetBatchListAsync() });

    [HttpGet]
    public async Task<IActionResult> GetBatch(int id)
        => Json(new { success = true, data = (await _svc.GetBatchListAsync(id)).FirstOrDefault() });

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CreateBatch([FromBody] GiftBatchCreateRequest req)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        req.ACTOR_EMPCD = CurrentUser.EmpCd;
        return Json(await _svc.CreateBatchAsync(req));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> CloseBatch([FromBody] GiftBatchCloseRequest req)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        req.ACTOR_EMPCD = CurrentUser.EmpCd;
        return Json(await _svc.CloseBatchAsync(req));
    }

    [HttpPost, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> Import(IFormFile file, int batchId)
    {
        try
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "Chưa chọn file" });
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Chỉ hỗ trợ file .xlsx" });
            if (CurrentUser == null)
                return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });

            var rows = new List<GiftRecipientImportRow>();
            using var stream = file.OpenReadStream();
            using var wb     = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var empcd    = ws.Cell(r, 1).GetString().Trim();
                var dateCell = ws.Cell(r, 2);
                var location = ws.Cell(r, 3).GetString().Trim();
                if (string.IsNullOrEmpty(empcd)) continue;

                string receiveDate = dateCell.DataType == XLDataType.DateTime
                    ? dateCell.GetDateTime().ToString("yyyy-MM-dd")
                    : dateCell.GetString().Trim();

                rows.Add(new GiftRecipientImportRow
                {
                    EMPCD        = empcd,
                    RECEIVE_DATE = receiveDate,
                    LOCATION     = string.IsNullOrEmpty(location) ? null : location
                });
            }

            if (rows.Count == 0)
                return Json(new { success = false, message = "File không có dữ liệu" });

            var result = await _svc.ImportRecipientsAsync(new GiftRecipientImportRequest
            {
                BATCH_ID = batchId, ACTOR_EMPCD = CurrentUser.EmpCd, ROWS = rows
            });
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet, Authorize(Roles = "Admin,HR")]
    public IActionResult DownloadTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DanhSachNhanQua");

        string[] headers = { "Mã nhân viên", "Ngày nhận quà (yyyy-mm-dd)", "Địa điểm nhận" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#db2777");
            cell.Style.Font.FontColor = XLColor.White;
        }

        ws.Cell(2, 1).Value = "12345678";
        ws.Cell(2, 2).Value = DateTime.Today.ToString("yyyy-MM-dd");
        ws.Cell(2, 3).Value = "Phòng Nhân sự";

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "template_DanhSachNhanQua.xlsx");
    }

    // ── Danh sách người nhận (dùng chung BatchDetail + Report) ──
    [HttpGet]
    public async Task<IActionResult> GetRecipients(
        int? batch_id, string? dept_id, string? line_id, string? work_id, string? search, string? status,
        string? date_from, string? date_to, int page = 1, int page_size = 50)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        var result = await _svc.GetRecipientsAsync(
            CurrentUser.EmpCd, batch_id, dept_id, line_id, work_id, search, status, date_from, date_to, page, page_size);
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> Deliver([FromBody] GiftDeliverRequest req)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        req.ACTOR_EMPCD = CurrentUser.EmpCd;
        return Json(await _svc.MarkDeliveredAsync(req));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> DeliverBulk([FromBody] List<int> recipientIds)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        if (recipientIds == null || recipientIds.Count == 0) return Json(new { success = false, message = "Chưa chọn người nhận nào" });

        var result = await _svc.MarkDeliveredBulkAsync(new GiftDeliverBulkRequest
        {
            RECIPIENT_IDS = recipientIds, ACTOR_EMPCD = CurrentUser.EmpCd
        });
        return Json(result);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> DeliverAll([FromBody] GiftDeliverAllRequest req)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        req.ACTOR_EMPCD = CurrentUser.EmpCd;
        return Json(await _svc.MarkDeliveredAllAsync(req));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,HR")]
    public async Task<IActionResult> SendConfirmRequest([FromBody] List<int> recipientIds)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        if (recipientIds == null || recipientIds.Count == 0) return Json(new { success = false, message = "Chưa chọn người nhận nào" });

        var result = await _svc.SendConfirmRequestAsync(new GiftSendConfirmBulkRequest
        {
            RECIPIENT_IDS = recipientIds, ACTOR_EMPCD = CurrentUser.EmpCd
        });
        return Json(result);
    }

    // Thư ký/HR/Admin nhắc công nhân đến lãnh quà (chỉ gửi thông báo, không đổi trạng thái) —
    // đây là action DUY NHẤT thuộc nhóm "thao tác" mà Clerk được phép dùng.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemindRecipients([FromBody] List<int> recipientIds)
    {
        if (CurrentUser == null) return Json(new { success = false, message = "Phiên đăng nhập hết hạn" });
        if (recipientIds == null || recipientIds.Count == 0) return Json(new { success = false, message = "Chưa chọn người nào" });

        var result = await _svc.RemindRecipientsAsync(new GiftSendConfirmBulkRequest
        {
            RECIPIENT_IDS = recipientIds, ACTOR_EMPCD = CurrentUser.EmpCd
        });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        int? batch_id, string? dept_id, string? line_id, string? work_id, string? search, string? status,
        string? date_from, string? date_to)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });

        var data = await _svc.GetRecipientsAsync(
            CurrentUser.EmpCd, batch_id, dept_id, line_id, work_id, search, status, date_from, date_to, 1, 5000);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("BaoCaoQua");

        string[] headers = { "STT", "Mã NV", "Họ Tên", "Bộ phận", "Chuyền", "Nhóm việc", "Đợt quà", "Quà", "Ngày nhận dự kiến",
            "Địa điểm dự kiến", "Trạng thái", "Ngày phát thực tế", "Địa điểm phát", "Người phát", "Đã xác nhận", "Ghi chú" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#db2777");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        string StatusLabel(string s) => s switch
        {
            "READY"     => "Sẵn sàng nhận",
            "DELIVERED" => "Đã nhận (chưa xác nhận)",
            "COMPLETED" => "Hoàn tất",
            "EXPIRED"   => "Quá hạn",
            _ => s
        };

        int row = 2;
        foreach (var item in data.data)
        {
            ws.Cell(row, 1).Value  = row - 1;
            ws.Cell(row, 2).Value  = item.EMPCD;
            ws.Cell(row, 3).Value  = item.EMP_NAME ?? "";
            ws.Cell(row, 4).Value  = item.DEPT_NAME ?? "";
            ws.Cell(row, 5).Value  = item.LINE_NAME ?? "";
            ws.Cell(row, 6).Value  = item.WORK_NAME ?? "";
            ws.Cell(row, 7).Value  = item.BATCH_NAME ?? "";
            ws.Cell(row, 8).Value  = item.GIFT_ITEM_NAME ?? "";
            ws.Cell(row, 9).Value  = item.RECEIVE_DATE;
            ws.Cell(row, 10).Value = item.LOCATION ?? "";
            ws.Cell(row, 11).Value = StatusLabel(item.STATUS);
            ws.Cell(row, 12).Value = item.DELIVERED_DT ?? "";
            ws.Cell(row, 13).Value = item.DELIVERED_LOCATION ?? "";
            ws.Cell(row, 14).Value = item.DELIVERED_BY_NAME ?? "";
            ws.Cell(row, 15).Value = item.CONFIRM_STATUS == "CONFIRMED" ? "Đã xác nhận" : "";
            ws.Cell(row, 16).Value = item.NOTE ?? "";
            row++;
        }

        ws.Range(1, 1, 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"BaoCaoQua_{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
