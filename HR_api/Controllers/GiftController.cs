using HR_api.Models.Gift;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class GiftController : ControllerBase
{
    private readonly GiftService _svc;

    public GiftController(GiftService svc)
    {
        _svc = svc;
    }

    // ── Danh mục ──────────────────────────────────────────────
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(bool active_only = false)
    {
        try { return Ok(new { success = true, data = await _svc.GetCategoriesAsync(active_only) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("categories")]
    public async Task<IActionResult> SaveCategory([FromBody] GiftCategoryItem req, [FromQuery] string actor_empcd)
    {
        try { return Ok(await _svc.SaveCategoryAsync(req, actor_empcd)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpPost("categories/delete")]
    public async Task<IActionResult> DeleteCategory([FromQuery] int id)
    {
        try { return Ok(await _svc.DeleteCategoryAsync(id)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(int? category_id = null, bool active_only = false)
    {
        try { return Ok(new { success = true, data = await _svc.GetCatalogAsync(category_id, active_only) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("catalog")]
    public async Task<IActionResult> SaveCatalogItem([FromBody] GiftCatalogSaveRequest req)
    {
        try { return Ok(await _svc.SaveCatalogItemAsync(req)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    // ── Đợt quà ───────────────────────────────────────────────
    [HttpGet("batch")]
    public async Task<IActionResult> GetBatchList(int? batch_id = null)
    {
        try { return Ok(new { success = true, data = await _svc.GetBatchListAsync(batch_id) }); }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    [HttpPost("batch")]
    public async Task<IActionResult> CreateBatch([FromBody] GiftBatchCreateRequest req)
    {
        try { return Ok(await _svc.CreateBatchAsync(req)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpPost("batch/close")]
    public async Task<IActionResult> CloseBatch([FromBody] GiftBatchCloseRequest req)
    {
        try { return Ok(await _svc.CloseBatchAsync(req)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpPost("batch/import")]
    public async Task<IActionResult> ImportRecipients([FromBody] GiftRecipientImportRequest req)
    {
        try
        {
            if (req?.ROWS == null || req.ROWS.Count == 0)
                return Ok(new GiftBulkActionResponse { success = false, message = "Danh sách rỗng" });
            return Ok(await _svc.ImportRecipientsAsync(req));
        }
        catch (Exception ex) { return Ok(new GiftBulkActionResponse { success = false, message = ex.Message }); }
    }

    // ── Danh sách người nhận (admin list / report) ───────────
    [HttpGet("recipients")]
    public async Task<IActionResult> GetRecipients(
        string  caller_empcd,
        int?    batch_id  = null,
        string? dept_id   = null,
        string? line_id   = null,
        string? work_id   = null,
        string? search    = null,
        string? status    = null,
        string? from_date = null,
        string? to_date   = null,
        int     page      = 1,
        int     page_size = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caller_empcd))
                return Ok(new { success = false, message = "Thiếu mã người dùng" });

            // Xem theo 1 đợt cụ thể (BatchDetail) -> không giới hạn theo tháng, phải thấy hết người
            // nhận của đợt đó bất kể RECEIVE_DATE xa hiện tại cỡ nào. Chỉ áp mặc định "tháng này" khi
            // xem tổng hợp nhiều đợt (Report) và người dùng chưa tự chọn ngày.
            DateTime fromDate = DateTime.TryParseExact(from_date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fd)
                ? fd : (batch_id.HasValue ? new DateTime(2000, 1, 1) : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
            DateTime toDate = DateTime.TryParseExact(to_date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var td)
                ? td : (batch_id.HasValue ? DateTime.Today.AddYears(5) : DateTime.Today.AddMonths(2));

            bool isAdminOrHr = await _svc.IsAdminOrHRAsync(caller_empcd);

            var result = await _svc.GetRecipientListAsync(
                caller_empcd, isAdminOrHr, batch_id, dept_id, line_id, work_id, search,
                fromDate, toDate, status, page, page_size);

            return Ok(result);
        }
        catch (Exception ex) { return Ok(new { success = false, message = "API Error: " + ex.Message }); }
    }

    [HttpPost("deliver")]
    public async Task<IActionResult> MarkDelivered([FromBody] GiftDeliverRequest req)
    {
        try { return Ok(await _svc.MarkDeliveredAsync(req)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpPost("deliver-bulk")]
    public async Task<IActionResult> MarkDeliveredBulk([FromBody] GiftDeliverBulkRequest req)
    {
        try
        {
            if (req?.RECIPIENT_IDS == null || req.RECIPIENT_IDS.Count == 0)
                return Ok(new GiftBulkActionResponse { success = false, message = "Danh sách rỗng" });
            return Ok(await _svc.MarkDeliveredBulkAsync(req));
        }
        catch (Exception ex) { return Ok(new GiftBulkActionResponse { success = false, message = ex.Message }); }
    }

    [HttpPost("deliver-all")]
    public async Task<IActionResult> MarkDeliveredAll([FromBody] GiftDeliverAllRequest req)
    {
        try { return Ok(await _svc.MarkDeliveredAllAsync(req)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpPost("send-confirm-request")]
    public async Task<IActionResult> SendConfirmRequest([FromBody] GiftSendConfirmBulkRequest req)
    {
        try
        {
            if (req?.RECIPIENT_IDS == null || req.RECIPIENT_IDS.Count == 0)
                return Ok(new GiftBulkActionResponse { success = false, message = "Danh sách rỗng" });
            return Ok(await _svc.SendConfirmRequestBulkAsync(req));
        }
        catch (Exception ex) { return Ok(new GiftBulkActionResponse { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Gift/remind — Thư ký/HR/Admin nhắc công nhân đến lãnh quà (chỉ gửi thông báo,
    // không đổi trạng thái, không chặn app).
    [HttpPost("remind")]
    public async Task<IActionResult> Remind([FromBody] GiftSendConfirmBulkRequest req)
    {
        try
        {
            if (req?.RECIPIENT_IDS == null || req.RECIPIENT_IDS.Count == 0)
                return Ok(new GiftBulkActionResponse { success = false, message = "Danh sách rỗng" });
            return Ok(await _svc.RemindRecipientsBulkAsync(req));
        }
        catch (Exception ex) { return Ok(new GiftBulkActionResponse { success = false, message = ex.Message }); }
    }

    // ── Công nhân ─────────────────────────────────────────────
    [HttpPost("confirm-receipt")]
    public async Task<IActionResult> ConfirmReceipt([FromBody] GiftConfirmReceiptRequest req)
    {
        try { return Ok(await _svc.ConfirmReceiptAsync(req)); }
        catch (Exception ex) { return Ok(new GiftActionResult { OK = false, MESSAGE = ex.Message }); }
    }

    [HttpGet("my-pending")]
    public async Task<IActionResult> GetMyPending(string empcd)
    {
        try { return Ok(await _svc.GetMyPendingAsync(empcd)); }
        catch (Exception ex) { return Ok(new GiftMyPendingResponse { success = false, message = ex.Message }); }
    }

    // GET /apiHR/Gift/oldest-pending — dùng riêng cho GiftGateFilter (HR_web)
    [HttpGet("oldest-pending")]
    public async Task<IActionResult> GetOldestPending(string empcd)
    {
        try
        {
            var id = await _svc.GetOldestPendingConfirmIdAsync(empcd);
            return Ok(new { pendingId = id });
        }
        catch (Exception) { return Ok(new { pendingId = (int?)null }); }
    }
}
