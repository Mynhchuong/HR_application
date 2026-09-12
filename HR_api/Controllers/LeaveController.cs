using System.Text;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Leave;
using HR_api.Services;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly NotificationService _notiSvc;
    private readonly HomeSummaryService _homeSummarySvc;
    private readonly ShiftLookupService _shiftLookup;

    public LeaveController(OracleService oracleService, NotificationService notiSvc, HomeSummaryService homeSummarySvc, ShiftLookupService shiftLookup)
    {
        _oracleService  = oracleService;
        _notiSvc        = notiSvc;
        _homeSummarySvc = homeSummarySvc;
        _shiftLookup    = shiftLookup;
    }

    // DT/VS/KT (đám tang/vợ sanh/khám thai): thường phát sinh đột xuất nên vẫn cho đăng ký ngay
    // trong ngày, NHƯNG không cho đăng ký sau khi ca làm việc hôm nay đã kết thúc (tránh xin nghỉ
    // "hồi tố" cho 1 ngày đã qua). Không áp dụng cho ngày tương lai — chỉ chặn khi FROM_DATE = hôm nay.
    private static readonly HashSet<string> SameDaySuddenLeaveTypes = new() { "DT", "VS", "KT" };

    private async Task<string?> CheckSameDayShiftEndAsync(string empcd, DateTime fromDate, string leaveTypeName)
    {
        if (fromDate.Date != DateTime.Today) return null;

        var shift = await _shiftLookup.GetShiftForDateAsync(empcd, fromDate);
        if (shift?.STIME?.Length != 4 || shift.ETIME?.Length != 4) return null; // không có lịch ca -> không chặn

        if (!int.TryParse(shift.STIME.Substring(0, 2), out var sh) || !int.TryParse(shift.STIME.Substring(2, 2), out var sm) ||
            !int.TryParse(shift.ETIME.Substring(0, 2), out var eh) || !int.TryParse(shift.ETIME.Substring(2, 2), out var em))
            return null;

        var shiftStart = fromDate.Date.AddHours(sh).AddMinutes(sm);
        var shiftEnd   = fromDate.Date.AddHours(eh).AddMinutes(em);
        if (shiftEnd <= shiftStart) shiftEnd = shiftEnd.AddDays(1); // ca đêm qua nửa đêm

        if (DateTime.Now > shiftEnd)
            return $"Đã qua giờ làm việc hôm nay (ca {shift.SHIFTCD} kết thúc {eh:D2}:{em:D2}), " +
                   $"không thể đăng ký {leaveTypeName} cho hôm nay. Vui lòng chọn ngày khác.";

        return null;
    }

    // NL/SI (Không lương / Bệnh có giấy): khác DT/VS/KT (việc đột xuất thật sự, cho phép xin ngay
    // cả khi đang trong ca) — NL/SI là nghỉ phải BÁO TRƯỚC khi ca hôm nay bắt đầu. Ca đã bắt đầu
    // thì không cho đăng ký NL/SI cho hôm nay nữa (tránh vừa đi làm vừa xin nghỉ ngay trong ca —
    // bug thật đã tái hiện: NV 14040127 xin SI lúc 11h06 dù ca N6 đã vào lúc 07h10, báo 2026-09-10).
    private static readonly HashSet<string> SameDayShiftStartTypes = new() { "NL", "SI" };

    private async Task<string?> CheckSameDayShiftStartAsync(string empcd, DateTime fromDate, string leaveTypeName)
    {
        if (fromDate.Date != DateTime.Today) return null;

        var shift = await _shiftLookup.GetShiftForDateAsync(empcd, fromDate);
        if (shift?.STIME?.Length != 4) return null; // không có lịch ca -> không chặn

        if (!int.TryParse(shift.STIME.Substring(0, 2), out var sh) || !int.TryParse(shift.STIME.Substring(2, 2), out var sm))
            return null;

        var shiftStart = fromDate.Date.AddHours(sh).AddMinutes(sm);

        if (DateTime.Now > shiftStart)
            return $"Ca {shift.SHIFTCD} hôm nay đã bắt đầu lúc {sh:D2}:{sm:D2}, không thể đăng ký {leaveTypeName} cho hôm nay. Vui lòng chọn ngày khác.";

        return null;
    }

    // Gom lại đúng 1 chỗ luật "báo trước theo loại" (AL/DC/DS/NL/DT/VS/KT/SI/CT) — dùng CHUNG cho
    // cả Submit() (tạo mới) LẪN Update() (sửa đơn đang PENDING). Trước đây Update() không gọi lại
    // luật này, nên NV có thể lách: xin phép AL/NL cho ngày hợp lệ rồi SỬA lại FROM_DATE về hôm
    // nay/ngày đã qua giờ chặn — bug thật đã tái hiện trên data thật (xem debug-samedaycheck).
    private async Task<string?> ValidateSelfLeaveDateRulesAsync(string empcd, string leaveType, DateTime fromDate)
    {
        if (leaveType == "AL")
        {
            var chk = await CheckAlDeadlineAsync(empcd, fromDate);
            return chk.Allowed ? null : chk.Message;
        }
        if (leaveType == "DC" || leaveType == "DS")
        {
            return fromDate.Date < DateTime.Today.AddDays(3)
                ? "Loại nghỉ này phải đăng ký trước ít nhất 3 ngày"
                : null;
        }
        if (SameDaySuddenLeaveTypes.Contains(leaveType))
        {
            if (fromDate.Date < DateTime.Today) return "Không được chọn ngày trong quá khứ";
            return await CheckSameDayShiftEndAsync(empcd, fromDate, NewLeaveTypeNames.GetValueOrDefault(leaveType, leaveType));
        }
        if (SameDayShiftStartTypes.Contains(leaveType))
        {
            if (fromDate.Date < DateTime.Today) return "Không được chọn ngày trong quá khứ";
            return await CheckSameDayShiftStartAsync(empcd, fromDate, NewLeaveTypeNames.GetValueOrDefault(leaveType, leaveType));
        }
        return fromDate.Date < DateTime.Today ? "Không được chọn ngày trong quá khứ" : null;
    }

    // Trước đây CHỈ có FE tự check trùng ngày (gọi my-requests rồi so sánh ở JS) trước khi Submit/
    // Update — dễ bị lách (gọi API thẳng, hoặc 2 tab bấm gần như cùng lúc). Giờ chặn LẠI ở server,
    // đúng luật "trùng" y hệt FE: đơn PENDING/APPROVED (không phải ASSIGNED) HOẶC đơn ASSIGNED đã
    // CONFIRMED coi là trùng nếu khoảng ngày giao nhau. excludeRequestId dùng khi Update() (không
    // tự đụng chính đơn đang sửa).
    private async Task<string?> CheckSelfLeaveOverlapAsync(string empcd, DateTime fromDate, DateTime toDate, string? excludeRequestId)
    {
        string excludeClause = string.IsNullOrEmpty(excludeRequestId) ? "" : "AND L.REQUEST_ID != :EXCLUDE_ID";
        string sql = $@"
            SELECT * FROM (
                SELECT R.STATUS, L.SOURCE, L.CONFIRM_STATUS, L.FROM_DATE, L.TO_DATE
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.EMPCD = :EMPCD
                  AND R.REQUEST_TYPE = 'LEAVE'
                  AND L.FROM_DATE <= :TO_DATE AND L.TO_DATE >= :FROM_DATE
                  AND ((L.SOURCE != 'ASSIGNED' AND R.STATUS IN ('PENDING','APPROVED'))
                       OR (L.SOURCE = 'ASSIGNED' AND L.CONFIRM_STATUS = 'CONFIRMED'))
                  {excludeClause}
            ) WHERE ROWNUM = 1";

        var pars = new List<OracleParameter>
        {
            new("EMPCD", empcd),
            new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = fromDate.Date },
            new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = toDate.Date },
        };
        if (!string.IsNullOrEmpty(excludeRequestId)) pars.Add(new OracleParameter("EXCLUDE_ID", excludeRequestId));

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            Status = r["STATUS"]?.ToString(),
            FromDate = Convert.ToDateTime(r["FROM_DATE"]),
            ToDate   = Convert.ToDateTime(r["TO_DATE"]),
        }, pars.ToArray());

        var hit = rows.FirstOrDefault();
        if (hit == null) return null;

        string statusText = hit.Status == "APPROVED" ? "đã được duyệt" : hit.Status == "PENDING" ? "đang chờ duyệt" : "đã xác nhận";
        return $"Bạn đã có đơn nghỉ {statusText} trùng khoảng ngày này ({hit.FromDate:dd/MM/yyyy} – {hit.ToDate:dd/MM/yyyy}). Chỉ có thể tạo lại nếu đơn cũ bị từ chối.";
    }

    // Trước đây KHÔNG có chỗ nào (Submit/Update/Approve) check số ngày phép năm còn lại — chỉ FE tự
    // so myBalance.LEFT_NUM trước khi gửi (bấm F12 sửa payload hoặc gọi thẳng API là bỏ qua được).
    // Ảnh hưởng lương thật (duyệt AL vượt số ngày được cấp) nên chặn lại ở server. Lưu ý: LEFT_NUM
    // chỉ trừ những ngày ĐÃ ĐƯỢC DUYỆT và đồng bộ ERP (EFM410), CHƯA trừ các đơn AL khác đang
    // PENDING cùng lúc — nếu NV có nhiều đơn AL PENDING song song, tổng có thể vượt LEFT_NUM cho
    // tới khi có đơn được duyệt (do ERP mới là nơi trừ thật). Đây là giới hạn đã biết, không phải
    // lỗi thiếu sót của riêng chỗ này.
    private async Task<string?> CheckAlBalanceAsync(string empcd, decimal totalDays)
    {
        const string sql = @"
            WITH ALLOC AS (
                SELECT MAX(RECEIVE_NUM) AS RECEIVE_NUM FROM HRMS.EFM100
                WHERE EMPCD = :EMPCD AND SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
            ),
            USED AS (
                SELECT COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                FROM HRMS.EFM410
                WHERE EMPCD = :EMPCD2 AND TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
            )
            SELECT NVL(A.RECEIVE_NUM,0) - NVL(U.USED_NUM,0) AS LEFT_NUM FROM ALLOC A, USED U";

        var rows = await _oracleService.ExecuteQueryAsync(sql,
            r => r["LEFT_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"]),
            new OracleParameter("EMPCD", empcd), new OracleParameter("EMPCD2", empcd));

        int leftNum = rows.FirstOrDefault();
        if (totalDays > leftNum)
            return $"Bạn chỉ còn {leftNum} ngày phép năm, không đủ để nghỉ {totalDays} ngày.";
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6 loại nghỉ (thay thế hoàn toàn AL/CL/SL/NPL/OTH) dùng cho SẮP LỊCH (Assign/AdminAssign) —
    // theo yêu cầu "quản lý sắp lịch như cũ", KHÔNG mở rộng thêm 3 loại mới ở đây.
    // Dữ liệu CL/SL/NPL/OTH cũ vẫn đọc/hiển thị bình thường, chỉ không cho chọn khi tạo đơn mới.
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly HashSet<string> NewLeaveTypeCodes = new() { "AL", "DT", "DC", "CT", "VS", "KT" };
    // Nhân viên TỰ TẠO ĐƠN (Submit) — bảng quy ước HR cập nhật thêm NL (Không lương)/SI (Bệnh có giấy)/
    // DS (Dưỡng sức), CHỈ áp dụng cho luồng tự nộp, không áp dụng cho Assign/AdminAssign.
    private static readonly HashSet<string> SelfSubmitLeaveTypeCodes = new() { "AL", "NL", "SI", "DT", "DC", "CT", "VS", "DS", "KT" };
    // Supervisor/Manager (Assign, sắp lịch cho team) HIỆN TẠI chỉ được sắp Phép năm — 5 loại còn lại
    // (DT/DC/CT/VS/KT) chỉ Admin (AdminAssign) mới được sắp toàn công ty. Nếu sau này HR yêu cầu
    // cho phép supervisor/manager sắp thêm các loại khác, chỉ cần thêm mã vào set này.
    private static readonly HashSet<string> SupervisorAssignTypes = new() { "AL" };
    // remark ERP kiểu mới "<prefix> <lý do>" — mọi loại trừ AL (AL dùng "VR"/"ASSIGNED" cố định,
    // không kèm lý do). NL (Không lương) dùng prefix "VR" + lý do NV gõ (yêu cầu HR 2026-08-25,
    // xác nhận lại — trước đó có lúc để NL = "VR" trần không kèm lý do, ĐÃ SỬA LẠI theo yêu cầu này).
    private static readonly HashSet<string> NewRemarkTypes = new() { "NL", "SI", "DT", "DC", "CT", "VS", "DS", "KT" };
    // Phần lớn prefix remark = chính LEAVE_TYPE (ASCII sẵn). Chỉ NL dùng chữ "VR" thay vì mã loại.
    // ĐC (Đám cưới) trước đây từng cố tình ghi có dấu "ĐC" theo yêu cầu HR 2026-08-25, nhưng ERP đọc
    // REMAR theo font VNI-Windows không xử lý ký tự có dấu này đúng — ĐÃ SỬA LẠI về ASCII "DC" cho
    // nhất quán với Đám tang (DT) vốn đã dùng ASCII "DT" thẳng từ đầu (xác nhận lại 2026-08-27).
    private static readonly Dictionary<string, string> RemarkPrefix = new()
    {
        ["NL"] = "VR"
    };
    // Loại nghỉ bắt buộc nộp giấy tờ chứng minh (nhắc sau 3 ngày) — CT theo yêu cầu vẫn KHÔNG cần nộp giấy tờ.
    private static readonly HashSet<string> DocRequiredTypes = new() { "SI", "DT", "DC", "VS", "DS", "KT" };
    // Trạng thái được coi là "đã chốt" để cho phép cập nhật giấy tờ — quản lý/Admin SẮP LỊCH
    // (SOURCE=ASSIGNED) không bao giờ có STATUS='APPROVED' (mãi mãi là 'ASSIGNED', không qua bước
    // duyệt riêng), nhưng bản chất đã chốt lịch nghỉ y như đã duyệt nên vẫn tính là được cập nhật
    // (yêu cầu 2026-09-10: "quản lý, admin sắp lịch tính là đã duyệt nha").
    private static readonly HashSet<string> DocEditableStatuses = new() { "APPROVED", "ASSIGNED" };
    private static readonly Dictionary<string, string> NewLeaveTypeNames = new()
    {
        ["AL"] = "Phép năm", ["NL"] = "Không lương", ["SI"] = "Bệnh có giấy",
        ["DT"] = "Đám tang", ["DC"] = "Đám cưới", ["CT"] = "Công tác",
        ["VS"] = "Vợ sanh",  ["DS"] = "Dưỡng sức", ["KT"] = "Khám thai"
    };

    // Công tác (CT) chỉ Manager/Expat/Admin được duyệt/từ chối — dùng chung cho GetApprovalList
    // (lọc danh sách) VÀ Approve/Reject (chặn hành động thật, không chỉ ẩn ở UI).
    private static bool CanApproveCT(string? approverRole) => approverRole switch
    {
        "Manager" or "Expat" or "Admin" => true,
        _ => false
    };

    // Remark ghi vào ERP. HRMS.EFM410.REMAR giới hạn 50 byte, NHƯNG SP_015_NEW insert qua bảng trung gian
    // HRMS.EFM410_WAIT trước — cột REMAR của bảng NÀY chỉ 30 byte (đã verify thật: ORA-12899 actual=33 max=30
    // khi thử remark 33 byte cho CT dù dưới 50). Lấy 30 byte làm giới hạn chung cho an toàn (áp dụng luôn
    // cho AS_REMAR gửi vào SP, không riêng insert thẳng EFM410 trong nhánh AdminAssign hết phép năm).
    // Lý do tiếng Việt có dấu tốn 2-3 byte/ký tự nên PHẢI cắt theo byte (không phải theo ký tự) — tránh
    // ORA-12899 khi Approve/Assign. 5 loại mới: "<CODE> <lý do NV gõ>" (mã 2 ký tự ASCII, không dấu —
    // xem note trong alter_leave_doctrack.sql). AL/CL/SL/NPL/OTH (loại cũ): giữ nguyên "VR"/"ASSIGNED".
    private const int ErpRemarkMaxBytes = 30;

    // Lý do NV gõ có dấu Unicode thường (VD "Đám tang bà nội") — ERP cũ đọc REMAR theo font
    // VNI-Windows (giống CNAME/họ tên), KHÔNG hiểu Unicode thường nên chèn thẳng sẽ hiển thị lỗi/
    // ký tự rác bên ERP. Bỏ dấu tiếng Việt trước khi ghi remark cho AN TOÀN (dùng lại đúng function
    // HRMS.FN_CONVERT_TO_VN đã có sẵn trong DB — theo yêu cầu HR 2026-08-25).
    private async Task<string?> StripDiacriticsForErpAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var rows = await _oracleService.ExecuteQueryAsync(
            "SELECT HRMS.FN_CONVERT_TO_VN(:TXT) AS RESULT FROM DUAL",
            // reader["X"]?.ToString() KHÔNG bắt được DBNull (ToString() trên DBNull.Value trả về "",
            // không phải null) — phải so sánh tường minh, nếu không "?? text" fallback bên dưới sẽ
            // không bao giờ kích hoạt và có thể âm thầm trả về chuỗi rỗng thay vì text gốc.
            r => r["RESULT"] == DBNull.Value ? null : r["RESULT"].ToString(),
            new OracleParameter("TXT", text));
        return rows.FirstOrDefault() ?? text;
    }

    // Gộp bỏ-dấu + build remark làm 1 bước — CHỈ gọi FN_CONVERT_TO_VN (round-trip DB) khi
    // BuildErpRemark thực sự nhúng lý do vào remark (NewRemarkTypes); AL/NL/legacy bỏ qua lý do
    // nên tránh round-trip thừa.
    private async Task<string> BuildErpRemarkAsync(string leaveType, string? reason, bool isAssignFlow)
    {
        string? cleanReason = NewRemarkTypes.Contains(leaveType) ? await StripDiacriticsForErpAsync(reason) : reason;
        return BuildErpRemark(leaveType, cleanReason, isAssignFlow);
    }

    private static string BuildErpRemark(string leaveType, string? reason, bool isAssignFlow)
    {
        if (NewRemarkTypes.Contains(leaveType))
        {
            string prefix = RemarkPrefix.GetValueOrDefault(leaveType, leaveType) + " ";
            int maxReasonBytes = ErpRemarkMaxBytes - Encoding.UTF8.GetByteCount(prefix);
            string r = TruncateUtf8Bytes((reason ?? "").Trim(), maxReasonBytes);
            return prefix + r;
        }
        string erpCdLocal = leaveType switch { "AL" => "PN", "CL" => "BH", "CT" => "CT", _ => "CP" };
        if (erpCdLocal != "CP") return isAssignFlow ? "ASSIGNED" : "VR";
        string legacyName = leaveType switch { "SL" => "Nghỉ bệnh", "NPL" => "Không lương", "OTH" => "Khác", _ => leaveType };
        return (isAssignFlow ? "ASSIGNED " : "VR ") + legacyName;
    }

    // Cắt chuỗi về tối đa maxBytes khi encode UTF-8, không cắt giữa 1 ký tự multi-byte (tránh chuỗi hỏng).
    private static string TruncateUtf8Bytes(string input, int maxBytes)
    {
        if (maxBytes <= 0) return "";
        var bytes = Encoding.UTF8.GetBytes(input);
        if (bytes.Length <= maxBytes) return input;

        int len = maxBytes;
        while (len > 0 && (bytes[len] & 0xC0) == 0x80) len--; // lùi về đầu ký tự nếu đang đứng giữa byte tiếp diễn
        return Encoding.UTF8.GetString(bytes, 0, len);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/submit  — worker submits SELF leave
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] LeaveSubmitRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            if (string.IsNullOrEmpty(model.LEAVE_TYPE))
                return Ok(new { success = false, message = "Thiếu loại nghỉ phép" });

            if (!SelfSubmitLeaveTypeCodes.Contains(model.LEAVE_TYPE))
                return Ok(new { success = false, message = "Loại nghỉ phép không hợp lệ" });

            if (model.LEAVE_TYPE != "AL" && string.IsNullOrWhiteSpace(model.REASON))
                return Ok(new { success = false, message = "Vui lòng nhập lý do nghỉ" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate))
                return Ok(new { success = false, message = "Ngày bắt đầu không hợp lệ" });

            if (!DateTime.TryParse(model.TO_DATE, out DateTime toDate))
                return Ok(new { success = false, message = "Ngày kết thúc không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            // Báo trước theo loại: AL = theo giờ ca làm (-6h), DC/DS = trước 3 ngày lịch,
            // DT/VS/KT = trong ngày được nhưng phải trước khi hết ca hôm nay (việc đột xuất),
            // NL/SI = trong ngày được nhưng phải trước khi ca hôm nay BẮT ĐẦU (báo trước 2026-09-10:
            // SI "bệnh có giấy" không cho khai retroactive giữa ca đang làm việc, giống NL),
            // CT = trong ngày cũng được, không giới hạn giờ
            string? dateRuleError = await ValidateSelfLeaveDateRulesAsync(model.EMPCD, model.LEAVE_TYPE, fromDate);
            if (dateRuleError != null)
                return Ok(new { success = false, message = dateRuleError });

            // Chặn trùng ngày với đơn khác đang PENDING/APPROVED (hoặc ASSIGNED đã CONFIRMED) —
            // trước đây chỉ FE tự check trước khi gửi, gọi thẳng API là bỏ qua được.
            string? overlapError = await CheckSelfLeaveOverlapAsync(model.EMPCD, fromDate, toDate, excludeRequestId: null);
            if (overlapError != null)
                return Ok(new { success = false, message = overlapError });

            // Chặn vượt số ngày phép năm còn lại — trước đây chỉ FE tự so myBalance.LEFT_NUM,
            // gọi thẳng API là bỏ qua được (ảnh hưởng lương thật nếu duyệt vượt số ngày được cấp).
            if (model.LEAVE_TYPE == "AL")
            {
                string? balanceError = await CheckAlBalanceAsync(model.EMPCD, model.TOTAL_DAYS);
                if (balanceError != null)
                    return Ok(new { success = false, message = balanceError });
            }

            var empRows = await _oracleService.ExecuteQueryAsync(
                "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["CNAME"]?.ToString(),
                new OracleParameter("EMPCD", model.EMPCD));

            string empName = empRows.FirstOrDefault() ?? "";

            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_REQUEST (REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE)
                VALUES ('LEAVE', :EMPCD, :EMP_NAME, SYSDATE, 'PENDING', :EMPCD1, SYSDATE)",
                new OracleParameter("EMPCD",    model.EMPCD),
                new OracleParameter("EMP_NAME", empName),
                new OracleParameter("EMPCD1",   model.EMPCD));

            var reqIds = await _oracleService.ExecuteQueryAsync(@"
                SELECT REQUEST_ID FROM (
                    SELECT REQUEST_ID FROM HRMS.HR_REQUEST
                    WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'LEAVE' AND STATUS = 'PENDING'
                      AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE)
                    ORDER BY CREATED_DATE DESC
                ) WHERE ROWNUM = 1",
                r => r["REQUEST_ID"]?.ToString(),
                new OracleParameter("EMPCD", model.EMPCD));

            if (reqIds.Count == 0 || string.IsNullOrEmpty(reqIds[0]))
                return Ok(new { success = false, message = "Lỗi tạo REQUEST_ID" });

            string requestId = reqIds[0]!;

            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_LEAVE_REQUEST
                    (REQUEST_ID, EMPCD, LEAVE_TYPE, FROM_DATE, TO_DATE, TOTAL_DAYS, REASON, CREATED_DATE, SOURCE)
                VALUES (:REQUEST_ID, :EMPCD, :LEAVE_TYPE, :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'SELF')",
                new OracleParameter("REQUEST_ID", requestId),
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("LEAVE_TYPE", model.LEAVE_TYPE),
                new OracleParameter("FROM_DATE",  fromDate),
                new OracleParameter("TO_DATE",    toDate),
                new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

            // CT (công tác): tự động tạo kèm 1 Gate Pass PENDING, giờ ra/vào CỐ ĐỊNH 07:30-16:30
            // (yêu cầu của sếp) — nhân viên KHÔNG cần nhập giờ, áp dụng chung mọi ca làm việc.
            // GP_TYPE='MID' (có cả OUT_TIME lẫn IN_TIME) vì SP_INSERT_GATE_PASS ở nhánh 'OUT' bỏ qua
            // giờ vào do mình truyền và tự lấy giờ tan ca theo lịch ca thật của NV — không đúng ý
            // "cố định 16:30" khi NV không phải ca hành chính. GP này chỉ thật sự APPROVED + đồng bộ
            // ERP (SP_INSERT_GATE_PASS) khi đơn CT được Manager/Expat/Admin duyệt (xem Approve()),
            // KHÔNG tự approve ngay lúc Submit (tránh cấp quyền ra cổng trước khi đơn được duyệt).
            if (model.LEAVE_TYPE == "CT")
            {
                DateTime outTime = fromDate.Date.AddHours(7).AddMinutes(30);
                DateTime inTime  = fromDate.Date.AddHours(16).AddMinutes(30);

                // Tạo HR_REQUEST cho GP (PENDING — chờ duyệt cùng lúc với đơn CT).
                // Tự sinh REQUEST_ID với hậu tố 'G' thay vì để trigger HR_REQUEST_TRG tự sinh
                // (TO_CHAR(SYSDATE,'YYYYMMDDHH24MISS') || EMPCD) — nếu để trigger tự sinh, insert
                // này và insert đơn CT phía trên (cùng EMPCD, cách nhau vài chục ms) có thể rơi vào
                // CÙNG 1 GIÂY → trùng REQUEST_ID → ORA-00001, đã tái hiện thật khi test. Hậu tố 'G'
                // đảm bảo không bao giờ trùng với ID của đơn Leave (EMPCD luôn thuần số).
                using var gpIdCmd = new OracleParameter("OUT_REQUEST_ID", OracleDbType.Varchar2, 40)
                    { Direction = System.Data.ParameterDirection.Output };
                await _oracleService.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_REQUEST (REQUEST_ID, REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, REMARK, CREATED_BY, CREATED_DATE)
                    VALUES (TO_CHAR(SYSDATE,'YYYYMMDDHH24MISS') || :EMPCD || 'G', 'GATEPASS', :EMPCD1, :EMP_NAME, SYSDATE, 'PENDING', :REMARK, :EMPCD2, SYSDATE)
                    RETURNING REQUEST_ID INTO :OUT_REQUEST_ID",
                    new OracleParameter("EMPCD",    model.EMPCD),
                    new OracleParameter("EMPCD1",   model.EMPCD),
                    new OracleParameter("EMP_NAME", empName),
                    new OracleParameter("REMARK",   "công tác"),
                    new OracleParameter("EMPCD2",   model.EMPCD),
                    gpIdCmd);

                string? gpRequestIdRaw = gpIdCmd.Value is Oracle.ManagedDataAccess.Types.OracleString os && !os.IsNull ? os.Value : null;

                if (!string.IsNullOrEmpty(gpRequestIdRaw))
                {
                    string gpRequestId = gpRequestIdRaw;
                    // Tạo HR_GATEPASS_REQUEST với GP_TYPE='MID' (OUT_TIME=07:30, IN_TIME=16:30 cố định)
                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_GATEPASS_REQUEST (REQUEST_ID, EMPCD, GP_TYPE, OUT_TIME, IN_TIME, CREATED_DATE)
                        VALUES (:REQUEST_ID, :EMPCD, 'MID', :OUT_TIME, :IN_TIME, SYSDATE)",
                        new OracleParameter("REQUEST_ID", gpRequestId),
                        new OracleParameter("EMPCD",      model.EMPCD),
                        new OracleParameter("OUT_TIME",   outTime),
                        new OracleParameter("IN_TIME",    inTime));

                    // Liên kết ngược lại đơn CT để Approve/Reject/Update/Delete xử lý cascade
                    await _oracleService.ExecuteNonQueryAsync(@"
                        UPDATE HRMS.HR_LEAVE_REQUEST SET GP_REQUEST_ID = :GP_REQUEST_ID WHERE REQUEST_ID = :REQUEST_ID",
                        new OracleParameter("GP_REQUEST_ID", gpRequestId),
                        new OracleParameter("REQUEST_ID",    requestId));
                }
            }

            string leaveTypeName = NewLeaveTypeNames.GetValueOrDefault(model.LEAVE_TYPE) ?? model.LEAVE_TYPE switch
            {
                "CL"  => "BHXH",
                "SL"  => "Nghỉ bệnh",
                "NPL" => "Không lương",
                _     => "Khác"
            };
            _notiSvc.LeaveSubmitted(model.EMPCD, empName, leaveTypeName);

            return Ok(new { success = true, message = "Đăng ký nghỉ phép thành công", request_id = requestId });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/my-requests?empcd=&source=SELF|ASSIGNED&page=&date_from=&date_to=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-requests")]
    public async Task<IActionResult> GetMyRequests(
        string  empcd,
        string? source    = null,
        int     page      = 1,
        int     page_size = 20,
        string? date_from = null,
        string? date_to   = null)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-3);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(3);

            var srcFlagVal = string.IsNullOrEmpty(source) ? (object)DBNull.Value : "Y";
            var srcVal     = string.IsNullOrEmpty(source) ? (object)DBNull.Value : source;

            string countSql = @"
                SELECT COUNT(*) CNT
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.EMPCD = :EMPCD
                  AND R.REQUEST_TYPE = 'LEAVE'
                  AND L.FROM_DATE >= :D_FROM AND L.FROM_DATE <= :D_TO
                  AND (:SRC_FLAG IS NULL OR L.SOURCE = :SRC_VAL)";

            var totalRows = await _oracleService.ExecuteQueryAsync(countSql,
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("EMPCD",    empcd),
                new OracleParameter("D_FROM",   dfrom.Date),
                new OracleParameter("D_TO",     dto.Date),
                new OracleParameter("SRC_FLAG", OracleDbType.Varchar2) { Value = srcFlagVal },
                new OracleParameter("SRC_VAL",  OracleDbType.Varchar2) { Value = srcVal });

            int total = totalRows.FirstOrDefault();

            string dataSql = @"
                SELECT * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (
                        ORDER BY
                            CASE WHEN T.FROM_DATE >= TRUNC(SYSDATE) THEN 0 ELSE 1 END ASC,
                            CASE WHEN T.FROM_DATE >= TRUNC(SYSDATE) THEN T.FROM_DATE END ASC,
                            T.FROM_DATE DESC
                    ) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS,
                               L.REASON, L.SOURCE, L.CONFIRM_STATUS, L.CONFIRM_DATE,
                               R.STATUS, R.REMARK, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE,
                               R.CREATED_BY ASSIGNED_BY, ASN.CNAME ASSIGNER_NAME, L.DOC_STATUS
                        FROM HRMS.HR_LEAVE_REQUEST L
                        JOIN HRMS.HR_REQUEST R    ON R.REQUEST_ID = L.REQUEST_ID
                        LEFT JOIN HRMS.ECM100 AP  ON AP.EMPCD     = R.FINAL_APPROVER
                        LEFT JOIN HRMS.ECM100 ASN ON ASN.EMPCD    = R.CREATED_BY
                        WHERE L.EMPCD = :EMPCD1
                          AND R.REQUEST_TYPE = 'LEAVE'
                          AND L.FROM_DATE >= :D_FROM1 AND L.FROM_DATE <= :D_TO1
                          AND (:SRC_FLAG1 IS NULL OR L.SOURCE = :SRC_VAL1)
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var list = await _oracleService.ExecuteQueryAsync(dataSql, r => new LeaveMyRequestModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()    ?? "",
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]        == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]     == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE   = r["CONFIRM_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                STATUS         = r["STATUS"]?.ToString(),
                REMARK         = r["REMARK"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                IS_EDITABLE    = r["STATUS"]?.ToString() == "PENDING" && r["SOURCE"]?.ToString() == "SELF",
                FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                APPROVER_NAME  = r["APPROVER_NAME"]?.ToString(),
                FINAL_DATE     = r["FINAL_DATE"]     == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                ASSIGNED_BY    = r["ASSIGNED_BY"]?.ToString(),
                ASSIGNER_NAME  = r["ASSIGNER_NAME"]?.ToString(),
                DOC_STATUS     = r["DOC_STATUS"]?.ToString()
            },
            new OracleParameter("EMPCD1",    empcd),
            new OracleParameter("D_FROM1",   dfrom.Date),
            new OracleParameter("D_TO1",     dto.Date),
            new OracleParameter("SRC_FLAG1", OracleDbType.Varchar2) { Value = srcFlagVal },
            new OracleParameter("SRC_VAL1",  OracleDbType.Varchar2) { Value = srcVal },
            new OracleParameter("R_MIN", offset),
            new OracleParameter("R_MAX", maxRn));

            return Ok(new
            {
                success     = true,
                total,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)total / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /apiHR/Leave/update
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPut("update")]
    public async Task<IActionResult> Update([FromBody] LeaveUpdateRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin cập nhật" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate) ||
                !DateTime.TryParse(model.TO_DATE,   out DateTime toDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            var statusRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS, L.LEAVE_TYPE, L.GP_REQUEST_ID FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'SELF' AND ROWNUM = 1",
                r => new {
                    Status      = r["STATUS"]?.ToString(),
                    LeaveType   = r["LEAVE_TYPE"]?.ToString(),
                    GpRequestId = r["GP_REQUEST_ID"]?.ToString()
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            if (statusRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            var current = statusRows[0];
            if (current.Status != "PENDING")
                return Ok(new { success = false, message = "Chỉ có thể sửa yêu cầu đang chờ duyệt" });

            string effectiveType = model.LEAVE_TYPE ?? current.LeaveType ?? "";
            // Trước đây Sửa đơn KHÔNG hề check LEAVE_TYPE hợp lệ (Submit() có check qua
            // SelfSubmitLeaveTypeCodes, Update() thì không) — gọi thẳng API có thể đổi thành mã bất
            // kỳ, kể cả mã không tồn tại/không dành cho tự nộp.
            if (!SelfSubmitLeaveTypeCodes.Contains(effectiveType))
                return Ok(new { success = false, message = "Loại nghỉ phép không hợp lệ" });

            // Re-check ĐÚNG luật báo trước như lúc Submit() — trước đây Sửa đơn không check lại,
            // NV có thể lách deadline bằng cách tạo đơn hợp lệ rồi sửa FROM_DATE về ngày đã quá hạn
            // (bug thật đã tái hiện trên data thật).
            string? dateRuleError = await ValidateSelfLeaveDateRulesAsync(model.EMPCD, effectiveType, fromDate);
            if (dateRuleError != null)
                return Ok(new { success = false, message = dateRuleError });

            // Chặn trùng ngày với đơn khác (loại trừ chính đơn đang sửa) + chặn vượt số ngày phép
            // năm còn lại — cùng lý do với Submit(): trước đây chỉ FE tự check, gọi thẳng API bỏ qua được.
            string? overlapError = await CheckSelfLeaveOverlapAsync(model.EMPCD, fromDate, toDate, excludeRequestId: model.REQUEST_ID);
            if (overlapError != null)
                return Ok(new { success = false, message = overlapError });

            if (effectiveType == "AL")
            {
                string? balanceError = await CheckAlBalanceAsync(model.EMPCD, model.TOTAL_DAYS);
                if (balanceError != null)
                    return Ok(new { success = false, message = balanceError });
            }

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET LEAVE_TYPE = :LEAVE_TYPE, FROM_DATE = :FROM_DATE, TO_DATE = :TO_DATE,
                    TOTAL_DAYS = :TOTAL_DAYS, REASON = :REASON,
                    UPDATED_BY = :UPDATED_BY, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("LEAVE_TYPE",  effectiveType),
                new OracleParameter("FROM_DATE",   fromDate),
                new OracleParameter("TO_DATE",     toDate),
                new OracleParameter("TOTAL_DAYS",  model.TOTAL_DAYS),
                new OracleParameter("REASON",      (object?)model.REASON ?? DBNull.Value),
                new OracleParameter("UPDATED_BY",  model.EMPCD),
                new OracleParameter("REQUEST_ID",  model.REQUEST_ID),
                new OracleParameter("EMPCD",       model.EMPCD));

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST SET UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("EMPCD",      model.EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            // CT: đổi FROM_DATE thì OUT_TIME/IN_TIME của Gate Pass liên kết (còn PENDING) phải dời
            // theo ngày mới, giờ vẫn cố định 07:30-16:30 — tránh Gate Pass trỏ về ngày cũ.
            if (current.LeaveType == "CT" && !string.IsNullOrEmpty(current.GpRequestId))
            {
                DateTime newOutTime = fromDate.Date.AddHours(7).AddMinutes(30);
                DateTime newInTime  = fromDate.Date.AddHours(16).AddMinutes(30);
                await _oracleService.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_GATEPASS_REQUEST SET OUT_TIME = :OUT_TIME, IN_TIME = :IN_TIME, UPDATED_BY = :EMPCD, UPDATED_DATE = SYSDATE
                    WHERE REQUEST_ID = :REQUEST_ID",
                    new OracleParameter("OUT_TIME", newOutTime),
                    new OracleParameter("IN_TIME",  newInTime),
                    new OracleParameter("EMPCD",    model.EMPCD),
                    new OracleParameter("REQUEST_ID", current.GpRequestId));
            }

            return Ok(new { success = true, message = "Cập nhật đơn nghỉ phép thành công" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /apiHR/Leave/delete?request_id=&empcd=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpDelete("delete")]
    public async Task<IActionResult> Delete(string request_id, string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(request_id) || string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu thông tin xoá" });

            var statusRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT R.STATUS, L.GP_REQUEST_ID FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'SELF' AND ROWNUM = 1",
                r => new { Status = r["STATUS"]?.ToString(), GpRequestId = r["GP_REQUEST_ID"]?.ToString() },
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            if (statusRows.Count == 0)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            var current = statusRows[0];
            if (current.Status != "PENDING")
                return Ok(new { success = false, message = "Chỉ có thể xoá yêu cầu đang chờ duyệt" });

            await _oracleService.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_LEAVE_REQUEST WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            await _oracleService.ExecuteNonQueryAsync(@"
                DELETE FROM HRMS.HR_REQUEST WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("REQUEST_ID", request_id),
                new OracleParameter("EMPCD",      empcd));

            // CT: xoá đơn thì Gate Pass 'OUT' liên kết (còn PENDING, chưa từng đồng bộ ERP) cũng
            // phải xoá theo — tránh để lại rác không ai duyệt/từ chối được nữa.
            if (!string.IsNullOrEmpty(current.GpRequestId))
            {
                await _oracleService.ExecuteNonQueryAsync(
                    "DELETE FROM HRMS.HR_GATEPASS_REQUEST WHERE REQUEST_ID = :REQUEST_ID",
                    new OracleParameter("REQUEST_ID", current.GpRequestId));
                await _oracleService.ExecuteNonQueryAsync(
                    "DELETE FROM HRMS.HR_REQUEST WHERE REQUEST_ID = :REQUEST_ID",
                    new OracleParameter("REQUEST_ID", current.GpRequestId));
            }

            return Ok(new { success = true, message = "Đã xoá đơn nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/confirm — worker acknowledges ASSIGNED leave notification
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] LeaveConfirmRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin xác nhận" });

            var infoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.CONFIRM_STATUS, R.CREATED_BY FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                WHERE R.REQUEST_ID = :REQUEST_ID AND L.EMPCD = :EMPCD AND L.SOURCE = 'ASSIGNED' AND ROWNUM = 1",
                r => new {
                    ConfirmStatus = r["CONFIRM_STATUS"]?.ToString(),
                    Assigner      = r["CREATED_BY"]?.ToString()
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                new OracleParameter("EMPCD",      model.EMPCD));

            var info = infoRows.FirstOrDefault();
            if (info == null)
                return Ok(new { success = false, message = "Không tìm thấy lịch nghỉ được sắp" });

            if (info.ConfirmStatus == "CONFIRMED")
                return Ok(new { success = false, message = "Đã nhận thông báo rồi" });

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET CONFIRM_STATUS = 'CONFIRMED', CONFIRM_DATE = SYSDATE,
                    UPDATED_BY = :UPDATED_BY, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND EMPCD = :EMPCD",
                new OracleParameter("UPDATED_BY",  model.EMPCD),
                new OracleParameter("REQUEST_ID",  model.REQUEST_ID),
                new OracleParameter("EMPCD",       model.EMPCD));

            if (!string.IsNullOrEmpty(info.Assigner))
            {
                _notiSvc.LeaveAcknowledged(info.Assigner, model.EMPCD);
            }

            return Ok(new { success = true, message = "Đã nhận thông báo lịch nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/approval-list — Supervisor/Manager/Deputy/Expat duyệt SELF leave
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("approval-list")]
    public async Task<IActionResult> GetApprovalList(
        string  approver_empcd,
        string? status     = null,
        string? leave_type = null,
        string? search     = null,
        string? dept_id    = null,
        string? line_id    = null,
        string? work_id    = null,
        string? date_from  = null,
        string? date_to    = null,
        int     page       = 1,
        int     page_size  = 50)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", approver_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            // Công tác (CT) chỉ Manager/Expat/Admin được duyệt — lấy role của approver để filter
            var approverRoles = await _oracleService.ExecuteQueryAsync(
                "SELECT NVL(RR.ROLE_NAME, '') ROLE_NAME FROM HRMS.HR_USERS UR LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = UR.ROLE_ID WHERE UR.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString() ?? "",
                new OracleParameter("EMPCD", approver_empcd));
            string approverRole = approverRoles.FirstOrDefault() ?? "";
            // Nếu không phải Manager/Expat/Admin mà request filter CT, return empty ngay
            if (leave_type == "CT" && !CanApproveCT(approverRole))
                return Ok(new { success = false, message = "Chỉ Manager, Expat hoặc Admin mới được duyệt công tác" });

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "SV");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(2);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410    B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_USERS  UR ON UR.EMPCD = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES  RR ON RR.ID    = UR.ROLE_ID
                LEFT JOIN HRMS.ECM100    AP ON AP.EMPCD  = R.FINAL_APPROVER";

            // Filter logic:
            //  - Non-PENDING (APPROVED/REJECTED): filter theo CREATED_DATE trong khoảng tháng
            //  - PENDING + TO_DATE >= hôm nay (đơn còn hiệu lực, kể cả nhiều ngày đang diễn ra dở):
            //    LUÔN hiện (tránh sếp quên khi req tạo trước nhiều tháng, hoặc bỏ sót đơn nhiều ngày
            //    có FROM_DATE đã qua nhưng TO_DATE chưa tới — bug thật 2026-09-11: đơn CT 9 ngày bị
            //    ẩn khỏi list duyệt dù đang diễn ra, vì trước đó lọc theo FROM_DATE thay vì TO_DATE)
            //  - PENDING + TO_DATE đã qua hẳn: bỏ hoàn toàn (không cần duyệt nữa)
            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE = 'SELF'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (
                        (R.STATUS <> 'PENDING' AND R.CREATED_DATE >= :D_FROM AND R.CREATED_DATE < :D_TO + 1)
                     OR (R.STATUS  = 'PENDING' AND L.TO_DATE      >= TRUNC(SYSDATE))
                  )
                  " + scopeFilter.SqlClause + @"
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:LT_FLAG   IS NULL OR L.LEAVE_TYPE    = :LT_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)";

            // Summary (4 thẻ tổng hợp) phải luôn tính trên toàn bộ status, không bị bó hẹp theo status đang filter
            string whereSqlNoStatus = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE = 'SELF'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND (
                        (R.STATUS <> 'PENDING' AND R.CREATED_DATE >= :D_FROM AND R.CREATED_DATE < :D_TO + 1)
                     OR (R.STATUS  = 'PENDING' AND L.TO_DATE      >= TRUNC(SYSDATE))
                  )
                  " + scopeFilter.SqlClause + @"
                  AND (:LT_FLAG   IS NULL OR L.LEAVE_TYPE    = :LT_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("LT_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(leave_type) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LT_VAL",    OracleDbType.Varchar2) { Value = (object?)leave_type ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };
            baseParams.AddRange(scopeFilter.Params);

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED
                {fromSql}{whereSqlNoStatus}";

            var summaryParams = baseParams
                .Where(p => p.ParameterName != "ST_FLAG" && p.ParameterName != "ST_VAL")
                .Select(p => (OracleParameter)p.Clone()).ToArray();

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL    = r["TOTAL"]    == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING  = r["PENDING"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED = r["APPROVED"] == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED = r["REJECTED"] == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"])
            }, summaryParams);

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY CASE WHEN T.STATUS = 'PENDING' AND T.FROM_DATE >= TRUNC(SYSDATE) THEN 0 WHEN T.STATUS = 'PENDING' AND T.FROM_DATE < TRUNC(SYSDATE) THEN 2 ELSE 1 END,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK, RR.ROLE_NAME REQUESTER_ROLE
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveListModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()   ?? "",
                EMPCD          = r["EMPCD"]?.ToString()         ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_ID        = r["DEPT_ID"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_ID        = r["LINE_ID"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_ID        = r["WORK_ID"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                APPROVER_NAME  = r["APPROVER_NAME"]?.ToString(),
                FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK         = r["REMARK"]?.ToString(),
                REQUESTER_ROLE = r["REQUESTER_ROLE"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/approve
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] LeaveApproveRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.APPROVER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin duyệt" });

            var approverRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.APPROVER_EMPCD));

            string? approverRole = approverRoleRows.FirstOrDefault();

            if (!Helpers.RoleHierarchyHelper.HasApprovalPermission(approverRole))
                return Ok(new { success = false, message = "Bạn không có quyền phê duyệt nghỉ phép" });

            var requestInfoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, RR.ROLE_NAME REQ_ROLE, L.LEAVE_TYPE, L.TOTAL_DAYS
                FROM HRMS.HR_LEAVE_REQUEST L
                LEFT JOIN HRMS.HR_USERS UR ON UR.EMPCD = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID    = UR.ROLE_ID
                WHERE L.REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new {
                    Empcd = r["EMPCD"]?.ToString(), Role = r["REQ_ROLE"]?.ToString(), LeaveType = r["LEAVE_TYPE"]?.ToString(),
                    TotalDays = r["TOTAL_DAYS"] == DBNull.Value ? 0m : Convert.ToDecimal(r["TOTAL_DAYS"])
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var requestInfo = requestInfoRows.FirstOrDefault();
            if (requestInfo == null)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (requestInfo.Empcd == model.APPROVER_EMPCD)
                return Ok(new { success = false, message = "Không thể tự duyệt đơn của mình" });

            if (!Helpers.RoleHierarchyHelper.CanApprove(approverRole, requestInfo.Role))
                return Ok(new { success = false, message = $"Phiếu này cần {Helpers.RoleHierarchyHelper.RequiredApproverName(requestInfo.Role)} phê duyệt." });

            // Công tác (CT) chỉ Manager/Expat/Admin được duyệt — chặn hành động thật, không chỉ ẩn ở UI/list.
            if (requestInfo.LeaveType == "CT" && !CanApproveCT(approverRole))
                return Ok(new { success = false, message = "Đơn Công tác chỉ Manager, Expat hoặc Admin mới được duyệt" });

            // Chặn duyệt vượt số ngày phép năm còn lại — re-check ngay tại thời điểm DUYỆT (không chỉ
            // lúc Submit/Update), vì NV có thể có NHIỀU đơn AL PENDING song song, mỗi đơn riêng lẻ đều
            // hợp lệ lúc nộp (LEFT_NUM chỉ trừ đơn ĐÃ duyệt/đồng bộ ERP), nhưng duyệt hết cả 2 thì
            // tổng vượt số ngày được cấp — ảnh hưởng lương thật nếu lọt qua ERP.
            if (requestInfo.LeaveType == "AL" && !string.IsNullOrEmpty(requestInfo.Empcd))
            {
                string? balanceError = await CheckAlBalanceAsync(requestInfo.Empcd, requestInfo.TotalDays);
                if (balanceError != null)
                    return Ok(new { success = false, message = balanceError + " (đơn khác của NV có thể đã được duyệt trước, làm giảm số ngày còn lại)" });
            }

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST
                SET STATUS = 'APPROVED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
                    REMARK = :REMARK_VAL, UPDATED_BY = :APPROVER1, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND STATUS = 'PENDING'",
                new OracleParameter("APPROVER",   model.APPROVER_EMPCD),
                new OracleParameter("REMARK_VAL", (object?)model.COMMENT ?? DBNull.Value),
                new OracleParameter("APPROVER1",  model.APPROVER_EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy hoặc đã được xử lý rồi" });

            // ERP: call SP_015_NEW after approval
            var ldRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT FROM_DATE, TO_DATE, LEAVE_TYPE, REASON, GP_REQUEST_ID FROM HRMS.HR_LEAVE_REQUEST
                WHERE REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new {
                    FromDate    = Convert.ToDateTime(r["FROM_DATE"]),
                    ToDate      = Convert.ToDateTime(r["TO_DATE"]),
                    LeaveType   = r["LEAVE_TYPE"]?.ToString(),
                    Reason      = r["REASON"]?.ToString(),
                    GpRequestId = r["GP_REQUEST_ID"]?.ToString()
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var ld = ldRows.FirstOrDefault();
            if (ld != null && !string.IsNullOrEmpty(requestInfo.Empcd))
            {
                string erpCd = ld.LeaveType switch { "AL" => "PN", "CL" => "BH", "CT" => "CT", _ => "CP" };

                var erpHolidays = (await _oracleService.ExecuteQueryAsync(
                    @"SELECT TRUNC(HUILDAY) AS HUILDAY FROM HRMS.EAM800
                      WHERE TRUNC(HUILDAY) BETWEEN TRUNC(:FROM_DATE) AND TRUNC(:TO_DATE)",
                    r => Convert.ToDateTime(r["HUILDAY"]).Date,
                    new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = ld.FromDate },
                    new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = ld.ToDate }
                )).ToHashSet();

                // NV được phép nghỉ Chủ Nhật → không skip Sunday dù nằm trong holiday. Dưỡng sức (DS)
                // tính theo ngày lịch kể cả Chủ Nhật/ngày lễ theo quy định — luôn coi như "được phép"
                // với loại này, không phụ thuộc whitelist HR_SUNDAY_LEAVE_ALLOWED (dành cho NV làm
                // ca Chủ Nhật thật, khác khái niệm với DS).
                bool isSundayAllowed = ld.LeaveType == "DS" || (await _oracleService.ExecuteQueryAsync(
                    "SELECT 1 AS X FROM HRMS.HR_SUNDAY_LEAVE_ALLOWED WHERE EMPCD = :EMPCD AND IS_ACTIVE = 1",
                    r => 1,
                    new OracleParameter("EMPCD", requestInfo.Empcd)
                )).Any();

                string? erpError = null;
                try
                {
                    // Tính remark BÊN TRONG try — nếu bước bỏ dấu (round-trip DB) lỗi, phải rơi vào
                    // đúng nhánh erpError rollback STATUS về PENDING bên dưới, không để đơn kẹt ở
                    // APPROVED mà chưa ghi ERP (STATUS đã UPDATE ở trên trước khi tới đây).
                    string erpRemark = await BuildErpRemarkAsync(ld.LeaveType ?? "", ld.Reason, isAssignFlow: false);

                    for (var day = ld.FromDate.Date; day <= ld.ToDate.Date; day = day.AddDays(1))
                    {
                        if (erpHolidays.Contains(day)
                            && !(day.DayOfWeek == DayOfWeek.Sunday && isSundayAllowed))
                            continue;

                        // SP_015_NEW hardcode bỏ Chủ Nhật. Với NV trong whitelist (HR_SUNDAY_LEAVE_ALLOWED),
                        // dùng SP_015_FORHRAPP riêng cho ngày Chủ Nhật (do sếp viết).
                        string spName = (day.DayOfWeek == DayOfWeek.Sunday && isSundayAllowed)
                            ? "HRMS.SP_015_FORHRAPP"
                            : "HRMS.SP_015_NEW";

                        await _oracleService.ExecuteProcedureAsync(spName,
                            new OracleParameter("AS_EMPCD",   requestInfo.Empcd),
                            new OracleParameter("AS_LEAVECD", erpCd),
                            new OracleParameter { ParameterName = "AD_ST_DAT", OracleDbType = OracleDbType.Date, Value = day },
                            new OracleParameter { ParameterName = "AD_ED_DAT", OracleDbType = OracleDbType.Date, Value = day },
                            new OracleParameter("AS_IN_ID",   model.APPROVER_EMPCD),
                            new OracleParameter("AS_REMAR",   erpRemark));
                    }

                    await _oracleService.ExecuteNonQueryAsync(
                        "UPDATE HRMS.EFM410 SET APPROVED_BY = :APPROVED_BY WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FR_DAT AND :TO_DAT",
                        new OracleParameter("APPROVED_BY", model.APPROVER_EMPCD),
                        new OracleParameter("EMPCD",       requestInfo.Empcd),
                        new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = ld.FromDate },
                        new OracleParameter { ParameterName = "TO_DAT", OracleDbType = OracleDbType.Date, Value = ld.ToDate });
                }
                catch (Exception ex) { erpError = ex.Message; }

                if (erpError != null)
                {
                    // ERP fail → rollback HR_REQUEST về PENDING để HR thấy đỏ và retry
                    await _oracleService.ExecuteNonQueryAsync(@"
                        UPDATE HRMS.HR_REQUEST
                        SET STATUS = 'PENDING', FINAL_APPROVER = NULL, FINAL_DATE = NULL,
                            REMARK = NULL, UPDATED_BY = NULL, UPDATED_DATE = NULL
                        WHERE REQUEST_ID = :REQUEST_ID",
                        new OracleParameter("REQUEST_ID", model.REQUEST_ID));
                    return Ok(new { success = false, message = "Insert ERP thất bại, phiếu đã trả về PENDING. Chi tiết: " + erpError });
                }

                // CT: duyệt luôn Gate Pass 'OUT' liên kết (được tạo PENDING lúc Submit) + đồng bộ ERP.
                // Dùng đúng 1 PL/SQL block UPDATE + SP_INSERT_GATE_PASS như GatePassController.Approve()
                // (KHÔNG gọi SP rời qua ExecuteProcedureAsync như bản đầu — đã verify thật: SP tự
                // TO_DATE(P_DAT,'YYYYMMDD') rồi ghi ngược vào cột DAT (VARCHAR2) theo NLS_DATE_FORMAT
                // hiện tại của session; thiếu ALTER SESSION ép 'YYYYMMDD' → DAT bị lưu sai định dạng
                // kiểu '27-AUG-26' thay vì '20260827', phá hỏng mọi query/báo cáo dựa vào DAT).
                // Không rollback đơn CT nếu bước này lỗi — đơn nghỉ đã ghi ERP thành công ở trên,
                // Gate Pass chỉ là phụ trợ; lỗi sẽ được báo qua message để HR retry duyệt GP riêng.
                if (ld.LeaveType == "CT" && !string.IsNullOrEmpty(ld.GpRequestId))
                {
                    try
                    {
                        string gpPlsql = @"
DECLARE
    v_rows    NUMBER;
    v_nls_fmt VARCHAR2(100);
    v_empcd   HRMS.HR_REQUEST.EMPCD%TYPE;
    v_gp_type HRMS.HR_GATEPASS_REQUEST.GP_TYPE%TYPE;
    v_out_dt  HRMS.HR_GATEPASS_REQUEST.OUT_TIME%TYPE;
    v_in_dt   HRMS.HR_GATEPASS_REQUEST.IN_TIME%TYPE;
    v_dat     VARCHAR2(8);
    v_timeout VARCHAR2(4);
    v_timein  VARCHAR2(4);
BEGIN
    SELECT VALUE INTO v_nls_fmt FROM NLS_SESSION_PARAMETERS WHERE PARAMETER = 'NLS_DATE_FORMAT';
    EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''YYYYMMDD''';

    UPDATE HRMS.HR_REQUEST
    SET STATUS = 'APPROVED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
        UPDATED_BY = :APPROVER, UPDATED_DATE = SYSDATE
    WHERE REQUEST_ID = :GP_REQUEST_ID AND STATUS = 'PENDING';

    v_rows := SQL%ROWCOUNT;
    :ROW_COUNT := v_rows;

    IF v_rows > 0 THEN
        SELECT R.EMPCD, G.GP_TYPE, G.OUT_TIME, G.IN_TIME
        INTO v_empcd, v_gp_type, v_out_dt, v_in_dt
        FROM HRMS.HR_REQUEST R
        JOIN HRMS.HR_GATEPASS_REQUEST G ON G.REQUEST_ID = R.REQUEST_ID
        WHERE R.REQUEST_ID = :GP_REQUEST_ID;

        v_dat     := COALESCE(TO_CHAR(v_out_dt, 'YYYYMMDD'), TO_CHAR(v_in_dt, 'YYYYMMDD'));
        v_timeout := CASE WHEN v_out_dt IS NOT NULL THEN TO_CHAR(v_out_dt, 'HH24MI') ELSE NULL END;
        v_timein  := CASE WHEN v_in_dt  IS NOT NULL THEN TO_CHAR(v_in_dt,  'HH24MI') ELSE NULL END;

        HRMS.SP_INSERT_GATE_PASS(
            P_EMPCD       => v_empcd,
            P_DAT         => v_dat,
            P_TYPE        => v_gp_type,
            P_TIMEIN      => v_timein,
            P_TIMEOUT     => v_timeout,
            P_INID        => :APPROVER,
            P_APPROVED_ID => :APPROVER
        );
    END IF;

    EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''' || v_nls_fmt || '''';
EXCEPTION WHEN OTHERS THEN
    EXECUTE IMMEDIATE 'ALTER SESSION SET NLS_DATE_FORMAT = ''' || v_nls_fmt || '''';
    RAISE;
END;";

                        var gpApprover  = new OracleParameter("APPROVER",      model.APPROVER_EMPCD);
                        var gpReqId     = new OracleParameter("GP_REQUEST_ID", ld.GpRequestId);
                        var gpRowCount  = new OracleParameter("ROW_COUNT", OracleDbType.Int32) { Direction = System.Data.ParameterDirection.Output };

                        await _oracleService.ExecuteNonQueryAsync(gpPlsql, gpApprover, gpReqId, gpRowCount);
                    }
                    catch { /* Gate Pass phụ trợ — lỗi ở đây không rollback đơn CT đã duyệt thành công */ }
                }
            }

            if (!string.IsNullOrEmpty(requestInfo.Empcd))
            {
                _notiSvc.LeaveApproved(requestInfo.Empcd, model.APPROVER_EMPCD);
            }

            // Invalidate Home summary cache của approver — số pending vừa giảm 1
            _homeSummarySvc.InvalidateFor(model.APPROVER_EMPCD);

            return Ok(new { success = true, message = "Đã duyệt đơn nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/reject
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] LeaveApproveRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.APPROVER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin từ chối" });

            var rejectRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.APPROVER_EMPCD));

            string? rejectRole = rejectRoleRows.FirstOrDefault();

            if (!Helpers.RoleHierarchyHelper.HasApprovalPermission(rejectRole))
                return Ok(new { success = false, message = "Bạn không có quyền từ chối nghỉ phép" });

            var rejectInfoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, RR.ROLE_NAME REQ_ROLE, L.LEAVE_TYPE, L.GP_REQUEST_ID
                FROM HRMS.HR_LEAVE_REQUEST L
                LEFT JOIN HRMS.HR_USERS UR ON UR.EMPCD = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID    = UR.ROLE_ID
                WHERE L.REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new {
                    Empcd       = r["EMPCD"]?.ToString(),
                    Role        = r["REQ_ROLE"]?.ToString(),
                    LeaveType   = r["LEAVE_TYPE"]?.ToString(),
                    GpRequestId = r["GP_REQUEST_ID"]?.ToString()
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var rejectInfo = rejectInfoRows.FirstOrDefault();
            if (rejectInfo == null)
                return Ok(new { success = false, message = "Không tìm thấy yêu cầu" });

            if (rejectInfo.Empcd == model.APPROVER_EMPCD)
                return Ok(new { success = false, message = "Không thể tự từ chối đơn của mình" });

            if (!Helpers.RoleHierarchyHelper.CanApprove(rejectRole, rejectInfo.Role))
                return Ok(new { success = false, message = $"Phiếu này cần {Helpers.RoleHierarchyHelper.RequiredApproverName(rejectInfo.Role)} xử lý." });

            // Công tác (CT) chỉ Manager/Expat/Admin được từ chối — chặn hành động thật.
            if (rejectInfo.LeaveType == "CT" && !CanApproveCT(rejectRole))
                return Ok(new { success = false, message = "Đơn Công tác chỉ Manager, Expat hoặc Admin mới được xử lý" });

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_REQUEST
                SET STATUS = 'REJECTED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
                    REMARK = :REMARK_VAL, UPDATED_BY = :APPROVER1, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID AND STATUS = 'PENDING'",
                new OracleParameter("APPROVER",   model.APPROVER_EMPCD),
                new OracleParameter("REMARK_VAL", (object?)model.COMMENT ?? DBNull.Value),
                new OracleParameter("APPROVER1",  model.APPROVER_EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy hoặc đã được xử lý rồi" });

            // CT: từ chối luôn Gate Pass 'OUT' liên kết (đang PENDING) — NV không còn được cấp
            // quyền ra cổng cho chuyến công tác đã bị từ chối.
            if (rejectInfo.LeaveType == "CT" && !string.IsNullOrEmpty(rejectInfo.GpRequestId))
            {
                try
                {
                    await _oracleService.ExecuteNonQueryAsync(@"
                        UPDATE HRMS.HR_REQUEST
                        SET STATUS = 'REJECTED', FINAL_APPROVER = :APPROVER, FINAL_DATE = SYSDATE,
                            UPDATED_BY = :APPROVER1, UPDATED_DATE = SYSDATE
                        WHERE REQUEST_ID = :REQUEST_ID AND STATUS = 'PENDING'",
                        new OracleParameter("APPROVER",   model.APPROVER_EMPCD),
                        new OracleParameter("APPROVER1",  model.APPROVER_EMPCD),
                        new OracleParameter("REQUEST_ID", rejectInfo.GpRequestId));
                }
                catch { /* best-effort — đơn CT đã từ chối thành công dù bước này lỗi */ }
            }

            if (!string.IsNullOrEmpty(rejectInfo.Empcd))
            {
                _notiSvc.LeaveRejected(rejectInfo.Empcd, model.APPROVER_EMPCD);
            }

            // Invalidate Home summary cache của approver
            _homeSummarySvc.InvalidateFor(model.APPROVER_EMPCD);

            return Ok(new { success = true, message = "Đã từ chối đơn nghỉ phép" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/assign — Supervisor/Deputy/Manager sắp lịch AL cho worker(s)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromBody] LeaveAssignRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.ASSIGNER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin người sắp lịch" });

            if (model.TARGET_EMPCDS == null || model.TARGET_EMPCDS.Count == 0)
                return Ok(new { success = false, message = "Chưa chọn nhân viên" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate) ||
                !DateTime.TryParse(model.TO_DATE,   out DateTime toDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            if (fromDate.Date <= DateTime.Today)
                return Ok(new { success = false, message = "Chỉ được sắp lịch từ ngày mai trở đi" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            if (string.IsNullOrEmpty(model.LEAVE_TYPE) || !SupervisorAssignTypes.Contains(model.LEAVE_TYPE))
                model.LEAVE_TYPE = "AL";

            if (model.LEAVE_TYPE != "AL" && string.IsNullOrWhiteSpace(model.REASON))
                return Ok(new { success = false, message = "Vui lòng nhập lý do nghỉ" });

            var assignerRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ASSIGNER_EMPCD));

            string? assignerRole = assignerRoleRows.FirstOrDefault();

            if (!Helpers.RoleHierarchyHelper.HasApprovalPermission(assignerRole) ||
                string.Equals(assignerRole, "Expat", StringComparison.OrdinalIgnoreCase))
                return Ok(new { success = false, message = "Bạn không có quyền sắp lịch nghỉ" });

            var results   = new List<object>();
            int successCt = 0;

            foreach (var targetEmpcd in model.TARGET_EMPCDS)
            {
                try
                {
                    // Không cho Quản lý/Supervisor tự sắp lịch nghỉ cho chính mình — phải tự nộp đơn
                    // (Submit) như nhân viên thường, tránh tự cấp phép nghỉ không qua ai kiểm soát.
                    if (string.Equals(targetEmpcd, model.ASSIGNER_EMPCD, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new { empcd = targetEmpcd, success = false, message = "Không thể tự sắp lịch nghỉ cho chính mình" });
                        continue;
                    }

                    var empRows = await _oracleService.ExecuteQueryAsync(
                        "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                        r => r["CNAME"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd));

                    string empName = empRows.FirstOrDefault() ?? "";

                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_REQUEST
                            (REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE)
                        VALUES ('LEAVE', :EMPCD, :EMP_NAME, SYSDATE, 'ASSIGNED', :CREATED_BY, SYSDATE)",
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("EMP_NAME",   empName),
                        new OracleParameter("CREATED_BY", model.ASSIGNER_EMPCD));

                    var reqIds = await _oracleService.ExecuteQueryAsync(@"
                        SELECT REQUEST_ID FROM (
                            SELECT REQUEST_ID FROM HRMS.HR_REQUEST
                            WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'LEAVE' AND STATUS = 'ASSIGNED'
                              AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE)
                            ORDER BY CREATED_DATE DESC
                        ) WHERE ROWNUM = 1",
                        r => r["REQUEST_ID"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd));

                    if (reqIds.Count == 0 || string.IsNullOrEmpty(reqIds[0]))
                    {
                        results.Add(new { empcd = targetEmpcd, success = false, message = "Lỗi tạo REQUEST_ID" });
                        continue;
                    }

                    string requestId = reqIds[0]!;

                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_LEAVE_REQUEST
                            (REQUEST_ID, EMPCD, LEAVE_TYPE, FROM_DATE, TO_DATE, TOTAL_DAYS, REASON, CREATED_DATE, SOURCE)
                        VALUES (:REQUEST_ID, :EMPCD, :LEAVE_TYPE, :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'ASSIGNED')",
                        new OracleParameter("REQUEST_ID", requestId),
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("LEAVE_TYPE", model.LEAVE_TYPE),
                        new OracleParameter("FROM_DATE",  fromDate),
                        new OracleParameter("TO_DATE",    toDate),
                        new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                        new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

                    // ERP: call SP_015_NEW immediately after assign (no worker confirm needed)
                    string erpCd     = model.LEAVE_TYPE switch { "AL" => "PN", "CL" => "BH", "CT" => "CT", _ => "CP" };
                    string erpRemark = await BuildErpRemarkAsync(model.LEAVE_TYPE, model.REASON, isAssignFlow: true);
                    var erpHolidays = (await _oracleService.ExecuteQueryAsync(
                        @"SELECT TRUNC(HUILDAY) AS HUILDAY FROM HRMS.EAM800
                          WHERE TRUNC(HUILDAY) BETWEEN TRUNC(:FROM_DATE) AND TRUNC(:TO_DATE)",
                        r => Convert.ToDateTime(r["HUILDAY"]).Date,
                        new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = fromDate },
                        new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = toDate }
                    )).ToHashSet();

                    // Dưỡng sức (DS) tính theo ngày lịch kể cả Chủ Nhật/ngày lễ — luôn coi như "được phép".
                    bool isSundayAllowed = model.LEAVE_TYPE == "DS" || (await _oracleService.ExecuteQueryAsync(
                        "SELECT 1 AS X FROM HRMS.HR_SUNDAY_LEAVE_ALLOWED WHERE EMPCD = :EMPCD AND IS_ACTIVE = 1",
                        r => 1,
                        new OracleParameter("EMPCD", targetEmpcd)
                    )).Any();
                    try
                    {
                        for (var day = fromDate.Date; day <= toDate.Date; day = day.AddDays(1))
                        {
                            if (erpHolidays.Contains(day)
                                && !(day.DayOfWeek == DayOfWeek.Sunday && isSundayAllowed))
                                continue;

                            // SP_015_NEW skip Chủ Nhật → dùng SP_015_FORHRAPP cho NV whitelist
                            string spName = (day.DayOfWeek == DayOfWeek.Sunday && isSundayAllowed)
                                ? "HRMS.SP_015_FORHRAPP"
                                : "HRMS.SP_015_NEW";

                            await _oracleService.ExecuteProcedureAsync(spName,
                                new OracleParameter("AS_EMPCD",   targetEmpcd),
                                new OracleParameter("AS_LEAVECD", erpCd),
                                new OracleParameter { ParameterName = "AD_ST_DAT", OracleDbType = OracleDbType.Date, Value = day },
                                new OracleParameter { ParameterName = "AD_ED_DAT", OracleDbType = OracleDbType.Date, Value = day },
                                new OracleParameter("AS_IN_ID",   model.ASSIGNER_EMPCD),
                                new OracleParameter("AS_REMAR",   erpRemark));
                        }

                        await _oracleService.ExecuteNonQueryAsync(
                            "UPDATE HRMS.EFM410 SET APPROVED_BY = :APPROVED_BY WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FR_DAT AND :TO_DAT",
                            new OracleParameter("APPROVED_BY", model.ASSIGNER_EMPCD),
                            new OracleParameter("EMPCD",       targetEmpcd),
                            new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = fromDate },
                            new OracleParameter { ParameterName = "TO_DAT", OracleDbType = OracleDbType.Date, Value = toDate });
                    }
                    catch { /* ERP failure không block assign */ }

                    string leaveTypeName = NewLeaveTypeNames.GetValueOrDefault(model.LEAVE_TYPE, model.LEAVE_TYPE);
                    _notiSvc.LeaveAssigned(targetEmpcd, model.ASSIGNER_EMPCD, leaveTypeName, fromDate, toDate);

                    results.Add(new { empcd = targetEmpcd, success = true, request_id = requestId });
                    successCt++;
                }
                catch (Exception ex)
                {
                    results.Add(new { empcd = targetEmpcd, success = false, message = ex.Message });
                }
            }

            return Ok(new
            {
                success = successCt > 0,
                message = $"Đã sắp lịch cho {successCt}/{model.TARGET_EMPCDS.Count} nhân viên",
                results
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/team-schedule?approver_empcd=&month=&year=
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("team-schedule")]
    public async Task<IActionResult> GetTeamSchedule(
        string  approver_empcd,
        int?    month = null,
        int?    year  = null)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", approver_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            int m = month ?? DateTime.Today.Month;
            int y = year  ?? DateTime.Today.Year;
            DateTime dfrom = new DateTime(y, m, 1);
            DateTime dto   = dfrom.AddMonths(1).AddDays(-1);

            var scopeFilter = Helpers.OTScopeFilterHelper.ForScopeByTuple(approver_empcd, empAlias: "EC", prefix: "TS");

            string sql = $@"
                SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                       L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS,
                       R.STATUS, L.CONFIRM_STATUS,
                       B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                       CASE WHEN L.SOURCE = 'ASSIGNED' THEN CB.FULL_NAME ELSE AP.FULL_NAME END APPROVED_BY,
                       CASE WHEN L.SOURCE = 'ASSIGNED' THEN R.CREATED_DATE ELSE R.FINAL_DATE END APPROVED_DATE
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410   B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.HR_USERS AP ON AP.EMPCD = R.FINAL_APPROVER
                LEFT JOIN HRMS.HR_USERS CB ON CB.EMPCD = R.CREATED_BY
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE <= :D_TO AND L.TO_DATE >= :D_FROM
                  AND (
                      (L.SOURCE = 'SELF'     AND R.STATUS = 'APPROVED') OR
                      (L.SOURCE = 'ASSIGNED')
                  )
                  {scopeFilter.SqlClause}
                ORDER BY L.FROM_DATE, L.EMPCD";

            var p = new List<OracleParameter>
            {
                new OracleParameter("D_FROM", OracleDbType.Date) { Value = dfrom },
                new OracleParameter("D_TO",   OracleDbType.Date) { Value = dto }
            };
            p.AddRange(scopeFilter.Params);

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new LeaveScheduleModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()  ?? "",
                EMPCD          = r["EMPCD"]?.ToString()        ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                APPROVED_BY    = r["APPROVED_BY"]?.ToString(),
                APPROVED_DATE  = r["APPROVED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["APPROVED_DATE"])
            }, p.ToArray());

            return Ok(new { success = true, month = m, year = y, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/my-assignments — Supervisor xem lịch mình đã sắp
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-assignments")]
    public async Task<IActionResult> GetMyAssignments(
        string  assigner_empcd,
        string? status    = null,
        string? search    = null,
        string? date_from = null,
        string? date_to   = null,
        int     page      = 1,
        int     page_size = 20)
    {
        try
        {
            if (string.IsNullOrEmpty(assigner_empcd))
                return Ok(new { success = false, message = "Thiếu mã người sắp lịch" });

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-3);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(3);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE       = 'ASSIGNED'
                  AND R.CREATED_BY   = :ASSIGNER
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)";

            // Summary (3 thẻ tổng hợp) phải luôn tính trên toàn bộ status, không bị bó hẹp theo status đang filter
            string whereSqlNoStatus = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE       = 'ASSIGNED'
                  AND R.CREATED_BY   = :ASSIGNER
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("ASSIGNER",  OracleDbType.Varchar2) { Value = assigner_empcd },
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
            };

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN L.CONFIRM_STATUS IS NULL       THEN 1 ELSE 0 END) PENDING_CONFIRM,
                       SUM(CASE WHEN L.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) CONFIRMED
                {fromSql}{whereSqlNoStatus}";

            var summaryParams = baseParams
                .Where(p => p.ParameterName != "ST_FLAG" && p.ParameterName != "ST_VAL")
                .Select(p => (OracleParameter)p.Clone()).ToArray();

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveAssignSummary
            {
                TOTAL           = r["TOTAL"]           == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING_CONFIRM = r["PENDING_CONFIRM"] == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING_CONFIRM"]),
                CONFIRMED       = r["CONFIRMED"]       == DBNull.Value ? 0 : Convert.ToInt32(r["CONFIRMED"])
            }, summaryParams);

            var summary = summaryRows.FirstOrDefault() ?? new LeaveAssignSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<object>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME,
                               L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CONFIRM_DATE, R.CREATED_DATE ASSIGN_DATE
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveAssignmentModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()    ?? "",
                EMPCD          = r["EMPCD"]?.ToString()          ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]     == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]       == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]    == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE   = r["CONFIRM_DATE"]  == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]),
                ASSIGN_DATE    = r["ASSIGN_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["ASSIGN_DATE"]),
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/assignment-log — HR xem log toàn bộ việc sắp lịch
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("assignment-log")]
    public async Task<IActionResult> GetAssignmentLog(
        string? assigner_cd = null,
        string? search      = null,
        string? dept_id     = null,
        string? line_id     = null,
        string? work_id     = null,
        string? status      = null,
        string? date_from   = null,
        string? date_to     = null,
        int     page        = 1,
        int     page_size   = 50)
    {
        try
        {
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-3);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(3);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R   ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC  ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410 B   ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100 ASN ON ASN.EMPCD   = R.CREATED_BY";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND L.SOURCE       = 'ASSIGNED'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR NVL(L.CONFIRM_STATUS,'ASSIGNED') = :ST_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)
                  AND (:ASN_FLAG  IS NULL OR R.CREATED_BY    = :ASN_VAL)";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)      ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status      ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)      ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)      ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id)     ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id     ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id)     ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id     ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id)     ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id     ?? DBNull.Value },
                new OracleParameter("ASN_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(assigner_cd) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ASN_VAL",   OracleDbType.Varchar2) { Value = (object?)assigner_cd ?? DBNull.Value },
            };

            string sqlCount = $"SELECT COUNT(*) CNT {fromSql}{whereSql}";
            var totalRows = await _oracleService.ExecuteQueryAsync(sqlCount,
                r => Convert.ToInt32(r["CNT"]),
                baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

            int total = totalRows.FirstOrDefault();

            if (total == 0)
                return Ok(new { success = true, total = 0, page, page_size, total_pages = 0, data = new List<object>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.ASSIGN_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CONFIRM_DATE,
                               R.CREATED_BY ASSIGNED_BY, ASN.CNAME ASSIGNER_NAME,
                               R.CREATED_DATE ASSIGN_DATE
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveAssignmentLogModel
            {
                REQUEST_ID    = r["REQUEST_ID"]?.ToString()    ?? "",
                EMPCD         = r["EMPCD"]?.ToString()          ?? "",
                EMP_NAME      = r["EMP_NAME"]?.ToString(),
                DEPT_ID       = r["DEPT_ID"]?.ToString(),
                DEPT_NAME     = r["DEPT_NAME"]?.ToString(),
                LINE_ID       = r["LINE_ID"]?.ToString(),
                LINE_NAME     = r["LINE_NAME"]?.ToString(),
                WORK_ID       = r["WORK_ID"]?.ToString(),
                WORK_NAME     = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE    = r["LEAVE_TYPE"]?.ToString(),
                FROM_DATE     = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE       = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS    = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON        = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CONFIRM_DATE   = r["CONFIRM_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRM_DATE"]).ToString("yyyy-MM-ddTHH:mm:ss"),
                ASSIGNED_BY    = r["ASSIGNED_BY"]?.ToString(),
                ASSIGNER_NAME = r["ASSIGNER_NAME"]?.ToString(),
                ASSIGN_DATE   = r["ASSIGN_DATE"]  == DBNull.Value ? null : Convert.ToDateTime(r["ASSIGN_DATE"]),
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                total,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)total / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/annual-balance?approver_empcd=
    // Trả về phép năm (RECEIVE/USED/LEFT) cho toàn bộ nhân viên trong phạm vi
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("annual-balance")]
    public async Task<IActionResult> GetAnnualBalance(string approver_empcd)
    {
        try
        {
            if (!Helpers.OTScopeFilterHelper.IsAuthorized(approver_empcd))
                return Ok(new { success = false, message = "Chưa đăng nhập" });

            var hasSvScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", approver_empcd));

            if (hasSvScope.FirstOrDefault() == 0)
                return Ok(new { success = false, message = "Chưa được phân quyền bộ phận" });

            const string sql = @"
                WITH SCOPE_EMP AS (
                    SELECT DISTINCT EC.EMPCD
                    FROM HRMS.HR_USERS_DEPT UD
                    JOIN HRMS.ECM100 EC
                        ON EC.DEPTCD = UD.DEPTCD
                       AND EC.LINECD = UD.LINECD
                       AND EC.WORKCD = UD.WORKCD
                    WHERE UD.EMPCD = :APPROVER
                      AND EC.JEAJIKGB = 'Y'
                      AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                ),
                USED AS (
                    SELECT EMPCD,
                           COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                      AND EMPCD IN (SELECT EMPCD FROM SCOPE_EMP)
                    GROUP BY EMPCD
                ),
                ALLOC AS (
                    SELECT EMPCD, MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                      AND EMPCD IN (SELECT EMPCD FROM SCOPE_EMP)
                    GROUP BY EMPCD
                )
                SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                       B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME,
                       NVL(AL.RECEIVE_NUM, 0)                       AS RECEIVE_NUM,
                       NVL(U.USED_NUM, 0)                           AS USED_NUM,
                       NVL(AL.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0) AS LEFT_NUM
                FROM SCOPE_EMP SE
                JOIN HRMS.ECM100 EC  ON EC.EMPCD = SE.EMPCD
                LEFT JOIN HRMS.EAM410 B
                    ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN ALLOC AL ON AL.EMPCD = EC.EMPCD
                LEFT JOIN USED  U  ON U.EMPCD  = EC.EMPCD
                ORDER BY EC.CNAME";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                EMPCD       = r["EMPCD"]?.ToString()     ?? "",
                EMP_NAME    = r["EMP_NAME"]?.ToString()  ?? "",
                DEPT_NAME   = r["DEPT_NAME"]?.ToString(),
                LINE_NAME   = r["LINE_NAME"]?.ToString(),
                RECEIVE_NUM = r["RECEIVE_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["RECEIVE_NUM"]),
                USED_NUM    = r["USED_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["USED_NUM"]),
                LEFT_NUM    = r["LEFT_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"])
            }, new OracleParameter("APPROVER", approver_empcd));

            return Ok(new { success = true, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/my-balance?empcd= — Số ngày phép năm của 1 nhân viên
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("my-balance")]
    public async Task<IActionResult> GetMyBalance(string empcd)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });

            const string sql = @"
                WITH ALLOC AS (
                    SELECT MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE EMPCD = :EMPCD
                      AND SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                ),
                USED AS (
                    SELECT COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE EMPCD = :EMPCD2
                      AND TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                )
                SELECT NVL(A.RECEIVE_NUM, 0) AS RECEIVE_NUM,
                       NVL(U.USED_NUM, 0)    AS USED_NUM,
                       NVL(A.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0) AS LEFT_NUM
                FROM ALLOC A, USED U";

            var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                RECEIVE_NUM = r["RECEIVE_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["RECEIVE_NUM"]),
                USED_NUM    = r["USED_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["USED_NUM"]),
                LEFT_NUM    = r["LEFT_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"])
            },
            new OracleParameter("EMPCD",  empcd),
            new OracleParameter("EMPCD2", empcd));

            var row = rows.FirstOrDefault() ?? new { RECEIVE_NUM = 0, USED_NUM = 0, LEFT_NUM = 0 };
            return Ok(new { success = true, data = row });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/hr-list — HR xem toàn công ty
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("hr-list")]
    public async Task<IActionResult> GetHRList(
        string? status     = null,
        string? source     = null,
        string? leave_type = null,
        string? search     = null,
        string? dept_id    = null,
        string? line_id    = null,
        string? work_id    = null,
        string? date_from  = null,
        string? date_to    = null,
        bool    nl_streak_only = false,
        int     page       = 1,
        int     page_size  = 50)
    {
        try
        {
            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(2);

            // NV có chuỗi nghỉ Không lương (NL) tổng >= 5 ngày, cho phép ngắt quãng tối đa 1 ngày đi làm
            // giữa 2 đợt nghỉ NL (gap = số ngày trống giữa TO_DATE đợt trước và FROM_DATE đợt sau).
            // Gộp nhóm theo kiểu "gaps and islands": mỗi khi gap > 1 thì bắt đầu nhóm (GRP_ID) mới.
            string nlStreakSql = @"
                AND (:NLQ_FLAG IS NULL OR L.LEAVE_TYPE = 'NL')
                AND (:NLQ_FLAG IS NULL OR L.EMPCD IN (
                    SELECT EMPCD FROM (
                        SELECT EMPCD, GRP_ID, SUM(TOTAL_DAYS) AS STREAK_DAYS
                        FROM (
                            SELECT N.EMPCD, N.TOTAL_DAYS, N.FROM_DATE,
                                   SUM(CASE WHEN N.GAP_DAYS IS NULL OR N.GAP_DAYS > 1 THEN 1 ELSE 0 END)
                                       OVER (PARTITION BY N.EMPCD ORDER BY N.FROM_DATE) AS GRP_ID
                            FROM (
                                SELECT L2.EMPCD, L2.TOTAL_DAYS, L2.FROM_DATE,
                                       L2.FROM_DATE - LAG(L2.TO_DATE) OVER (PARTITION BY L2.EMPCD ORDER BY L2.FROM_DATE) - 1 AS GAP_DAYS
                                FROM HRMS.HR_LEAVE_REQUEST L2
                                JOIN HRMS.HR_REQUEST R2 ON R2.REQUEST_ID = L2.REQUEST_ID
                                WHERE L2.LEAVE_TYPE = 'NL' AND R2.STATUS != 'REJECTED'
                                  AND L2.TO_DATE >= :D_FROM AND L2.FROM_DATE <= :D_TO
                            ) N
                        ) X
                        GROUP BY EMPCD, GRP_ID
                        HAVING SUM(TOTAL_DAYS) >= 5
                    )
                ))";

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410    B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100    AP ON AP.EMPCD  = R.FINAL_APPROVER
                LEFT JOIN HRMS.ECM100    ASN ON ASN.EMPCD = R.CREATED_BY
                LEFT JOIN HRMS.HR_USERS  UR ON UR.EMPCD  = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES  RR ON RR.ID     = UR.ROLE_ID";

            string whereSql = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.TO_DATE >= :D_FROM AND L.FROM_DATE <= :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRC_FLAG  IS NULL OR L.SOURCE       = :SRC_VAL)
                  AND (:LT_FLAG   IS NULL OR L.LEAVE_TYPE    = :LT_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)" + nlStreakSql;

            // Summary (4 thẻ tổng hợp) phải luôn tính trên toàn bộ status, không bị bó hẹp theo status đang filter
            string whereSqlNoStatus = @"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.TO_DATE >= :D_FROM AND L.FROM_DATE <= :D_TO
                  AND (:SRC_FLAG  IS NULL OR L.SOURCE       = :SRC_VAL)
                  AND (:LT_FLAG   IS NULL OR L.LEAVE_TYPE    = :LT_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)" + nlStreakSql;

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("SRC_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(source)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRC_VAL",   OracleDbType.Varchar2) { Value = (object?)source  ?? DBNull.Value },
                new OracleParameter("LT_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(leave_type) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LT_VAL",    OracleDbType.Varchar2) { Value = (object?)leave_type ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
                new OracleParameter("NLQ_FLAG",  OracleDbType.Varchar2) { Value = (object?)(nl_streak_only ? "Y" : null) ?? DBNull.Value },
            };

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED,
                       SUM(CASE WHEN R.STATUS = 'ASSIGNED' AND NVL(L.CONFIRM_STATUS,'ASSIGNED') NOT IN ('CONFIRMED','WORKER_REJECTED') THEN 1 ELSE 0 END) ASSIGNED_PENDING,
                       SUM(CASE WHEN L.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) ASSIGNED_CONFIRMED
                {fromSql}{whereSqlNoStatus}";

            var summaryParams = baseParams
                .Where(p => p.ParameterName != "ST_FLAG" && p.ParameterName != "ST_VAL")
                .Select(p => (OracleParameter)p.Clone()).ToArray();

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL              = r["TOTAL"]              == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING            = r["PENDING"]            == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED           = r["APPROVED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED           = r["REJECTED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"]),
                ASSIGNED_PENDING   = r["ASSIGNED_PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_PENDING"]),
                ASSIGNED_CONFIRMED = r["ASSIGNED_CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_CONFIRMED"]),
            }, summaryParams);

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK,
                               RR.ROLE_NAME REQUESTER_ROLE,
                               R.CREATED_BY ASSIGNED_BY, ASN.CNAME ASSIGNER_NAME,
                               L.DOC_STATUS, L.DOC_SUBMITTED_DATE, L.DOC_SUBMITTED_BY, L.DOC_REMARK
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveListModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()   ?? "",
                EMPCD          = r["EMPCD"]?.ToString()         ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_ID        = r["DEPT_ID"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_ID        = r["LINE_ID"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_ID        = r["WORK_ID"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                APPROVER_NAME  = r["APPROVER_NAME"]?.ToString(),
                FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK         = r["REMARK"]?.ToString(),
                REQUESTER_ROLE = r["REQUESTER_ROLE"]?.ToString(),
                ASSIGNED_BY    = r["ASSIGNED_BY"]?.ToString(),
                ASSIGNER_NAME  = r["ASSIGNER_NAME"]?.ToString(),
                DOC_STATUS         = r["DOC_STATUS"]?.ToString(),
                DOC_SUBMITTED_DATE = r["DOC_SUBMITTED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["DOC_SUBMITTED_DATE"]),
                DOC_SUBMITTED_BY   = r["DOC_SUBMITTED_BY"]?.ToString(),
                DOC_REMARK         = r["DOC_REMARK"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/clerk — Thư ký xem nghỉ phép theo scope HR_USERS_DEPT
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("clerk")]
    public async Task<IActionResult> GetClerkList(
        string  clerk_empcd,
        string? status     = null,
        string? source     = null,
        string? leave_type = null,
        string? search     = null,
        string? dept_id    = null,
        string? line_id    = null,
        string? work_id    = null,
        string? date_from  = null,
        string? date_to    = null,
        int     page       = 1,
        int     page_size  = 50)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(clerk_empcd))
                return BadRequest(new { success = false, message = "Thiếu mã thư ký" });

            var hasScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :CE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CE", clerk_empcd));

            if (!hasScope.Any() || hasScope[0] == 0)
                return Ok(new { success = false, message = "Thư ký chưa được phân bộ phận" });

            var scopeFilter = OTScopeFilterHelper.ForScopeByTuple(clerk_empcd, empAlias: "EC", prefix: "CK");

            int offset = (page - 1) * page_size;
            int maxRn  = offset + page_size;

            DateTime dfrom = (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var _df)) ? _df : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var _dt)) ? _dt : DateTime.Today.AddMonths(2);

            string fromSql = @"
                FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST  R  ON R.REQUEST_ID = L.REQUEST_ID
                JOIN HRMS.ECM100      EC ON EC.EMPCD     = L.EMPCD
                LEFT JOIN HRMS.EAM410    B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                LEFT JOIN HRMS.ECM100    AP ON AP.EMPCD  = R.FINAL_APPROVER
                LEFT JOIN HRMS.HR_USERS  UR ON UR.EMPCD  = L.EMPCD
                LEFT JOIN HRMS.HR_ROLES  RR ON RR.ID     = UR.ROLE_ID";

            string whereSql = $@"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:ST_FLAG   IS NULL OR R.STATUS       = :ST_VAL)
                  AND (:SRC_FLAG  IS NULL OR L.SOURCE       = :SRC_VAL)
                  AND (:LT_FLAG   IS NULL OR L.LEAVE_TYPE    = :LT_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)
                  {scopeFilter.SqlClause}";

            // Summary (4 thẻ tổng hợp) phải luôn tính trên toàn bộ status, không bị bó hẹp theo status đang filter
            string whereSqlNoStatus = $@"
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
                  AND L.FROM_DATE BETWEEN :D_FROM AND :D_TO
                  AND (:SRC_FLAG  IS NULL OR L.SOURCE       = :SRC_VAL)
                  AND (:LT_FLAG   IS NULL OR L.LEAVE_TYPE    = :LT_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:DPT_FLAG  IS NULL OR EC.DEPTCD      = :DPT_VAL)
                  AND (:LN_FLAG   IS NULL OR EC.LINECD       = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD       = :WK_VAL)
                  {scopeFilter.SqlClause}";

            var baseParams = new List<OracleParameter>
            {
                new OracleParameter("D_FROM",    OracleDbType.Date)     { Value = dfrom },
                new OracleParameter("D_TO",      OracleDbType.Date)     { Value = dto },
                new OracleParameter("ST_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("ST_VAL",    OracleDbType.Varchar2) { Value = (object?)status  ?? DBNull.Value },
                new OracleParameter("SRC_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(source)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRC_VAL",   OracleDbType.Varchar2) { Value = (object?)source  ?? DBNull.Value },
                new OracleParameter("LT_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(leave_type) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LT_VAL",    OracleDbType.Varchar2) { Value = (object?)leave_type ?? DBNull.Value },
                new OracleParameter("SRCH_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "Y") ?? DBNull.Value },
                new OracleParameter("SRCH_VAL",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search)  ? null : "%" + search.ToUpper() + "%") ?? DBNull.Value },
                new OracleParameter("DPT_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(dept_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("DPT_VAL",   OracleDbType.Varchar2) { Value = (object?)dept_id ?? DBNull.Value },
                new OracleParameter("LN_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(line_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("LN_VAL",    OracleDbType.Varchar2) { Value = (object?)line_id ?? DBNull.Value },
                new OracleParameter("WK_FLAG",   OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(work_id) ? null : "Y") ?? DBNull.Value },
                new OracleParameter("WK_VAL",    OracleDbType.Varchar2) { Value = (object?)work_id ?? DBNull.Value },
            };
            baseParams.AddRange(scopeFilter.Params);

            string sqlSummary = $@"
                SELECT COUNT(*) TOTAL,
                       SUM(CASE WHEN R.STATUS = 'PENDING'  THEN 1 ELSE 0 END) PENDING,
                       SUM(CASE WHEN R.STATUS = 'APPROVED' THEN 1 ELSE 0 END) APPROVED,
                       SUM(CASE WHEN R.STATUS = 'REJECTED' THEN 1 ELSE 0 END) REJECTED,
                       SUM(CASE WHEN R.STATUS = 'ASSIGNED' AND NVL(L.CONFIRM_STATUS,'ASSIGNED') NOT IN ('CONFIRMED','WORKER_REJECTED') THEN 1 ELSE 0 END) ASSIGNED_PENDING,
                       SUM(CASE WHEN L.CONFIRM_STATUS = 'CONFIRMED' THEN 1 ELSE 0 END) ASSIGNED_CONFIRMED
                {fromSql}{whereSqlNoStatus}";

            var summaryParams = baseParams
                .Where(p => p.ParameterName != "ST_FLAG" && p.ParameterName != "ST_VAL")
                .Select(p => (OracleParameter)p.Clone()).ToArray();

            var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new LeaveSummary
            {
                TOTAL              = r["TOTAL"]              == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
                PENDING            = r["PENDING"]            == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING"]),
                APPROVED           = r["APPROVED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["APPROVED"]),
                REJECTED           = r["REJECTED"]           == DBNull.Value ? 0 : Convert.ToInt32(r["REJECTED"]),
                ASSIGNED_PENDING   = r["ASSIGNED_PENDING"]   == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_PENDING"]),
                ASSIGNED_CONFIRMED = r["ASSIGNED_CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["ASSIGNED_CONFIRMED"]),
            }, summaryParams);

            var summary = summaryRows.FirstOrDefault() ?? new LeaveSummary();

            if (summary.TOTAL == 0)
                return Ok(new { success = true, summary, total = 0, page, page_size, total_pages = 0, data = new List<LeaveListModel>() });

            string sqlData = $@"
                SELECT /*+ FIRST_ROWS({page_size}) */ * FROM (
                    SELECT T.*, ROW_NUMBER() OVER (ORDER BY T.STATUS,
                                                             CASE WHEN T.REQUESTER_ROLE = 'Expat' THEN 1 WHEN T.REQUESTER_ROLE = 'Manager' THEN 2 WHEN T.REQUESTER_ROLE = 'DeputyManager' THEN 3 WHEN T.REQUESTER_ROLE = 'Supervisor' THEN 4 WHEN T.REQUESTER_ROLE = 'HR' THEN 5 WHEN T.REQUESTER_ROLE = 'Clerk' THEN 6 WHEN T.REQUESTER_ROLE = 'Employee' THEN 7 ELSE 8 END,
                                                             T.FROM_DATE DESC) RN
                    FROM (
                        SELECT L.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                               EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                               EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                               EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                               L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                               R.STATUS, L.CONFIRM_STATUS, L.CREATED_DATE,
                               R.FINAL_APPROVER, AP.CNAME APPROVER_NAME, R.FINAL_DATE, R.REMARK,
                               RR.ROLE_NAME REQUESTER_ROLE,
                               L.DOC_STATUS, L.DOC_SUBMITTED_DATE, L.DOC_SUBMITTED_BY, L.DOC_REMARK
                        {fromSql}{whereSql}
                    ) T
                ) WHERE RN > :R_MIN AND RN <= :R_MAX";

            var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("R_MIN", offset));
            dataParams.Add(new OracleParameter("R_MAX", maxRn));

            var list = await _oracleService.ExecuteQueryAsync(sqlData, r => new LeaveListModel
            {
                REQUEST_ID     = r["REQUEST_ID"]?.ToString()   ?? "",
                EMPCD          = r["EMPCD"]?.ToString()         ?? "",
                EMP_NAME       = r["EMP_NAME"]?.ToString(),
                DEPT_ID        = r["DEPT_ID"]?.ToString(),
                DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                LINE_ID        = r["LINE_ID"]?.ToString(),
                LINE_NAME      = r["LINE_NAME"]?.ToString(),
                WORK_ID        = r["WORK_ID"]?.ToString(),
                WORK_NAME      = r["WORK_NAME"]?.ToString(),
                LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                SOURCE         = r["SOURCE"]?.ToString(),
                FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? null : Convert.ToDateTime(r["FROM_DATE"]),
                TO_DATE        = r["TO_DATE"]      == DBNull.Value ? null : Convert.ToDateTime(r["TO_DATE"]),
                TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? null : Convert.ToDecimal(r["TOTAL_DAYS"]),
                REASON         = r["REASON"]?.ToString(),
                STATUS         = r["STATUS"]?.ToString(),
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CREATED_DATE"]),
                FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                APPROVER_NAME  = r["APPROVER_NAME"]?.ToString(),
                FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? null : Convert.ToDateTime(r["FINAL_DATE"]),
                REMARK         = r["REMARK"]?.ToString(),
                REQUESTER_ROLE = r["REQUESTER_ROLE"]?.ToString(),
                DOC_STATUS         = r["DOC_STATUS"]?.ToString(),
                DOC_SUBMITTED_DATE = r["DOC_SUBMITTED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["DOC_SUBMITTED_DATE"]),
                DOC_SUBMITTED_BY   = r["DOC_SUBMITTED_BY"]?.ToString(),
                DOC_REMARK         = r["DOC_REMARK"]?.ToString()
            }, dataParams.ToArray());

            return Ok(new
            {
                success     = true,
                summary,
                total       = summary.TOTAL,
                page,
                page_size,
                total_pages = page_size > 0 ? (int)Math.Ceiling((double)summary.TOTAL / page_size) : 0,
                data        = list
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/doc-submitted — HR/Clerk/Admin đánh dấu đã nhận giấy tờ
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("doc-submitted")]
    public async Task<IActionResult> DocSubmitted([FromBody] LeaveDocStatusRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.ACTOR_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin" });

            var roleRows = await _oracleService.ExecuteQueryAsync(
                "SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ACTOR_EMPCD));
            string? role = roleRows.FirstOrDefault();
            if (role != "HR" && role != "Clerk" && role != "Admin")
                return Ok(new { success = false, message = "Bạn không có quyền cập nhật giấy tờ" });

            var statusRows = await _oracleService.ExecuteQueryAsync(
                "SELECT R.STATUS FROM HRMS.HR_REQUEST R WHERE R.REQUEST_ID = :ID AND ROWNUM = 1",
                r => r["STATUS"]?.ToString() ?? "",
                new OracleParameter("ID", model.REQUEST_ID));
            string? curStatus = statusRows.FirstOrDefault();
            if (curStatus == null) return Ok(new { success = false, message = "Không tìm thấy đơn nghỉ phép" });
            // Chỉ được cập nhật giấy tờ khi đơn đã ở trạng thái "Đã duyệt".
            if (!DocEditableStatuses.Contains(curStatus))
                return Ok(new { success = false, message = "Chỉ được cập nhật giấy tờ khi đơn đã Đã duyệt hoặc đã Sắp lịch nghỉ" });

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET DOC_STATUS = 'SUBMITTED', DOC_SUBMITTED_DATE = SYSDATE, DOC_SUBMITTED_BY = :ACTOR,
                    UPDATED_BY = :ACTOR1, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("ACTOR",      model.ACTOR_EMPCD),
                new OracleParameter("ACTOR1",     model.ACTOR_EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy đơn nghỉ phép" });

            return Ok(new { success = true, message = "Đã cập nhật: đã nộp giấy tờ" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/doc-resubmit-request — HR/Clerk/Admin yêu cầu NV nộp lại giấy tờ
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("doc-resubmit-request")]
    public async Task<IActionResult> DocResubmitRequest([FromBody] LeaveDocStatusRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.ACTOR_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin" });

            var roleRows = await _oracleService.ExecuteQueryAsync(
                "SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ACTOR_EMPCD));
            string? role = roleRows.FirstOrDefault();
            if (role != "HR" && role != "Clerk" && role != "Admin")
                return Ok(new { success = false, message = "Bạn không có quyền yêu cầu nộp lại" });

            var infoRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, L.LEAVE_TYPE, L.FROM_DATE, L.TO_DATE, R.STATUS
                FROM HRMS.HR_LEAVE_REQUEST L JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.REQUEST_ID = :REQUEST_ID AND ROWNUM = 1",
                r => new {
                    Empcd     = r["EMPCD"]?.ToString(),
                    LeaveType = r["LEAVE_TYPE"]?.ToString(),
                    FromDate  = Convert.ToDateTime(r["FROM_DATE"]),
                    ToDate    = Convert.ToDateTime(r["TO_DATE"]),
                    Status    = r["STATUS"]?.ToString() ?? ""
                },
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            var info = infoRows.FirstOrDefault();
            if (info == null)
                return Ok(new { success = false, message = "Không tìm thấy đơn nghỉ phép" });
            // Chỉ được cập nhật giấy tờ khi đơn đã Đã duyệt hoặc đã Sắp lịch nghỉ (quản lý/Admin
            // sắp lịch tính như đã chốt, dù STATUS mãi mãi là ASSIGNED chứ không qua APPROVED).
            if (!DocEditableStatuses.Contains(info.Status))
                return Ok(new { success = false, message = "Chỉ được cập nhật giấy tờ khi đơn đã Đã duyệt hoặc đã Sắp lịch nghỉ" });

            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET DOC_STATUS = 'RESUBMIT_REQUESTED', DOC_REMARK = :REMARK,
                    UPDATED_BY = :ACTOR, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :REQUEST_ID",
                new OracleParameter("REMARK",     (object?)model.REMARK ?? DBNull.Value),
                new OracleParameter("ACTOR",      model.ACTOR_EMPCD),
                new OracleParameter("REQUEST_ID", model.REQUEST_ID));

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy đơn nghỉ phép" });

            if (!string.IsNullOrEmpty(info.Empcd))
                _notiSvc.LeaveDocResubmitRequested(info.Empcd, model.ACTOR_EMPCD, info.LeaveType ?? "", info.FromDate, info.ToDate, model.REMARK);

            return Ok(new { success = true, message = "Đã gửi yêu cầu nộp lại giấy tờ" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/doc-day-list?requestId= — Danh sách từng ngày trong đơn + trạng thái đã
    // xác nhận nộp giấy hay chưa (để HR tick chọn ngày nào có giấy khi xác nhận).
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("doc-day-list")]
    public async Task<IActionResult> GetDocDayList(string requestId)
    {
        try
        {
            var reqRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, L.FROM_DATE, L.TO_DATE FROM HRMS.HR_LEAVE_REQUEST L
                WHERE L.REQUEST_ID = :ID AND ROWNUM = 1",
                r => new {
                    Empcd    = r["EMPCD"]?.ToString() ?? "",
                    FromDate = Convert.ToDateTime(r["FROM_DATE"]),
                    ToDate   = Convert.ToDateTime(r["TO_DATE"])
                },
                new OracleParameter("ID", requestId));

            var info = reqRows.FirstOrDefault();
            if (info == null) return Ok(new { success = false, message = "Không tìm thấy đơn nghỉ phép" });

            var confirmedRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT TO_CHAR(LEAVE_DATE,'YYYY-MM-DD') D, LEAVECD, REMARK, CONFIRMED_BY, CONFIRMED_DATE
                FROM HRMS.HR_LEAVE_DOC_DAY WHERE REQUEST_ID = :ID",
                r => new {
                    Date          = r["D"]?.ToString() ?? "",
                    Leavecd       = r["LEAVECD"]?.ToString(),
                    Remark        = r["REMARK"]?.ToString(),
                    ConfirmedBy   = r["CONFIRMED_BY"]?.ToString(),
                    ConfirmedDate = r["CONFIRMED_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CONFIRMED_DATE"])
                },
                new OracleParameter("ID", requestId));
            var byDate = confirmedRows.ToDictionary(x => x.Date);

            var days = new List<object>();
            for (var d = info.FromDate.Date; d <= info.ToDate.Date; d = d.AddDays(1))
            {
                var key = d.ToString("yyyy-MM-dd");
                if (byDate.TryGetValue(key, out var c))
                    days.Add(new { date = key, confirmed = true, leavecd = c.Leavecd, remark = c.Remark, confirmedBy = c.ConfirmedBy, confirmedDate = c.ConfirmedDate });
                else
                    days.Add(new { date = key, confirmed = false, leavecd = (string?)null, remark = (string?)null, confirmedBy = (string?)null, confirmedDate = (DateTime?)null });
            }

            return Ok(new { success = true, empcd = info.Empcd, days });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/doc-confirm-days — HR xác nhận nộp giấy theo từng ngày, tự gõ Absent
    // Code/remark (không đoán tự động) — app ghi thẳng sang ERP (EFM410) cho đúng những ngày đó,
    // đồng thời lưu lịch sử vào HR_LEAVE_DOC_DAY để tính đúng trạng thái nộp thiếu/đủ ngày.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("doc-confirm-days")]
    public async Task<IActionResult> DocConfirmDays([FromBody] LeaveDocConfirmDaysRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.REQUEST_ID) || string.IsNullOrEmpty(model.ACTOR_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin" });
            if (model.DAYS == null || model.DAYS.Count == 0)
                return Ok(new { success = false, message = "Chưa chọn ngày nào" });

            var roleRows = await _oracleService.ExecuteQueryAsync(
                "SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ACTOR_EMPCD));
            string? role = roleRows.FirstOrDefault();
            if (role != "HR" && role != "Clerk" && role != "Admin")
                return Ok(new { success = false, message = "Bạn không có quyền cập nhật giấy tờ" });

            var reqRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.EMPCD, L.FROM_DATE, L.TO_DATE, R.STATUS
                FROM HRMS.HR_LEAVE_REQUEST L JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.REQUEST_ID = :ID AND ROWNUM = 1",
                r => new {
                    Empcd    = r["EMPCD"]?.ToString() ?? "",
                    FromDate = Convert.ToDateTime(r["FROM_DATE"]),
                    ToDate   = Convert.ToDateTime(r["TO_DATE"]),
                    Status   = r["STATUS"]?.ToString() ?? ""
                },
                new OracleParameter("ID", model.REQUEST_ID));

            var info = reqRows.FirstOrDefault();
            if (info == null) return Ok(new { success = false, message = "Không tìm thấy đơn nghỉ phép" });
            // Chỉ được cập nhật giấy tờ khi đơn đã Đã duyệt hoặc đã Sắp lịch nghỉ — các trạng thái
            // khác (chờ duyệt, từ chối...) chưa chốt nên chưa cho ghi ERP.
            if (!DocEditableStatuses.Contains(info.Status))
                return Ok(new { success = false, message = "Chỉ được cập nhật giấy tờ khi đơn đã Đã duyệt hoặc đã Sắp lịch nghỉ" });

            int totalDays = (info.ToDate.Date - info.FromDate.Date).Days + 1;

            foreach (var day in model.DAYS)
            {
                if (!DateTime.TryParse(day.DATE, out var d)) continue;
                if (d.Date < info.FromDate.Date || d.Date > info.ToDate.Date) continue; // chặn ghi ngày ngoài khoảng đơn

                // Mã là TÙY CHỌN — để trống nghĩa là giữ nguyên mã cũ trên ERP, chỉ đổi Remark (VD
                // case SI: mã vẫn "CP" như cũ, chỉ cần thêm "BHXH" vào remark, không cần gõ lại mã).
                var leavecdInput = string.IsNullOrWhiteSpace(day.LEAVECD) ? null : day.LEAVECD.Trim().ToUpperInvariant();
                var remark       = string.IsNullOrWhiteSpace(day.REMARK)  ? null : day.REMARK!.Trim();
                if (leavecdInput == null && remark == null) continue; // không nhập gì thì bỏ qua ngày này

                // Ghi thẳng sang ERP trước — UPDATE nếu ngày đó đã có dòng EFM410 (thường có sẵn, do
                // lúc duyệt/sắp lịch đã insert qua SP_015_NEW). NVL giữ nguyên cột nào HR để trống.
                int updated = await _oracleService.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.EFM410
                    SET LEAVECD = NVL(:LEAVECD, LEAVECD), REMAR = NVL(:REMARK, REMAR), APPROVED_BY = :ACTOR
                    WHERE EMPCD = :EMPCD AND FR_DAT = :LEAVE_DATE",
                    new OracleParameter("LEAVECD",    (object?)leavecdInput ?? DBNull.Value),
                    new OracleParameter("REMARK",     (object?)remark ?? DBNull.Value),
                    new OracleParameter("ACTOR",      model.ACTOR_EMPCD),
                    new OracleParameter("EMPCD",      info.Empcd),
                    new OracleParameter("LEAVE_DATE", OracleDbType.Date) { Value = d.Date });

                if (updated == 0)
                {
                    // Chưa có dòng ERP cho ngày này — bắt buộc phải có Mã mới tạo được (cột NOT NULL),
                    // để trống thì không đủ thông tin để insert, đành bỏ qua ngày này.
                    if (leavecdInput == null) continue;
                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.EFM410 (EMPCD, LEAVECD, FR_DAT, IN_ID, REMAR, APPROVED_BY)
                        VALUES (:EMPCD, :LEAVECD, :LEAVE_DATE, :ACTOR, :REMARK, :ACTOR)",
                        new OracleParameter("EMPCD",      info.Empcd),
                        new OracleParameter("LEAVECD",    leavecdInput),
                        new OracleParameter("LEAVE_DATE", OracleDbType.Date) { Value = d.Date },
                        new OracleParameter("ACTOR",      model.ACTOR_EMPCD),
                        new OracleParameter("REMARK",     (object?)remark ?? DBNull.Value));
                }

                // Đọc lại mã/remark THẬT SỰ đang có trên ERP sau khi ghi — để lưu đúng vào lịch sử
                // xác nhận, kể cả khi HR để trống Mã (giữ nguyên mã cũ).
                var efmNow = await _oracleService.ExecuteQueryAsync(
                    "SELECT LEAVECD, REMAR FROM HRMS.EFM410 WHERE EMPCD = :EMPCD AND FR_DAT = :LEAVE_DATE",
                    r => new { Leavecd = r["LEAVECD"]?.ToString() ?? "", Remark = r["REMAR"]?.ToString() },
                    new OracleParameter("EMPCD",      info.Empcd),
                    new OracleParameter("LEAVE_DATE", OracleDbType.Date) { Value = d.Date });
                var final = efmNow.FirstOrDefault();
                if (final == null) continue;

                // Lưu lịch sử xác nhận trên MySamho (ai xác nhận, ngày nào, ghi gì) — nguồn xác định
                // trạng thái "đã nộp", không dựa vào việc đoán nội dung EFM410 như job đối chiếu cũ.
                await _oracleService.ExecuteNonQueryAsync(@"
                    MERGE INTO HRMS.HR_LEAVE_DOC_DAY T
                    USING (SELECT :REQUEST_ID AS REQUEST_ID, :LEAVE_DATE AS LEAVE_DATE FROM DUAL) S
                    ON (T.REQUEST_ID = S.REQUEST_ID AND T.LEAVE_DATE = S.LEAVE_DATE)
                    WHEN MATCHED THEN UPDATE SET
                        T.LEAVECD = :LEAVECD, T.REMARK = :REMARK,
                        T.CONFIRMED_BY = :ACTOR, T.CONFIRMED_DATE = SYSDATE
                    WHEN NOT MATCHED THEN INSERT
                        (REQUEST_ID, LEAVE_DATE, EMPCD, LEAVECD, REMARK, CONFIRMED_BY, CONFIRMED_DATE)
                    VALUES
                        (:REQUEST_ID, :LEAVE_DATE, :EMPCD, :LEAVECD, :REMARK, :ACTOR, SYSDATE)",
                    new OracleParameter("REQUEST_ID", model.REQUEST_ID),
                    new OracleParameter("LEAVE_DATE", OracleDbType.Date) { Value = d.Date },
                    new OracleParameter("EMPCD",      info.Empcd),
                    new OracleParameter("LEAVECD",    final.Leavecd),
                    new OracleParameter("REMARK",     (object?)final.Remark ?? DBNull.Value),
                    new OracleParameter("ACTOR",      model.ACTOR_EMPCD));
            }

            // Đủ hết số ngày trong đơn -> SUBMITTED; còn thiếu vài ngày -> PARTIALLY_SUBMITTED.
            var confirmedCountRows = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_LEAVE_DOC_DAY WHERE REQUEST_ID = :ID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("ID", model.REQUEST_ID));
            int confirmedCount = confirmedCountRows.FirstOrDefault();
            string newStatus = confirmedCount >= totalDays ? "SUBMITTED" : "PARTIALLY_SUBMITTED";

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_LEAVE_REQUEST
                SET DOC_STATUS = :STATUS, DOC_SUBMITTED_DATE = SYSDATE, DOC_SUBMITTED_BY = :ACTOR,
                    UPDATED_BY = :ACTOR2, UPDATED_DATE = SYSDATE
                WHERE REQUEST_ID = :ID",
                new OracleParameter("STATUS", newStatus),
                new OracleParameter("ACTOR",  model.ACTOR_EMPCD),
                new OracleParameter("ACTOR2", model.ACTOR_EMPCD),
                new OracleParameter("ID",     model.REQUEST_ID));

            return Ok(new {
                success = true,
                message = $"Đã ghi nhận {model.DAYS.Count} ngày — tổng {confirmedCount}/{totalDays} ngày đã nộp giấy",
                docStatus = newStatus, confirmedCount, totalDays
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HR đổi ý (2026-09-10): bỏ kiểu gõ tay trong đơn nghỉ MySamho, chuyển sang sửa trực tiếp trên
    // 1 trang riêng y chang màn ERP thật (theo đúng SQL HR đưa) — CHỈ sửa được Leavecd + Remark,
    // các cột còn lại (NV, ngày, phòng ban...) chỉ xem. Sửa xong tự map ngược lại đơn nghỉ MySamho
    // tương ứng (nếu ngày đó thuộc 1 đơn Đã duyệt cần giấy tờ) để cập nhật DOC_STATUS luôn — gộp
    // 2 bước (sửa ERP + đánh dấu đã nộp bên MySamho) thành 1 cho gọn.
    //
    // GET /apiHR/Leave/erp-absent-list — danh sách EFM410 trong khoảng ngày, y chang màn ERP.
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("erp-absent-list")]
    public async Task<IActionResult> GetErpAbsentList(
        string date_from, string date_to, string? empcd = null, string? deptcd = null,
        string? linecd = null, string? workcd = null, string? leavecd = null,
        string? ms_status = null, string? ms_leave_type = null, int page = 1, int page_size = 100)
    {
        try
        {
            if (string.IsNullOrEmpty(date_from) || string.IsNullOrEmpty(date_to))
                return Ok(new { success = false, message = "Thiếu khoảng ngày" });
            if (!DateTime.TryParse(date_from, out var frDate) || !DateTime.TryParse(date_to, out var toDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            var empcdP  = string.IsNullOrWhiteSpace(empcd)  ? "%" : $"%{empcd.Trim()}%";
            var deptcdP = string.IsNullOrWhiteSpace(deptcd) ? "%" : deptcd.Trim();
            var linecdP = string.IsNullOrWhiteSpace(linecd) ? "%" : linecd.Trim();
            var workcdP = string.IsNullOrWhiteSpace(workcd) ? "%" : workcd.Trim();
            var leavecdP= string.IsNullOrWhiteSpace(leavecd)? "%" : leavecd.Trim();
            var msLeaveTypeP = string.IsNullOrWhiteSpace(ms_leave_type) ? null : ms_leave_type.Trim().ToUpperInvariant();

            // Lọc theo đơn MySamho tương ứng đã duyệt hay chưa — dùng EXISTS/NOT EXISTS riêng (không
            // parse lại MS_COMBINED) cho gọn. "Đã duyệt" tính luôn Sắp lịch nghỉ (ASSIGNED) — đồng
            // nhất với rule "quản lý/Admin sắp lịch tính như đã duyệt" áp dụng toàn hệ thống.
            string msStatusFilter = ms_status switch
            {
                "APPROVED" => @"
                  AND EXISTS (SELECT 1 FROM HRMS.HR_LEAVE_REQUEST ML JOIN HRMS.HR_REQUEST MR ON MR.REQUEST_ID = ML.REQUEST_ID
                              WHERE ML.EMPCD = A.EMPCD AND A.FR_DAT BETWEEN ML.FROM_DATE AND ML.TO_DATE
                                AND MR.STATUS IN ('APPROVED','ASSIGNED'))",
                // 1 nhân viên/ngày hiếm khi có > 1 đơn MySamho trùng nhau, nhưng nếu có (VD bị từ
                // chối rồi nộp lại) thì msSub bên dưới LUÔN ưu tiên hiện đơn Đã duyệt/Sắp lịch nếu
                // có — nên "chưa duyệt" phải là "có đơn NHƯNG không có đơn nào Đã duyệt/Sắp lịch cả",
                // chứ không phải chỉ "có 1 đơn chưa duyệt" (dễ sai khi có nhiều đơn trùng ngày).
                "NOT_APPROVED" => @"
                  AND EXISTS (SELECT 1 FROM HRMS.HR_LEAVE_REQUEST ML JOIN HRMS.HR_REQUEST MR ON MR.REQUEST_ID = ML.REQUEST_ID
                              WHERE ML.EMPCD = A.EMPCD AND A.FR_DAT BETWEEN ML.FROM_DATE AND ML.TO_DATE)
                  AND NOT EXISTS (SELECT 1 FROM HRMS.HR_LEAVE_REQUEST ML2 JOIN HRMS.HR_REQUEST MR2 ON MR2.REQUEST_ID = ML2.REQUEST_ID
                              WHERE ML2.EMPCD = A.EMPCD AND A.FR_DAT BETWEEN ML2.FROM_DATE AND ML2.TO_DATE
                                AND MR2.STATUS IN ('APPROVED','ASSIGNED'))",
                _ => ""
            };
            // Lọc theo loại nghỉ trên MySamho (AL/NL/SI/DT/DC/CT/VS/DS/KT) — khác với filter "Mã
            // nghỉ" (lọc theo Absent Code bên ERP), cái này lọc theo LEAVE_TYPE của đơn MySamho map
            // sang ngày đó (yêu cầu 2026-09-10).
            string msLeaveTypeFilter = msLeaveTypeP == null ? "" : @"
                  AND EXISTS (SELECT 1 FROM HRMS.HR_LEAVE_REQUEST ML4 JOIN HRMS.HR_REQUEST MR4 ON MR4.REQUEST_ID = ML4.REQUEST_ID
                              WHERE ML4.EMPCD = A.EMPCD AND A.FR_DAT BETWEEN ML4.FROM_DATE AND ML4.TO_DATE
                                AND ML4.LEAVE_TYPE = :MS_LEAVE_TYPE)";

            // Tái sử dụng đúng điều kiện WHERE/JOIN theo SQL HR đưa cho cả 2 câu (đếm tổng + lấy trang).
            string baseFrom = $@"
                FROM HRMS.EFM410 A
                INNER JOIN HRMS.ECM100 B ON A.EMPCD = B.EMPCD
                INNER JOIN HRMS.EAM410 C ON B.DEPTCD = C.DEPTCD AND B.LINECD = C.LINECD AND B.WORKCD = C.WORKCD
                INNER JOIN HRMS.EAM700 D ON A.LEAVECD = D.LEAVECD
                LEFT JOIN  HRMS.ECM100 APV ON APV.EMPCD = A.APPROVED_BY
                WHERE A.FR_DAT BETWEEN :FR_DATE AND :TO_DATE
                  AND A.EMPCD  LIKE :EMPCD
                  AND B.DEPTCD LIKE :DEPTCD
                  AND B.LINECD LIKE :LINECD
                  AND B.WORKCD LIKE :WORKCD
                  AND A.LEAVECD LIKE :LEAVECD
                  AND NOT EXISTS (SELECT 'X' FROM HRMS.EFM410_WAIT W
                                  WHERE W.EMPCD = A.EMPCD AND W.FR_DAT = A.FR_DAT AND W.FLAG_APPROVE = 'N')
                  {msStatusFilter}
                  {msLeaveTypeFilter}";

            // Local function tạo OracleParameter MỚI mỗi lần gọi (không tái dùng lại instance cũ giữa
            // 2 lệnh COUNT/DATA) — thêm MS_LEAVE_TYPE vào cuối danh sách chỉ khi có lọc theo loại.
            OracleParameter[] MakeParams()
            {
                var list = new List<OracleParameter> {
                    new OracleParameter("FR_DATE", OracleDbType.Date) { Value = frDate.Date },
                    new OracleParameter("TO_DATE", OracleDbType.Date) { Value = toDate.Date },
                    new OracleParameter("EMPCD",   empcdP),
                    new OracleParameter("DEPTCD",  deptcdP),
                    new OracleParameter("LINECD",  linecdP),
                    new OracleParameter("WORKCD",  workcdP),
                    new OracleParameter("LEAVECD", leavecdP)
                };
                if (msLeaveTypeP != null) list.Add(new OracleParameter("MS_LEAVE_TYPE", msLeaveTypeP));
                return list.ToArray();
            }

            var countRows = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT " + baseFrom,
                r => Convert.ToInt32(r["CNT"]),
                MakeParams());
            int total = countRows.FirstOrDefault();

            int safePage = Math.Max(1, page);
            int safeSize = Math.Max(1, Math.Min(page_size, 5000));
            int minRow = (safePage - 1) * safeSize;
            int maxRow = safePage * safeSize;

            // Gộp 5 giá trị MySamho vào 1 subquery correlated DUY NHẤT (nối chuỗi bằng CHR(1), tách
            // lại bên C#) thay vì chạy 5 subquery giống hệt nhau cho mỗi dòng — trước đây mỗi dòng
            // EFM410 phải quét HR_LEAVE_REQUEST tới 5 lần, giờ chỉ 1 lần (đỡ ~80% chi phí phần này).
            // Chỉ lấy 1 đơn đại diện (ROWNUM=1) đủ để hiện gợi ý cho HR, tránh nhân dòng nếu trùng ngày.
            // KHÔNG lọc theo LEAVE_TYPE ở đây (khác với ErpAbsentUpdate) — cột MySamho phải hiện MỌI
            // đơn có trên MySamho (kể cả Phép năm/AL...), không riêng 6 loại cần giấy tờ. Trước đây
            // lọc cứng IN ('SI','DT','DC','VS','DS','KT') khiến đơn AL/CT... bị báo nhầm "Chưa có
            // trên MySamho" dù NV đã nộp đơn thật (HR phát hiện 2026-09-10, VD NV 14123005).
            // Nếu 1 NV/ngày trùng > 1 đơn (VD bị từ chối rồi nộp lại) — ưu tiên hiện đơn Đã duyệt/Sắp
            // lịch trước (khớp với msStatusFilter phía trên: "chưa duyệt" nghĩa là hoàn toàn không có
            // đơn nào Đã duyệt/Sắp lịch, không phải chỉ tình cờ ROWNUM=1 vớ trúng đơn chưa duyệt).
            // MAX(...) KEEP (DENSE_RANK FIRST ORDER BY ...) = lấy đúng 1 giá trị từ dòng "hạng nhất"
            // theo thứ tự chỉ định, coi cả kết quả WHERE là 1 nhóm (không cần GROUP BY) — tránh phải
            // lồng thêm 1 lớp subquery/ROWNUM (Oracle 10g+, không phải LISTAGG nên vẫn hợp lệ).
            const string msSub = @"
                (SELECT MAX(ML.REQUEST_ID || CHR(1) || ML.LEAVE_TYPE || CHR(1) || MR.STATUS || CHR(1)
                             || NVL(ML.DOC_STATUS,'') || CHR(1) || NVL(ML.REASON,''))
                        KEEP (DENSE_RANK FIRST ORDER BY CASE WHEN MR.STATUS IN ('APPROVED','ASSIGNED') THEN 0 ELSE 1 END)
                 FROM HRMS.HR_LEAVE_REQUEST ML JOIN HRMS.HR_REQUEST MR ON MR.REQUEST_ID = ML.REQUEST_ID
                 WHERE ML.EMPCD = A.EMPCD AND A.FR_DAT BETWEEN ML.FROM_DATE AND ML.TO_DATE)";

            var dataSql = $@"
                SELECT * FROM (
                    SELECT ROWNUM RN, T.* FROM (
                        SELECT A.EMPCD, B.CNAME EMP_NAME, TO_CHAR(A.FR_DAT,'YYYY-MM-DD') FR_DATE,
                               A.LEAVECD, D.LEAVENM, A.REMAR, C.DEPTNM, C.TEAMNM, C.WORKNM,
                               B.DEPTCD, B.LINECD, B.WORKCD, A.APPROVED_BY, APV.CNAME APPROVED_BY_NAME,
                               {msSub} MS_COMBINED
                        {baseFrom}
                        ORDER BY A.FR_DAT, A.EMPCD
                    ) T WHERE ROWNUM <= :MAX_ROW
                ) WHERE RN > :MIN_ROW";

            var listParams = MakeParams().ToList();
            listParams.Add(new OracleParameter("MAX_ROW", maxRow));
            listParams.Add(new OracleParameter("MIN_ROW", minRow));

            var rawRows = await _oracleService.ExecuteQueryAsync(
                dataSql,
                r => new
                {
                    Empcd        = r["EMPCD"]?.ToString() ?? "",
                    EmpName      = r["EMP_NAME"]?.ToString(),
                    FrDate       = r["FR_DATE"]?.ToString(),
                    Leavecd      = r["LEAVECD"]?.ToString(),
                    Leavenm      = r["LEAVENM"]?.ToString(),
                    Remark       = r["REMAR"] == DBNull.Value ? null : r["REMAR"]?.ToString(),
                    DeptNm       = r["DEPTNM"]?.ToString(),
                    TeamNm       = r["TEAMNM"]?.ToString(),
                    WorkNm       = r["WORKNM"]?.ToString(),
                    Deptcd       = r["DEPTCD"]?.ToString(),
                    Linecd       = r["LINECD"]?.ToString(),
                    Workcd       = r["WORKCD"]?.ToString(),
                    ApprovedBy     = r["APPROVED_BY"] == DBNull.Value ? null : r["APPROVED_BY"]?.ToString(),
                    ApprovedByName = r["APPROVED_BY_NAME"] == DBNull.Value ? null : r["APPROVED_BY_NAME"]?.ToString(),
                    MsCombined   = r["MS_COMBINED"] == DBNull.Value ? null : r["MS_COMBINED"]?.ToString()
                },
                listParams.ToArray());

            // Tách MS_COMBINED (REQUEST_ID\x01LEAVE_TYPE\x01STATUS\x01DOC_STATUS\x01REASON) —
            // NVL() ở DOC_STATUS/REASON trong SQL trả '' cho null nên map lại thành null ở đây.
            var rows = rawRows.Select(x =>
            {
                var p = x.MsCombined?.Split('\u0001') ?? Array.Empty<string>();
                string? Part(int i) => i < p.Length && p[i].Length > 0 ? p[i] : null;
                return new
                {
                    x.Empcd, x.EmpName, x.FrDate, x.Leavecd, x.Leavenm, x.Remark,
                    x.DeptNm, x.TeamNm, x.WorkNm, x.Deptcd, x.Linecd, x.Workcd,
                    x.ApprovedBy, x.ApprovedByName,
                    MsRequestId = Part(0), MsLeaveType = Part(1), MsStatus = Part(2),
                    MsDocStatus = Part(3), MsReason = Part(4)
                };
            });

            return Ok(new {
                success = true, total, page = safePage, page_size = safeSize,
                total_pages = (int)Math.Ceiling(total / (double)safeSize),
                data = rows
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/Leave/absent-code-list — danh sách mã Absent Code gốc bên ERP (EAM700), cho combobox.
    [HttpGet("absent-code-list")]
    public async Task<IActionResult> GetAbsentCodeList()
    {
        try
        {
            var rows = await _oracleService.ExecuteQueryAsync(
                "SELECT LEAVECD, LEAVENM, SA_PAY FROM HRMS.EAM700 ORDER BY LEAVECD",
                r => new {
                    Code = r["LEAVECD"]?.ToString() ?? "",
                    Name = r["LEAVENM"]?.ToString() ?? "",
                    Pay  = r["SA_PAY"]?.ToString() == "Y"
                });
            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Leave/erp-absent-update — sửa Leavecd/Remark 1 dòng EFM410 đã có sẵn (không tạo
    // mới), tự map ngược sang đơn nghỉ MySamho tương ứng (nếu có, đã Đã duyệt) để cập nhật DOC_STATUS.
    [HttpPost("erp-absent-update")]
    public async Task<IActionResult> ErpAbsentUpdate([FromBody] ErpAbsentUpdateRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.EMPCD) || string.IsNullOrEmpty(model.FR_DATE)
                || string.IsNullOrEmpty(model.LEAVECD) || string.IsNullOrEmpty(model.ACTOR_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin" });
            if (!DateTime.TryParse(model.FR_DATE, out var frDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            var roleRows = await _oracleService.ExecuteQueryAsync(
                "SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ACTOR_EMPCD));
            string? role = roleRows.FirstOrDefault();
            // Chỉ HR/Admin — Thư ký (Clerk) KHÔNG được sửa Absent Code ERP (yêu cầu 2026-09-10).
            if (role != "HR" && role != "Admin")
                return Ok(new { success = false, message = "Bạn không có quyền sửa" });

            var leavecd = model.LEAVECD.Trim().ToUpperInvariant();
            var remark  = string.IsNullOrWhiteSpace(model.REMARK) ? null : model.REMARK.Trim();

            // Trang này chỉ SỬA dòng ERP đã tồn tại sẵn (không insert mới) — đúng yêu cầu "chỉ sửa
            // code với remark thôi".
            int rows = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.EFM410
                SET LEAVECD = :LEAVECD, REMAR = :REMARK, APPROVED_BY = :ACTOR
                WHERE EMPCD = :EMPCD AND FR_DAT = :FR_DATE",
                new OracleParameter("LEAVECD", leavecd),
                new OracleParameter("REMARK",  (object?)remark ?? DBNull.Value),
                new OracleParameter("ACTOR",   model.ACTOR_EMPCD),
                new OracleParameter("EMPCD",   model.EMPCD),
                new OracleParameter("FR_DATE", OracleDbType.Date) { Value = frDate.Date });

            if (rows == 0)
                return Ok(new { success = false, message = "Không tìm thấy dòng ERP cho nhân viên/ngày này" });

            // Map ngược sang MySamho — chỉ áp dụng khi ngày này thuộc 1 đơn nghỉ đã Đã duyệt HOẶC
            // đã Sắp lịch nghỉ (quản lý/Admin sắp lịch tính như đã chốt), loại cần giấy tờ.
            var msRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT L.REQUEST_ID, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS FROM HRMS.HR_LEAVE_REQUEST L
                JOIN HRMS.HR_REQUEST R ON R.REQUEST_ID = L.REQUEST_ID
                WHERE L.EMPCD = :EMPCD AND :FR_DATE BETWEEN L.FROM_DATE AND L.TO_DATE
                  AND L.LEAVE_TYPE IN ('SI','DT','DC','VS','DS','KT') AND R.STATUS IN ('APPROVED','ASSIGNED')
                  AND ROWNUM = 1",
                r => new {
                    RequestId = r["REQUEST_ID"]?.ToString() ?? "",
                    FromDate  = Convert.ToDateTime(r["FROM_DATE"]),
                    ToDate    = Convert.ToDateTime(r["TO_DATE"]),
                    TotalDays = r["TOTAL_DAYS"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["TOTAL_DAYS"])
                },
                new OracleParameter("EMPCD",   model.EMPCD),
                new OracleParameter("FR_DATE", OracleDbType.Date) { Value = frDate.Date });

            var ms = msRows.FirstOrDefault();
            bool mapped = false;
            string? newStatus = null;
            if (ms != null)
            {
                await _oracleService.ExecuteNonQueryAsync(@"
                    MERGE INTO HRMS.HR_LEAVE_DOC_DAY T
                    USING (SELECT :REQUEST_ID AS REQUEST_ID, :LEAVE_DATE AS LEAVE_DATE FROM DUAL) S
                    ON (T.REQUEST_ID = S.REQUEST_ID AND T.LEAVE_DATE = S.LEAVE_DATE)
                    WHEN MATCHED THEN UPDATE SET
                        T.LEAVECD = :LEAVECD, T.REMARK = :REMARK, T.CONFIRMED_BY = :ACTOR, T.CONFIRMED_DATE = SYSDATE
                    WHEN NOT MATCHED THEN INSERT
                        (REQUEST_ID, LEAVE_DATE, EMPCD, LEAVECD, REMARK, CONFIRMED_BY, CONFIRMED_DATE)
                    VALUES
                        (:REQUEST_ID, :LEAVE_DATE, :EMPCD, :LEAVECD, :REMARK, :ACTOR, SYSDATE)",
                    new OracleParameter("REQUEST_ID", ms.RequestId),
                    new OracleParameter("LEAVE_DATE", OracleDbType.Date) { Value = frDate.Date },
                    new OracleParameter("LEAVECD",    leavecd),
                    new OracleParameter("REMARK",     (object?)remark ?? DBNull.Value),
                    new OracleParameter("ACTOR",      model.ACTOR_EMPCD),
                    new OracleParameter("EMPCD",      model.EMPCD));

                // Dùng TOTAL_DAYS đã lưu sẵn trên đơn (tính đúng loại trừ Chủ Nhật lúc tạo đơn) —
                // KHÔNG tự tính lại (ToDate-FromDate+1) theo lịch thô, vì công ty không làm Chủ Nhật
                // nên ERP không bao giờ tạo dòng EFM410 cho ngày CN (trừ NV whitelist CN/loại DS) —
                // tính theo lịch thô sẽ khiến DOC_STATUS kẹt mãi ở PARTIALLY_SUBMITTED, không bao giờ
                // lên SUBMITTED được, dù HR đã xác nhận đủ hết các ngày EFM410 thực có (bug thật phát
                // hiện 2026-09-12, rõ nhất với đơn Vợ sanh 7-12 ngày luôn dính ít nhất 1 Chủ Nhật).
                decimal totalDays = ms.TotalDays ?? ((ms.ToDate.Date - ms.FromDate.Date).Days + 1);
                var cntRows = await _oracleService.ExecuteQueryAsync(
                    "SELECT COUNT(*) CNT FROM HRMS.HR_LEAVE_DOC_DAY WHERE REQUEST_ID = :ID",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("ID", ms.RequestId));
                int confirmedCount = cntRows.FirstOrDefault();
                newStatus = confirmedCount >= totalDays ? "SUBMITTED" : "PARTIALLY_SUBMITTED";

                await _oracleService.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_LEAVE_REQUEST
                    SET DOC_STATUS = :STATUS, DOC_SUBMITTED_DATE = SYSDATE, DOC_SUBMITTED_BY = :ACTOR,
                        UPDATED_BY = :ACTOR2, UPDATED_DATE = SYSDATE
                    WHERE REQUEST_ID = :ID",
                    new OracleParameter("STATUS", newStatus),
                    new OracleParameter("ACTOR",  model.ACTOR_EMPCD),
                    new OracleParameter("ACTOR2", model.ACTOR_EMPCD),
                    new OracleParameter("ID",     ms.RequestId));

                mapped = true;
            }

            return Ok(new {
                success = true,
                message = mapped ? $"Đã lưu ERP và cập nhật MySamho ({newStatus})" : "Đã lưu ERP (ngày này không thuộc đơn nghỉ MySamho nào cần giấy tờ)",
                mapped
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/admin-emp-list — Toàn bộ NV + phép năm còn lại (Admin)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("admin-emp-list")]
    public async Task<IActionResult> GetAdminEmpList(
        string? search  = null,
        string? dept_id = null,
        string? line_id = null,
        string? work_id = null)
    {
        try
        {
            var whereParts = new List<string>
            {
                "EC.JEAJIKGB = 'Y'",
                "(EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))"
            };
            var parameters = new List<OracleParameter>();

            if (!string.IsNullOrEmpty(dept_id))
            {
                whereParts.Add("EC.DEPTCD = :DEPT_ID");
                parameters.Add(new OracleParameter("DEPT_ID", dept_id));
            }
            if (!string.IsNullOrEmpty(line_id))
            {
                whereParts.Add("EC.LINECD = :LINE_ID");
                parameters.Add(new OracleParameter("LINE_ID", line_id));
            }
            if (!string.IsNullOrEmpty(work_id))
            {
                whereParts.Add("EC.WORKCD = :WORK_ID");
                parameters.Add(new OracleParameter("WORK_ID", work_id));
            }
            if (!string.IsNullOrEmpty(search))
            {
                whereParts.Add("(UPPER(EC.CNAME) LIKE '%' || UPPER(:SEARCH) || '%' OR EC.EMPCD LIKE '%' || :SEARCH2 || '%')");
                parameters.Add(new OracleParameter("SEARCH",  search));
                parameters.Add(new OracleParameter("SEARCH2", search));
            }

            string whereClause = string.Join(" AND ", whereParts);

            string sql = $@"
                WITH ALLOC AS (
                    SELECT EMPCD, MAX(RECEIVE_NUM) AS RECEIVE_NUM
                    FROM HRMS.EFM100
                    WHERE SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                    GROUP BY EMPCD
                ),
                USED AS (
                    SELECT EMPCD,
                           COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                    FROM HRMS.EFM410
                    WHERE TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                    GROUP BY EMPCD
                )
                SELECT EC.EMPCD,
                       EC.CNAME  AS EMP_NAME,
                       EC.DEPTCD AS DEPT_ID,
                       EA.DEPTNM AS DEPT_NAME,
                       EC.LINECD AS LINE_ID,
                       EA.TEAMNM AS LINE_NAME,
                       EC.WORKCD AS WORK_ID,
                       EA.WORKNM AS WORK_NAME,
                       NVL(AL.RECEIVE_NUM, 0)                        AS RECEIVE_NUM,
                       NVL(U.USED_NUM, 0)                            AS USED_NUM,
                       NVL(AL.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0)  AS LEFT_NUM
                FROM HRMS.ECM100 EC
                LEFT JOIN HRMS.EAM410 EA
                    ON EA.DEPTCD = EC.DEPTCD AND EA.LINECD = EC.LINECD AND EA.WORKCD = EC.WORKCD
                LEFT JOIN ALLOC AL ON AL.EMPCD = EC.EMPCD
                LEFT JOIN USED  U  ON U.EMPCD  = EC.EMPCD
                WHERE {whereClause}
                ORDER BY EA.DEPTNM, EA.TEAMNM, EA.WORKNM, EC.CNAME";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new
            {
                EMPCD       = r["EMPCD"]?.ToString()    ?? "",
                EMP_NAME    = r["EMP_NAME"]?.ToString() ?? "",
                DEPT_ID     = r["DEPT_ID"]?.ToString(),
                DEPT_NAME   = r["DEPT_NAME"]?.ToString(),
                LINE_ID     = r["LINE_ID"]?.ToString(),
                LINE_NAME   = r["LINE_NAME"]?.ToString(),
                WORK_ID     = r["WORK_ID"]?.ToString(),
                WORK_NAME   = r["WORK_NAME"]?.ToString(),
                RECEIVE_NUM = r["RECEIVE_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["RECEIVE_NUM"]),
                USED_NUM    = r["USED_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["USED_NUM"]),
                LEFT_NUM    = r["LEFT_NUM"]    == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"])
            }, parameters.ToArray());

            return Ok(new { success = true, total = list.Count, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/admin-assign — Admin sắp lịch nghỉ toàn công ty
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("admin-assign")]
    public async Task<IActionResult> AdminAssign([FromBody] LeaveAssignRequest model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.ASSIGNER_EMPCD))
                return Ok(new { success = false, message = "Thiếu thông tin người sắp lịch" });

            if (model.TARGET_EMPCDS == null || model.TARGET_EMPCDS.Count == 0)
                return Ok(new { success = false, message = "Chưa chọn nhân viên" });

            if (!DateTime.TryParse(model.FROM_DATE, out DateTime fromDate) ||
                !DateTime.TryParse(model.TO_DATE,   out DateTime toDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            if (fromDate > toDate)
                return Ok(new { success = false, message = "Ngày kết thúc phải sau ngày bắt đầu" });

            if (fromDate.Date < DateTime.Today)
                return Ok(new { success = false, message = "Chỉ được sắp lịch từ ngày hôm nay trở đi" });

            if (model.TOTAL_DAYS <= 0)
                return Ok(new { success = false, message = "Số ngày nghỉ không hợp lệ" });

            if (string.IsNullOrEmpty(model.LEAVE_TYPE) || !NewLeaveTypeCodes.Contains(model.LEAVE_TYPE))
                model.LEAVE_TYPE = "AL";

            if (model.LEAVE_TYPE != "AL" && string.IsNullOrWhiteSpace(model.REASON))
                return Ok(new { success = false, message = "Vui lòng nhập lý do nghỉ" });

            var assignerRoleRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
                WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ASSIGNER_EMPCD));

            string? assignerRole = assignerRoleRows.FirstOrDefault();
            if (!string.Equals(assignerRole, "Admin", StringComparison.OrdinalIgnoreCase))
                return Ok(new { success = false, message = "Chỉ Admin mới có quyền sắp lịch toàn công ty" });

            string erpCd         = model.LEAVE_TYPE switch { "AL" => "PN", "CL" => "BH", "CT" => "CT", _ => "CP" };
            string erpRemark     = await BuildErpRemarkAsync(model.LEAVE_TYPE, model.REASON, isAssignFlow: true);
            string leaveTypeName = NewLeaveTypeNames.GetValueOrDefault(model.LEAVE_TYPE, model.LEAVE_TYPE);

            var results   = new List<object>();
            var warnings  = new List<object>();
            int successCt = 0;

            foreach (var targetEmpcd in model.TARGET_EMPCDS)
            {
                try
                {
                    // Không cho Admin tự sắp lịch nghỉ cho chính mình — cùng lý do với endpoint
                    // Assign của Supervisor/Manager (tránh tự cấp phép nghỉ không qua ai kiểm soát).
                    if (string.Equals(targetEmpcd, model.ASSIGNER_EMPCD, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new { empcd = targetEmpcd, success = false, message = "Không thể tự sắp lịch nghỉ cho chính mình" });
                        continue;
                    }

                    var empRows = await _oracleService.ExecuteQueryAsync(
                        "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                        r => r["CNAME"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd));
                    string empName = empRows.FirstOrDefault() ?? "";

                    // Chặn sắp lịch trùng: NV đã có đơn nghỉ (bất kỳ nguồn nào, chưa bị từ chối)
                    // đè lên khoảng ngày này rồi thì báo lỗi thay vì tạo thêm 1 đơn ASSIGNED nữa —
                    // tránh trường hợp double-click/chạy sắp lịch 2 lần ra 2 đơn y hệt cho cùng 1 ngày.
                    var dupRows = await _oracleService.ExecuteQueryAsync(@"
                        SELECT R.STATUS FROM HRMS.HR_REQUEST R
                        JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                        WHERE L.EMPCD = :EMPCD AND R.REQUEST_TYPE = 'LEAVE' AND R.STATUS != 'REJECTED'
                          AND L.FROM_DATE <= :TO_DATE AND L.TO_DATE >= :FROM_DATE
                          AND ROWNUM = 1",
                        r => r["STATUS"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd),
                        new OracleParameter("FROM_DATE", OracleDbType.Date) { Value = fromDate },
                        new OracleParameter("TO_DATE",   OracleDbType.Date) { Value = toDate });

                    if (dupRows.Count > 0)
                    {
                        results.Add(new { empcd = targetEmpcd, emp_name = empName, success = false,
                            message = $"NV đã có đơn nghỉ trùng khoảng ngày này (trạng thái: {dupRows[0]}) — bỏ qua, không tạo thêm" });
                        continue;
                    }

                    int leftNum = 999;
                    if (model.LEAVE_TYPE == "AL")
                    {
                        var balRows = await _oracleService.ExecuteQueryAsync(@"
                            WITH ALLOC AS (
                                SELECT MAX(RECEIVE_NUM) AS RECEIVE_NUM
                                FROM HRMS.EFM100
                                WHERE EMPCD = :EMPCD
                                  AND SUBSTR(CAL_MONTH,1,4) = TO_CHAR(SYSDATE,'YYYY')
                            ),
                            USED AS (
                                SELECT COUNT(CASE WHEN LEAVECD IN ('PN','LP') AND REMAR IN ('VR','ASSIGNED') THEN 1 END) AS USED_NUM
                                FROM HRMS.EFM410
                                WHERE EMPCD = :EMPCD2
                                  AND TO_CHAR(FR_DAT,'YYYY') = TO_CHAR(SYSDATE,'YYYY')
                            )
                            SELECT NVL(A.RECEIVE_NUM, 0) - NVL(U.USED_NUM, 0) AS LEFT_NUM
                            FROM ALLOC A, USED U",
                            r => r["LEFT_NUM"] == DBNull.Value ? 0 : Convert.ToInt32(r["LEFT_NUM"]),
                            new OracleParameter("EMPCD",  targetEmpcd),
                            new OracleParameter("EMPCD2", targetEmpcd));

                        leftNum = balRows.FirstOrDefault();
                        if (leftNum <= 0)
                            warnings.Add(new { empcd = targetEmpcd, emp_name = empName, left_num = leftNum });
                    }

                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_REQUEST
                            (REQUEST_TYPE, EMPCD, EMP_NAME, REQUEST_DATE, STATUS, CREATED_BY, CREATED_DATE)
                        VALUES ('LEAVE', :EMPCD, :EMP_NAME, SYSDATE, 'ASSIGNED', :CREATED_BY, SYSDATE)",
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("EMP_NAME",   empName),
                        new OracleParameter("CREATED_BY", model.ASSIGNER_EMPCD));

                    var reqIds = await _oracleService.ExecuteQueryAsync(@"
                        SELECT REQUEST_ID FROM (
                            SELECT REQUEST_ID FROM HRMS.HR_REQUEST
                            WHERE EMPCD = :EMPCD AND REQUEST_TYPE = 'LEAVE' AND STATUS = 'ASSIGNED'
                              AND TRUNC(CREATED_DATE) = TRUNC(SYSDATE)
                            ORDER BY CREATED_DATE DESC
                        ) WHERE ROWNUM = 1",
                        r => r["REQUEST_ID"]?.ToString(),
                        new OracleParameter("EMPCD", targetEmpcd));

                    if (reqIds.Count == 0 || string.IsNullOrEmpty(reqIds[0]))
                    {
                        results.Add(new { empcd = targetEmpcd, success = false, message = "Lỗi tạo REQUEST_ID" });
                        continue;
                    }

                    string requestId = reqIds[0]!;

                    await _oracleService.ExecuteNonQueryAsync(@"
                        INSERT INTO HRMS.HR_LEAVE_REQUEST
                            (REQUEST_ID, EMPCD, LEAVE_TYPE, FROM_DATE, TO_DATE, TOTAL_DAYS, REASON, CREATED_DATE, SOURCE)
                        VALUES (:REQUEST_ID, :EMPCD, :LEAVE_TYPE, :FROM_DATE, :TO_DATE, :TOTAL_DAYS, :REASON, SYSDATE, 'ASSIGNED')",
                        new OracleParameter("REQUEST_ID", requestId),
                        new OracleParameter("EMPCD",      targetEmpcd),
                        new OracleParameter("LEAVE_TYPE", model.LEAVE_TYPE),
                        new OracleParameter("FROM_DATE",  fromDate),
                        new OracleParameter("TO_DATE",    toDate),
                        new OracleParameter("TOTAL_DAYS", model.TOTAL_DAYS),
                        new OracleParameter("REASON",     (object?)model.REASON ?? DBNull.Value));

                    var erpHolidays = (await _oracleService.ExecuteQueryAsync(
                        @"SELECT TRUNC(HUILDAY) AS HUILDAY FROM HRMS.EAM800
                          WHERE TRUNC(HUILDAY) BETWEEN TRUNC(:FROM_DATE) AND TRUNC(:TO_DATE)",
                        r => Convert.ToDateTime(r["HUILDAY"]).Date,
                        new OracleParameter { ParameterName = "FROM_DATE", OracleDbType = OracleDbType.Date, Value = fromDate },
                        new OracleParameter { ParameterName = "TO_DATE",   OracleDbType = OracleDbType.Date, Value = toDate }
                    )).ToHashSet();

                    // Dưỡng sức (DS) tính theo ngày lịch kể cả Chủ Nhật/ngày lễ — luôn coi như "được phép"
                    // (dù AdminAssign hiện không cho chọn DS, giữ nhất quán với 2 chỗ Submit/Approve).
                    bool isSundayAllowed = model.LEAVE_TYPE == "DS" || (await _oracleService.ExecuteQueryAsync(
                        "SELECT 1 AS X FROM HRMS.HR_SUNDAY_LEAVE_ALLOWED WHERE EMPCD = :EMPCD AND IS_ACTIVE = 1",
                        r => 1,
                        new OracleParameter("EMPCD", targetEmpcd)
                    )).Any();
                    try
                    {
                        for (var day = fromDate.Date; day <= toDate.Date; day = day.AddDays(1))
                        {
                            if (erpHolidays.Contains(day)
                                && !(day.DayOfWeek == DayOfWeek.Sunday && isSundayAllowed))
                                continue;

                            if (model.LEAVE_TYPE == "AL" && leftNum <= 0)
                            {
                                // Hết phép năm nhưng Admin vẫn sắp -> Ghi trực tiếp vào EFM410 (bỏ qua checks/exit trong SP_015_NEW)
                                var cntRows = await _oracleService.ExecuteQueryAsync(
                                    "SELECT COUNT(*) AS CNT FROM HRMS.EFM410 WHERE EMPCD = :EMPCD AND FR_DAT = :FR_DAT",
                                    r => Convert.ToInt32(r["CNT"]),
                                    new OracleParameter("EMPCD", targetEmpcd),
                                    new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = day });

                                if (cntRows.FirstOrDefault() == 0)
                                {
                                    await _oracleService.ExecuteNonQueryAsync(
                                        "INSERT INTO HRMS.EFM410 (EMPCD, LEAVECD, FR_DAT, IN_ID, REMAR) VALUES (:EMPCD, :LEAVECD, :FR_DAT, :IN_ID, :REMAR)",
                                        new OracleParameter("EMPCD", targetEmpcd),
                                        new OracleParameter("LEAVECD", erpCd),
                                        new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = day },
                                        new OracleParameter("IN_ID", model.ASSIGNER_EMPCD),
                                        new OracleParameter("REMAR", erpRemark));
                                }
                                else
                                {
                                    await _oracleService.ExecuteNonQueryAsync(
                                        "UPDATE HRMS.EFM410 SET LEAVECD = :LEAVECD, REMAR = :REMAR WHERE EMPCD = :EMPCD AND FR_DAT = :FR_DAT",
                                        new OracleParameter("LEAVECD", erpCd),
                                        new OracleParameter("REMAR", erpRemark),
                                        new OracleParameter("EMPCD", targetEmpcd),
                                        new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = day });
                                }
                            }
                            else
                            {
                                // SP_015_NEW skip Chủ Nhật → dùng SP_015_FORHRAPP cho NV whitelist
                                string spName = (day.DayOfWeek == DayOfWeek.Sunday && isSundayAllowed)
                                    ? "HRMS.SP_015_FORHRAPP"
                                    : "HRMS.SP_015_NEW";

                                await _oracleService.ExecuteProcedureAsync(spName,
                                    new OracleParameter("AS_EMPCD",   targetEmpcd),
                                    new OracleParameter("AS_LEAVECD", erpCd),
                                    new OracleParameter { ParameterName = "AD_ST_DAT", OracleDbType = OracleDbType.Date, Value = day },
                                    new OracleParameter { ParameterName = "AD_ED_DAT", OracleDbType = OracleDbType.Date, Value = day },
                                    new OracleParameter("AS_IN_ID",   model.ASSIGNER_EMPCD),
                                    new OracleParameter("AS_REMAR",   erpRemark));
                            }
                        }

                        await _oracleService.ExecuteNonQueryAsync(
                            "UPDATE HRMS.EFM410 SET APPROVED_BY = :APPROVED_BY WHERE EMPCD = :EMPCD AND FR_DAT BETWEEN :FR_DAT AND :TO_DAT",
                            new OracleParameter("APPROVED_BY", model.ASSIGNER_EMPCD),
                            new OracleParameter("EMPCD",       targetEmpcd),
                            new OracleParameter { ParameterName = "FR_DAT", OracleDbType = OracleDbType.Date, Value = fromDate },
                            new OracleParameter { ParameterName = "TO_DAT", OracleDbType = OracleDbType.Date, Value = toDate });
                    }
                    catch { /* ERP failure không block assign */ }

                    _notiSvc.LeaveAssigned(targetEmpcd, model.ASSIGNER_EMPCD, leaveTypeName, fromDate, toDate);

                    results.Add(new { empcd = targetEmpcd, success = true, request_id = requestId });
                    successCt++;
                }
                catch (Exception ex)
                {
                    results.Add(new { empcd = targetEmpcd, success = false, message = ex.Message });
                }
            }

            return Ok(new
            {
                success        = successCt > 0,
                message        = $"Đã sắp lịch cho {successCt}/{model.TARGET_EMPCDS.Count} nhân viên",
                total_inserted = successCt,
                warnings,
                results
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Leave/admin-confirmed-leaves
    [HttpGet("admin-confirmed-leaves")]
    public async Task<IActionResult> GetAdminConfirmedLeaves(
        string? dept_id = null, string? line_id = null, string? work_id = null,
        string? date_from = null, string? date_to = null, string? status = null,
        string? search = null,
        int page = 1, int page_size = 50)
    {
        try
        {
            DateTime? dFrom = null, dTo = null;
            if (!string.IsNullOrEmpty(date_from) && DateTime.TryParse(date_from, out var df)) dFrom = df;
            if (!string.IsNullOrEmpty(date_to)   && DateTime.TryParse(date_to,   out var dt)) dTo   = dt;

            var validStatuses = new[] { "PENDING", "APPROVED", "ASSIGNED" };
            string? statusFilter = !string.IsNullOrEmpty(status) && validStatuses.Contains(status.ToUpper())
                ? status.ToUpper() : null;

            const string baseSql = @"
                SELECT R.REQUEST_ID, L.EMPCD, EC.CNAME EMP_NAME,
                       EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                       EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                       EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                       L.LEAVE_TYPE, L.SOURCE, L.FROM_DATE, L.TO_DATE, L.TOTAL_DAYS, L.REASON,
                       R.STATUS, L.CONFIRM_STATUS, R.FINAL_DATE, R.FINAL_APPROVER, R.CREATED_DATE
                FROM HRMS.HR_REQUEST R
                JOIN HRMS.HR_LEAVE_REQUEST L ON L.REQUEST_ID = R.REQUEST_ID
                JOIN HRMS.ECM100 EC           ON EC.EMPCD    = L.EMPCD
                LEFT JOIN HRMS.EAM410 B       ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
                WHERE R.REQUEST_TYPE = 'LEAVE'
                  AND R.STATUS != 'REJECTED'
                  AND (:ST_FLAG  IS NULL OR R.STATUS      = :ST_VAL)
                  AND (:DPT_FLAG IS NULL OR EC.DEPTCD    = :DPT_VAL)
                  AND (:LN_FLAG  IS NULL OR EC.LINECD    = :LN_VAL)
                  AND (:WK_FLAG   IS NULL OR EC.WORKCD    = :WK_VAL)
                  AND (:SRCH_FLAG IS NULL OR UPPER(L.EMPCD) LIKE :SRCH_VAL)
                  AND (:FR_FLAG   IS NULL OR L.TO_DATE   >= :FR_VAL)
                  AND (:TO_FLAG   IS NULL OR L.FROM_DATE < :TO_VAL + 1)";

            OracleParameter[] MakePs() => new[]
            {
                new OracleParameter("ST_FLAG",  (object?)(statusFilter != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("ST_VAL",   (object?)statusFilter ?? DBNull.Value),
                new OracleParameter("DPT_FLAG", (object?)(dept_id != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("DPT_VAL",  (object?)dept_id ?? DBNull.Value),
                new OracleParameter("LN_FLAG",  (object?)(line_id != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("LN_VAL",   (object?)line_id ?? DBNull.Value),
                new OracleParameter("WK_FLAG",   (object?)(work_id != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("WK_VAL",    (object?)work_id ?? DBNull.Value),
                new OracleParameter("SRCH_FLAG", (object?)(!string.IsNullOrEmpty(search) ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("SRCH_VAL",  (object?)(!string.IsNullOrEmpty(search) ? "%" + search.ToUpper() + "%" : null) ?? DBNull.Value),
                new OracleParameter("FR_FLAG",   (object?)(dFrom != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("FR_VAL",   (object?)dFrom ?? DBNull.Value),
                new OracleParameter("TO_FLAG",  (object?)(dTo   != null ? "Y" : null) ?? DBNull.Value),
                new OracleParameter("TO_VAL",   (object?)dTo   ?? DBNull.Value),
            };

            var cntRows = await _oracleService.ExecuteQueryAsync(
                $"SELECT COUNT(*) CNT FROM ({baseSql})",
                r => Convert.ToInt32(r["CNT"]), MakePs());
            int total = cntRows.FirstOrDefault();

            if (total == 0)
                return Ok(new { success = true, total = 0, page, page_size, total_pages = 0, data = Array.Empty<object>() });

            var dataPs = MakePs().ToList();
            dataPs.Add(new OracleParameter("P_END",   page * page_size));
            dataPs.Add(new OracleParameter("P_START", (page - 1) * page_size));

            var rows = await _oracleService.ExecuteQueryAsync($@"
                SELECT * FROM (
                    SELECT A.*, ROWNUM RN
                    FROM ({baseSql} ORDER BY NVL(R.FINAL_DATE, R.CREATED_DATE) DESC) A
                    WHERE ROWNUM <= :P_END
                ) WHERE RN > :P_START",
                r => new
                {
                    REQUEST_ID     = r["REQUEST_ID"]?.ToString() ?? "",
                    EMPCD          = r["EMPCD"]?.ToString() ?? "",
                    EMP_NAME       = r["EMP_NAME"]?.ToString(),
                    DEPT_ID        = r["DEPT_ID"]?.ToString(),
                    DEPT_NAME      = r["DEPT_NAME"]?.ToString(),
                    LINE_ID        = r["LINE_ID"]?.ToString(),
                    LINE_NAME      = r["LINE_NAME"]?.ToString(),
                    WORK_ID        = r["WORK_ID"]?.ToString(),
                    WORK_NAME      = r["WORK_NAME"]?.ToString(),
                    LEAVE_TYPE     = r["LEAVE_TYPE"]?.ToString(),
                    SOURCE         = r["SOURCE"]?.ToString(),
                    FROM_DATE      = r["FROM_DATE"]    == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FROM_DATE"]),
                    TO_DATE        = r["TO_DATE"]      == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["TO_DATE"]),
                    TOTAL_DAYS     = r["TOTAL_DAYS"]   == DBNull.Value ? (decimal?)null  : Convert.ToDecimal(r["TOTAL_DAYS"]),
                    REASON         = r["REASON"]?.ToString(),
                    STATUS         = r["STATUS"]?.ToString(),
                    CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString(),
                    FINAL_DATE     = r["FINAL_DATE"]   == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["FINAL_DATE"]),
                    FINAL_APPROVER = r["FINAL_APPROVER"]?.ToString(),
                    CREATED_DATE   = r["CREATED_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CREATED_DATE"]),
                }, dataPs.ToArray());

            return Ok(new { success = true, total, page, page_size, total_pages = (int)Math.Ceiling((double)total / page_size), data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/Leave/admin-delete-leaves
    [HttpPost("admin-delete-leaves")]
    public async Task<IActionResult> AdminDeleteLeaves([FromBody] AdminBulkDeleteRequest model)
    {
        if (model.REQUEST_IDS == null || model.REQUEST_IDS.Count == 0)
            return Ok(new { success = false, message = "Không có đơn nào được chọn" });
        try
        {
            var roleRows = await _oracleService.ExecuteQueryAsync(
                "SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["ROLE_NAME"]?.ToString(),
                new OracleParameter("EMPCD", model.ACTOR_EMPCD));
            if (roleRows.FirstOrDefault() != "Admin")
                return Ok(new { success = false, message = "Chỉ Admin mới có quyền xóa" });

            var idParams     = model.REQUEST_IDS.Select((id, i) => new OracleParameter($"ID{i}", id)).ToArray();
            var placeholders = string.Join(",", idParams.Select(p => $":{p.ParameterName}"));
            var deletedCount = new OracleParameter("DELETED_COUNT", OracleDbType.Int32) { Direction = System.Data.ParameterDirection.Output };

            await _oracleService.ExecuteNonQueryAsync($@"
                BEGIN
                    DELETE FROM HRMS.HR_LEAVE_REQUEST WHERE REQUEST_ID IN ({placeholders});
                    DELETE FROM HRMS.HR_REQUEST        WHERE REQUEST_ID IN ({placeholders}) AND REQUEST_TYPE = 'LEAVE';
                    :DELETED_COUNT := SQL%ROWCOUNT;
                    COMMIT;
                END;", idParams.Append(deletedCount).ToArray());

            int total = int.Parse(deletedCount.Value?.ToString() ?? "0");
            return Ok(new { success = true, message = $"Đã xóa {total} đơn nghỉ phép khỏi hệ thống", total_deleted = total });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/sunday-allowed?empCd=xxx
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("sunday-allowed")]
    public async Task<IActionResult> SundayAllowed([FromQuery] string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd))
            return Ok(new { allowed = false });
        var rows = await _oracleService.ExecuteQueryAsync<int>(
            "SELECT 1 FROM HRMS.HR_SUNDAY_LEAVE_ALLOWED WHERE EMPCD = :EMPCD AND IS_ACTIVE = 1",
            r => 1,
            new OracleParameter("EMPCD", empCd));
        return Ok(new { allowed = rows.Count > 0 });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /apiHR/Leave/sunday-list
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("sunday-list")]
    public async Task<IActionResult> SundayList(
        [FromQuery] string? deptCd, [FromQuery] string? lineCd,
        [FromQuery] string? workCd, [FromQuery] string? search)
    {
        var where = new List<string> { "S.IS_ACTIVE = 1" };
        var pms   = new List<OracleParameter>();
        if (!string.IsNullOrWhiteSpace(deptCd)) { where.Add("EC.DEPTCD = :DEPTCD"); pms.Add(new OracleParameter("DEPTCD", deptCd)); }
        if (!string.IsNullOrWhiteSpace(lineCd)) { where.Add("EC.LINECD = :LINECD"); pms.Add(new OracleParameter("LINECD", lineCd)); }
        if (!string.IsNullOrWhiteSpace(workCd)) { where.Add("EC.WORKCD = :WORKCD"); pms.Add(new OracleParameter("WORKCD", workCd)); }
        if (!string.IsNullOrWhiteSpace(search)) {
            where.Add("(S.EMPCD LIKE :SEARCH OR UPPER(EC.CNAME) LIKE UPPER(:SEARCH))");
            pms.Add(new OracleParameter("SEARCH", "%" + search.Trim() + "%"));
        }
        var sql = $@"
            SELECT S.EMPCD, EC.CNAME EMP_NAME,
                   EC.DEPTCD DEPT_ID, B.DEPTNM DEPT_NAME,
                   EC.LINECD LINE_ID, B.TEAMNM LINE_NAME,
                   EC.WORKCD WORK_ID, B.WORKNM WORK_NAME,
                   TO_CHAR(S.INST_DT,'DD/MM/YYYY') INST_DT
            FROM HRMS.HR_SUNDAY_LEAVE_ALLOWED S
            JOIN  HRMS.ECM100 EC ON EC.EMPCD = S.EMPCD
            LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
            WHERE {string.Join(" AND ", where)}
            ORDER BY S.INST_DT DESC, S.EMPCD";
        var data = await _oracleService.ExecuteQueryAsync(sql, r => new {
            EMPCD     = r["EMPCD"]?.ToString(),
            EMP_NAME  = r["EMP_NAME"]?.ToString(),
            DEPT_NAME = r["DEPT_NAME"]?.ToString(),
            LINE_NAME = r["LINE_NAME"]?.ToString(),
            WORK_NAME = r["WORK_NAME"]?.ToString(),
            DEPT_ID   = r["DEPT_ID"]?.ToString(),
            LINE_ID   = r["LINE_ID"]?.ToString(),
            WORK_ID   = r["WORK_ID"]?.ToString(),
            INST_DT   = r["INST_DT"]?.ToString()
        }, pms.ToArray());
        return Ok(new { success = true, data });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/sunday-add
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("sunday-add")]
    public async Task<IActionResult> SundayAdd([FromBody] SundayActionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmpCd))
            return Ok(new { success = false, message = "Mã nhân viên không hợp lệ" });

        var empCheck = await _oracleService.ExecuteQueryAsync<int>(
            "SELECT 1 FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
            r => 1, new OracleParameter("EMPCD", req.EmpCd.Trim()));
        if (empCheck.Count == 0)
            return Ok(new { success = false, message = $"Không tìm thấy nhân viên {req.EmpCd}" });

        await _oracleService.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_SUNDAY_LEAVE_ALLOWED T
            USING (SELECT :EMPCD EMPCD FROM DUAL) S ON (T.EMPCD = S.EMPCD)
            WHEN MATCHED     THEN UPDATE SET IS_ACTIVE=1, UPDT_ID=:LOGIN, UPDT_DT=SYSDATE
            WHEN NOT MATCHED THEN INSERT (EMPCD,IS_ACTIVE,INST_ID,INST_DT) VALUES (:EMPCD,1,:LOGIN,SYSDATE)",
            new OracleParameter("EMPCD", req.EmpCd.Trim()),
            new OracleParameter("LOGIN", req.LoginUser ?? "HR"));

        return Ok(new { success = true, message = $"Đã thêm nhân viên {req.EmpCd}" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/sunday-remove
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("sunday-remove")]
    public async Task<IActionResult> SundayRemove([FromBody] SundayActionRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EmpCd))
            return Ok(new { success = false, message = "Mã nhân viên không hợp lệ" });

        await _oracleService.ExecuteNonQueryAsync(
            "UPDATE HRMS.HR_SUNDAY_LEAVE_ALLOWED SET IS_ACTIVE=0, UPDT_ID=:LOGIN, UPDT_DT=SYSDATE WHERE EMPCD=:EMPCD",
            new OracleParameter("LOGIN", req.LoginUser ?? "HR"),
            new OracleParameter("EMPCD", req.EmpCd.Trim()));

        return Ok(new { success = true, message = $"Đã xoá nhân viên {req.EmpCd}" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/sunday-bulk-remove
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("sunday-bulk-remove")]
    public async Task<IActionResult> SundayBulkRemove([FromBody] SundayBulkRemoveRequest req)
    {
        if (req.EmpCds == null || req.EmpCds.Count == 0)
            return Ok(new { success = false, message = "Danh sách rỗng" });

        var login = req.LoginUser ?? "HR";
        int count = 0;
        foreach (var empCd in req.EmpCds.Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct())
        {
            await _oracleService.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_SUNDAY_LEAVE_ALLOWED SET IS_ACTIVE=0, UPDT_ID=:LOGIN, UPDT_DT=SYSDATE WHERE EMPCD=:EMPCD",
                new OracleParameter("LOGIN", login),
                new OracleParameter("EMPCD", empCd));
            count++;
        }
        return Ok(new { success = true, message = $"Đã xoá {count} nhân viên" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /apiHR/Leave/sunday-import
    // ─────────────────────────────────────────────────────────────────────────
    [HttpPost("sunday-import")]
    public async Task<IActionResult> SundayImport([FromBody] SundayImportRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            return Ok(new { success = false, message = "Danh sách rỗng" });

        var results = new List<object>();
        foreach (var empCd in req.Items.Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct())
        {
            var empCheck = await _oracleService.ExecuteQueryAsync<int>(
                "SELECT 1 FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                r => 1, new OracleParameter("EMPCD", empCd));
            if (empCheck.Count == 0) { results.Add(new { empCd, success = false, message = "Không tìm thấy NV" }); continue; }

            await _oracleService.ExecuteNonQueryAsync(@"
                MERGE INTO HRMS.HR_SUNDAY_LEAVE_ALLOWED T
                USING (SELECT :EMPCD EMPCD FROM DUAL) S ON (T.EMPCD = S.EMPCD)
                WHEN MATCHED     THEN UPDATE SET IS_ACTIVE=1, UPDT_ID=:LOGIN, UPDT_DT=SYSDATE
                WHEN NOT MATCHED THEN INSERT (EMPCD,IS_ACTIVE,INST_ID,INST_DT) VALUES (:EMPCD,1,:LOGIN,SYSDATE)",
                new OracleParameter("EMPCD", empCd),
                new OracleParameter("LOGIN", req.LoginUser ?? "HR"));
            results.Add(new { empCd, success = true, message = "OK" });
        }
        return Ok(new { success = true, results });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AL Deadline: STIME của ca ngày FROM_DATE - 6h.
    // Fair cho ca đêm: K3 22:30 → deadline 16:30 cùng ngày (không phải 00:00 hôm trước).
    // Nếu user chưa được xếp lịch ca cho ngày đó → fallback: chỉ chặn from <= today.
    // ─────────────────────────────────────────────────────────────────────────
    private record AlDeadlineCheck(bool Allowed, string? Message, DateTime? Deadline, string ShiftCd, string ShiftStart);

    private async Task<AlDeadlineCheck> CheckAlDeadlineAsync(string empcd, DateTime fromDate)
    {
        // Lấy shift ngày FROM_DATE.
        // Union EBM300 + EBM300_WAIT vì lịch tương lai có thể chưa finalize
        // (giống ShiftLookupService.GetShiftForDateAsync).
        const string sql = @"
            SELECT T.SHIFTCD, S.STIME FROM (
                SELECT SHIFTCD FROM HRMS.EBM300      WHERE EMPCD = :EMPCD  AND DAT = :FROM_DATE  AND ROWNUM = 1
                UNION ALL
                SELECT SHIFTCD FROM HRMS.EBM300_WAIT WHERE EMPCD = :EMPCD1 AND DAT = :FROM_DATE1 AND ROWNUM = 1
            ) T
            JOIN HRMS.EBM100 S ON S.SHIFTCD = T.SHIFTCD
            WHERE ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r => new
        {
            SHIFTCD = r["SHIFTCD"]?.ToString() ?? "",
            STIME   = r["STIME"]?.ToString()   ?? "",
        },
        new OracleParameter("EMPCD",      empcd),
        new OracleParameter("FROM_DATE",  OracleDbType.Date) { Value = fromDate.Date },
        new OracleParameter("EMPCD1",     empcd),
        new OracleParameter("FROM_DATE1", OracleDbType.Date) { Value = fromDate.Date });

        var shift = rows.FirstOrDefault();

        // Fallback: không có shift → giữ luật cũ (from phải > today)
        if (shift == null || shift.STIME.Length != 4)
        {
            if (fromDate.Date <= DateTime.Today)
                return new AlDeadlineCheck(false,
                    "Phép năm phải đăng ký trước ít nhất 1 ngày (chưa có lịch ca ngày này).",
                    null, "", "");
            return new AlDeadlineCheck(true, null, null, "", "");
        }

        if (!int.TryParse(shift.STIME.Substring(0, 2), out var hh) ||
            !int.TryParse(shift.STIME.Substring(2, 2), out var mm))
        {
            // STIME format lạ → fallback
            if (fromDate.Date <= DateTime.Today)
                return new AlDeadlineCheck(false,
                    "Phép năm phải đăng ký trước ít nhất 1 ngày.", null, shift.SHIFTCD, "");
            return new AlDeadlineCheck(true, null, null, shift.SHIFTCD, "");
        }

        var shiftStart  = fromDate.Date.AddHours(hh).AddMinutes(mm);
        var deadline    = shiftStart.AddMinutes(-360); // -6h
        var shiftStartStr = $"{hh:D2}:{mm:D2}";

        if (DateTime.Now > deadline)
        {
            string msg = $"Đã qua giờ đăng ký phép năm cho ngày {fromDate:dd/MM/yyyy}. " +
                         $"Ca {shift.SHIFTCD} bắt đầu {shiftStartStr}, hạn đăng ký: {deadline:HH:mm dd/MM/yyyy}.";
            return new AlDeadlineCheck(false, msg, deadline, shift.SHIFTCD, shiftStartStr);
        }

        return new AlDeadlineCheck(true, null, deadline, shift.SHIFTCD, shiftStartStr);
    }

    // GET /apiHR/Leave/al-deadline?empcd=&from_date=YYYY-MM-DD
    // Frontend gọi trước khi enable nút Submit để UX không bị lỡ deadline
    [HttpGet("al-deadline")]
    public async Task<IActionResult> GetAlDeadline(string empcd, string from_date)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });
            if (!DateTime.TryParse(from_date, out var fromDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            var chk = await CheckAlDeadlineAsync(empcd, fromDate);
            return Ok(new
            {
                success     = true,
                allowed     = chk.Allowed,
                message     = chk.Message,
                deadline    = chk.Deadline?.ToString("yyyy-MM-dd HH:mm"),
                shift_cd    = chk.ShiftCd,
                shift_start = chk.ShiftStart
            });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    // GET /apiHR/Leave/same-day-deadline?empcd=&leave_type=&from_date=YYYY-MM-DD
    // Frontend gọi khi NV chọn loại NL/SI hoặc DT/VS/KT với FROM_DATE = hôm nay, để tự động dời
    // min date sang ngày mai nếu đã quá hạn (giống hệt cơ chế al-deadline) — tránh bug UI vẫn
    // cho chọn hôm nay dù ca đã bắt đầu (NL/SI) / đã hết ca (DT/VS/KT), Submit() sẽ chặn ở server.
    [HttpGet("same-day-deadline")]
    public async Task<IActionResult> GetSameDayDeadline(string empcd, string leave_type, string from_date)
    {
        try
        {
            if (string.IsNullOrEmpty(empcd))
                return Ok(new { success = false, message = "Thiếu mã nhân viên" });
            if (!DateTime.TryParse(from_date, out var fromDate))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            string leaveTypeName = NewLeaveTypeNames.GetValueOrDefault(leave_type, leave_type);
            string? error = SameDayShiftStartTypes.Contains(leave_type)
                ? await CheckSameDayShiftStartAsync(empcd, fromDate, leaveTypeName)
                : SameDaySuddenLeaveTypes.Contains(leave_type)
                    ? await CheckSameDayShiftEndAsync(empcd, fromDate, leaveTypeName)
                    : null;

            return Ok(new { success = true, allowed = error == null, message = error });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}

public class SundayActionRequest     { public string EmpCd { get; set; } = ""; public string? LoginUser { get; set; } }
public class SundayBulkRemoveRequest { public List<string> EmpCds { get; set; } = new(); public string? LoginUser { get; set; } }
public class SundayImportRequest     { public List<string> Items  { get; set; } = new(); public string? LoginUser { get; set; } }

// ── TEMP TEST: remove after testing ──────────────────────────────────────────
