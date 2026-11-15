using HR_web.API.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR_web.Controllers;

// Nguồn dữ liệu DUY NHẤT cho widget màn hình chính của app iOS/Android.
//
// Vì sao đặt ở HR_web chứ không gọi thẳng HR_api: HR_api chỉ chặn bằng 1 API key dùng chung
// (ApiKeyMiddleware), không có danh tính từng người — widget gọi thẳng sẽ phải nhúng key vào app,
// ai moi được key là tra được dữ liệu của mọi mã NV. Ở đây tái dùng đúng cookie phiên đăng nhập mà
// WebView đã có sẵn, nên widget chỉ đọc được dữ liệu của chính người đang đăng nhập, không phát
// sinh cơ chế xác thực mới nào.
//
// Hết phiên -> [Authorize] trả 302 về trang Đăng nhập; app KHÔNG đi theo redirect, giữ snapshot cũ
// và hiện nhắc mở app đăng nhập lại.
[Authorize]
public class WidgetController : BaseController
{
    private readonly HomeApiService      _homeApi;
    private readonly NotificationService _notiSvc;
    private readonly BulletinService     _bulletinSvc;
    private readonly LeaveService        _leaveSvc;
    private readonly GatePassService     _gpSvc;

    public WidgetController(HomeApiService homeApi, NotificationService notiSvc,
                            BulletinService bulletinSvc, LeaveService leaveSvc, GatePassService gpSvc)
    {
        _homeApi     = homeApi;
        _notiSvc     = notiSvc;
        _bulletinSvc = bulletinSvc;
        _leaveSvc    = leaveSvc;
        _gpSvc       = gpSvc;
    }

    // Widget chỉ có 2 bố cục: "worker" (xem đơn của mình) và "manager" (đếm việc cần xử lý).
    // Expat dùng chung bố cục manager, chỉ khác ngôn ngữ — theo đúng yêu cầu "expat tương tự quản lý".
    // Giữ ĐÚNG cách phân nhánh của HomeController.Index để widget không nói khác trang chủ.
    private static string WidgetRoleOf(string role, string? deptRole)
    {
        if (string.Equals(role, "Expat", StringComparison.OrdinalIgnoreCase)) return "expat";
        if (role is "Supervisor" or "DeputyManager" or "Manager" or "Assistant") return "manager";
        if (role is "Clerk" or "CSR" or "HR" or "Admin") return "manager";
        if (deptRole is "HR" or "CSR") return "manager";
        return "worker";
    }

    // Gom trạng thái đơn về 3 nhóm widget hiện được: chờ duyệt / đã duyệt / từ chối.
    //
    // Liệt kê TƯỜNG MINH theo giá trị thật đang có trong DB, không dò chuỗi con: dò chuỗi thì một
    // trạng thái mới thêm sau này sẽ âm thầm bị xếp vào "chờ duyệt" mà không ai biết, và widget
    // báo sai cho công nhân. Gặp giá trị lạ thì cũng về "chờ duyệt" nhưng đó là mặc định có chủ ý.
    private static string NormStatus(string? s) => (s ?? "").Trim().ToUpperInvariant() switch
    {
        "APPROVED" or "CONFIRMED"                              => "APPROVED",
        "REJECTED"                                             => "REJECTED",
        "PENDING" or "ASSIGNED" or "SUBMITTED"
            or "PARTIALLY_SUBMITTED" or "RESUBMIT_REQUESTED"   => "PENDING",
        _                                                      => "PENDING"
    };

    // GET /Widget/Data — JSON gọn cho widget. Mỗi khối bọc try riêng: thiếu 1 ô thì widget vẫn
    // dùng được, còn ném lỗi ra ngoài thì widget trắng trơn.
    [HttpGet]
    public async Task<IActionResult> Data()
    {
        var u = CurrentUser;
        if (u == null || string.IsNullOrEmpty(u.EmpCd)) return Unauthorized();

        var role       = u.RoleName ?? "Employee";
        var widgetRole = WidgetRoleOf(role, u.DeptRoleName);
        var lang       = u.UiLang ?? "vi";

        int unread = 0;
        try { unread = await _notiSvc.GetUnreadCountAsync(u.EmpCd); } catch { }

        var    notices   = new List<object>();
        var    myReqs    = new List<object>();
        object? bulletin = null;
        object? counters = null;

        if (widgetRole == "worker")
        {
            // ── Thông báo quan trọng ──────────────────────────────────────────
            // Lấy 20 cái gần nhất rồi lọc PRIORITY=HIGH; nếu không có cái nào quan trọng thì
            // rơi về thông báo CHƯA ĐỌC mới nhất — widget trống trơn thì vô dụng.
            try
            {
                var all  = await _notiSvc.GetMyAsync(u.EmpCd, 1, 20);
                var high = all.Where(n => string.Equals(n.PRIORITY, "HIGH", StringComparison.OrdinalIgnoreCase)).ToList();
                var pick = high.Count > 0 ? high : all.Where(n => n.IS_READ == 0).ToList();
                foreach (var n in pick.Take(3))
                    notices.Add(new
                    {
                        id      = n.ID,
                        title   = lang == "vi" ? n.TITLE : (string.IsNullOrWhiteSpace(n.TITLE_EN) ? n.TITLE : n.TITLE_EN),
                        unread  = n.IS_READ == 0,
                        high    = string.Equals(n.PRIORITY, "HIGH", StringComparison.OrdinalIgnoreCase),
                        at      = n.CREATED_DATE.ToString("yyyy-MM-dd HH:mm")
                    });
            }
            catch { }

            // ── Đơn của mình: duyệt chưa? ─────────────────────────────────────
            try
            {
                var lv = await _leaveSvc.GetMyRequestsAsync(u.EmpCd, null, 1, 10);
                foreach (var r in (lv?.data ?? new()).Take(5))
                    myReqs.Add(new
                    {
                        kind   = "LEAVE",
                        id     = r.REQUEST_ID,
                        label  = r.LEAVE_TYPE ?? "",
                        date   = r.FROM_DATE?.ToString("dd/MM") ?? "",
                        status = NormStatus(r.STATUS)
                    });
            }
            catch { }
            try
            {
                var gp = await _gpSvc.GetMyRequestsAsync(u.EmpCd, 1, 10);
                foreach (var r in (gp?.data ?? new()).Take(5))
                    myReqs.Add(new
                    {
                        kind   = "GATEPASS",
                        id     = r.REQUEST_ID,
                        label  = r.GP_TYPE ?? "",
                        date   = r.OUT_TIME?.ToString("dd/MM") ?? "",
                        status = NormStatus(r.STATUS)
                    });
            }
            catch { }

            // ── Bản tin Samho ─────────────────────────────────────────────────
            // Lấy bài ĐẦU danh sách, tức là đúng bài công nhân thấy trên cùng khi mở trang Bản tin
            // (API xếp bài ghim lên trước rồi mới tới ngày đăng) — KHÔNG phải luôn là bài mới nhất.
            try
            {
                var list = await _bulletinSvc.GetListAsync(u.EmpCd);
                var b    = list.FirstOrDefault();
                if (b != null) bulletin = new { id = b.ID, title = b.TitleFor(lang) };
            }
            catch { }
        }
        else
        {
            // ── Quản lý / Expat ───────────────────────────────────────────────
            // needApprove = đơn CHỜ MÌNH DUYỆT (nghỉ phép + ra cổng)
            // leaveToday  = số NV nghỉ trong hôm nay
            // gatePass    = đơn ra cổng chờ ký (tách riêng vì sếp hỏi số này độc lập)
            try
            {
                var s = await _homeApi.GetSummaryAsync(u.EmpCd, role, false, u.DeptRoleName);
                if (s != null)
                    counters = new
                    {
                        needApprove = s.LEAVE_PENDING + s.GP_PENDING,
                        leavePending = s.LEAVE_PENDING,
                        gatePass    = s.GP_PENDING,
                        leaveToday  = s.LEAVE_TODAY_TOTAL
                    };
            }
            catch { }
        }

        return Json(new
        {
            ok        = true,
            empCd     = u.EmpCd,
            fullName  = u.FullName ?? "",
            role      = widgetRole,
            lang,
            updatedAt = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            unread,
            notices,
            myReqs,
            bulletin,
            counters
        });
    }
}
