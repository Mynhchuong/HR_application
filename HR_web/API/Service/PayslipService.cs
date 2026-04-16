using HR_web.Models.Payslip;
using Newtonsoft.Json;
using System.Globalization;

namespace HR_web.API.Service;

public class PayslipService
{
    private readonly ApiService _api;

    public PayslipService(ApiService api)
    {
        _api = api;
    }

    private class PayslipResponse<T>
    {
        public bool success { get; set; }
        public T? data { get; set; }
        public string? message { get; set; }
    }

    private async Task<T?> ParseResponse<T>(HttpResponseMessage? response)
    {
        if (response == null || !response.IsSuccessStatusCode) return default;
        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(json) ? default : JsonConvert.DeserializeObject<T>(json);
    }

    public async Task<List<PayrollPeriodModel>> GetPeriodsAsync()
    {
        var result = await _api.GetAsync<PayslipResponse<List<PayrollPeriodModel>>>("payslip/periods");
        return (result != null && result.success) ? result.data ?? new() : new();
    }

    public async Task<PayslipApiResponse<decimal>> CreatePeriodAsync(string periodName, DateTime? start, DateTime? end,
        DateTime? publishDate, int isAutoPublish, string remark, string instId)
    {
        var payload = new { PERIOD_NAME = periodName, START_DATE = start, END_DATE = end, PUBLISH_DATE = publishDate, IS_AUTO_PUBLISH = isAutoPublish, REMARK = remark, INST_ID = instId };
        var response = await _api.PostAsync("payslip/period", payload);
        return await ParseResponse<PayslipApiResponse<decimal>>(response)
               ?? new PayslipApiResponse<decimal> { success = false, message = "Lỗi server" };
    }

    public async Task<List<PayrollItemModel>> GetItemsVisibilityAsync(decimal periodId)
    {
        var result = await _api.GetAsync<PayslipResponse<List<PayrollItemModel>>>("payslip/items", $"periodId={periodId.ToString(CultureInfo.InvariantCulture)}");
        return (result != null && result.success) ? result.data ?? new() : new();
    }

    public async Task<PayslipApiResponse<object>> UpdateItemsVisibilityAsync(decimal periodId, List<PayrollItemModel> items)
    {
        var response = await _api.PostAsync("payslip/items/update", new { PERIOD_ID = periodId, Items = items });
        return await ParseResponse<PayslipApiResponse<object>>(response)
               ?? new PayslipApiResponse<object> { success = false, message = "Lỗi server" };
    }

    public async Task<PayslipApiResponse<object>> UploadPayslipAsync(decimal periodId, List<PayslipUploadRow> data, bool isFirstBatch)
    {
        var response = await _api.PostAsync("payslip/upload", new { PERIOD_ID = periodId, Data = data, IsFirstBatch = isFirstBatch });
        return await ParseResponse<PayslipApiResponse<object>>(response)
               ?? new PayslipApiResponse<object> { success = false, message = "Lỗi từ API Backend" };
    }

    public async Task<List<PayrollDataModel>> GetMyPayslipAsync(string empcd, decimal periodId)
    {
        var result = await _api.GetAsync<PayslipResponse<List<PayrollDataModel>>>("payslip/my-payslip",
            $"empcd={empcd}&periodId={periodId.ToString(CultureInfo.InvariantCulture)}");
        return (result != null && result.success) ? result.data ?? new() : new();
    }

    public async Task<PayslipAdminPagedResponse> GetAdminListAsync(decimal periodId, string? search = null, int page = 1, int pageSize = 100)
    {
        string query = $"periodId={periodId.ToString(CultureInfo.InvariantCulture)}&page={page}&page_size={pageSize}";
        if (!string.IsNullOrEmpty(search)) query += $"&search={search}";
        var result = await _api.GetAsync<PayslipAdminPagedResponse>("payslip/admin/list", query);
        return result ?? new PayslipAdminPagedResponse { success = false, data = new() };
    }

    public async Task<PayslipAdminPagedResponse> GetExportDataAsync(decimal periodId)
    {
        var result = await _api.GetAsync<PayslipAdminPagedResponse>("payslip/admin/export", $"periodId={periodId.ToString(CultureInfo.InvariantCulture)}");
        return result ?? new PayslipAdminPagedResponse { success = false, data = new() };
    }

    public async Task<PayslipApiResponse<object>> ReleasePeriodAsync(decimal id, string updtId)
    {
        var response = await _api.PostAsync("payslip/release", new { ID = id, UPDT_ID = updtId });
        return await ParseResponse<PayslipApiResponse<object>>(response)
               ?? new PayslipApiResponse<object> { success = false, message = "Lỗi server" };
    }
}
