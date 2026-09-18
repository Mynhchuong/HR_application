using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.AttendanceConfirm;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Xác nhận chấm công thiếu — nguồn phát hiện: HRMS.ADD_TIME (ERP, chỉ SELECT).
// QUAN TRỌNG: tín hiệu "thiếu" là cột REASON IS NOT NULL, KHÔNG phải TIME_IN/TIME_OUT IS NULL —
// HR (IN_ID) đã tự tay điền sẵn giờ mặc định (= giờ ca chuẩn) cho phía thiếu trước khi lưu, nên 2
// cột này hầu như không bao giờ NULL thật (xác nhận thực tế trên DB 2026-09-18). Xem ClassifyReason
// để biết REASON nào ứng với thiếu IN/OUT/cả hai, và phía nào trong TIME_IN/TIME_OUT là giờ quẹt
// thẻ thật (đáng tin) vs giờ giả (cần NV xác nhận lại).
// 2 bước: NV khai giờ vào/ra thực tế (PENDING_WORKER -> PENDING_MANAGER) -> quản lý theo
// dept/line/work của NV (hoặc Admin/HR) xem, có thể sửa, ghi chú, rồi chốt (CONFIRMED/REJECTED).
// Trạng thái lưu ở HRMS.HR_ATT_CONFIRM (app tự quản) — KHÔNG ghi ngược vào ADD_TIME.
public class AttendanceConfirmService
{
    // REASON là text tự do HR gõ tay suốt nhiều năm (142k dòng, 142 giá trị khác nhau — đủ kiểu note
    // như "may sai gio", "wfh", "bu TC"...) — CHỈ 3 giá trị này là case cần app xử lý (theo yêu cầu
    // 2026-09-18); "New Commer (not In Out)" và "Delete Time In or Time Out" KHÔNG thuộc luồng xác
    // nhận này dù cũng phổ biến. So sánh UPPER+TRIM để bắt được lỗi gõ hoa/thường (đã thấy thực tế có
    // "Not Time in"/"Not Time out" viết sai) mà không vơ nhầm các note tự do khác.
    private const string ValidReasonSqlFilter =
        "UPPER(TRIM(A.REASON)) IN ('NOT TIME IN','NOT TIME OUT','NOT TIME IN AND NOT TIME OUT')";

    // Cùng logic với ClassifyReason (C#) nhưng viết dạng SQL để lọc được trực tiếp trong WHERE.
    private const string MissingTypeSqlExpr =
        "CASE WHEN UPPER(TRIM(A.REASON)) = 'NOT TIME IN' THEN 'IN' WHEN UPPER(TRIM(A.REASON)) = 'NOT TIME OUT' THEN 'OUT' ELSE 'BOTH' END";

    private readonly OracleService _oracleService;
    private readonly ShiftLookupService _shiftLookup;
    private readonly NotificationService _notiSvc;
    private readonly NotificationHelper _notiHelper;
    private readonly AttendanceConfirmLogHelper _log;

    public AttendanceConfirmService(
        OracleService oracleService,
        ShiftLookupService shiftLookup,
        NotificationService notiSvc,
        NotificationHelper notiHelper,
        AttendanceConfirmLogHelper log)
    {
        _oracleService = oracleService;
        _shiftLookup   = shiftLookup;
        _notiSvc       = notiSvc;
        _notiHelper    = notiHelper;
        _log           = log;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Danh sách "cần xác nhận" — join live ADD_TIME + ECM100/EAM410 + LEFT JOIN HR_ATT_CONFIRM.
    // callerEmpCd rỗng/Admin-HR => không áp scope (nhìn hết, có thể lọc thêm bằng dept/line/work).
    // Quản lý thường => bắt buộc có scope tuple qua HR_USERS_DEPT (giống OT/Leave).
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AttendanceMissingListResponse> GetMissingListAsync(
        string callerEmpCd, bool isAdminOrHr,
        string? deptId, string? lineId, string? workId, string? search,
        DateTime fromDate, DateTime toDate, string? status, string? missingType,
        int page, int pageSize)
    {
        // Chỉ true khi caller là quản lý thật (Supervisor+), KHÔNG phải Admin/HR — Admin/HR chỉ xem/
        // xuất Excel/gửi nhắc, không tự bấm Xác nhận thay (yêu cầu HR 2026-09-18).
        bool canConfirm = !isAdminOrHr && RoleHierarchyHelper.HasApprovalPermission(await GetRoleNameAsync(callerEmpCd));

        OTScopeFilterHelper.FilterResult? scopeFilter = null;
        if (!isAdminOrHr)
        {
            var hasScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", callerEmpCd));
            if (hasScope.FirstOrDefault() == 0)
                return new AttendanceMissingListResponse
                {
                    success = true, total = 0, page = page, page_size = pageSize, total_pages = 0,
                    message = "Chưa được phân quyền bộ phận"
                };
            scopeFilter = OTScopeFilterHelper.ForScopeByTuple(callerEmpCd, empAlias: "B", prefix: "SC");
        }

        int offset = (page - 1) * pageSize;
        int maxRn  = offset + pageSize;
        string searchPattern = string.IsNullOrEmpty(search) ? "%" : "%" + search.ToUpper() + "%";

        // MISSING = ERP thiếu, chưa từng có ai khởi tạo record app. Còn lại lấy CONFIRM_STATUS của app.
        const string statusExpr = "NVL(CF.CONFIRM_STATUS, 'MISSING')";

        string fromSql = @"
            FROM (SELECT * FROM HRMS.ADD_TIME WHERE DAT BETWEEN :D_FROM AND :D_TO) A
            JOIN HRMS.ECM100 B ON B.EMPCD = A.EMPCD
            JOIN HRMS.EAM410 C ON C.DEPTCD = B.DEPTCD AND C.LINECD = B.LINECD AND C.WORKCD = B.WORKCD
            LEFT JOIN HRMS.EBM100 S ON S.SHIFTCD = A.SHIFTCD
            LEFT JOIN HRMS.HR_ATT_CONFIRM CF ON CF.EMPCD = A.EMPCD AND TRUNC(CF.WORK_DATE) = TRUNC(A.DAT)";

        string whereSql = @"
            WHERE " + ValidReasonSqlFilter + @"
              AND (B.RETDAT IS NULL OR B.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              AND (:DID_FLAG IS NULL OR B.DEPTCD = :DID_VAL)
              AND (:LID_FLAG IS NULL OR B.LINECD = :LID_VAL)
              AND (:WID_FLAG IS NULL OR B.WORKCD = :WID_VAL)
              AND (:S_FLAG   IS NULL OR UPPER(A.EMPCD) LIKE :S_VAL)
              AND (:ST_FLAG  IS NULL OR (" + statusExpr + @") = :ST_VAL)
              AND (:MT_FLAG  IS NULL OR (" + MissingTypeSqlExpr + @") = :MT_VAL)
              " + (scopeFilter?.SqlClause ?? "");

        var baseParams = new List<OracleParameter>
        {
            new OracleParameter("D_FROM",  OracleDbType.Date)     { Value = fromDate.Date },
            new OracleParameter("D_TO",    OracleDbType.Date)     { Value = toDate.Date },
            new OracleParameter("DID_FLAG",OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(deptId) ? null : "Y") ?? DBNull.Value },
            new OracleParameter("DID_VAL", OracleDbType.Varchar2) { Value = (object?)deptId ?? DBNull.Value },
            new OracleParameter("LID_FLAG",OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(lineId) ? null : "Y") ?? DBNull.Value },
            new OracleParameter("LID_VAL", OracleDbType.Varchar2) { Value = (object?)lineId ?? DBNull.Value },
            new OracleParameter("WID_FLAG",OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(workId) ? null : "Y") ?? DBNull.Value },
            new OracleParameter("WID_VAL", OracleDbType.Varchar2) { Value = (object?)workId ?? DBNull.Value },
            new OracleParameter("S_FLAG",  OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(search) ? null : "Y") ?? DBNull.Value },
            new OracleParameter("S_VAL",   OracleDbType.Varchar2) { Value = searchPattern },
            new OracleParameter("ST_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(status) ? null : "Y") ?? DBNull.Value },
            new OracleParameter("ST_VAL",  OracleDbType.Varchar2) { Value = (object?)status ?? DBNull.Value },
            new OracleParameter("MT_FLAG", OracleDbType.Varchar2) { Value = (object?)(string.IsNullOrEmpty(missingType) ? null : "Y") ?? DBNull.Value },
            new OracleParameter("MT_VAL",  OracleDbType.Varchar2) { Value = (object?)missingType ?? DBNull.Value },
        };
        if (scopeFilter != null) baseParams.AddRange(scopeFilter.Params);

        string sqlSummary = @"
            SELECT COUNT(*) TOTAL,
                   SUM(CASE WHEN (" + statusExpr + @") = 'MISSING'         THEN 1 ELSE 0 END) MISSING,
                   SUM(CASE WHEN (" + statusExpr + @") = 'PENDING_WORKER'  THEN 1 ELSE 0 END) PENDING_WORKER,
                   SUM(CASE WHEN (" + statusExpr + @") = 'PENDING_MANAGER' THEN 1 ELSE 0 END) PENDING_MANAGER,
                   SUM(CASE WHEN (" + statusExpr + @") = 'CONFIRMED'       THEN 1 ELSE 0 END) CONFIRMED
            " + fromSql + whereSql;

        var summaryRows = await _oracleService.ExecuteQueryAsync(sqlSummary, r => new
        {
            TOTAL           = r["TOTAL"] == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
            MISSING         = r["MISSING"] == DBNull.Value ? 0 : Convert.ToInt32(r["MISSING"]),
            PENDING_WORKER  = r["PENDING_WORKER"]  == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING_WORKER"]),
            PENDING_MANAGER = r["PENDING_MANAGER"] == DBNull.Value ? 0 : Convert.ToInt32(r["PENDING_MANAGER"]),
            CONFIRMED       = r["CONFIRMED"] == DBNull.Value ? 0 : Convert.ToInt32(r["CONFIRMED"]),
        }, baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());

        var summary = summaryRows.FirstOrDefault() ?? new { TOTAL = 0, MISSING = 0, PENDING_WORKER = 0, PENDING_MANAGER = 0, CONFIRMED = 0 };

        if (summary.TOTAL == 0)
            return new AttendanceMissingListResponse { success = true, summary = summary, total = 0, page = page, page_size = pageSize, total_pages = 0, can_confirm = canConfirm };

        string sqlData = @"
            SELECT * FROM (
                SELECT T.*, ROW_NUMBER() OVER (ORDER BY DECODE(CONFIRM_STATUS,'MISSING',1,'PENDING_MANAGER',2,'PENDING_WORKER',3,4), DEPT_ID, EMPCD, WORK_DATE) RN
                FROM (
                    SELECT A.EMPCD, B.CNAME EMP_NAME, TO_CHAR(A.DAT,'YYYY-MM-DD') WORK_DATE,
                           B.DEPTCD DEPT_ID, C.DEPTNM DEPT_NAME, B.LINECD LINE_ID, C.TEAMNM LINE_NAME,
                           B.WORKCD WORK_ID, C.WORKNM WORK_NAME,
                           -- ADD_TIME.TIME_IN/TIME_OUT HẦU NHƯ KHÔNG BAO GIỜ NULL — HR đã tự điền sẵn
                           -- giờ mặc định (giờ ca chuẩn) cho phía thiếu trước khi lưu. REASON mới là
                           -- tín hiệu thật; chỉ tin giờ ERP ở phía REASON xác nhận là giờ quẹt thẻ thật.
                           CASE WHEN UPPER(TRIM(A.REASON)) = 'NOT TIME OUT' THEN A.TIME_IN  ELSE NULL END ERP_TIME_IN,
                           CASE WHEN UPPER(TRIM(A.REASON)) = 'NOT TIME IN'  THEN A.TIME_OUT ELSE NULL END ERP_TIME_OUT,
                           A.SHIFTCD, S.STIME, S.ETIME, A.REASON, A.OVER_TIME,
                           CF.ID CONFIRM_ID, (" + statusExpr + @") CONFIRM_STATUS,
                           CF.WORKER_TIME_IN, CF.WORKER_TIME_OUT, CF.CONFIRM_TIME_IN, CF.CONFIRM_TIME_OUT,
                           CF.SHIFT_TYPE, CF.WORKER_NOTE, CF.NOTE, CF.REQUESTED_BY, CF.REQUESTED_DATE,
                           CF.CONFIRMED_BY, CF.CONFIRMED_DATE, CB.CNAME CONFIRMED_BY_NAME
                    " + fromSql + @"
                    LEFT JOIN HRMS.ECM100 CB ON CB.EMPCD = CF.CONFIRMED_BY
                    " + whereSql + @"
                ) T
            ) WHERE RN > :R_MIN AND RN <= :R_MAX";

        var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
        dataParams.Add(new OracleParameter("R_MIN", offset));
        dataParams.Add(new OracleParameter("R_MAX", maxRn));

        var rows = await _oracleService.ExecuteQueryAsync(sqlData, r =>
        {
            string? erpIn  = FmtTime(r["ERP_TIME_IN"]);
            string? erpOut = FmtTime(r["ERP_TIME_OUT"]);
            string missingType = erpIn == null && erpOut == null ? "BOTH" : erpIn == null ? "IN" : "OUT";
            var (shiftStime, shiftEtime, isNight) = ComputeShiftDisplay(r["STIME"], r["ETIME"]);

            return new AttendanceMissingItem
            {
                EMPCD            = r["EMPCD"]?.ToString() ?? "",
                EMP_NAME         = r["EMP_NAME"]?.ToString(),
                WORK_DATE        = r["WORK_DATE"]?.ToString() ?? "",
                DEPT_ID          = r["DEPT_ID"]?.ToString(),
                DEPT_NAME        = r["DEPT_NAME"]?.ToString(),
                LINE_ID          = r["LINE_ID"]?.ToString(),
                LINE_NAME        = r["LINE_NAME"]?.ToString(),
                WORK_ID          = r["WORK_ID"]?.ToString(),
                WORK_NAME        = r["WORK_NAME"]?.ToString(),
                ERP_TIME_IN      = erpIn,
                ERP_TIME_OUT     = erpOut,
                MISSING_TYPE     = missingType,
                SHIFT_CD         = r["SHIFTCD"]?.ToString(),
                SHIFT_STIME      = shiftStime,
                SHIFT_ETIME      = shiftEtime,
                IS_NIGHT_SHIFT   = isNight,
                REASON           = r["REASON"]?.ToString(),
                OVER_TIME        = r["OVER_TIME"] == DBNull.Value ? null : Convert.ToDecimal(r["OVER_TIME"]),
                CONFIRM_ID       = r["CONFIRM_ID"] == DBNull.Value ? null : Convert.ToInt32(r["CONFIRM_ID"]),
                CONFIRM_STATUS   = r["CONFIRM_STATUS"]?.ToString(),
                WORKER_TIME_IN   = FmtTime(r["WORKER_TIME_IN"]),
                WORKER_TIME_OUT  = FmtTime(r["WORKER_TIME_OUT"]),
                CONFIRM_TIME_IN  = FmtTime(r["CONFIRM_TIME_IN"]),
                CONFIRM_TIME_OUT = FmtTime(r["CONFIRM_TIME_OUT"]),
                SHIFT_TYPE       = r["SHIFT_TYPE"]?.ToString(),
                WORKER_NOTE      = r["WORKER_NOTE"]?.ToString(),
                NOTE             = r["NOTE"]?.ToString(),
                REQUESTED_BY     = r["REQUESTED_BY"]?.ToString(),
                REQUESTED_DATE   = r["REQUESTED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["REQUESTED_DATE"]).ToString("dd/MM/yyyy HH:mm"),
                CONFIRMED_BY     = r["CONFIRMED_BY"]?.ToString(),
                CONFIRMED_BY_NAME= r["CONFIRMED_BY_NAME"]?.ToString(),
                CONFIRMED_DATE   = r["CONFIRMED_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["CONFIRMED_DATE"]).ToString("dd/MM/yyyy HH:mm"),
                TOTAL_COUNT      = summary.TOTAL
            };
        }, dataParams.ToArray());

        return new AttendanceMissingListResponse
        {
            success     = true,
            summary     = summary,
            total       = summary.TOTAL,
            page        = page,
            page_size   = pageSize,
            total_pages = pageSize > 0 ? (int)Math.Ceiling((double)summary.TOTAL / pageSize) : 0,
            can_confirm = canConfirm,
            data        = rows
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NV xem lại chính ngày của mình (WorkerForm) — KHÔNG áp scope quản lý (ai cũng được xem
    // ngày của chính mình, không cần HR_USERS_DEPT).
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AttendanceMissingItem?> GetMyDayAsync(string empcd, DateTime workDate)
    {
        const string sql = @"
            SELECT A.EMPCD, B.CNAME EMP_NAME, TO_CHAR(A.DAT,'YYYY-MM-DD') WORK_DATE,
                   B.DEPTCD DEPT_ID, C.DEPTNM DEPT_NAME, B.LINECD LINE_ID, C.TEAMNM LINE_NAME,
                   B.WORKCD WORK_ID, C.WORKNM WORK_NAME,
                   CASE WHEN UPPER(TRIM(A.REASON)) = 'NOT TIME OUT' THEN A.TIME_IN  ELSE NULL END ERP_TIME_IN,
                   CASE WHEN UPPER(TRIM(A.REASON)) = 'NOT TIME IN'  THEN A.TIME_OUT ELSE NULL END ERP_TIME_OUT,
                   A.SHIFTCD, S.STIME, S.ETIME, A.REASON, A.OVER_TIME,
                   CF.ID CONFIRM_ID, NVL(CF.CONFIRM_STATUS,'MISSING') CONFIRM_STATUS,
                   CF.WORKER_TIME_IN, CF.WORKER_TIME_OUT, CF.CONFIRM_TIME_IN, CF.CONFIRM_TIME_OUT,
                   CF.SHIFT_TYPE, CF.WORKER_NOTE, CF.NOTE, CF.REQUESTED_BY, CF.REQUESTED_DATE, CF.CONFIRMED_BY, CF.CONFIRMED_DATE
            FROM HRMS.ADD_TIME A
            JOIN HRMS.ECM100 B ON B.EMPCD = A.EMPCD
            JOIN HRMS.EAM410 C ON C.DEPTCD = B.DEPTCD AND C.LINECD = B.LINECD AND C.WORKCD = B.WORKCD
            LEFT JOIN HRMS.EBM100 S ON S.SHIFTCD = A.SHIFTCD
            LEFT JOIN HRMS.HR_ATT_CONFIRM CF ON CF.EMPCD = A.EMPCD AND TRUNC(CF.WORK_DATE) = TRUNC(A.DAT)
            WHERE A.EMPCD = :EMPCD AND TRUNC(A.DAT) = :WORK_DATE AND " + ValidReasonSqlFilter + @" AND ROWNUM = 1";

        var rows = await _oracleService.ExecuteQueryAsync(sql, r =>
        {
            string? erpIn  = FmtTime(r["ERP_TIME_IN"]);
            string? erpOut = FmtTime(r["ERP_TIME_OUT"]);
            var (shiftStime, shiftEtime, isNight) = ComputeShiftDisplay(r["STIME"], r["ETIME"]);
            return new AttendanceMissingItem
            {
                EMPCD            = r["EMPCD"]?.ToString() ?? "",
                EMP_NAME         = r["EMP_NAME"]?.ToString(),
                WORK_DATE        = r["WORK_DATE"]?.ToString() ?? "",
                DEPT_ID          = r["DEPT_ID"]?.ToString(),
                DEPT_NAME        = r["DEPT_NAME"]?.ToString(),
                LINE_ID          = r["LINE_ID"]?.ToString(),
                LINE_NAME        = r["LINE_NAME"]?.ToString(),
                WORK_ID          = r["WORK_ID"]?.ToString(),
                WORK_NAME        = r["WORK_NAME"]?.ToString(),
                ERP_TIME_IN      = erpIn,
                ERP_TIME_OUT     = erpOut,
                MISSING_TYPE     = erpIn == null && erpOut == null ? "BOTH" : erpIn == null ? "IN" : "OUT",
                SHIFT_CD         = r["SHIFTCD"]?.ToString(),
                SHIFT_STIME      = shiftStime,
                SHIFT_ETIME      = shiftEtime,
                IS_NIGHT_SHIFT   = isNight,
                REASON           = r["REASON"]?.ToString(),
                OVER_TIME        = r["OVER_TIME"] == DBNull.Value ? null : Convert.ToDecimal(r["OVER_TIME"]),
                CONFIRM_ID       = r["CONFIRM_ID"] == DBNull.Value ? null : Convert.ToInt32(r["CONFIRM_ID"]),
                CONFIRM_STATUS   = r["CONFIRM_STATUS"]?.ToString(),
                WORKER_TIME_IN   = FmtTime(r["WORKER_TIME_IN"]),
                WORKER_TIME_OUT  = FmtTime(r["WORKER_TIME_OUT"]),
                CONFIRM_TIME_IN  = FmtTime(r["CONFIRM_TIME_IN"]),
                CONFIRM_TIME_OUT = FmtTime(r["CONFIRM_TIME_OUT"]),
                SHIFT_TYPE       = r["SHIFT_TYPE"]?.ToString(),
                WORKER_NOTE      = r["WORKER_NOTE"]?.ToString(),
                NOTE             = r["NOTE"]?.ToString(),
                REQUESTED_BY     = r["REQUESTED_BY"]?.ToString(),
                CONFIRMED_BY     = r["CONFIRMED_BY"]?.ToString(),
            };
        },
        new OracleParameter("EMPCD", empcd),
        new OracleParameter("WORK_DATE", workDate.Date));

        var item = rows.FirstOrDefault();
        if (item == null) return null;

        // Chưa từng khai (SHIFT_TYPE null) — auto-detect ngay từ ERP để WorkerForm hiện được luôn,
        // không phải đợi tới lúc submit mới biết loại ca.
        var (shiftType, otBeforeHours, otAfterHours) = await GetShiftTypeInfoAsync(empcd, workDate);
        if (string.IsNullOrEmpty(item.SHIFT_TYPE))
            item.SHIFT_TYPE = shiftType;

        // Gợi ý giờ mặc định = giờ ca làm việc (STIME/ETIME), cộng thêm giờ tăng ca trước/sau nếu
        // ERP có log — NV chỉ cần xác nhận thay vì gõ tay từ đầu (yêu cầu 2026-09-18).
        if (item.ERP_TIME_IN == null && !string.IsNullOrEmpty(item.SHIFT_STIME) &&
            DateTime.TryParseExact(workDate.ToString("yyyyMMdd") + item.SHIFT_STIME.Replace(":", ""), "yyyyMMddHHmm", null,
                System.Globalization.DateTimeStyles.None, out var baseIn))
        {
            if (shiftType == "OT_BEFORE" && otBeforeHours > 0) baseIn = baseIn.AddHours((double)-otBeforeHours);
            item.SUGGESTED_TIME_IN = baseIn.ToString("HH:mm");
        }
        if (item.ERP_TIME_OUT == null && !string.IsNullOrEmpty(item.SHIFT_ETIME) &&
            DateTime.TryParseExact(workDate.ToString("yyyyMMdd") + item.SHIFT_ETIME.Replace(":", ""), "yyyyMMddHHmm", null,
                System.Globalization.DateTimeStyles.None, out var baseOut))
        {
            if (item.IS_NIGHT_SHIFT) baseOut = baseOut.AddDays(1);
            if (shiftType == "OT_AFTER" && otAfterHours > 0) baseOut = baseOut.AddHours((double)otAfterHours);
            item.SUGGESTED_TIME_OUT = baseOut.ToString("HH:mm");
        }

        return item;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Các ngày công nhân CẦN tự khai/khai lại (MISSING/PENDING_WORKER/REJECTED) — nguồn cho badge
    // menu sidebar + list gợi ý ngày trên WorkerForm (không cần scope quản lý, chỉ đúng chính NV).
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<List<MyPendingDayItem>> GetMyPendingDaysAsync(string empcd, int lookbackDays = 60)
    {
        if (string.IsNullOrEmpty(empcd)) return new();

        const string sql = @"
            SELECT TO_CHAR(A.DAT,'YYYY-MM-DD') WORK_DATE,
                   NVL(CF.CONFIRM_STATUS,'MISSING') CONFIRM_STATUS,
                   A.REASON
            FROM HRMS.ADD_TIME A
            LEFT JOIN HRMS.HR_ATT_CONFIRM CF ON CF.EMPCD = A.EMPCD AND TRUNC(CF.WORK_DATE) = TRUNC(A.DAT)
            WHERE A.EMPCD = :EMPCD
              AND A.DAT BETWEEN :D_FROM AND :D_TO
              AND " + ValidReasonSqlFilter + @"
              AND NVL(CF.CONFIRM_STATUS,'MISSING') IN ('MISSING','PENDING_WORKER','REJECTED')
            ORDER BY A.DAT DESC";

        return await _oracleService.ExecuteQueryAsync(sql, r =>
        {
            var (inMissing, outMissing) = ClassifyReason(r["REASON"]?.ToString());
            return new MyPendingDayItem
            {
                WORK_DATE      = r["WORK_DATE"]?.ToString() ?? "",
                CONFIRM_STATUS = r["CONFIRM_STATUS"]?.ToString() ?? "MISSING",
                MISSING_TYPE   = inMissing && outMissing ? "BOTH" : inMissing ? "IN" : "OUT"
            };
        },
        new OracleParameter("EMPCD",  empcd),
        new OracleParameter("D_FROM", DateTime.Today.AddDays(-lookbackDays)),
        new OracleParameter("D_TO",   DateTime.Today));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bước 1 — công nhân khai giờ vào/ra thực tế (tự khởi tạo từ lịch Home, hoặc sau khi
    // được HR/Clerk/Admin gửi yêu cầu — record đã tồn tại ở PENDING_WORKER).
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AttendanceConfirmActionResult> SubmitWorkerConfirmAsync(WorkerSubmitRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EMPCD) ||
            !DateTime.TryParseExact(req.WORK_DATE, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var workDate))
            return Fail(req.EMPCD, req.WORK_DATE, "Thông tin không hợp lệ");

        if (string.IsNullOrEmpty(req.TIME_IN) && string.IsNullOrEmpty(req.TIME_OUT))
            return Fail(req.EMPCD, req.WORK_DATE, "Vui lòng nhập ít nhất 1 giờ vào/ra");

        var shift = await _shiftLookup.GetShiftForDateAsync(req.EMPCD, workDate);
        bool isNightShift = shift != null && int.TryParse(shift.STIME, out var st) && int.TryParse(shift.ETIME, out var et) && st > et;

        // Loại ca + số giờ tăng ca tự detect từ log trên ERP (EBM300/EBM300_WAIT.OT_BEFORE/OT_AFTER)
        // — KHÔNG để công nhân tự chọn, tránh chọn sai (yêu cầu 2026-09-18: "có tăng ca là có log
        // trên ERP, check thôi chứ đâu cần công nhân chọn").
        var (shiftType, otBeforeHours, otAfterHours) = await GetShiftTypeInfoAsync(req.EMPCD, workDate);

        DateTime? timeIn  = CombineTime(workDate, req.TIME_IN);
        DateTime? timeOut = CombineTime(workDate, req.TIME_OUT);
        // Ca đêm: giờ ra qua nửa đêm nếu bé hơn giờ vào (giống fix GatePass).
        if (isNightShift && timeIn.HasValue && timeOut.HasValue && timeOut < timeIn)
            timeOut = timeOut.Value.AddDays(1);

        // Chặn giờ không hợp lý so với ca làm việc thật trên ERP — vd ca hành chính 7h30 mà khai
        // giờ vào 7h25 là sai (không có log tăng ca trước ca thì không được vào sớm hơn giờ ca).
        // Chỉ áp cho phía ERP đang thật sự thiếu — phía ERP đã có sẵn (REASON không chỉ ra) thì tin
        // theo ERP, không so lại. Dựa vào REASON (ClassifyReason), KHÔNG dựa TIME_IN/TIME_OUT IS
        // NULL — ADD_TIME hầu như luôn có sẵn giờ (HR đã điền mặc định giờ ca cho phía thiếu).
        var erpReasonRow = (await _oracleService.ExecuteQueryAsync(
            "SELECT REASON FROM HRMS.ADD_TIME WHERE EMPCD = :E AND TRUNC(DAT) = :WD AND ROWNUM = 1",
            r => r["REASON"]?.ToString(),
            new OracleParameter("E", req.EMPCD), new OracleParameter("WD", workDate.Date))).FirstOrDefault();
        var (erpInMissing, erpOutMissing) = ClassifyReason(erpReasonRow);

        if (erpInMissing && timeIn.HasValue && shift != null && int.TryParse(shift.STIME, out _) &&
            DateTime.TryParseExact(workDate.ToString("yyyyMMdd") + shift.STIME.PadLeft(4, '0'), "yyyyMMddHHmm", null,
                System.Globalization.DateTimeStyles.None, out var minIn))
        {
            if (shiftType == "OT_BEFORE" && otBeforeHours > 0) minIn = minIn.AddHours((double)-otBeforeHours);
            if (timeIn.Value < minIn)
                return Fail(req.EMPCD, req.WORK_DATE,
                    $"Giờ vào không được sớm hơn {minIn:HH:mm}" + (shiftType == "OT_BEFORE" ? " (đã cộng giờ tăng ca trước ca theo ERP)" : " (giờ bắt đầu ca làm việc theo ERP)"));
        }
        if (erpOutMissing && timeOut.HasValue && shift != null && int.TryParse(shift.ETIME, out _) &&
            DateTime.TryParseExact(workDate.ToString("yyyyMMdd") + shift.ETIME.PadLeft(4, '0'), "yyyyMMddHHmm", null,
                System.Globalization.DateTimeStyles.None, out var maxOut))
        {
            if (isNightShift) maxOut = maxOut.AddDays(1);
            if (shiftType == "OT_AFTER" && otAfterHours > 0) maxOut = maxOut.AddHours((double)otAfterHours);
            if (timeOut.Value > maxOut)
                return Fail(req.EMPCD, req.WORK_DATE,
                    $"Giờ ra không được muộn hơn {maxOut:HH:mm}" + (shiftType == "OT_AFTER" ? " (đã cộng giờ tăng ca sau ca theo ERP)" : " (giờ kết thúc ca làm việc theo ERP)"));
        }

        var existing = (await _oracleService.ExecuteQueryAsync(
            "SELECT ID, CONFIRM_STATUS FROM HRMS.HR_ATT_CONFIRM WHERE EMPCD = :E AND TRUNC(WORK_DATE) = :WD AND ROWNUM = 1",
            r => new { ID = Convert.ToInt32(r["ID"]), STATUS = r["CONFIRM_STATUS"]?.ToString() },
            new OracleParameter("E", req.EMPCD), new OracleParameter("WD", workDate.Date))).FirstOrDefault();

        int confirmId;
        string? oldStatus = existing?.STATUS;

        if (existing == null)
        {
            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_ATT_CONFIRM
                    (EMPCD, WORK_DATE, SHIFT_CD, SHIFT_TYPE, WORKER_TIME_IN, WORKER_TIME_OUT, WORKER_NOTE, CONFIRM_STATUS, INST_ID)
                VALUES
                    (:EMPCD, :WORK_DATE, :SHIFT_CD, :SHIFT_TYPE, :TIME_IN, :TIME_OUT, :WNOTE, 'PENDING_MANAGER', :EMPCD2)
                RETURNING ID INTO :OUT_ID",
                new OracleParameter("EMPCD",      req.EMPCD),
                new OracleParameter("WORK_DATE",  workDate.Date),
                new OracleParameter("SHIFT_CD",   (object?)shift?.SHIFTCD ?? DBNull.Value),
                new OracleParameter("SHIFT_TYPE", shiftType),
                new OracleParameter("TIME_IN",    (object?)timeIn  ?? DBNull.Value),
                new OracleParameter("TIME_OUT",   (object?)timeOut ?? DBNull.Value),
                new OracleParameter("WNOTE",      (object?)req.NOTE ?? DBNull.Value),
                new OracleParameter("EMPCD2",     req.EMPCD),
                outIdParam);
            confirmId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull ? decimal.ToInt32(od.Value) : 0;
        }
        else
        {
            if (existing.STATUS == "CONFIRMED")
                return Fail(req.EMPCD, req.WORK_DATE, "Ngày này đã được xác nhận rồi");

            confirmId = existing.ID;
            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_ATT_CONFIRM
                   SET SHIFT_CD = :SHIFT_CD, SHIFT_TYPE = :SHIFT_TYPE,
                       WORKER_TIME_IN = :TIME_IN, WORKER_TIME_OUT = :TIME_OUT, WORKER_NOTE = :WNOTE,
                       CONFIRM_STATUS = 'PENDING_MANAGER', UPDT_ID = :EMPCD
                 WHERE ID = :ID",
                new OracleParameter("SHIFT_CD",   (object?)shift?.SHIFTCD ?? DBNull.Value),
                new OracleParameter("SHIFT_TYPE", shiftType),
                new OracleParameter("TIME_IN",    (object?)timeIn  ?? DBNull.Value),
                new OracleParameter("TIME_OUT",   (object?)timeOut ?? DBNull.Value),
                new OracleParameter("WNOTE",      (object?)req.NOTE ?? DBNull.Value),
                new OracleParameter("EMPCD",      req.EMPCD),
                new OracleParameter("ID",         confirmId));
        }

        _log.Log(AttendanceConfirmLogHelper.LogAction.WORKER_SUBMIT, confirmId, req.EMPCD, workDate,
            oldStatus, "PENDING_MANAGER", $"NV khai {req.TIME_IN}-{req.TIME_OUT}", req.EMPCD);

        var empName = await _notiHelper.GetEmpNameAsync(req.EMPCD);
        _notiSvc.AttendanceConfirmSubmitted(req.EMPCD, empName, workDate);

        return new AttendanceConfirmActionResult { EMPCD = req.EMPCD, WORK_DATE = req.WORK_DATE, OK = true, MESSAGE = "Đã gửi khai báo, chờ quản lý xác nhận" };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bước 2 — CHỈ quản lý đúng scope dept/line/work của NV mới được xác nhận (yêu cầu HR
    // 2026-09-18: "hr không có thao tác, chỉ công nhân + quản lý mới đúng quy trình"). Admin/HR
    // KHÔNG còn được xác nhận thay nữa — chỉ xem danh sách/xuất Excel/gửi nhắc (RequestConfirmBulkAsync).
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AttendanceConfirmActionResult> ManagerConfirmAsync(ManagerConfirmRequest req)
    {
        var existing = (await _oracleService.ExecuteQueryAsync(
            "SELECT ID, EMPCD, WORK_DATE, CONFIRM_STATUS FROM HRMS.HR_ATT_CONFIRM WHERE ID = :ID AND ROWNUM = 1",
            r => new
            {
                ID       = Convert.ToInt32(r["ID"]),
                EMPCD    = r["EMPCD"]?.ToString() ?? "",
                WorkDate = Convert.ToDateTime(r["WORK_DATE"]),
                STATUS   = r["CONFIRM_STATUS"]?.ToString()
            },
            new OracleParameter("ID", req.CONFIRM_ID))).FirstOrDefault();

        if (existing == null) return Fail("", null, "Không tìm thấy yêu cầu xác nhận");
        if (existing.STATUS == "PENDING_WORKER") return Fail(existing.EMPCD, null, "Nhân viên chưa khai giờ, chưa thể xác nhận");

        bool inScope = (await _oracleService.ExecuteQueryAsync(@"
            SELECT 1 X FROM HRMS.ECM100 EC
            WHERE EC.EMPCD = :TARGET
              AND (EC.DEPTCD, EC.LINECD, EC.WORKCD) IN (
                  SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :ACTOR)
              AND ROWNUM = 1",
            r => 1,
            new OracleParameter("TARGET", existing.EMPCD),
            new OracleParameter("ACTOR", req.ACTOR_EMPCD))).Any();

        if (!inScope) return Fail(existing.EMPCD, null, "Bạn không thuộc phạm vi quản lý của nhân viên này");

        var actorRole = await GetRoleNameAsync(req.ACTOR_EMPCD);
        if (!RoleHierarchyHelper.HasApprovalPermission(actorRole))
            return Fail(existing.EMPCD, null, "Bạn không có quyền xác nhận");

        string newStatus = req.STATUS == "REJECTED" ? "REJECTED" : "CONFIRMED";
        DateTime? timeIn  = CombineTime(existing.WorkDate, req.TIME_IN);
        DateTime? timeOut = CombineTime(existing.WorkDate, req.TIME_OUT);
        if (timeIn.HasValue && timeOut.HasValue && timeOut < timeIn) timeOut = timeOut.Value.AddDays(1);

        await _oracleService.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_ATT_CONFIRM
               SET CONFIRM_TIME_IN = :TIME_IN, CONFIRM_TIME_OUT = :TIME_OUT, NOTE = :NOTE,
                   CONFIRM_STATUS = :STATUS, CONFIRMED_BY = :ACTOR, CONFIRMED_DATE = SYSDATE, UPDT_ID = :ACTOR2
             WHERE ID = :ID",
            new OracleParameter("TIME_IN",  (object?)timeIn  ?? DBNull.Value),
            new OracleParameter("TIME_OUT", (object?)timeOut ?? DBNull.Value),
            new OracleParameter("NOTE",     (object?)req.NOTE ?? DBNull.Value),
            new OracleParameter("STATUS",   newStatus),
            new OracleParameter("ACTOR",    req.ACTOR_EMPCD),
            new OracleParameter("ACTOR2",   req.ACTOR_EMPCD),
            new OracleParameter("ID",       req.CONFIRM_ID));

        _log.Log(newStatus == "CONFIRMED" ? AttendanceConfirmLogHelper.LogAction.MANAGER_CONFIRM : AttendanceConfirmLogHelper.LogAction.MANAGER_REJECT,
            req.CONFIRM_ID, existing.EMPCD, existing.WorkDate, existing.STATUS, newStatus, req.NOTE, req.ACTOR_EMPCD);

        _notiSvc.AttendanceConfirmResult(existing.EMPCD, req.ACTOR_EMPCD, existing.WorkDate, newStatus);

        return new AttendanceConfirmActionResult
        {
            EMPCD = existing.EMPCD, WORK_DATE = existing.WorkDate.ToString("yyyy-MM-dd"), OK = true,
            MESSAGE = newStatus == "CONFIRMED" ? "Đã xác nhận" : "Đã từ chối"
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Admin/HR/Clerk gửi yêu cầu xác nhận tới 1 NV cho 1 ngày thiếu chấm công.
    // Mirror OTController.RequestSuppOneAsync — KHÔNG đụng ERP.
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<AttendanceConfirmResponse> RequestConfirmBulkAsync(List<RequestConfirmRequest> items)
    {
        var res = new AttendanceConfirmResponse { success = true };
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.EMPCD) ||
                !DateTime.TryParseExact(it.WORK_DATE, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var workDate))
            {
                res.failed++;
                res.results.Add(new AttendanceConfirmActionResult { EMPCD = it.EMPCD, WORK_DATE = it.WORK_DATE, OK = false, MESSAGE = "Thông tin không hợp lệ" });
                continue;
            }
            await RequestConfirmOneAsync(it.EMPCD.Trim(), workDate, it.ACTOR_EMPCD, res);
        }
        res.message = $"Gửi yêu cầu xác nhận: OK {res.processed}, Bỏ qua {res.skipped}, Lỗi {res.failed}";
        return res;
    }

    private async Task RequestConfirmOneAsync(string empcd, DateTime workDate, string actorEmpCd, AttendanceConfirmResponse res)
    {
        try
        {
            // REASON là tín hiệu thật (xem ClassifyReason) — ADD_TIME.TIME_IN/TIME_OUT hầu như
            // không bao giờ NULL vì HR đã tự điền giờ mặc định cho phía thiếu trước khi lưu.
            var erpReason = (await _oracleService.ExecuteQueryAsync(
                "SELECT REASON FROM HRMS.ADD_TIME WHERE EMPCD = :E AND TRUNC(DAT) = :WD AND ROWNUM = 1",
                r => r["REASON"]?.ToString(),
                new OracleParameter("E", empcd), new OracleParameter("WD", workDate.Date))).FirstOrDefault();
            var (reasonInMissing, reasonOutMissing) = ClassifyReason(erpReason);

            if (!reasonInMissing && !reasonOutMissing)
            {
                res.failed++;
                res.results.Add(new AttendanceConfirmActionResult { EMPCD = empcd, WORK_DATE = workDate.ToString("yyyy-MM-dd"), OK = false, MESSAGE = "Ngày này không thiếu chấm công trên ERP" });
                return;
            }

            var existing = (await _oracleService.ExecuteQueryAsync(
                "SELECT ID, CONFIRM_STATUS FROM HRMS.HR_ATT_CONFIRM WHERE EMPCD = :E AND TRUNC(WORK_DATE) = :WD AND ROWNUM = 1",
                r => new { ID = Convert.ToInt32(r["ID"]), STATUS = r["CONFIRM_STATUS"]?.ToString() },
                new OracleParameter("E", empcd), new OracleParameter("WD", workDate.Date))).FirstOrDefault();

            if (existing != null)
            {
                res.skipped++;
                res.results.Add(new AttendanceConfirmActionResult
                {
                    EMPCD = empcd, WORK_DATE = workDate.ToString("yyyy-MM-dd"), OK = false, SKIPPED = true,
                    MESSAGE = existing.STATUS == "CONFIRMED" ? "Đã xác nhận rồi" : "Đã có yêu cầu đang chờ xử lý"
                });
                return;
            }

            var shift = await _shiftLookup.GetShiftForDateAsync(empcd, workDate);
            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_ATT_CONFIRM
                    (EMPCD, WORK_DATE, SHIFT_CD, CONFIRM_STATUS, REQUESTED_BY, REQUESTED_DATE, INST_ID)
                VALUES
                    (:EMPCD, :WORK_DATE, :SHIFT_CD, 'PENDING_WORKER', :REQBY, SYSDATE, :REQBY2)
                RETURNING ID INTO :OUT_ID",
                new OracleParameter("EMPCD",     empcd),
                new OracleParameter("WORK_DATE", workDate.Date),
                new OracleParameter("SHIFT_CD",  (object?)shift?.SHIFTCD ?? DBNull.Value),
                new OracleParameter("REQBY",     actorEmpCd),
                new OracleParameter("REQBY2",    actorEmpCd),
                outIdParam);
            int confirmId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull ? decimal.ToInt32(od.Value) : 0;

            _log.Log(AttendanceConfirmLogHelper.LogAction.REQUEST_SENT, confirmId, empcd, workDate, null, "PENDING_WORKER", null, actorEmpCd);
            _notiSvc.AttendanceConfirmRequested(empcd, actorEmpCd, workDate);

            res.processed++;
            res.results.Add(new AttendanceConfirmActionResult { EMPCD = empcd, WORK_DATE = workDate.ToString("yyyy-MM-dd"), OK = true, MESSAGE = "Đã gửi yêu cầu xác nhận" });
        }
        catch (Exception ex)
        {
            res.failed++;
            res.results.Add(new AttendanceConfirmActionResult { EMPCD = empcd, WORK_DATE = workDate.ToString("yyyy-MM-dd"), OK = false, MESSAGE = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> IsAdminOrHRAsync(string? empCd)
    {
        var role = await GetRoleNameAsync(empCd);
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "HR", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> GetRoleNameAsync(string? empCd)
    {
        if (string.IsNullOrEmpty(empCd)) return null;
        var roleRows = await _oracleService.ExecuteQueryAsync(@"
            SELECT RR.ROLE_NAME FROM HRMS.HR_USERS U
            LEFT JOIN HRMS.HR_ROLES RR ON RR.ID = U.ROLE_ID
            WHERE U.EMPCD = :EMPCD AND ROWNUM = 1",
            r => r["ROLE_NAME"]?.ToString(),
            new OracleParameter("EMPCD", empCd));
        return roleRows.FirstOrDefault();
    }

    private static DateTime? CombineTime(DateTime date, string? hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm)) return null;
        hhmm = hhmm.Trim();
        if (TimeSpan.TryParse(hhmm, out var ts)) return date.Date.Add(ts);
        if (hhmm.Length == 4 && int.TryParse(hhmm, out _))
            return DateTime.ParseExact(date.ToString("yyyyMMdd") + hhmm, "yyyyMMddHHmm", null);
        return null;
    }

    // ADD_TIME.TIME_IN/TIME_OUT có thể là DATE (timestamp) hoặc VARCHAR2 (HHmm) tuỳ cấu hình ERP —
    // xử lý cả 2 dạng để không phụ thuộc kiểu cột thật.
    private static string? FmtTime(object val)
    {
        if (val == null || val == DBNull.Value) return null;
        if (val is DateTime dt) return dt.ToString("HH:mm");
        var s = val.ToString()?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Length == 4 && s.All(char.IsDigit)) return s.Insert(2, ":");
        if (DateTime.TryParse(s, out var parsed)) return parsed.ToString("HH:mm");
        return s;
    }

    // ADD_TIME.REASON là text tự do HR gõ tay suốt nhiều năm (142k dòng, 142 giá trị khác nhau, đủ
    // kiểu note như "may sai gio", "wfh"...) — CHỈ 3 giá trị dưới đây là case app xử lý (yêu cầu
    // 2026-09-18); "New Commer (not In Out)" và "Delete Time In or Time Out" tuy cũng phổ biến nhưng
    // KHÔNG thuộc luồng xác nhận này. So sánh UPPER+TRIM để bắt lỗi gõ hoa/thường (thực tế có "Not
    // Time in"/"Not Time out" viết sai) — khớp ValidReasonSqlFilter dùng lọc ở SQL. TIME_IN/TIME_OUT
    // hầu như không bao giờ NULL vì HR đã tự điền giờ mặc định (giờ ca chuẩn) cho phía thiếu trước
    // khi lưu — REASON mới là tín hiệu thật để biết phía nào là giờ giả cần NV xác nhận lại.
    private static (bool inMissing, bool outMissing) ClassifyReason(string? reason) => reason?.Trim().ToUpperInvariant() switch
    {
        "NOT TIME IN"                  => (true, false),
        "NOT TIME OUT"                 => (false, true),
        "NOT TIME IN AND NOT TIME OUT" => (true, true),
        _                              => (false, false) // gồm cả null, New Commer, Delete..., và mọi note tự do khác
    };

    // Tự detect loại ca + số giờ tăng ca từ log thật trên ERP (EBM300/EBM300_WAIT.OT_BEFORE/OT_AFTER)
    // — cùng cách OTController xác định OT_TYPE. KHÔNG hỏi công nhân vì dễ chọn sai, ERP đã có sẵn log.
    private async Task<(string ShiftType, decimal OtBeforeHours, decimal OtAfterHours)> GetShiftTypeInfoAsync(string empcd, DateTime workDate)
    {
        var rows = await _oracleService.ExecuteQueryAsync(@"
            SELECT MAX(OT_BEFORE) OT_BEFORE, MAX(OT_BEFORE_TIME) OT_BEFORE_TIME,
                   MAX(OT_AFTER)  OT_AFTER,  MAX(OT_AFTER_TIME)  OT_AFTER_TIME
            FROM (
                SELECT OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                FROM HRMS.EBM300 WHERE EMPCD = :E1 AND DAT = :D1
                UNION ALL
                SELECT OT_BEFORE, OT_BEFORE_TIME, OT_AFTER, OT_AFTER_TIME
                FROM HRMS.EBM300_WAIT WHERE EMPCD = :E2 AND DAT = :D2
            )",
            r => new
            {
                OT_BEFORE      = r["OT_BEFORE"]?.ToString(),
                OT_BEFORE_TIME = r["OT_BEFORE_TIME"] == DBNull.Value ? 0m : Convert.ToDecimal(r["OT_BEFORE_TIME"]),
                OT_AFTER       = r["OT_AFTER"]?.ToString(),
                OT_AFTER_TIME  = r["OT_AFTER_TIME"]  == DBNull.Value ? 0m : Convert.ToDecimal(r["OT_AFTER_TIME"]),
            },
            new OracleParameter("E1", empcd), new OracleParameter("D1", workDate.Date),
            new OracleParameter("E2", empcd), new OracleParameter("D2", workDate.Date));

        var row = rows.FirstOrDefault();
        if (row == null) return ("REGULAR", 0m, 0m);
        if (row.OT_BEFORE == "Y" || row.OT_BEFORE_TIME > 0) return ("OT_BEFORE", row.OT_BEFORE_TIME, 0m);
        if (row.OT_AFTER  == "Y" || row.OT_AFTER_TIME  > 0) return ("OT_AFTER", 0m, row.OT_AFTER_TIME);
        return ("REGULAR", 0m, 0m);
    }

    // EBM100.STIME/ETIME lưu dạng chuỗi HHmm — tự phát hiện ca đêm (STIME > ETIME), cùng logic với
    // ShiftLookupService/GpRequestForm để hiển thị đúng badge "(ca đêm)" cho NV khi khai giờ.
    private static (string? stime, string? etime, bool isNight) ComputeShiftDisplay(object stimeRaw, object etimeRaw)
    {
        string? stime = FmtTime(stimeRaw);
        string? etime = FmtTime(etimeRaw);
        bool isNight = stimeRaw != null && stimeRaw != DBNull.Value && etimeRaw != null && etimeRaw != DBNull.Value
            && int.TryParse(stimeRaw.ToString(), out var st) && int.TryParse(etimeRaw.ToString(), out var et) && st > et;
        return (stime, etime, isNight);
    }

    private static AttendanceConfirmActionResult Fail(string empcd, string? workDate, string message)
        => new() { EMPCD = empcd, WORK_DATE = workDate, OK = false, MESSAGE = message };
}
