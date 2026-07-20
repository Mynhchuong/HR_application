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

    public async Task<OTTodayModel?> GetOTTodayAsync(string empcd, string? workDate = null)
    {
        try
        {
            var query = $"empcd={empcd}";
            if (!string.IsNullOrEmpty(workDate)) query += $"&work_date={workDate}";
            var result = await _api.GetAsync<OTResponse<OTTodayModel>>("ot/today", query);
            return (result != null && result.success) ? result.data : null;
        }
        catch (Exception ex) { Console.WriteLine($"[OtService] GetOTTodayAsync error: {ex.Message}"); return null; }
    }

    public async Task<OTConfirmResponse> ConfirmOTAsync(string empcd, string confirmStatus, string? workDate = null, decimal? otHours = null)
    {
        try
        {
            var payload = new OTConfirmRequest { 
                EMPCD = empcd, 
                CONFIRM_STATUS = confirmStatus, 
                WORK_DATE = workDate,
                OT_HOURS = otHours
            };
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
        catch (Exception ex) { Console.WriteLine($"[OtService] GetOTClerkAsync error: {ex.Message}"); return null; }
    }

    public async Task<OTClerkPagedResponse> GetOTClerkDetailAsync(
        string clerkEmpcd, string? workDate = null,
        string? status = null, string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        int page = 1, int pageSize = 100)
    {
        try
        {
            var q = new List<string> { $"clerk_empcd={Uri.EscapeDataString(clerkEmpcd)}" };
            if (!string.IsNullOrEmpty(workDate)) q.Add($"work_date={Uri.EscapeDataString(workDate)}");
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");

            var response = await _api.GetAsync_Raw("ot/clerk", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OTClerkPagedResponse>(json);
                if (result != null) return result;
            }
            return new OTClerkPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex)
        {
            return new OTClerkPagedResponse { success = false, message = ex.Message, data = new() };
        }
    }

    public async Task<OTHRDetailPagedResponse> GetOTSupervisorDetailAsync(
        string supervisorEmpcd, string filterType,
        string? workDate = null, string? status = null,
        string? search = null,
        string? deptId = null, string? lineId = null, string? workId = null,
        int page = 1, int pageSize = 100)
    {
        try
        {
            var q = new List<string>
            {
                $"supervisor_empcd={Uri.EscapeDataString(supervisorEmpcd)}",
                $"filter_type={Uri.EscapeDataString(filterType)}"
            };
            if (!string.IsNullOrEmpty(workDate)) q.Add($"work_date={Uri.EscapeDataString(workDate)}");
            if (!string.IsNullOrEmpty(status))   q.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(search))   q.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(deptId))   q.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(lineId))   q.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId))   q.Add($"work_id={Uri.EscapeDataString(workId)}");
            q.Add($"page={page}");
            q.Add($"page_size={pageSize}");

            var response = await _api.GetAsync_Raw("ot/supervisor", string.Join("&", q));
            if (response != null && response.IsSuccessStatusCode)
            {
                var json   = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<OTHRDetailPagedResponse>(json);
                if (result != null) return result;
            }
            return new OTHRDetailPagedResponse { success = false, message = "Lỗi kết nối API", data = new() };
        }
        catch (Exception ex)
        {
            return new OTHRDetailPagedResponse { success = false, message = ex.Message, data = new() };
        }
    }

    public async Task<List<OTHRSummaryModel>> GetOTHRSummaryAsync(string? workDate = null, string? deptId = null, string? actorEmpCd = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(actorEmpCd)) queryParams.Add($"actor_empcd={Uri.EscapeDataString(actorEmpCd)}");
            if (!string.IsNullOrEmpty(workDate)) queryParams.Add($"work_date={workDate}");
            if (!string.IsNullOrEmpty(deptId)) queryParams.Add($"dept_id={deptId}");
            var result = await _api.GetAsync<OTResponse<List<OTHRSummaryModel>>>("ot/hr/summary", string.Join("&", queryParams));
            return (result != null && result.success) ? result.data ?? new() : new();
        }
        catch (Exception ex) { Console.WriteLine($"[OtService] GetOTHRSummaryAsync error: {ex.Message}"); return new(); }
    }

    public async Task<OTHRDetailPagedResponse> GetOTHRDetailAsync(
        string? workDate = null, string? deptId = null,
        string? search = null, string? status = null,
        string? deptName = null, string? lineName = null,
        string? lineId = null, string? workId = null,
        int page = 1, int pageSize = 50, int admin = 0, string? actorEmpCd = null)
    {
        try
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(actorEmpCd)) queryParams.Add($"actor_empcd={Uri.EscapeDataString(actorEmpCd)}");
            if (!string.IsNullOrEmpty(workDate)) queryParams.Add($"work_date={Uri.EscapeDataString(workDate)}");
            if (!string.IsNullOrEmpty(deptId)) queryParams.Add($"dept_id={Uri.EscapeDataString(deptId)}");
            if (!string.IsNullOrEmpty(search)) queryParams.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrEmpty(status)) queryParams.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(deptName)) queryParams.Add($"dept_name={Uri.EscapeDataString(deptName)}");
            if (!string.IsNullOrEmpty(lineName)) queryParams.Add($"line_name={Uri.EscapeDataString(lineName)}");
            if (!string.IsNullOrEmpty(lineId)) queryParams.Add($"line_id={Uri.EscapeDataString(lineId)}");
            if (!string.IsNullOrEmpty(workId)) queryParams.Add($"work_id={Uri.EscapeDataString(workId)}");
            queryParams.Add($"page={page}");
            queryParams.Add($"page_size={pageSize}");
            if (admin == 1) queryParams.Add("admin=1");

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

    public async Task<(bool success, string message, int sent)> RemindPendingOTSignAsync(
        string clerkEmpCd, string workDate,
        string? deptId = null, string? lineId = null, string? workId = null)
    {
        try
        {
            var response = await _api.PostAsync("ot/clerk/remind-pending", new
            {
                clerk_empcd = clerkEmpCd,
                work_date   = workDate,
                dept_id     = deptId,
                line_id     = lineId,
                work_id     = workId
            });
            if (response != null && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var obj  = JsonConvert.DeserializeObject<dynamic>(json);
                bool ok  = obj?.success == true;
                string msg = (string?)obj?.message ?? "";
                int sent = ok ? (int)(obj?.sent ?? 0) : 0;
                return (ok, msg, sent);
            }
            return (false, "Lỗi kết nối server", 0);
        }
        catch (Exception ex) { return (false, ex.Message, 0); }
    }

    // ─────────────────────────────────────────────────────────────
    // Admin bulk operations
    // ─────────────────────────────────────────────────────────────
    public async Task<OTAdminBulkResponse> AdminBulkSignForAsync(OTAdminBulkSignForRequest req)
    {
        try
        {
            var response = await _api.PostAsync("ot/admin/bulk-signfor", req);
            if (response?.IsSuccessStatusCode == true)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<OTAdminBulkResponse>(json)
                       ?? new OTAdminBulkResponse { success = false, message = "Không parse được response" };
            }
            return new OTAdminBulkResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new OTAdminBulkResponse { success = false, message = ex.Message }; }
    }

    public async Task<OTAdminBulkResponse> AdminBulkUpdateAsync(OTAdminBulkUpdateRequest req)
    {
        try
        {
            var response = await _api.PostAsync("ot/admin/bulk-update", req);
            if (response?.IsSuccessStatusCode == true)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<OTAdminBulkResponse>(json)
                       ?? new OTAdminBulkResponse { success = false, message = "Không parse được response" };
            }
            return new OTAdminBulkResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new OTAdminBulkResponse { success = false, message = ex.Message }; }
    }

    public async Task<OTAdminBulkResponse> AdminBulkDeleteAsync(OTAdminBulkDeleteRequest req)
    {
        try
        {
            var response = await _api.PostAsync("ot/admin/bulk-delete", req);
            if (response?.IsSuccessStatusCode == true)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<OTAdminBulkResponse>(json)
                       ?? new OTAdminBulkResponse { success = false, message = "Không parse được response" };
            }
            return new OTAdminBulkResponse { success = false, message = "Lỗi kết nối server" };
        }
        catch (Exception ex) { return new OTAdminBulkResponse { success = false, message = ex.Message }; }
    }

    // HR bấm "Thông báo ký OT" — broadcast theo dept/line/work cho ngày work_date
    public async Task<string> HrNotifyPendingRawAsync(string workDate, string? deptId, string? createdBy)
    {
        try
        {
            var response = await _api.PostAsync("ot/hr/notify-pending", new
            {
                work_date  = workDate,
                dept_id    = deptId,
                created_by = createdBy
            });
            if (response?.IsSuccessStatusCode == true)
                return await response.Content.ReadAsStringAsync();
            return "{\"success\":false,\"message\":\"Lỗi kết nối server\"}";
        }
        catch (Exception ex)
        {
            return "{\"success\":false,\"message\":\"" + ex.Message.Replace("\"","'") + "\"}";
        }
    }
}
