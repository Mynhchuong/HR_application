using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HR_web.API.Service;

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
    private bool Allowed => CurrentUser?.RoleName is not ("HR" or "Expat");

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
        if (string.IsNullOrEmpty(Empcd) || !Allowed) return Json(new { success = false, message = "Không có quyền" });
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

    // GET /Tpm/HistoryData?status=
    [HttpGet]
    public async Task<IActionResult> HistoryData(string? status)
    {
        if (string.IsNullOrEmpty(Empcd) || !Allowed) return Json(new { success = false, message = "Không có quyền" });
        return Content(await _svc.HistoryRawAsync(Empcd, status), "application/json");
    }

    public class ReportBody
    {
        public string? ItemCode { get; set; }
        public string? Description { get; set; }
    }
}
