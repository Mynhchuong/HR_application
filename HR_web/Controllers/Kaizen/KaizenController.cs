using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers.Kaizen;

/// <summary>Đăng ký Kaizen cho MỌI nhân viên (nút trên trang Hộp thư): nhập ý tưởng + SĐT/Zalo, xem lịch sử đã nộp.</summary>
[Authorize]
public class KaizenController : BaseController
{
    private readonly KaizenService _svc;
    public KaizenController(KaizenService svc) { _svc = svc; }

    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> GetMy()
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false });
        return Content(await _svc.MyRawAsync(CurrentUser.EmpCd), "application/json");
    }

    public class KaizenSubmitBody
    {
        public string? Phone { get; set; }
        public string? Idea  { get; set; }
    }

    // POST /Kaizen/Submit — chỉ nộp được cho CHÍNH MÌNH (mã NV lấy từ phiên đăng nhập, không nhận từ client)
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] KaizenSubmitBody body)
    {
        if (string.IsNullOrEmpty(CurrentUser?.EmpCd)) return Json(new { success = false, message = "Chưa đăng nhập" });
        return Content(await _svc.SubmitRawAsync(CurrentUser.EmpCd, body?.Phone, body?.Idea), "application/json");
    }
}
