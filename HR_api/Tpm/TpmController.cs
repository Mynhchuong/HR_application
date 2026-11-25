using Microsoft.AspNetCore.Mvc;

namespace HR_api.Tpm;

/// <summary>Công nhân sản xuất dùng My Samho: quét mã máy xem thông tin, báo máy hư, xem lịch sử báo
/// hư của mình. HR_api chỉ kiểm tra người dùng (không phải Expat/HR, còn đi làm) rồi chuyển tiếp sang
/// TPM.API, giữ nguyên JSON TPM trả về (success/code/message/data) để app dịch theo `code`.
/// Module tự chứa trong thư mục Tpm/, xem HR_application/TPM.DOC/README.md.</summary>
[ApiController]
[Route("apiHR/[controller]")]
public class TpmController : ControllerBase
{
    private readonly TpmService _tpm;
    private readonly TpmBrokenNotifyService _notify;
    private readonly IConfiguration _cfg;

    public TpmController(TpmService tpm, TpmBrokenNotifyService notify, IConfiguration cfg)
    {
        _tpm = tpm;
        _notify = notify;
        _cfg = cfg;
    }

    private static readonly object Unavailable = new { success = false, code = "TPM_UNAVAILABLE", message = "Hệ thống bảo trì máy tạm thời không khả dụng, vui lòng thử lại sau." };

    // POST apiHR/Tpm/notify/broken  header X-MySamho-Key  body { BrokenId }
    // Webhook: TPM gọi ngay khi có phiếu báo hư mới → báo tức thì cho bảo trì đúng khu vực (TpmBrokenNotifyService). Không cần X-Api-Key (middleware
    // miễn path này), tự kiểm khóa nội bộ. Trả nhanh, việc gửi chạy nền.
    [HttpPost("notify/broken")]
    public IActionResult NotifyBroken([FromBody] TpmBrokenCreatedBody body)
    {
        var expected = _cfg["Tpm:InternalKey"] ?? "";
        var got = Request.Headers["X-MySamho-Key"].ToString();
        if (expected.Length == 0 || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(got), System.Text.Encoding.UTF8.GetBytes(expected)))
            return Unauthorized(new { success = false, message = "Unauthorized" });
        if (body?.BrokenId is not > 0) return BadRequest(new { success = false, message = "Thiếu BrokenId" });

        var id = body.BrokenId.Value;
        _ = Task.Run(async () => { try { await _notify.NotifyBrokenAsync(id); } catch (Exception ex) { Console.WriteLine($"[TpmNotify] {ex.Message}"); } });
        return Ok(new { success = true });
    }

    // GET apiHR/Tpm/machine?empcd=...&itemCode=...
    [HttpGet("machine")]
    public async Task<IActionResult> GetMachine(string? empcd, string? itemCode)
    {
        var (emp, error) = await _tpm.ResolveEmployeeAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        if (string.IsNullOrWhiteSpace(itemCode)) return Ok(new { success = false, code = "MISSING_ITEM_CODE", message = "Thiếu mã máy." });

        return Relay(await _tpm.CallAsync(HttpMethod.Get, $"/api/mysamho/machines/{Uri.EscapeDataString(itemCode.Trim())}"));
    }

    // POST apiHR/Tpm/report  body { empcd, itemCode, description }
    [HttpPost("report")]
    public async Task<IActionResult> Report([FromBody] TpmReportBody body)
    {
        var (emp, error) = await _tpm.ResolveEmployeeAsync(body?.Empcd);
        if (emp == null) return Ok(new { success = false, message = error });

        var reportResult = await _tpm.CallAsync(HttpMethod.Post, "/api/mysamho/broken/report", new
        {
            ItemCode = body!.ItemCode,
            Description = body.Description,
            EmpCd = emp.EmpCd,
            // Tên/bộ phận trong HR lưu bảng mã VNI -> đổi sang Unicode cho TPM (không có font VNI).
            Name = VniConverter.ToUnicode(emp.Name),
            DeptCd = emp.DeptCd, DeptNm = VniConverter.ToUnicode(emp.DeptNm),
            LineCd = emp.LineCd, LineNm = VniConverter.ToUnicode(emp.LineNm),
            WorkCd = emp.WorkCd, WorkNm = VniConverter.ToUnicode(emp.WorkNm),
        });

        // Báo hư từ My Samho: HR_api đã biết ngay brokenId → báo cho bảo trì khu vực luôn, không phụ thuộc việc TPM có gọi webhook hay không
        // (TPM gọi thêm cũng không sao: TpmBrokenNotifyService chống gửi trùng bằng HR_NOTIFICATIONS).
        if (reportResult.reached && reportResult.status == 200)
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(reportResult.body);
                if (d.RootElement.TryGetProperty("brokenId", out var idEl) && idEl.TryGetInt64(out var newId) && newId > 0)
                    _ = Task.Run(async () => { try { await _notify.NotifyBrokenAsync(newId); } catch (Exception ex) { Console.WriteLine($"[TpmNotify] {ex.Message}"); } });
            }
            catch { /* body lạ → bỏ qua, lưới an toàn hỏi thưa vẫn bắt được */ }
        }
        return Relay(reportResult);
    }

    // GET apiHR/Tpm/locations?empcd=...&parentId=...   (không có parentId = cấp 1) — dựng combobox liên động chọn vị trí
    [HttpGet("locations")]
    public async Task<IActionResult> Locations(string? empcd, long? parentId)
    {
        var (emp, error) = await _tpm.ResolveEmployeeAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });

        var path = "/api/mysamho/locations";
        if (parentId.HasValue) path += $"?parentId={parentId.Value}";
        return Relay(await _tpm.CallAsync(HttpMethod.Get, path));
    }

    // POST apiHR/Tpm/location-report  body { empcd, itemCode, suggestedLocationId, note }
    // Báo "vị trí máy trong hệ thống sai": chỉ GHI NHẬN, bảo trì xác nhận trên TPM.Web mới đổi vị trí thật.
    [HttpPost("location-report")]
    public async Task<IActionResult> LocationReport([FromBody] TpmLocationReportBody body)
    {
        var (emp, error) = await _tpm.ResolveEmployeeAsync(body?.Empcd);
        if (emp == null) return Ok(new { success = false, message = error });

        var note = body!.Note?.Trim();
        if (!string.IsNullOrEmpty(note) && System.Text.Encoding.UTF8.GetByteCount(note) > 500)
            return Ok(new { success = false, code = "NOTE_TOO_LONG", message = "Ghi chú quá dài." });

        return Relay(await _tpm.CallAsync(HttpMethod.Post, "/api/mysamho/location-report", new
        {
            ItemCode = body.ItemCode,
            SuggestedLocationId = body.SuggestedLocationId ?? 0,
            Note = string.IsNullOrEmpty(note) ? null : note,
            EmpCd = emp.EmpCd,
            Name = VniConverter.ToUnicode(emp.Name),
            DeptCd = emp.DeptCd, DeptNm = VniConverter.ToUnicode(emp.DeptNm),
            LineCd = emp.LineCd, LineNm = VniConverter.ToUnicode(emp.LineNm),
            WorkCd = emp.WorkCd, WorkNm = VniConverter.ToUnicode(emp.WorkNm),
        }));
    }

    // GET apiHR/Tpm/history?empcd=...&status=...
    [HttpGet("history")]
    public async Task<IActionResult> History(string? empcd, string? status)
    {
        var (emp, error) = await _tpm.ResolveEmployeeAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });

        var path = $"/api/mysamho/broken?empCd={Uri.EscapeDataString(emp.EmpCd)}";
        if (!string.IsNullOrWhiteSpace(status)) path += $"&status={Uri.EscapeDataString(status)}";
        return Relay(await _tpm.CallAsync(HttpMethod.Get, path));
    }

    // ───────── Bảo trì dùng My Samho (tích hợp 3): chỉ thẻ trong HR_TPM_MAINT; TPM tự tra TPM_USER theo EmpCd và trả 401/403
    // nếu không có quyền → chuyển nguyên body để HR_web đọc `code`.

    // GET apiHR/Tpm/maintenance/areas?empcd=   (200 = là bảo trì; 401/403 = không có quyền ở TPM)
    [HttpGet("maintenance/areas")]
    public async Task<IActionResult> MaintAreas(string? empcd)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        return Relay(await _tpm.CallAsync(HttpMethod.Get, $"/api/mysamho/maintenance/areas?empCd={Uri.EscapeDataString(emp.EmpCd)}"));
    }

    // GET apiHR/Tpm/maintenance/broken?empcd=&myAreaOnly=true|false
    [HttpGet("maintenance/broken")]
    public async Task<IActionResult> MaintBroken(string? empcd, bool myAreaOnly = true)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        return Relay(await _tpm.CallAsync(HttpMethod.Get,
            $"/api/mysamho/maintenance/broken?empCd={Uri.EscapeDataString(emp.EmpCd)}&myAreaOnly={(myAreaOnly ? "true" : "false")}"));
    }

    // PUT apiHR/Tpm/maintenance/receive  body { empcd, brokenId }
    [HttpPut("maintenance/receive")]
    public async Task<IActionResult> MaintReceive([FromBody] TpmReceiveBody body)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(body?.Empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        if (body!.BrokenId is not > 0) return Ok(new { success = false, message = "Thiếu mã phiếu." });
        return Relay(await _tpm.CallAsync(HttpMethod.Put, $"/api/mysamho/maintenance/broken/{body.BrokenId}/receive", new { EmpCd = emp.EmpCd }));
    }

    // PUT apiHR/Tpm/maintenance/repair  body { empcd, brokenId, itemCode, issueId?, solveGroupId?, repairNote? }
    [HttpPut("maintenance/repair")]
    public async Task<IActionResult> MaintRepair([FromBody] TpmRepairBody body)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(body?.Empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        if (body!.BrokenId is not > 0) return Ok(new { success = false, message = "Thiếu mã phiếu." });
        if (string.IsNullOrWhiteSpace(body.ItemCode)) return Ok(new { success = false, code = "MISSING_ITEM_CODE", message = "Cần quét/nhập lại mã máy để xác nhận." });
        return Relay(await _tpm.CallAsync(HttpMethod.Put, $"/api/mysamho/maintenance/broken/{body.BrokenId}/repair", new
        {
            EmpCd = emp.EmpCd,
            ItemCode = body.ItemCode.Trim(),
            IssueId = body.IssueId,
            SolveGroupId = body.SolveGroupId,
            RepairNote = string.IsNullOrWhiteSpace(body.RepairNote) ? null : body.RepairNote.Trim(),
        }));
    }

    // POST apiHR/Tpm/maintenance/transfer  body { empcd, locationId, itemCodes[] }
    // Bảo trì chuyển vị trí hàng loạt (giống trang "Chuyển vị trí" bên TPM.Web). TPM kiểm quyền + vị trí đích + từng máy,
    // trả kết quả theo từng mã máy (máy không tồn tại/đã đúng vị trí không chặn các máy còn lại).
    [HttpPost("maintenance/transfer")]
    public async Task<IActionResult> MaintTransfer([FromBody] TpmTransferBody body)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(body?.Empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        if (body!.LocationId is not > 0) return Ok(new { success = false, code = "MISSING_LOCATION", message = "Vui lòng chọn vị trí đích." });

        var codes = (body.ItemCodes ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        if (codes.Count == 0) return Ok(new { success = false, code = "MISSING_ITEM_CODE", message = "Vui lòng thêm ít nhất một máy." });
        if (codes.Count > 50) return Ok(new { success = false, code = "TOO_MANY_ITEMS", message = "Tối đa 50 máy mỗi lần chuyển." });

        return Relay(await _tpm.CallAsync(HttpMethod.Post, "/api/mysamho/maintenance/transfer", new
        {
            EmpCd = emp.EmpCd,
            LocationId = body.LocationId,
            ItemCodes = codes,
        }));
    }

    // GET apiHR/Tpm/maintenance/issues?empcd=   |   GET apiHR/Tpm/maintenance/solve-groups?empcd=   (dropdown của màn Sửa xong)
    [HttpGet("maintenance/issues")]
    public async Task<IActionResult> MaintIssues(string? empcd)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        return Relay(await _tpm.CallAsync(HttpMethod.Get, "/api/mysamho/maintenance/issues"));
    }

    [HttpGet("maintenance/solve-groups")]
    public async Task<IActionResult> MaintSolveGroups(string? empcd)
    {
        var (emp, error) = await _tpm.ResolveMaintenanceAsync(empcd);
        if (emp == null) return Ok(new { success = false, message = error });
        return Relay(await _tpm.CallAsync(HttpMethod.Get, "/api/mysamho/maintenance/solve-groups"));
    }

    // HR_api trả 200 + success=false cho lỗi nghiệp vụ; TPM trả 4xx kèm body cùng dạng nên chuyển
    // nguyên body (kể cả lỗi) để app đọc `code`.
    private IActionResult Relay((bool reached, int status, string body) r)
    {
        // TPM/IIS lỗi 5xx thường trả trang HTML (không phải JSON) → coi như không khả dụng, đừng đẩy HTML xuống app.
        if (!r.reached || string.IsNullOrWhiteSpace(r.body) || r.status >= 500 || r.body.TrimStart().StartsWith('<')) return Ok(Unavailable);
        return Content(r.body, "application/json");
    }
}
