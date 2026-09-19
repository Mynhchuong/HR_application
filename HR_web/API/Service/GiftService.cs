using HR_web.Models.Gift;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class GiftService
{
    private readonly ApiService _api;

    public GiftService(ApiService api)
    {
        _api = api;
    }

    // ── Danh mục ──────────────────────────────────────────────
    public async Task<List<GiftCategoryItem>> GetCategoriesAsync(bool activeOnly = false)
    {
        var result = await _api.GetAsync<CategoryListResponse>("gift/categories", $"active_only={activeOnly}");
        return result?.data ?? new();
    }

    public async Task<GiftActionResult> SaveCategoryAsync(GiftCategoryItem req, string actorEmpcd)
        => await PostForResult("gift/categories?actor_empcd=" + Uri.EscapeDataString(actorEmpcd), req);

    public async Task<GiftActionResult> DeleteCategoryAsync(int id)
        => await PostForResult($"gift/categories/delete?id={id}", new { });

    public async Task<List<GiftCatalogItem>> GetCatalogAsync(int? categoryId = null, bool activeOnly = false)
    {
        var query = $"active_only={activeOnly}";
        if (categoryId.HasValue) query += $"&category_id={categoryId}";
        var result = await _api.GetAsync<CatalogListResponse>("gift/catalog", query);
        return result?.data ?? new();
    }

    public async Task<GiftActionResult> SaveCatalogItemAsync(GiftCatalogSaveRequest req)
        => await PostForResult("gift/catalog", req);

    // ── Đợt quà ───────────────────────────────────────────────
    public async Task<List<GiftBatchItem>> GetBatchListAsync(int? batchId = null)
    {
        var result = batchId.HasValue
            ? await _api.GetAsync<BatchListResponse>("gift/batch", $"batch_id={batchId}")
            : await _api.GetAsync<BatchListResponse>("gift/batch");
        return result?.data ?? new();
    }

    public async Task<GiftActionResult> CreateBatchAsync(GiftBatchCreateRequest req)
        => await PostForResult("gift/batch", req);

    public async Task<GiftActionResult> CloseBatchAsync(GiftBatchCloseRequest req)
        => await PostForResult("gift/batch/close", req);

    public async Task<GiftBulkActionResponse> ImportRecipientsAsync(GiftRecipientImportRequest req)
    {
        var response = await _api.PostAsync("gift/batch/import", req);
        if (response != null)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GiftBulkActionResponse>(json) ?? new GiftBulkActionResponse { success = false, message = "Lỗi parse response" };
        }
        return new GiftBulkActionResponse { success = false, message = "Lỗi kết nối server" };
    }

    // ── Danh sách người nhận ──────────────────────────────────
    public async Task<GiftRecipientListResponse> GetRecipientsAsync(
        string callerEmpcd, int? batchId = null, string? deptId = null, string? lineId = null, string? workId = null,
        string? search = null, string? status = null, string? fromDate = null, string? toDate = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var query = $"caller_empcd={callerEmpcd}&page={page}&page_size={pageSize}";
            if (batchId.HasValue)                  query += $"&batch_id={batchId}";
            if (!string.IsNullOrEmpty(deptId))     query += $"&dept_id={deptId}";
            if (!string.IsNullOrEmpty(lineId))     query += $"&line_id={lineId}";
            if (!string.IsNullOrEmpty(workId))     query += $"&work_id={workId}";
            if (!string.IsNullOrEmpty(search))     query += $"&search={Uri.EscapeDataString(search)}";
            if (!string.IsNullOrEmpty(status))     query += $"&status={status}";
            if (!string.IsNullOrEmpty(fromDate))   query += $"&from_date={fromDate}";
            if (!string.IsNullOrEmpty(toDate))     query += $"&to_date={toDate}";

            var result = await _api.GetAsync<GiftRecipientListResponse>("gift/recipients", query);
            return result ?? new GiftRecipientListResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new GiftRecipientListResponse { success = false, message = ex.Message }; }
    }

    public async Task<GiftActionResult> MarkDeliveredAsync(GiftDeliverRequest req)
        => await PostForResult("gift/deliver", req);

    public async Task<GiftBulkActionResponse> MarkDeliveredBulkAsync(GiftDeliverBulkRequest req)
    {
        var response = await _api.PostAsync("gift/deliver-bulk", req);
        if (response != null)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GiftBulkActionResponse>(json) ?? new GiftBulkActionResponse { success = false, message = "Lỗi parse response" };
        }
        return new GiftBulkActionResponse { success = false, message = "Lỗi kết nối server" };
    }

    public async Task<GiftActionResult> MarkDeliveredAllAsync(GiftDeliverAllRequest req)
        => await PostForResult("gift/deliver-all", req);

    public async Task<GiftBulkActionResponse> SendConfirmRequestAsync(GiftSendConfirmBulkRequest req)
    {
        var response = await _api.PostAsync("gift/send-confirm-request", req);
        if (response != null)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GiftBulkActionResponse>(json) ?? new GiftBulkActionResponse { success = false, message = "Lỗi parse response" };
        }
        return new GiftBulkActionResponse { success = false, message = "Lỗi kết nối server" };
    }

    public async Task<GiftBulkActionResponse> RemindRecipientsAsync(GiftSendConfirmBulkRequest req)
    {
        var response = await _api.PostAsync("gift/remind", req);
        if (response != null)
        {
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GiftBulkActionResponse>(json) ?? new GiftBulkActionResponse { success = false, message = "Lỗi parse response" };
        }
        return new GiftBulkActionResponse { success = false, message = "Lỗi kết nối server" };
    }

    // ── Công nhân ─────────────────────────────────────────────
    public async Task<GiftActionResult> ConfirmReceiptAsync(GiftConfirmReceiptRequest req)
        => await PostForResult("gift/confirm-receipt", req);

    public async Task<GiftMyPendingResponse> GetMyPendingAsync(string empcd)
    {
        var result = await _api.GetAsync<GiftMyPendingResponse>("gift/my-pending", $"empcd={empcd}");
        return result ?? new GiftMyPendingResponse { success = false, message = "Lỗi kết nối server" };
    }

    public async Task<int?> GetOldestPendingIdAsync(string empcd)
    {
        var result = await _api.GetAsync<OldestPendingResponse>("gift/oldest-pending", $"empcd={empcd}");
        return result?.pendingId;
    }

    private async Task<GiftActionResult> PostForResult(string endpoint, object body)
    {
        try
        {
            var response = await _api.PostAsync(endpoint, body);
            if (response != null)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<GiftActionResult>(json) ?? new GiftActionResult { OK = false, MESSAGE = "Lỗi parse response" };
            }
            return new GiftActionResult { OK = false, MESSAGE = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new GiftActionResult { OK = false, MESSAGE = ex.Message }; }
    }

    private class CategoryListResponse { public bool success { get; set; } public List<GiftCategoryItem>? data { get; set; } }
    private class CatalogListResponse  { public bool success { get; set; } public List<GiftCatalogItem>?  data { get; set; } }
    private class BatchListResponse    { public bool success { get; set; } public List<GiftBatchItem>?    data { get; set; } }
    private class OldestPendingResponse { public int? pendingId { get; set; } }
}
