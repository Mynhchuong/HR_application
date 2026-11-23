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

    public TpmController(TpmService tpm)
    {
        _tpm = tpm;
    }

    private static readonly object Unavailable = new { success = false, code = "TPM_UNAVAILABLE", message = "Hệ thống bảo trì máy tạm thời không khả dụng, vui lòng thử lại sau." };

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

        return Relay(await _tpm.CallAsync(HttpMethod.Post, "/api/mysamho/broken/report", new
        {
            ItemCode = body!.ItemCode,
            Description = body.Description,
            EmpCd = emp.EmpCd,
            // Tên/bộ phận trong HR lưu bảng mã VNI -> đổi sang Unicode cho TPM (không có font VNI).
            Name = VniConverter.ToUnicode(emp.Name),
            DeptCd = emp.DeptCd, DeptNm = VniConverter.ToUnicode(emp.DeptNm),
            LineCd = emp.LineCd, LineNm = VniConverter.ToUnicode(emp.LineNm),
            WorkCd = emp.WorkCd, WorkNm = VniConverter.ToUnicode(emp.WorkNm),
        }));
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
