using HR_api.Models.AttendanceConfirm;
using HR_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class AttendanceConfirmController : ControllerBase
{
    private readonly AttendanceConfirmService _svc;

    public AttendanceConfirmController(AttendanceConfirmService svc)
    {
        _svc = svc;
    }

    // GET /apiHR/AttendanceConfirm/missing-list
    [HttpGet("missing-list")]
    public async Task<IActionResult> GetMissingList(
        string  caller_empcd,
        string? dept_id   = null,
        string? line_id   = null,
        string? work_id   = null,
        string? search       = null,
        string? status       = null,
        string? missing_type = null,
        string? from_date    = null,
        string? to_date      = null,
        int     page         = 1,
        int     page_size    = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caller_empcd))
                return Ok(new { success = false, message = "Thiếu mã người dùng" });

            DateTime fromDate = DateTime.TryParseExact(from_date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fd)
                ? fd : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            // Mặc định tới hôm qua — dữ liệu ADD_TIME hôm nay chưa chốt (GetMissingListAsync cũng
            // tự ép trần này nếu FE lỡ truyền to_date=hôm nay).
            DateTime toDate = DateTime.TryParseExact(to_date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var td)
                ? td : DateTime.Today.AddDays(-1);

            bool isAdminOrHr = await _svc.IsAdminOrHRAsync(caller_empcd);

            var result = await _svc.GetMissingListAsync(
                caller_empcd, isAdminOrHr, dept_id, line_id, work_id, search,
                fromDate, toDate, status, missing_type, page, page_size);

            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message });
        }
    }

    // GET /apiHR/AttendanceConfirm/supp-pending-ot — HR đã gửi "xác nhận bổ sung" tăng ca (chờ chính
    // NV tự ký ở OtConfirmForm), lọc theo đúng scope dept/line/work của Clerk/Supervisor/Manager gọi
    // (Admin/HR xem hết) — chỉ để biết mà nhắc NV, không thao tác được ở đây.
    [HttpGet("supp-pending-ot")]
    public async Task<IActionResult> GetSuppPendingOt(string caller_empcd, string? dept_id = null, string? line_id = null, string? work_id = null, string? search = null, string? date_from = null, string? date_to = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caller_empcd))
                return Ok(new { success = false, message = "Thiếu mã người dùng" });

            DateTime? fromDate = DateTime.TryParse(date_from, out var df) ? df : null;
            DateTime? toDate   = DateTime.TryParse(date_to, out var dt) ? dt : null;

            bool isAdminOrHr = await _svc.IsAdminOrHRAsync(caller_empcd);
            var result = await _svc.GetSuppPendingOtAsync(caller_empcd, isAdminOrHr, dept_id, line_id, work_id, search, fromDate, toDate);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message });
        }
    }

    // POST /apiHR/AttendanceConfirm/remind-supp-ot — nhắc lại NV tự xác nhận bổ sung tăng ca (không
    // gia hạn, chỉ gửi lại thông báo). Chặn scope y hệt manager-confirm.
    [HttpPost("remind-supp-ot")]
    public async Task<IActionResult> RemindSuppOt([FromBody] RemindSuppOtRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.ACTOR_EMPCD) || string.IsNullOrWhiteSpace(req.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin" });
            if (!DateTime.TryParseExact(req.WORK_DATE, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var workDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            bool isAdminOrHr = await _svc.IsAdminOrHRAsync(req.ACTOR_EMPCD);
            var result = await _svc.RemindSuppOtAsync(req.ACTOR_EMPCD, isAdminOrHr, req.EMPCD, workDate);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message });
        }
    }

    // GET /apiHR/AttendanceConfirm/my-pending — danh sách ngày NV cần khai/khai lại (badge menu + gợi ý WorkerForm)
    [HttpGet("my-pending")]
    public async Task<IActionResult> GetMyPending(string empcd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên", count = 0, data = new List<object>() });

            var list = await _svc.GetMyPendingDaysAsync(empcd);
            return Ok(new { success = true, count = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message, count = 0, data = new List<object>() });
        }
    }

    // GET /apiHR/AttendanceConfirm/my-day — NV xem chính ngày của mình (WorkerForm), không áp scope
    [HttpGet("my-day")]
    public async Task<IActionResult> GetMyDay(string empcd, string work_date)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empcd) ||
                !DateTime.TryParseExact(work_date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var workDate))
                return Ok(new { success = false, message = "Thông tin không hợp lệ" });

            // Dữ liệu ADD_TIME hôm nay chưa chốt — bên chấm công tự thêm vân tay tạm cho người xin
            // vào trễ + người quên thật để kịp báo cáo 10h, qua sáng hôm sau mới đồng bộ lại vân tay
            // thật. Chặn khai/xem ngay từ đây để công nhân không xác nhận nhầm giờ tạm (yêu cầu HR 2026-09-22).
            if (workDate.Date >= DateTime.Today)
                return Ok(new { success = false, message = "Dữ liệu chấm công hôm nay chưa chốt (ERP đồng bộ lại vân tay vào sáng hôm sau) — vui lòng xác nhận từ ngày mai trở đi" });

            var item = await _svc.GetMyDayAsync(empcd, workDate);
            if (item == null) return Ok(new { success = false, message = "Ngày này không thiếu chấm công trên ERP" });
            return Ok(new { success = true, data = item });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = "API Error: " + ex.Message });
        }
    }

    // POST /apiHR/AttendanceConfirm/submit-worker — bước 1: NV khai giờ vào/ra
    [HttpPost("submit-worker")]
    public async Task<IActionResult> SubmitWorker([FromBody] WorkerSubmitRequest req)
    {
        try
        {
            var result = await _svc.SubmitWorkerConfirmAsync(req);
            return Ok(new { success = result.OK, message = result.MESSAGE });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/AttendanceConfirm/manager-confirm — bước 2: CHỈ quản lý đúng scope của NV mới
    // được xác nhận (Admin/HR không còn được xác nhận thay — yêu cầu HR 2026-09-18).
    [HttpPost("manager-confirm")]
    public async Task<IActionResult> ManagerConfirm([FromBody] ManagerConfirmRequest req)
    {
        try
        {
            var result = await _svc.ManagerConfirmAsync(req);
            return Ok(new { success = result.OK, message = result.MESSAGE });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // POST /apiHR/AttendanceConfirm/request-confirm — Admin/HR/Clerk gửi yêu cầu (1 hoặc nhiều NV/ngày)
    [HttpPost("request-confirm")]
    public async Task<IActionResult> RequestConfirm([FromBody] RequestConfirmBulkRequest body)
    {
        try
        {
            if (body?.ITEMS == null || body.ITEMS.Count == 0)
                return Ok(new AttendanceConfirmResponse { success = false, message = "Danh sách rỗng" });

            // Không mutate ERP, chỉ tạo record app + gửi thông báo — quyền hiển thị nút này đã
            // chặn ở trang web ([Authorize] + scope filter trên danh sách), không cần gate thêm ở đây.
            var res = await _svc.RequestConfirmBulkAsync(body.ITEMS);
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Ok(new AttendanceConfirmResponse { success = false, message = ex.Message });
        }
    }
}
