using HR_web.API;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Trang báo cáo "ai đã cài app My Samho, ai còn dùng web" — dựa vào HR_USERS.LAST_DEVICE_TYPE (tự ghi
// mỗi lần đăng nhập, xem DeviceDetector.cs + HR_api/Controllers/DeviceReportController.cs). Mục đích:
// tìm NV còn dùng web mobile (chưa cài app) để HR chủ động nhắc tải (yêu cầu 2026-10-26).
[Authorize(Roles = "Admin")]   // chỉ Admin xem (yêu cầu 2026-10-28), HR không còn vào được
public class DeviceReportController : BaseController
{
    private readonly ApiService _api;

    public DeviceReportController(ApiService api)
    {
        _api = api;
    }

    public IActionResult Index() => View();

    // GET /DeviceReport/GetStats — passthrough nguyên JSON từ HR_api, JS đọc trực tiếp.
    [HttpGet]
    public async Task<IActionResult> GetStats()
    {
        var response = await _api.GetAsync_Raw("DeviceReport/stats");
        if (response == null) return Json(new { success = false, message = "Không kết nối được máy chủ" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }

    // GET /DeviceReport/GetList?deviceType=&search=&page=&page_size=
    [HttpGet]
    public async Task<IActionResult> GetList(string? deviceType, string? search, int page = 1, int page_size = 50)
    {
        var qs = $"page={page}&page_size={page_size}";
        if (!string.IsNullOrEmpty(deviceType)) qs += $"&deviceType={Uri.EscapeDataString(deviceType)}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";

        var response = await _api.GetAsync_Raw("DeviceReport/list", qs);
        if (response == null) return Json(new { success = false, message = "Không kết nối được máy chủ" });
        var json = await response.Content.ReadAsStringAsync();
        return Content(json, "application/json");
    }
}
