using HR_web.Models.OT;
using Newtonsoft.Json;

namespace HR_web.API.Service;

public class OtService
{
    private readonly ApiService _api;

    public OtService(ApiService api)
    {
        _api = api;
    }

    public async Task<OTTodayModel?> GetOTTodayAsync(string empcd)
    {
        try
        {
            var result = await _api.GetAsync<OTResponse<OTTodayModel>>("ot/today", $"empcd={empcd}");
            return (result != null && result.success) ? result.data : null;
        }
        catch { return null; }
    }

    public async Task<OTConfirmResponse> ConfirmOTAsync(string empcd, string confirmStatus, string? rejectReason = null)
    {
        try
        {
            var payload = new OTConfirmRequest { EMPCD = empcd, CONFIRM_STATUS = confirmStatus, REJECT_REASON = rejectReason };
            var response = await _api.PostAsync("ot/confirm", payload);
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<OTConfirmResponse>(json)
                       ?? new OTConfirmResponse { success = false, message = "Lỗi parse response" };
            }
            return new OTConfirmResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex)
        {
            return new OTConfirmResponse { success = false, message = ex.Message };
        }
    }

    public async Task<OTClerkResponse?> GetOTClerkAsync(string clerkEmpcd, string? workDate = null)
    {
        try
        {
            var query = $"clerk_empcd={clerkEmpcd}";
            if (!string.IsNullOrEmpty(workDate)) query += $"&work_date={workDate}";
            return await _api.GetAsync<OTClerkResponse>("ot/clerk", query);
        }
        catch { return null; }
    }

    public async Task<List<OTHRSummaryModel>> GetOTHRSummaryAsync(string? workDate = null, string? deptId = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(workDate)) queryParams.Add($"work_date={workDate}");
            if (!string.IsNullOrEmpty(deptId)) queryParams.Add($"dept_id={deptId}");
            var result = await _api.GetAsync<OTResponse<List<OTHRSummaryModel>>>("ot/hr/summary", string.Join("&", queryParams));
            return (result != null && result.success) ? result.data ?? new() : new();
        }
        catch { return new(); }
    }

    public async Task<OTHRDetailPagedResponse> GetOTHRDetailAsync(
        string? workDate = null, string? deptId = null,
        string? search = null, string? status = null,
        string? deptName = null, string? lineName = null,
        int page = 1, int pageSize = 50)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(workDate)) queryParams.Add($"work_date={Uri.EscapeDataString(workDate)}");
            if (!string.IsNullOrEmpty(deptId)) queryParams.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(search)) queryParams.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(deptName)) queryParams.Add($"dept_name={Uri.EscapeDataString(deptName)}");
            if (!string.IsNullOrEmpty(lineName)) queryParams.Add($"line_name={Uri.EscapeDataString(lineName)}");
            queryParams.Add($"page={page}");
            queryParams.Add($"page_size={pageSize}");

            var response = await _api.GetAsync_Raw("ot/hr/detail", string.Join("&", queryParams));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OTHRDetailPagedResponse>(json);
                if (result == null)
                {
                    return new OTHRDetailPagedResponse
                    {
                        success = false,
                        message = "Khong parse duoc du lieu OT HR.",
                        page = page,
                        page_size = pageSize,
                        data = new()
                    };
                }

                result.data ??= new();

                // Backend hien tai co luc tra ve payload rong voi success=false khi khong co ban ghi.
                // Chuan hoa truong hop nay thanh ket qua hop le de man hinh HR hien "khong co du lieu".
                if (!result.success &&
                    result.page == 0 &&
                    result.page_size == 0 &&
                    result.total == 0 &&
                    result.total_pages == 0 &&
                    result.data.Count == 0 &&
                    string.IsNullOrWhiteSpace(result.message))
                {
                    result.success = true;
                    result.page = page;
                    result.page_size = pageSize;
                }

                return result;
            }
            return new OTHRDetailPagedResponse
            {
                success = false,
                message = response == null ? "Khong ket noi duoc API OT HR." : $"API OT HR tra ve HTTP {(int)response.StatusCode}.",
                page = page,
                page_size = pageSize,
                data = new()
            };
        }
        catch (Exception ex)
        {
            return new OTHRDetailPagedResponse
            {
                success = false,
                message = ex.Message,
                page = page,
                page_size = pageSize,
                data = new()
            };
        }
    }
}
