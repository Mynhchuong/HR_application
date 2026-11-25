using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR_web.API.Service;
using HR_web.Models.Account;

namespace HR_web.Controllers.Tpm;

// "Báo máy hư" (TPM) cho CÔNG NHÂN SẢN XUẤT trong My Samho: quét mã trên tem máy -> xem thông tin ->
// báo hư; xem lịch sử các phiếu mình đã báo. Dữ liệu ở hệ thống TPM (DB riêng), HR_web chỉ là giao
// diện, đi qua HR_api (apiHR/Tpm). EMPCD luôn lấy từ session, KHÔNG tin client. HR và Expat không
// dùng chức năng này (HR_api cũng chặn lại lần nữa).
[Authorize]
public class TpmController : HR_web.Controllers.BaseController
{
    private readonly TpmService _svc;
    public TpmController(TpmService svc) { _svc = svc; }

    private string Empcd => CurrentUser?.EmpCd ?? "";
    private bool Allowed => !CurrentUser.HasAnyRole("HR", "Expat") && CurrentUser?.TpmBlocked != true;   // role chính HOẶC role dept; TpmBlocked = khối văn phòng

    // GET /Tpm/Scan
    public IActionResult Scan()
    {
        if (!Allowed) return RedirectToAction("Index", "Home");
        return View();
    }

    // GET /Tpm/History
    public IActionResult History()
    {
        if (!Allowed) return RedirectToAction("Index", "Home");
        return View();
    }

    // GET /Tpm/Machine?itemCode=
    [HttpGet]
    public async Task<IActionResult> Machine(string? itemCode)
    {
        if (string.IsNullOrEmpty(Empcd) || !(Allowed || AllowedMaint)) return Json(new { success = false, message = "Không có quyền" });
        if (string.IsNullOrWhiteSpace(itemCode)) return Json(new { success = false, message = "Thiếu mã máy" });
        return Content(await _svc.MachineRawAsync(Empcd, itemCode.Trim()), "application/json");
    }

    // POST /Tpm/Report  { itemCode, description }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Report([FromBody] ReportBody req)
    {
        if (string.IsNullOrEmpty(Empcd) || !Allowed) return Json(new { success = false, message = "Không có quyền" });
        if (string.IsNullOrWhiteSpace(req?.ItemCode) || string.IsNullOrWhiteSpace(req.Description))
            return Json(new { success = false, message = "Vui lòng nhập mã máy và mô tả lỗi" });

        var payload = new { empcd = Empcd, itemCode = req.ItemCode.Trim(), description = req.Description.Trim() };
        return Content(await _svc.ReportRawAsync(payload), "application/json");
    }

    // ───────── Bảo trì dùng My Samho (tích hợp 3): thẻ trong HR_TPM_MAINT (cờ TpmMaintenance lúc đăng nhập); TPM quyết quyền thật.
    private bool AllowedMaint => CurrentUser?.TpmMaintenance == true;

    // GET /Tpm/MachineInfo — bảo trì tra cứu máy (quét/nhập mã): xem thông tin đầy đủ + xử lý phiếu báo hư đang mở của máy (như màn quét của PDA)
    public IActionResult MachineInfo()
    {
        if (!AllowedMaint) return RedirectToAction("Index", "Home");
        return View();
    }

    // GET /Tpm/Transfer — bảo trì chuyển vị trí máy hàng loạt
    public IActionResult Transfer()
    {
        if (!AllowedMaint) return RedirectToAction("Index", "Home");
        return View();
    }

    // POST /Tpm/MaintTransfer { locationId, itemCodes[] }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MaintTransfer([FromBody] MaintTransferBody req)
    {
        if (string.IsNullOrEmpty(Empcd) || !AllowedMaint) return Json(new { success = false, message = "Không có quyền" });
        if (req?.LocationId is not > 0 || req.ItemCodes == null || req.ItemCodes.Count == 0)
            return Json(new { success = false, message = "Vui lòng chọn vị trí đích và thêm máy cần chuyển" });
        return Content(await _svc.MaintTransferRawAsync(new { empcd = Empcd, locationId = req.LocationId, itemCodes = req.ItemCodes }), "application/json");
    }

    // GET /Tpm/Maintenance
    public IActionResult Maintenance()
    {
        if (!AllowedMaint) return RedirectToAction("Index", "Home");
        return View();
    }

    // GET /Tpm/MaintBroken?myAreaOnly=true|false
    [HttpGet]
    public async Task<IActionResult> MaintBroken(bool myAreaOnly = true)
    {
        if (string.IsNullOrEmpty(Empcd) || !AllowedMaint) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _svc.MaintBrokenRawAsync(Empcd, myAreaOnly), "application/json");
    }

    // GET /Tpm/MaintLookup?kind=areas|issues|solve-groups
    [HttpGet]
    public async Task<IActionResult> MaintLookup(string kind)
    {
        if (string.IsNullOrEmpty(Empcd) || !AllowedMaint) return Json(new { success = false, message = "Không có quyền" });
        if (kind != "areas" && kind != "issues" && kind != "solve-groups") return Json(new { success = false, message = "Yêu cầu không hợp lệ" });
        return Content(await _svc.MaintLookupRawAsync(kind, Empcd), "application/json");
    }

    // POST /Tpm/MaintReceive { brokenId }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MaintReceive([FromBody] MaintReceiveBody req)
    {
        if (string.IsNullOrEmpty(Empcd) || !AllowedMaint) return Json(new { success = false, message = "Không có quyền" });
        if (req?.BrokenId is not > 0) return Json(new { success = false, message = "Thiếu mã phiếu" });
        return Content(await _svc.MaintReceiveRawAsync(new { empcd = Empcd, brokenId = req.BrokenId }), "application/json");
    }

    // POST /Tpm/MaintRepair { brokenId, itemCode, issueId?, solveGroupId?, repairNote? }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MaintRepair([FromBody] MaintRepairBody req)
    {
        if (string.IsNullOrEmpty(Empcd) || !AllowedMaint) return Json(new { success = false, message = "Không có quyền" });
        if (req?.BrokenId is not > 0) return Json(new { success = false, message = "Thiếu mã phiếu" });
        if (string.IsNullOrWhiteSpace(req.ItemCode)) return Json(new { success = false, message = "Cần quét hoặc nhập lại mã máy để xác nhận" });
        return Content(await _svc.MaintRepairRawAsync(new
        {
            empcd = Empcd, brokenId = req.BrokenId, itemCode = req.ItemCode.Trim(),
            issueId = req.IssueId, solveGroupId = req.SolveGroupId, repairNote = req.RepairNote?.Trim()
        }), "application/json");
    }

    // GET /Tpm/Locations?parentId=   (không có parentId = cấp 1)
    [HttpGet]
    public async Task<IActionResult> Locations(long? parentId)
    {
        if (string.IsNullOrEmpty(Empcd) || !(Allowed || AllowedMaint)) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _svc.LocationsRawAsync(Empcd, parentId), "application/json");
    }

    // POST /Tpm/LocationReport  { itemCode, suggestedLocationId, note }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LocationReport([FromBody] LocationReportBody req)
    {
        if (string.IsNullOrEmpty(Empcd) || !Allowed) return Json(new { success = false, message = "Không có quyền" });
        if (string.IsNullOrWhiteSpace(req?.ItemCode) || req.SuggestedLocationId is not > 0)
            return Json(new { success = false, message = "Vui lòng chọn vị trí đúng của máy" });

        var payload = new { empcd = Empcd, itemCode = req.ItemCode.Trim(), suggestedLocationId = req.SuggestedLocationId, note = req.Note?.Trim() };
        return Content(await _svc.LocationReportRawAsync(payload), "application/json");
    }

    // GET /Tpm/HistoryData?status=
    [HttpGet]
    public async Task<IActionResult> HistoryData(string? status)
    {
        if (string.IsNullOrEmpty(Empcd) || !Allowed) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _svc.HistoryRawAsync(Empcd, status), "application/json");
    }

    public class MaintTransferBody
    {
        public long? LocationId { get; set; }
        public List<string>? ItemCodes { get; set; }
    }

    public class MaintReceiveBody { public long? BrokenId { get; set; } }

    public class MaintRepairBody
    {
        public long? BrokenId { get; set; }
        public string? ItemCode { get; set; }
        public long? IssueId { get; set; }
        public long? SolveGroupId { get; set; }
        public string? RepairNote { get; set; }
    }

    public class LocationReportBody
    {
        public string? ItemCode { get; set; }
        public long? SuggestedLocationId { get; set; }
        public string? Note { get; set; }
    }

    public class ReportBody
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
    }
}
