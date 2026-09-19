using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Gift;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Quản lý Quà (Gift Catalog & Lifecycle). HR tạo danh mục (đơn lẻ/combo) → tạo "đợt quà" +
// import Excel danh sách (EMPCD, ngày nhận, địa điểm) → tới ngày báo lịch Home + push → HR bấm
// "đã phát" rồi "gửi yêu cầu xác nhận" → CÔNG NHÂN PHẢI XÁC NHẬN MỚI ĐƯỢC DÙNG TIẾP APP
// (chặn qua GiftGateFilter bên HR_web, dựa vào GetOldestPendingConfirmIdAsync).
// STATUS tổng (READY/DELIVERED/COMPLETED/EXPIRED) KHÔNG lưu cột — tính live mỗi lần load,
// giống MISSING_TYPE bên AttendanceConfirmService.
public class GiftService
{
    // Vòng đời (chốt 2026-09-19): READY (Sẵn sàng nhận, ngay từ lúc tạo/import — không có mốc
    // "chờ đến ngày" riêng) -> DELIVERED (Đã nhận — HR đã phát nhưng NV CHƯA xác nhận) ->
    // COMPLETED (Hoàn tất — NV đã xác nhận). EXPIRED tách riêng: chưa phát mà đã quá RECEIVE_TO_DATE.
    private const string StatusSqlExpr = @"
        CASE
            WHEN R.CONFIRM_STATUS = 'CONFIRMED' THEN 'COMPLETED'
            WHEN R.DELIVERED_DT IS NOT NULL THEN 'DELIVERED'
            WHEN B.RECEIVE_TO_DATE IS NOT NULL AND TRUNC(SYSDATE) > TRUNC(B.RECEIVE_TO_DATE) THEN 'EXPIRED'
            ELSE 'READY'
        END";

    private readonly OracleService _oracleService;
    private readonly NotificationService _notiSvc;
    private readonly NotificationHelper _notiHelper;
    private readonly GiftLogHelper _log;

    public GiftService(
        OracleService oracleService,
        NotificationService notiSvc,
        NotificationHelper notiHelper,
        GiftLogHelper log)
    {
        _oracleService = oracleService;
        _notiSvc       = notiSvc;
        _notiHelper    = notiHelper;
        _log           = log;
    }

    // ═══════════════════════════════════════════════════════════════
    // DANH MỤC (Category / Item / Combo)
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<GiftCategoryItem>> GetCategoriesAsync(bool activeOnly = false)
    {
        string sql = "SELECT ID, NAME, DISPLAY_ORDER, IS_ACTIVE FROM HRMS.HR_GIFT_CATEGORY" +
                     (activeOnly ? " WHERE IS_ACTIVE = 1" : "") + " ORDER BY DISPLAY_ORDER, NAME";
        return await _oracleService.ExecuteQueryAsync(sql, r => new GiftCategoryItem
        {
            ID            = Convert.ToInt32(r["ID"]),
            NAME          = r["NAME"]?.ToString() ?? "",
            DISPLAY_ORDER = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"]),
            IS_ACTIVE     = r["IS_ACTIVE"]?.ToString() == "1"
        });
    }

    public async Task<GiftActionResult> SaveCategoryAsync(GiftCategoryItem req, string actorEmpCd)
    {
        if (string.IsNullOrWhiteSpace(req.NAME))
            return new GiftActionResult { OK = false, MESSAGE = "Tên phân loại không được trống" };

        if (req.ID > 0)
        {
            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_GIFT_CATEGORY
                   SET NAME = :NAME, DISPLAY_ORDER = :DORDER, IS_ACTIVE = :ACTIVE, UPDT_ID = :ACTOR
                 WHERE ID = :ID",
                new OracleParameter("NAME",   req.NAME),
                new OracleParameter("DORDER", req.DISPLAY_ORDER),
                new OracleParameter("ACTIVE", req.IS_ACTIVE ? 1 : 0),
                new OracleParameter("ACTOR",  actorEmpCd),
                new OracleParameter("ID",     req.ID));
            return new GiftActionResult { RECIPIENT_ID = req.ID, OK = true, MESSAGE = "Đã cập nhật" };
        }

        await _oracleService.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_GIFT_CATEGORY (NAME, DISPLAY_ORDER, IS_ACTIVE, INST_ID)
            VALUES (:NAME, :DORDER, :ACTIVE, :ACTOR)",
            new OracleParameter("NAME",   req.NAME),
            new OracleParameter("DORDER", req.DISPLAY_ORDER),
            new OracleParameter("ACTIVE", req.IS_ACTIVE ? 1 : 0),
            new OracleParameter("ACTOR",  actorEmpCd));
        return new GiftActionResult { OK = true, MESSAGE = "Đã thêm phân loại" };
    }

    public async Task<GiftActionResult> DeleteCategoryAsync(int id)
    {
        var countRows = await _oracleService.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_GIFT_ITEM WHERE CATEGORY_ID = :ID",
            r => Convert.ToInt32(r["CNT"]), new OracleParameter("ID", id));
        int itemCount = countRows.FirstOrDefault();
        if (itemCount > 0)
            return new GiftActionResult { OK = false, MESSAGE = $"Không thể xóa: đang có {itemCount} quà thuộc phân loại này" };

        int n = await _oracleService.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_GIFT_CATEGORY WHERE ID = :ID", new OracleParameter("ID", id));
        return n > 0
            ? new GiftActionResult { RECIPIENT_ID = id, OK = true, MESSAGE = "Đã xóa phân loại" }
            : new GiftActionResult { OK = false, MESSAGE = "Không tìm thấy phân loại" };
    }

    public async Task<List<GiftCatalogItem>> GetCatalogAsync(int? categoryId = null, bool activeOnly = false)
    {
        string sql = @"
            SELECT I.ID, I.CATEGORY_ID, C.NAME CATEGORY_NAME, I.ITEM_NAME, I.ITEM_TYPE, I.IMAGE_PATH, I.IS_ACTIVE
            FROM HRMS.HR_GIFT_ITEM I
            JOIN HRMS.HR_GIFT_CATEGORY C ON C.ID = I.CATEGORY_ID
            WHERE (:CID_FLAG IS NULL OR I.CATEGORY_ID = :CID_VAL)
              AND (:ACT_FLAG IS NULL OR I.IS_ACTIVE = 1)
            ORDER BY C.DISPLAY_ORDER, I.ITEM_NAME";

        var items = await _oracleService.ExecuteQueryAsync(sql, r => new GiftCatalogItem
        {
            ID            = Convert.ToInt32(r["ID"]),
            CATEGORY_ID   = Convert.ToInt32(r["CATEGORY_ID"]),
            CATEGORY_NAME = r["CATEGORY_NAME"]?.ToString(),
            ITEM_NAME     = r["ITEM_NAME"]?.ToString() ?? "",
            ITEM_TYPE     = r["ITEM_TYPE"]?.ToString() ?? "SINGLE",
            IMAGE_PATH    = r["IMAGE_PATH"]?.ToString(),
            IS_ACTIVE     = r["IS_ACTIVE"]?.ToString() == "1"
        },
        new OracleParameter("CID_FLAG", (object?)(categoryId.HasValue ? "Y" : null) ?? DBNull.Value),
        new OracleParameter("CID_VAL",  (object?)categoryId ?? DBNull.Value),
        new OracleParameter("ACT_FLAG", (object?)(activeOnly ? "Y" : null) ?? DBNull.Value));

        if (items.Count == 0) return items;

        var comboIds = items.Where(i => i.ITEM_TYPE == "COMBO").Select(i => i.ID).ToList();
        if (comboIds.Count == 0) return items;

        var detailRows = await _oracleService.ExecuteQueryAsync(@"
            SELECT ID, GIFT_ITEM_ID, COMPONENT_NAME, DISPLAY_ORDER
            FROM HRMS.HR_GIFT_COMBO_DETAIL
            WHERE GIFT_ITEM_ID IN (SELECT COLUMN_VALUE FROM TABLE(SYS.ODCINUMBERLIST(" +
                string.Join(",", comboIds) + @")))
            ORDER BY GIFT_ITEM_ID, DISPLAY_ORDER",
            r => new
            {
                GiftItemId = Convert.ToInt32(r["GIFT_ITEM_ID"]),
                Detail = new GiftComboDetailItem
                {
                    ID             = Convert.ToInt32(r["ID"]),
                    COMPONENT_NAME = r["COMPONENT_NAME"]?.ToString() ?? "",
                    DISPLAY_ORDER  = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"])
                }
            });

        var byItem = detailRows.GroupBy(d => d.GiftItemId).ToDictionary(g => g.Key, g => g.Select(x => x.Detail).ToList());
        foreach (var item in items)
            if (byItem.TryGetValue(item.ID, out var details))
                item.COMBO_DETAILS = details;

        return items;
    }

    public async Task<GiftActionResult> SaveCatalogItemAsync(GiftCatalogSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ITEM_NAME))
            return new GiftActionResult { OK = false, MESSAGE = "Tên quà không được trống" };
        if (req.CATEGORY_ID <= 0)
            return new GiftActionResult { OK = false, MESSAGE = "Vui lòng chọn phân loại" };

        int itemId = req.ID;
        if (itemId > 0)
        {
            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_GIFT_ITEM
                   SET CATEGORY_ID = :CAT, ITEM_NAME = :NAME, ITEM_TYPE = :TYPE, IMAGE_PATH = :IMG, UPDT_ID = :ACTOR
                 WHERE ID = :ID",
                new OracleParameter("CAT",   req.CATEGORY_ID),
                new OracleParameter("NAME",  req.ITEM_NAME),
                new OracleParameter("TYPE",  req.ITEM_TYPE),
                new OracleParameter("IMG",   (object?)req.IMAGE_PATH ?? DBNull.Value),
                new OracleParameter("ACTOR", req.ACTOR_EMPCD),
                new OracleParameter("ID",    itemId));

            await _oracleService.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_GIFT_COMBO_DETAIL WHERE GIFT_ITEM_ID = :ID",
                new OracleParameter("ID", itemId));
        }
        else
        {
            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_GIFT_ITEM (CATEGORY_ID, ITEM_NAME, ITEM_TYPE, IMAGE_PATH, INST_ID)
                VALUES (:CAT, :NAME, :TYPE, :IMG, :ACTOR)
                RETURNING ID INTO :OUT_ID",
                new OracleParameter("CAT",   req.CATEGORY_ID),
                new OracleParameter("NAME",  req.ITEM_NAME),
                new OracleParameter("TYPE",  req.ITEM_TYPE),
                new OracleParameter("IMG",   (object?)req.IMAGE_PATH ?? DBNull.Value),
                new OracleParameter("ACTOR", req.ACTOR_EMPCD),
                outIdParam);
            itemId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull ? decimal.ToInt32(od.Value) : 0;
        }

        if (req.ITEM_TYPE == "COMBO")
        {
            int order = 1;
            foreach (var comp in req.COMBO_COMPONENTS.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                await _oracleService.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_GIFT_COMBO_DETAIL (GIFT_ITEM_ID, COMPONENT_NAME, DISPLAY_ORDER)
                    VALUES (:ITEM_ID, :COMP, :DORDER)",
                    new OracleParameter("ITEM_ID", itemId),
                    new OracleParameter("COMP",    comp.Trim()),
                    new OracleParameter("DORDER",  order++));
            }
        }

        return new GiftActionResult { RECIPIENT_ID = itemId, OK = true, MESSAGE = "Đã lưu danh mục quà" };
    }

    // ═══════════════════════════════════════════════════════════════
    // ĐỢT QUÀ (Batch)
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<GiftBatchItem>> GetBatchListAsync(int? batchId = null)
    {
        string sql = @"
            SELECT B.ID, B.BATCH_NAME, B.GIFT_ITEM_ID, I.ITEM_NAME GIFT_ITEM_NAME, C.NAME CATEGORY_NAME,
                   TO_CHAR(B.RECEIVE_FROM_DATE,'YYYY-MM-DD') RECEIVE_FROM_DATE,
                   TO_CHAR(B.RECEIVE_TO_DATE,'YYYY-MM-DD')   RECEIVE_TO_DATE,
                   B.REMARK, B.IS_COMPANY_WIDE, B.LOCATION,
                   B.IS_CLOSED, TO_CHAR(B.CLOSED_DT,'YYYY-MM-DD HH24:MI') CLOSED_DT, B.CLOSED_BY,
                   (SELECT COUNT(*) FROM HRMS.HR_GIFT_RECIPIENT R WHERE R.BATCH_ID = B.ID) TOTAL_RECIPIENT,
                   (SELECT COUNT(*) FROM HRMS.HR_GIFT_RECIPIENT R WHERE R.BATCH_ID = B.ID AND R.DELIVERED_DT IS NOT NULL) DELIVERED_COUNT,
                   (SELECT COUNT(*) FROM HRMS.HR_GIFT_RECIPIENT R WHERE R.BATCH_ID = B.ID AND R.CONFIRM_STATUS = 'CONFIRMED') CONFIRMED_COUNT
            FROM HRMS.HR_GIFT_BATCH B
            JOIN HRMS.HR_GIFT_ITEM I ON I.ID = B.GIFT_ITEM_ID
            JOIN HRMS.HR_GIFT_CATEGORY C ON C.ID = I.CATEGORY_ID"
            + (batchId.HasValue ? " WHERE B.ID = :BID" : "") + @"
            ORDER BY B.INST_DT DESC";

        var ps = batchId.HasValue ? new[] { new OracleParameter("BID", batchId.Value) } : Array.Empty<OracleParameter>();

        return await _oracleService.ExecuteQueryAsync(sql, r => new GiftBatchItem
        {
            ID                = Convert.ToInt32(r["ID"]),
            BATCH_NAME        = r["BATCH_NAME"]?.ToString() ?? "",
            GIFT_ITEM_ID      = Convert.ToInt32(r["GIFT_ITEM_ID"]),
            GIFT_ITEM_NAME    = r["GIFT_ITEM_NAME"]?.ToString(),
            CATEGORY_NAME     = r["CATEGORY_NAME"]?.ToString(),
            RECEIVE_FROM_DATE = r["RECEIVE_FROM_DATE"]?.ToString() ?? "",
            RECEIVE_TO_DATE   = r["RECEIVE_TO_DATE"]?.ToString(),
            REMARK            = r["REMARK"]?.ToString(),
            IS_COMPANY_WIDE   = r["IS_COMPANY_WIDE"]?.ToString() == "1",
            LOCATION          = r["LOCATION"]?.ToString(),
            IS_CLOSED         = r["IS_CLOSED"]?.ToString() == "1",
            CLOSED_DT         = r["CLOSED_DT"]?.ToString(),
            CLOSED_BY         = r["CLOSED_BY"]?.ToString(),
            TOTAL_RECIPIENT   = Convert.ToInt32(r["TOTAL_RECIPIENT"]),
            DELIVERED_COUNT   = Convert.ToInt32(r["DELIVERED_COUNT"]),
            CONFIRMED_COUNT   = Convert.ToInt32(r["CONFIRMED_COUNT"])
        }, ps);
    }

    public async Task<GiftActionResult> CloseBatchAsync(GiftBatchCloseRequest req)
    {
        int n = await _oracleService.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_GIFT_BATCH
               SET IS_CLOSED = 1, CLOSED_DT = SYSDATE, CLOSED_BY = :ACTOR
             WHERE ID = :ID AND IS_CLOSED = 0",
            new OracleParameter("ACTOR", req.ACTOR_EMPCD),
            new OracleParameter("ID",    req.BATCH_ID));

        return n > 0
            ? new GiftActionResult { RECIPIENT_ID = req.BATCH_ID, OK = true, MESSAGE = "Đã kết thúc đợt quà" }
            : new GiftActionResult { OK = false, MESSAGE = "Đợt quà không tồn tại hoặc đã kết thúc trước đó" };
    }

    public async Task<GiftActionResult> CreateBatchAsync(GiftBatchCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.BATCH_NAME) || req.GIFT_ITEM_ID <= 0 ||
            !DateTime.TryParseExact(req.RECEIVE_FROM_DATE, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fromDate))
            return new GiftActionResult { OK = false, MESSAGE = "Thông tin đợt quà không hợp lệ" };

        DateTime? toDate = DateTime.TryParseExact(req.RECEIVE_TO_DATE, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var td) ? td : null;

        var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);
        await _oracleService.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_GIFT_BATCH (BATCH_NAME, GIFT_ITEM_ID, RECEIVE_FROM_DATE, RECEIVE_TO_DATE, REMARK, IS_COMPANY_WIDE, LOCATION, INST_ID)
            VALUES (:NAME, :ITEM_ID, :FROM_DATE, :TO_DATE, :REMARK, :CW, :LOC, :ACTOR)
            RETURNING ID INTO :OUT_ID",
            new OracleParameter("NAME",      req.BATCH_NAME),
            new OracleParameter("ITEM_ID",   req.GIFT_ITEM_ID),
            new OracleParameter("FROM_DATE", fromDate.Date),
            new OracleParameter("TO_DATE",   (object?)toDate?.Date ?? DBNull.Value),
            new OracleParameter("REMARK",    (object?)req.REMARK ?? DBNull.Value),
            new OracleParameter("CW",        req.IS_COMPANY_WIDE ? 1 : 0),
            new OracleParameter("LOC",       (object?)req.LOCATION ?? DBNull.Value),
            new OracleParameter("ACTOR",     req.ACTOR_EMPCD),
            outIdParam);

        int batchId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull ? decimal.ToInt32(od.Value) : 0;

        if (req.IS_COMPANY_WIDE)
        {
            // Tự động áp dụng cho toàn bộ NV đang làm việc — 1 câu INSERT...SELECT duy nhất
            // (không loop từng dòng như import) — HR khỏi phải chuẩn bị file Excel toàn công ty.
            await _oracleService.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_GIFT_RECIPIENT (BATCH_ID, EMPCD, RECEIVE_DATE, LOCATION, INST_ID)
                SELECT :BATCH_ID, EC.EMPCD, :RDATE, :LOC, :ACTOR
                FROM HRMS.ECM100 EC
                WHERE (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))",
                new OracleParameter("BATCH_ID", batchId),
                new OracleParameter("RDATE",    fromDate.Date),
                new OracleParameter("LOC",      (object?)req.LOCATION ?? DBNull.Value),
                new OracleParameter("ACTOR",    req.ACTOR_EMPCD));

            // 1 thông báo broadcast toàn công ty (NOTI_TYPE=COMPANY) thay vì bắn riêng từng người —
            // mirror BulletinPublished/SurveyPublished.
            var giftName = (await _oracleService.ExecuteQueryAsync(
                "SELECT ITEM_NAME FROM HRMS.HR_GIFT_ITEM WHERE ID = :ID",
                r => r["ITEM_NAME"]?.ToString() ?? "", new OracleParameter("ID", req.GIFT_ITEM_ID))).FirstOrDefault() ?? "";
            _notiSvc.GiftReadyCompanyWide(giftName, req.ACTOR_EMPCD);
        }

        return new GiftActionResult { RECIPIENT_ID = batchId, OK = true, MESSAGE = "Đã tạo đợt quà" };
    }

    // Import danh sách người nhận theo đợt — MERGE INTO theo UNIQUE(BATCH_ID, EMPCD),
    // copy pattern UserDeptController.cs (HR_api) Import.
    public async Task<GiftBulkActionResponse> ImportRecipientsAsync(GiftRecipientImportRequest req)
    {
        if (await IsBatchClosedAsync(req.BATCH_ID))
            return new GiftBulkActionResponse { success = false, message = "Đợt quà đã kết thúc, không thể import thêm" };

        var res = new GiftBulkActionResponse { success = true };
        foreach (var row in req.ROWS)
        {
            if (string.IsNullOrWhiteSpace(row.EMPCD) ||
                !DateTime.TryParseExact(row.RECEIVE_DATE, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var receiveDate))
            {
                res.failed++;
                res.results.Add(new GiftActionResult { OK = false, MESSAGE = $"Dòng {row.EMPCD}: thông tin không hợp lệ" });
                continue;
            }

            try
            {
                await _oracleService.ExecuteNonQueryAsync(@"
                    MERGE INTO HRMS.HR_GIFT_RECIPIENT R
                    USING (SELECT :BATCH_ID BID, :EMPCD EC FROM DUAL) S
                    ON (R.BATCH_ID = S.BID AND R.EMPCD = S.EC)
                    WHEN MATCHED THEN UPDATE SET
                        R.RECEIVE_DATE = :RDATE, R.LOCATION = :LOC, R.UPDT_ID = :ACTOR
                    WHEN NOT MATCHED THEN INSERT
                        (BATCH_ID, EMPCD, RECEIVE_DATE, LOCATION, INST_ID)
                    VALUES
                        (:BATCH_ID, :EMPCD, :RDATE, :LOC, :ACTOR)",
                    new OracleParameter("BATCH_ID", req.BATCH_ID),
                    new OracleParameter("EMPCD",    row.EMPCD.Trim()),
                    new OracleParameter("RDATE",    receiveDate.Date),
                    new OracleParameter("LOC",      (object?)row.LOCATION ?? DBNull.Value),
                    new OracleParameter("ACTOR",    req.ACTOR_EMPCD));

                res.processed++;
            }
            catch (Exception ex)
            {
                res.failed++;
                res.results.Add(new GiftActionResult { OK = false, MESSAGE = $"Dòng {row.EMPCD}: {ex.Message}" });
            }
        }
        res.message = $"Import: OK {res.processed}, Lỗi {res.failed}";
        return res;
    }

    // ═══════════════════════════════════════════════════════════════
    // DANH SÁCH NGƯỜI NHẬN (admin list / report / export) — scope theo Clerk, Admin/HR xem hết.
    // ═══════════════════════════════════════════════════════════════
    public async Task<GiftRecipientListResponse> GetRecipientListAsync(
        string callerEmpCd, bool isAdminOrHr,
        int? batchId, string? deptId, string? lineId, string? workId, string? search,
        DateTime fromDate, DateTime toDate, string? status,
        int page, int pageSize)
    {
        OTScopeFilterHelper.FilterResult? scopeFilter = null;
        if (!isAdminOrHr)
        {
            var hasScope = await _oracleService.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :SE AND ROWNUM = 1",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("SE", callerEmpCd));
            if (hasScope.FirstOrDefault() == 0)
                return new GiftRecipientListResponse { success = true, total = 0, page = page, page_size = pageSize, total_pages = 0, message = "Chưa được phân quyền bộ phận" };
            scopeFilter = OTScopeFilterHelper.ForScopeByTuple(callerEmpCd, empAlias: "EC", prefix: "SC");
        }

        int offset = (page - 1) * pageSize;
        int maxRn  = offset + pageSize;
        string searchPattern = string.IsNullOrEmpty(search) ? "%" : "%" + search.ToUpper() + "%";

        string fromSql = @"
            FROM HRMS.HR_GIFT_RECIPIENT R
            JOIN HRMS.HR_GIFT_BATCH B ON B.ID = R.BATCH_ID
            JOIN HRMS.HR_GIFT_ITEM I ON I.ID = B.GIFT_ITEM_ID
            JOIN HRMS.HR_GIFT_CATEGORY C ON C.ID = I.CATEGORY_ID
            JOIN HRMS.ECM100 EC ON EC.EMPCD = R.EMPCD
            LEFT JOIN HRMS.EAM410 EA ON EA.DEPTCD = EC.DEPTCD AND EA.LINECD = EC.LINECD AND EA.WORKCD = EC.WORKCD
            LEFT JOIN HRMS.ECM100 DB ON DB.EMPCD = R.DELIVERED_BY";

        string whereSql = @"
            WHERE R.RECEIVE_DATE BETWEEN :D_FROM AND :D_TO
              AND (:BID_FLAG IS NULL OR R.BATCH_ID = :BID_VAL)
              AND (:DID_FLAG IS NULL OR EC.DEPTCD = :DID_VAL)
              AND (:LID_FLAG IS NULL OR EC.LINECD = :LID_VAL)
              AND (:WID_FLAG IS NULL OR EC.WORKCD = :WID_VAL)
              AND (:S_FLAG   IS NULL OR UPPER(R.EMPCD) LIKE :S_VAL)
              AND (:ST_FLAG  IS NULL OR (" + StatusSqlExpr + @") = :ST_VAL)
              " + (scopeFilter?.SqlClause ?? "");

        var baseParams = new List<OracleParameter>
        {
            new OracleParameter("D_FROM",  OracleDbType.Date)     { Value = fromDate.Date },
            new OracleParameter("D_TO",    OracleDbType.Date)     { Value = toDate.Date },
            new OracleParameter("BID_FLAG",OracleDbType.Varchar2) { Value = (object?)(batchId.HasValue ? "Y" : null) ?? DBNull.Value },
            new OracleParameter("BID_VAL", OracleDbType.Int32)    { Value = (object?)batchId ?? DBNull.Value },
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
        };
        if (scopeFilter != null) baseParams.AddRange(scopeFilter.Params);

        string sqlCount = "SELECT COUNT(*) TOTAL " + fromSql + whereSql;
        var totalRows = await _oracleService.ExecuteQueryAsync(sqlCount,
            r => r["TOTAL"] == DBNull.Value ? 0 : Convert.ToInt32(r["TOTAL"]),
            baseParams.Select(p => (OracleParameter)p.Clone()).ToArray());
        int total = totalRows.FirstOrDefault();

        if (total == 0)
            return new GiftRecipientListResponse { success = true, total = 0, page = page, page_size = pageSize, total_pages = 0 };

        string sqlData = @"
            SELECT * FROM (
                SELECT T.*, ROW_NUMBER() OVER (ORDER BY RECEIVE_DATE DESC, EMPCD) RN
                FROM (
                    SELECT R.ID, R.BATCH_ID, B.BATCH_NAME, I.ITEM_NAME GIFT_ITEM_NAME, C.NAME CATEGORY_NAME,
                           R.EMPCD, EC.CNAME EMP_NAME,
                           EC.DEPTCD DEPT_ID, EA.DEPTNM DEPT_NAME, EC.LINECD LINE_ID, EA.TEAMNM LINE_NAME,
                           EC.WORKCD WORK_ID, EA.WORKNM WORK_NAME,
                           TO_CHAR(R.RECEIVE_DATE,'YYYY-MM-DD') RECEIVE_DATE, R.LOCATION,
                           TO_CHAR(R.DELIVERED_DT,'YYYY-MM-DD HH24:MI') DELIVERED_DT,
                           R.DELIVERED_LOCATION, R.DELIVERED_BY, DB.CNAME DELIVERED_BY_NAME,
                           R.CONFIRM_STATUS, TO_CHAR(R.CONFIRMED_DT,'YYYY-MM-DD HH24:MI') CONFIRMED_DT,
                           R.NOTE, (" + StatusSqlExpr + @") STATUS
                    " + fromSql + whereSql + @"
                ) T
            ) WHERE RN > :R_MIN AND RN <= :R_MAX";

        var dataParams = baseParams.Select(p => (OracleParameter)p.Clone()).ToList();
        dataParams.Add(new OracleParameter("R_MIN", offset));
        dataParams.Add(new OracleParameter("R_MAX", maxRn));

        var rows = await _oracleService.ExecuteQueryAsync(sqlData, r => new GiftRecipientListItem
        {
            ID                 = Convert.ToInt32(r["ID"]),
            BATCH_ID           = Convert.ToInt32(r["BATCH_ID"]),
            BATCH_NAME         = r["BATCH_NAME"]?.ToString(),
            GIFT_ITEM_NAME     = r["GIFT_ITEM_NAME"]?.ToString(),
            CATEGORY_NAME      = r["CATEGORY_NAME"]?.ToString(),
            EMPCD              = r["EMPCD"]?.ToString() ?? "",
            EMP_NAME           = r["EMP_NAME"]?.ToString(),
            DEPT_ID            = r["DEPT_ID"]?.ToString(),
            DEPT_NAME          = r["DEPT_NAME"]?.ToString(),
            LINE_ID            = r["LINE_ID"]?.ToString(),
            LINE_NAME          = r["LINE_NAME"]?.ToString(),
            WORK_ID            = r["WORK_ID"]?.ToString(),
            WORK_NAME          = r["WORK_NAME"]?.ToString(),
            RECEIVE_DATE       = r["RECEIVE_DATE"]?.ToString() ?? "",
            LOCATION           = r["LOCATION"]?.ToString(),
            DELIVERED_DT       = r["DELIVERED_DT"]?.ToString(),
            DELIVERED_LOCATION = r["DELIVERED_LOCATION"]?.ToString(),
            DELIVERED_BY       = r["DELIVERED_BY"]?.ToString(),
            DELIVERED_BY_NAME  = r["DELIVERED_BY_NAME"]?.ToString(),
            CONFIRM_STATUS     = r["CONFIRM_STATUS"]?.ToString() ?? "NONE",
            CONFIRMED_DT       = r["CONFIRMED_DT"]?.ToString(),
            NOTE               = r["NOTE"]?.ToString(),
            STATUS             = r["STATUS"]?.ToString() ?? "READY",
            TOTAL_COUNT        = total
        }, dataParams.ToArray());

        return new GiftRecipientListResponse
        {
            success = true, total = total, page = page, page_size = pageSize,
            total_pages = pageSize > 0 ? (int)Math.Ceiling((double)total / pageSize) : 0,
            data = rows
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // HR đánh dấu "đã phát"
    // ═══════════════════════════════════════════════════════════════
    public async Task<GiftActionResult> MarkDeliveredAsync(GiftDeliverRequest req)
    {
        var existing = await GetRecipientBasicAsync(req.RECIPIENT_ID);
        if (existing == null) return new GiftActionResult { OK = false, MESSAGE = "Không tìm thấy người nhận" };
        if (existing.DeliveredDt != null) return new GiftActionResult { OK = false, MESSAGE = "Đã đánh dấu phát rồi" };
        if (await IsBatchClosedAsync(existing.BatchId)) return new GiftActionResult { OK = false, MESSAGE = "Đợt quà đã kết thúc, không thể thao tác" };

        await _oracleService.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_GIFT_RECIPIENT
               SET DELIVERED_DT = SYSDATE, DELIVERED_LOCATION = :LOC, DELIVERED_BY = :ACTOR, UPDT_ID = :ACTOR2
             WHERE ID = :ID",
            new OracleParameter("LOC",    (object?)req.DELIVERED_LOCATION ?? DBNull.Value),
            new OracleParameter("ACTOR",  req.ACTOR_EMPCD),
            new OracleParameter("ACTOR2", req.ACTOR_EMPCD),
            new OracleParameter("ID",     req.RECIPIENT_ID));

        _log.Log(GiftLogHelper.LogAction.DELIVERED, req.RECIPIENT_ID, req.ACTOR_EMPCD, req.DELIVERED_LOCATION);

        return new GiftActionResult { RECIPIENT_ID = req.RECIPIENT_ID, OK = true, MESSAGE = "Đã đánh dấu đã phát" };
    }

    // Đánh dấu "đã phát" cho nhiều người được chọn bằng checkbox — sài chung ô checkbox với
    // "Gửi yêu cầu xác nhận" (mỗi nút tự bỏ qua dòng không hợp lệ với nó).
    public async Task<GiftBulkActionResponse> MarkDeliveredBulkAsync(GiftDeliverBulkRequest req)
    {
        var res = new GiftBulkActionResponse { success = true };
        foreach (var recipientId in req.RECIPIENT_IDS)
        {
            var existing = await GetRecipientBasicAsync(recipientId);
            if (existing == null)
            {
                res.failed++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, MESSAGE = "Không tìm thấy" });
                continue;
            }
            if (existing.DeliveredDt != null)
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Đã phát rồi" });
                continue;
            }
            if (await IsBatchClosedAsync(existing.BatchId))
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Đợt quà đã kết thúc" });
                continue;
            }

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_GIFT_RECIPIENT
                   SET DELIVERED_DT = SYSDATE, DELIVERED_LOCATION = :LOC, DELIVERED_BY = :ACTOR, UPDT_ID = :ACTOR2
                 WHERE ID = :ID",
                new OracleParameter("LOC",    (object?)req.DELIVERED_LOCATION ?? DBNull.Value),
                new OracleParameter("ACTOR",  req.ACTOR_EMPCD),
                new OracleParameter("ACTOR2", req.ACTOR_EMPCD),
                new OracleParameter("ID",     recipientId));

            _log.Log(GiftLogHelper.LogAction.DELIVERED, recipientId, req.ACTOR_EMPCD, req.DELIVERED_LOCATION);

            res.processed++;
            res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = true, MESSAGE = "Đã đánh dấu đã phát" });
        }
        res.message = $"Đánh dấu đã phát: OK {res.processed}, Bỏ qua {res.skipped}, Lỗi {res.failed}";
        return res;
    }

    // "Đã phát cho tất cả" — áp dụng cho MỌI người nhận CHƯA phát trong cả đợt (bỏ qua bộ lọc hiện
    // tại trên màn hình), 1 câu UPDATE bộ (không loop) để chịu được đợt hàng ngàn người (vd toàn
    // công ty). Log vẫn ghi đủ từng dòng bằng INSERT...SELECT chạy TRƯỚC khi UPDATE xoá mất điều
    // kiện DELIVERED_DT IS NULL.
    public async Task<GiftActionResult> MarkDeliveredAllAsync(GiftDeliverAllRequest req)
    {
        if (await IsBatchClosedAsync(req.BATCH_ID))
            return new GiftActionResult { OK = false, MESSAGE = "Đợt quà đã kết thúc, không thể thao tác" };

        await _oracleService.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_GIFT_LOG (RECIPIENT_ID, ACTION, ACTOR_EMPCD, DETAIL, INST_DT)
            SELECT ID, 'DELIVERED', :ACTOR, :DETAIL, SYSDATE
            FROM HRMS.HR_GIFT_RECIPIENT WHERE BATCH_ID = :BID AND DELIVERED_DT IS NULL",
            new OracleParameter("ACTOR",  req.ACTOR_EMPCD),
            new OracleParameter("DETAIL", (object?)req.DELIVERED_LOCATION ?? DBNull.Value),
            new OracleParameter("BID",    req.BATCH_ID));

        int n = await _oracleService.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_GIFT_RECIPIENT
               SET DELIVERED_DT = SYSDATE, DELIVERED_LOCATION = :LOC, DELIVERED_BY = :ACTOR, UPDT_ID = :ACTOR2
             WHERE BATCH_ID = :BID AND DELIVERED_DT IS NULL",
            new OracleParameter("LOC",    (object?)req.DELIVERED_LOCATION ?? DBNull.Value),
            new OracleParameter("ACTOR",  req.ACTOR_EMPCD),
            new OracleParameter("ACTOR2", req.ACTOR_EMPCD),
            new OracleParameter("BID",    req.BATCH_ID));

        return new GiftActionResult { RECIPIENT_ID = req.BATCH_ID, OK = true, MESSAGE = $"Đã đánh dấu đã phát cho {n} người" };
    }

    // HR gửi yêu cầu xác nhận (bulk) — set PENDING_CONFIRM, kích hoạt GiftGateFilter chặn app của NV.
    public async Task<GiftBulkActionResponse> SendConfirmRequestBulkAsync(GiftSendConfirmBulkRequest req)
    {
        var res = new GiftBulkActionResponse { success = true };
        foreach (var recipientId in req.RECIPIENT_IDS)
        {
            var existing = await GetRecipientBasicAsync(recipientId);
            if (existing == null)
            {
                res.failed++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, MESSAGE = "Không tìm thấy" });
                continue;
            }
            if (existing.DeliveredDt == null)
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Chưa đánh dấu đã phát" });
                continue;
            }
            if (existing.ConfirmStatus == "CONFIRMED")
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Đã xác nhận rồi" });
                continue;
            }
            if (await IsBatchClosedAsync(existing.BatchId))
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Đợt quà đã kết thúc" });
                continue;
            }

            await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_GIFT_RECIPIENT SET CONFIRM_STATUS = 'PENDING_CONFIRM', UPDT_ID = :ACTOR WHERE ID = :ID",
                new OracleParameter("ACTOR", req.ACTOR_EMPCD),
                new OracleParameter("ID", recipientId));

            _log.Log(GiftLogHelper.LogAction.CONFIRM_REQUESTED, recipientId, req.ACTOR_EMPCD, null);

            var giftName = await GetGiftNameForRecipientAsync(recipientId);
            _notiSvc.GiftConfirmRequested(existing.Empcd, req.ACTOR_EMPCD, giftName);

            res.processed++;
            res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = true, MESSAGE = "Đã gửi yêu cầu xác nhận" });
        }
        res.message = $"Gửi yêu cầu xác nhận: OK {res.processed}, Bỏ qua {res.skipped}, Lỗi {res.failed}";
        return res;
    }

    // Thư ký (Clerk)/HR/Admin nhắc công nhân đến lãnh quà — CHỈ gửi thông báo (GIFT_READY),
    // KHÔNG đổi CONFIRM_STATUS, KHÔNG kích hoạt GiftGateFilter. Dùng cho các dòng đã đến hạn/quá
    // hạn mà chưa phát (DELIVERED_DT NULL) — khác hẳn SendConfirmRequestBulkAsync (chỉ HR, chỉ áp
    // dụng SAU KHI đã phát quà thật, có chặn app).
    public async Task<GiftBulkActionResponse> RemindRecipientsBulkAsync(GiftSendConfirmBulkRequest req)
    {
        var res = new GiftBulkActionResponse { success = true };
        foreach (var recipientId in req.RECIPIENT_IDS)
        {
            var existing = await GetRecipientBasicAsync(recipientId);
            if (existing == null)
            {
                res.failed++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, MESSAGE = "Không tìm thấy" });
                continue;
            }
            if (existing.DeliveredDt != null)
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Đã phát rồi, không cần nhắc" });
                continue;
            }
            if (await IsBatchClosedAsync(existing.BatchId))
            {
                res.skipped++;
                res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = false, SKIPPED = true, MESSAGE = "Đợt quà đã kết thúc" });
                continue;
            }

            var giftName = await GetGiftNameForRecipientAsync(recipientId);
            _notiSvc.GiftReady(existing.Empcd, req.ACTOR_EMPCD, giftName);
            _log.Log(GiftLogHelper.LogAction.REMINDED, recipientId, req.ACTOR_EMPCD, null);

            res.processed++;
            res.results.Add(new GiftActionResult { RECIPIENT_ID = recipientId, OK = true, MESSAGE = "Đã nhắc" });
        }
        res.message = $"Nhắc công nhân: OK {res.processed}, Bỏ qua {res.skipped}, Lỗi {res.failed}";
        return res;
    }

    // NV xác nhận đã nhận — mở lại app (InvalidateUser cache ở HR_web sau khi gọi API này thành công).
    public async Task<GiftActionResult> ConfirmReceiptAsync(GiftConfirmReceiptRequest req)
    {
        var existing = await GetRecipientBasicAsync(req.RECIPIENT_ID);
        if (existing == null) return new GiftActionResult { OK = false, MESSAGE = "Không tìm thấy quà cần xác nhận" };
        if (!string.Equals(existing.Empcd, req.EMPCD, StringComparison.OrdinalIgnoreCase))
            return new GiftActionResult { OK = false, MESSAGE = "Không đúng người nhận" };
        if (existing.ConfirmStatus == "CONFIRMED")
            return new GiftActionResult { RECIPIENT_ID = req.RECIPIENT_ID, OK = true, MESSAGE = "Đã xác nhận rồi" };

        await _oracleService.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_GIFT_RECIPIENT
               SET CONFIRM_STATUS = 'CONFIRMED', CONFIRMED_DT = SYSDATE, UPDT_ID = :EMPCD
             WHERE ID = :ID",
            new OracleParameter("EMPCD", req.EMPCD),
            new OracleParameter("ID",    req.RECIPIENT_ID));

        _log.Log(GiftLogHelper.LogAction.CONFIRMED, req.RECIPIENT_ID, req.EMPCD, null);

        return new GiftActionResult { RECIPIENT_ID = req.RECIPIENT_ID, OK = true, MESSAGE = "Đã xác nhận nhận quà" };
    }

    // ═══════════════════════════════════════════════════════════════
    // GATE / BADGE
    // ═══════════════════════════════════════════════════════════════

    // Dùng riêng cho GiftGateFilter (HR_web) — chặn app cho tới khi xác nhận.
    public async Task<int?> GetOldestPendingConfirmIdAsync(string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return null;
        var rows = await _oracleService.ExecuteQueryAsync(@"
            SELECT ID FROM (
                SELECT ID FROM HRMS.HR_GIFT_RECIPIENT
                WHERE EMPCD = :EMPCD AND CONFIRM_STATUS = 'PENDING_CONFIRM'
                ORDER BY DELIVERED_DT ASC
            ) WHERE ROWNUM = 1",
            r => Convert.ToInt32(r["ID"]),
            new OracleParameter("EMPCD", empcd));
        return rows.FirstOrDefault();
    }

    public async Task<GiftMyPendingResponse> GetMyPendingAsync(string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return new GiftMyPendingResponse { count = 0 };

        var rows = await _oracleService.ExecuteQueryAsync(@"
            SELECT R.ID, I.ID GIFT_ITEM_ID, I.ITEM_NAME GIFT_NAME, I.ITEM_TYPE,
                   R.LOCATION, TO_CHAR(R.DELIVERED_DT,'DD/MM/YYYY HH24:MI') DELIVERED_DT
            FROM HRMS.HR_GIFT_RECIPIENT R
            JOIN HRMS.HR_GIFT_BATCH B ON B.ID = R.BATCH_ID
            JOIN HRMS.HR_GIFT_ITEM I ON I.ID = B.GIFT_ITEM_ID
            WHERE R.EMPCD = :EMPCD AND R.CONFIRM_STATUS = 'PENDING_CONFIRM'
            ORDER BY R.DELIVERED_DT ASC",
            r => new GiftMyPendingItem
            {
                RECIPIENT_ID = Convert.ToInt32(r["ID"]),
                GIFT_ITEM_ID = Convert.ToInt32(r["GIFT_ITEM_ID"]),
                GIFT_NAME    = r["GIFT_NAME"]?.ToString() ?? "",
                ITEM_TYPE    = r["ITEM_TYPE"]?.ToString() ?? "SINGLE",
                LOCATION     = r["LOCATION"]?.ToString(),
                DELIVERED_DT = r["DELIVERED_DT"]?.ToString()
            },
            new OracleParameter("EMPCD", empcd));

        // Combo -> nạp thêm danh sách thành phần (số lượng NV đang PENDING_CONFIRM cùng lúc rất
        // nhỏ, query riêng từng quà không đáng lo về hiệu năng như GetCatalogAsync).
        foreach (var item in rows.Where(r => r.ITEM_TYPE == "COMBO"))
        {
            item.COMBO_DETAILS = await _oracleService.ExecuteQueryAsync(
                "SELECT ID, COMPONENT_NAME, DISPLAY_ORDER FROM HRMS.HR_GIFT_COMBO_DETAIL WHERE GIFT_ITEM_ID = :ID ORDER BY DISPLAY_ORDER",
                r => new GiftComboDetailItem
                {
                    ID             = Convert.ToInt32(r["ID"]),
                    COMPONENT_NAME = r["COMPONENT_NAME"]?.ToString() ?? "",
                    DISPLAY_ORDER  = r["DISPLAY_ORDER"] == DBNull.Value ? 0 : Convert.ToInt32(r["DISPLAY_ORDER"])
                },
                new OracleParameter("ID", item.GIFT_ITEM_ID));
        }

        return new GiftMyPendingResponse { success = true, count = rows.Count, data = rows };
    }

    public async Task<bool> IsAdminOrHRAsync(string? empCd)
    {
        var role = await GetRoleNameAsync(empCd);
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "HR", StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════
    private record RecipientBasic(string Empcd, int BatchId, DateTime? DeliveredDt, string ConfirmStatus);

    private async Task<RecipientBasic?> GetRecipientBasicAsync(int recipientId)
    {
        var rows = await _oracleService.ExecuteQueryAsync(
            "SELECT EMPCD, BATCH_ID, DELIVERED_DT, CONFIRM_STATUS FROM HRMS.HR_GIFT_RECIPIENT WHERE ID = :ID AND ROWNUM = 1",
            r => new RecipientBasic(
                r["EMPCD"]?.ToString() ?? "",
                Convert.ToInt32(r["BATCH_ID"]),
                r["DELIVERED_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["DELIVERED_DT"]),
                r["CONFIRM_STATUS"]?.ToString() ?? "NONE"
            ),
            new OracleParameter("ID", recipientId));
        return rows.Count > 0 ? rows[0] : null;
    }

    // Đợt đã "kết thúc" (chốt báo cáo) -> khoá mọi thao tác thay đổi dữ liệu trên đợt đó.
    private async Task<bool> IsBatchClosedAsync(int batchId)
    {
        var rows = await _oracleService.ExecuteQueryAsync(
            "SELECT IS_CLOSED FROM HRMS.HR_GIFT_BATCH WHERE ID = :ID",
            r => r["IS_CLOSED"]?.ToString() == "1",
            new OracleParameter("ID", batchId));
        return rows.FirstOrDefault();
    }

    private async Task<string> GetGiftNameForRecipientAsync(int recipientId)
    {
        var rows = await _oracleService.ExecuteQueryAsync(@"
            SELECT I.ITEM_NAME FROM HRMS.HR_GIFT_RECIPIENT R
            JOIN HRMS.HR_GIFT_BATCH B ON B.ID = R.BATCH_ID
            JOIN HRMS.HR_GIFT_ITEM I ON I.ID = B.GIFT_ITEM_ID
            WHERE R.ID = :ID AND ROWNUM = 1",
            r => r["ITEM_NAME"]?.ToString() ?? "",
            new OracleParameter("ID", recipientId));
        return rows.FirstOrDefault() ?? "";
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
}
