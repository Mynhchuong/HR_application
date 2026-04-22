using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Payslip;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class PayslipController : ControllerBase
{
    private readonly OracleService _oracleService;
    private readonly Helpers.NotificationHelper _notiHelper;

    public PayslipController(OracleService oracleService, Helpers.NotificationHelper notiHelper)
    {
        _oracleService = oracleService;
        _notiHelper = notiHelper;
    }

    [HttpGet("periods")]
    public async Task<IActionResult> GetPeriods(string? empcd = null)
    {
        try
        {
            string sql = "SELECT * FROM HRMS.HR_PAYROLL_PERIOD P WHERE 1=1 ";
            var parameters = new List<OracleParameter>();

            if (!string.IsNullOrEmpty(empcd))
            {
                sql += " AND EXISTS (SELECT 1 FROM HRMS.HR_PAYROLL_DATA D WHERE D.PERIOD_ID = P.ID AND D.EMPCD = :EMPCD)";
                parameters.Add(new OracleParameter("EMPCD", empcd));
            }

            sql += " ORDER BY INST_DT DESC";

            var result = await _oracleService.ExecuteQueryAsync(sql, r => new PayrollPeriodModel
            {
                ID = Convert.ToDecimal(r["ID"]),
                PERIOD_NAME = r["PERIOD_NAME"]?.ToString() ?? string.Empty,
                START_DATE = r["START_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["START_DATE"]),
                END_DATE = r["END_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["END_DATE"]),
                IS_PUBLISHED = Convert.ToInt32(r["IS_PUBLISHED"]),
                INST_ID = r["INST_ID"]?.ToString() ?? string.Empty,
                INST_DT = r["INST_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["INST_DT"]),
                UPDT_ID = r["UPDT_ID"]?.ToString() ?? string.Empty,
                UPDT_DT = r["UPDT_DT"] == DBNull.Value ? null : Convert.ToDateTime(r["UPDT_DT"]),
                PUBLISH_DATE = r["PUBLISH_DATE"] == DBNull.Value ? null : Convert.ToDateTime(r["PUBLISH_DATE"]),
                IS_AUTO_PUBLISH = r["IS_AUTO_PUBLISH"] == DBNull.Value ? 0 : Convert.ToInt32(r["IS_AUTO_PUBLISH"]),
                REMARK = r["REMARK"]?.ToString() ?? string.Empty
            }, parameters.ToArray());

            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("period")]
    public async Task<IActionResult> CreatePeriod([FromBody] PayrollPeriodModel model)
    {
        try
        {
            if (model == null || string.IsNullOrEmpty(model.PERIOD_NAME))
                return Ok(new { success = false, message = "Thiếu tên kỳ lương" });

            var idResults = await _oracleService.ExecuteQueryAsync("SELECT HRMS.SEQ_PAYROLL_PERIOD.NEXTVAL FROM DUAL", r => Convert.ToDecimal(r[0]));
            var id = idResults.First();

            string sqlInsert = @"
                INSERT INTO HRMS.HR_PAYROLL_PERIOD (ID, PERIOD_NAME, START_DATE, END_DATE, INST_ID, INST_DT, PUBLISH_DATE, IS_AUTO_PUBLISH, REMARK)
                VALUES (:ID, :PERIOD_NAME, :START_DATE, :END_DATE, :INST_ID, SYSDATE, :PUBLISH_DATE, :IS_AUTO_PUBLISH, :REMARK)";

            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("ID", id),
                new OracleParameter("PERIOD_NAME", model.PERIOD_NAME),
                new OracleParameter("START_DATE", (object?)model.START_DATE ?? DBNull.Value),
                new OracleParameter("END_DATE", (object?)model.END_DATE ?? DBNull.Value),
                new OracleParameter("INST_ID", (object?)model.INST_ID ?? DBNull.Value),
                new OracleParameter("PUBLISH_DATE", (object?)model.PUBLISH_DATE ?? DBNull.Value),
                new OracleParameter("IS_AUTO_PUBLISH", model.IS_AUTO_PUBLISH),
                new OracleParameter("REMARK", (object?)model.REMARK ?? DBNull.Value));

            string sqlInitVisibility = @"
                INSERT INTO HRMS.HR_PAYROLL_PERIOD_ITEMS (PERIOD_ID, ITEM_ID, IS_VISIBLE, DISPLAY_ORDER)
                SELECT :PERIOD_ID, ID, IS_VISIBLE_DEFAULT, DISPLAY_ORDER FROM HRMS.HR_PAYROLL_ITEMS";

            await _oracleService.ExecuteNonQueryAsync(sqlInitVisibility, new OracleParameter("PERIOD_ID", id));

            return Ok(new { success = true, message = "Tạo kỳ lương thành công", id = id });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("release")]
    public async Task<IActionResult> ReleasePeriod([FromBody] dynamic model)
    {
        try
        {
            decimal id = model.ID;
            string updtId = model.UPDT_ID;
            string sql = "UPDATE HRMS.HR_PAYROLL_PERIOD SET IS_PUBLISHED = 1, UPDT_ID = :UPDT_ID, UPDT_DT = SYSDATE WHERE ID = :ID";
            await _oracleService.ExecuteNonQueryAsync(sql,
                new OracleParameter("UPDT_ID", (object?)updtId ?? DBNull.Value),
                new OracleParameter("ID", id));

            // Gửi thông báo cho toàn bộ nhân viên có trong kỳ lương này
            _ = _notiHelper.SendNotificationAsync(new Models.Notification.SendNotificationRequest
            {
                TITLE = "Thông báo phiếu lương",
                BODY = "Bạn đã có phiếu lương mới. Vui lòng kiểm tra trên ứng dụng.",
                NOTI_TYPE = "COMPANY",
                TARGET_VAL = "ALL",
                LINK_ACTION = "PAYSLIP",
                CREATED_BY = updtId
            });

            return Ok(new { success = true, message = "Đã công bố phiếu lương" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("items")]
    public async Task<IActionResult> GetItemsVisibility(decimal periodId)
    {
        try
        {
            string sql = @"
                SELECT I.ID, I.ITEM_CODE, I.ITEM_NAME, I.ITEM_TYPE, I.UNIT,
                       NVL(V.IS_VISIBLE, I.IS_VISIBLE_DEFAULT) IS_VISIBLE,
                       NVL(V.DISPLAY_ORDER, I.DISPLAY_ORDER) DISPLAY_ORDER
                FROM HRMS.HR_PAYROLL_ITEMS I
                LEFT JOIN HRMS.HR_PAYROLL_PERIOD_ITEMS V ON V.ITEM_ID = I.ID AND V.PERIOD_ID = :PERIOD_ID
                ORDER BY DISPLAY_ORDER";

            var result = await _oracleService.ExecuteQueryAsync(sql, r => new PayrollItemModel
            {
                ID = Convert.ToDecimal(r["ID"]),
                ITEM_CODE = r["ITEM_CODE"]?.ToString() ?? string.Empty,
                ITEM_NAME = r["ITEM_NAME"]?.ToString() ?? string.Empty,
                ITEM_TYPE = r["ITEM_TYPE"]?.ToString() ?? string.Empty,
                IS_VISIBLE = Convert.ToInt32(r["IS_VISIBLE"]),
                DISPLAY_ORDER = Convert.ToDecimal(r["DISPLAY_ORDER"]),
                UNIT = r["UNIT"]?.ToString()
            }, new OracleParameter("PERIOD_ID", periodId));

            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("items/update")]
    public async Task<IActionResult> UpdateVisibility([FromBody] dynamic model)
    {
        try
        {
            decimal periodId = model.PERIOD_ID;
            var items = ((Newtonsoft.Json.Linq.JArray)model.Items).ToObject<List<PayrollItemModel>>();
            
            foreach (var item in items!)
            {
                string sql = @"
                    MERGE INTO HRMS.HR_PAYROLL_PERIOD_ITEMS T
                    USING (SELECT :PERIOD_ID PI, :ITEM_ID II FROM DUAL) S
                    ON (T.PERIOD_ID = S.PI AND T.ITEM_ID = S.II)
                    WHEN MATCHED THEN UPDATE SET IS_VISIBLE = :IS_VISIBLE
                    WHEN NOT MATCHED THEN INSERT (PERIOD_ID, ITEM_ID, IS_VISIBLE) 
                                          VALUES (:PERIOD_ID, :ITEM_ID, :IS_VISIBLE)";

                await _oracleService.ExecuteNonQueryAsync(sql,
                    new OracleParameter("PERIOD_ID", periodId),
                    new OracleParameter("ITEM_ID", item.ID),
                    new OracleParameter("IS_VISIBLE", item.IS_VISIBLE));
            }
            return Ok(new { success = true, message = "Cập nhật hiển thị thành công" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadPayslip([FromBody] dynamic model)
    {
        try
        {
            decimal periodId = model.PERIOD_ID;
            var data = ((Newtonsoft.Json.Linq.JArray)model.Data).ToObject<List<PayslipUploadRow>>();
            bool isFirstBatch = model.IsFirstBatch;

            if (data == null || data.Count == 0)
                return Ok(new { success = false, message = "Không có dữ liệu upload" });

            if (isFirstBatch)
            {
                string sqlClear = "DELETE FROM HRMS.HR_PAYROLL_DATA WHERE PERIOD_ID = :PERIOD_ID";
                await _oracleService.ExecuteNonQueryAsync(sqlClear, new OracleParameter("PERIOD_ID", periodId));
            }

            string sqlItems = "SELECT ID, ITEM_CODE FROM HRMS.HR_PAYROLL_ITEMS ORDER BY DISPLAY_ORDER";
            var items = await _oracleService.ExecuteQueryAsync(sqlItems, r => new { ID = Convert.ToDecimal(r["ID"]), CODE = r["ITEM_CODE"]?.ToString() });

            int successCount = 0;

            var pIds = new List<decimal>();
            var eCodes = new List<string>();
            var iIds = new List<decimal>();
            var amts = new List<object>();
            var tVals = new List<object>();

            foreach (var row in data)
            {
                if (string.IsNullOrEmpty(row.EmpCd)) continue;
                
                bool hasDataForRow = false;
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var val = (row.Values != null && i < row.Values.Count) ? row.Values[i] : null;
                    var textVal = (row.TextValues != null && i < row.TextValues.Count) ? row.TextValues[i] : null;

                    if (val == null && string.IsNullOrEmpty(textVal)) continue;

                    pIds.Add(periodId);
                    eCodes.Add(row.EmpCd);
                    iIds.Add(item.ID);
                    amts.Add((object?)val ?? DBNull.Value);
                    tVals.Add((object?)textVal ?? DBNull.Value);
                    hasDataForRow = true;
                }
                if (hasDataForRow) successCount++;
            }

            if (pIds.Count > 0)
            {
                string sqlInsert = @"
                    INSERT INTO HRMS.HR_PAYROLL_DATA (PERIOD_ID, EMPCD, ITEM_ID, AMOUNT, TEXT_VALUE)
                    VALUES (:PERIOD_ID, :EMPCD, :ITEM_ID, :AMOUNT, :TEXT_VAL)";

                await _oracleService.ExecuteBulkInsertAsync(sqlInsert, pIds.Count,
                    new OracleParameter("PERIOD_ID", OracleDbType.Decimal) { Value = pIds.ToArray() },
                    new OracleParameter("EMPCD", OracleDbType.Varchar2) { Value = eCodes.ToArray() },
                    new OracleParameter("ITEM_ID", OracleDbType.Decimal) { Value = iIds.ToArray() },
                    new OracleParameter("AMOUNT", OracleDbType.Decimal) { Value = amts.ToArray() },
                    new OracleParameter("TEXT_VAL", OracleDbType.Varchar2) { Value = tVals.ToArray() }
                );
            }

            return Ok(new { success = true, message = $"Đã upload thành công {successCount}/{data.Count} nhân viên" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("my-payslip")]
    public async Task<IActionResult> GetMyPayslip(string empcd, decimal periodId)
    {
        try
        {
            string sql = @"
                SELECT I.ITEM_CODE, I.ITEM_NAME, I.ITEM_TYPE, I.UNIT, D.AMOUNT, D.TEXT_VALUE,
                       NVL(V.IS_VISIBLE, I.IS_VISIBLE_DEFAULT) IS_VISIBLE
                FROM HRMS.HR_PAYROLL_ITEMS I
                LEFT JOIN HRMS.HR_PAYROLL_DATA D ON D.ITEM_ID = I.ID AND D.PERIOD_ID = :PERIOD_ID AND D.EMPCD = :EMPCD
                LEFT JOIN HRMS.HR_PAYROLL_PERIOD_ITEMS V ON V.ITEM_ID = I.ID AND V.PERIOD_ID = :PERIOD_ID2
                WHERE NVL(V.IS_VISIBLE, I.IS_VISIBLE_DEFAULT) = 1
                ORDER BY I.DISPLAY_ORDER";

            var result = await _oracleService.ExecuteQueryAsync(sql, r => new PayrollDataModel
            {
                ITEM_CODE = r["ITEM_CODE"]?.ToString() ?? string.Empty,
                ITEM_NAME = r["ITEM_NAME"]?.ToString() ?? string.Empty,
                ITEM_TYPE = r["ITEM_TYPE"]?.ToString() ?? string.Empty,
                AMOUNT = r["AMOUNT"] == DBNull.Value ? null : Convert.ToDecimal(r["AMOUNT"]),
                TEXT_VALUE = r["TEXT_VALUE"]?.ToString() ?? string.Empty,
                IS_VISIBLE = Convert.ToInt32(r["IS_VISIBLE"]),
                UNIT = r["UNIT"]?.ToString()
            },
            new OracleParameter("PERIOD_ID", periodId),
            new OracleParameter("EMPCD", empcd),
            new OracleParameter("PERIOD_ID2", periodId));

            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminList(decimal periodId, string? search = null, int page = 1, int page_size = 100)
    {
        try
        {
            if (page < 1) page = 1;
            if (page_size < 1) page_size = 100;
            int offset = (page - 1) * page_size;
            string searchPattern = string.IsNullOrEmpty(search) ? "%" : "%" + search.ToUpper() + "%";

            string sqlTotal = @"
                SELECT COUNT(DISTINCT D.EMPCD) FROM HRMS.HR_PAYROLL_DATA D
                JOIN HRMS.ECM100 E ON E.EMPCD = D.EMPCD
                WHERE D.PERIOD_ID = :PERIOD_ID AND (D.EMPCD LIKE :SEARCH OR UPPER(E.CNAME) LIKE :SEARCH2)";

            var totalRows = await _oracleService.ExecuteQueryAsync(sqlTotal, r => r[0],
                new OracleParameter("PERIOD_ID", periodId),
                new OracleParameter("SEARCH", searchPattern),
                new OracleParameter("SEARCH2", searchPattern));
            
            int total = totalRows.Count > 0 ? Convert.ToInt32(totalRows[0]) : 0;
            int totalPages = (int)Math.Ceiling((double)total / page_size);

            string sqlEmp = @"
                SELECT * FROM (
                    SELECT T.EMPCD, T.CNAME, ROW_NUMBER() OVER (ORDER BY T.EMPCD) RN
                    FROM (
                        SELECT DISTINCT D.EMPCD, E.CNAME FROM HRMS.HR_PAYROLL_DATA D
                        JOIN HRMS.ECM100 E ON E.EMPCD = D.EMPCD
                        WHERE D.PERIOD_ID = :PERIOD_ID AND (D.EMPCD LIKE :SEARCH OR UPPER(E.CNAME) LIKE :SEARCH2)
                    ) T
                ) WHERE RN > :OFFSET AND RN <= :OFFSET + :PAGE_SIZE";

            var emps = await _oracleService.ExecuteQueryAsync(sqlEmp, r => new {
                EMPCD = r["EMPCD"]?.ToString() ?? string.Empty,
                CNAME = r["CNAME"]?.ToString() ?? string.Empty
            },
            new OracleParameter("PERIOD_ID", periodId),
            new OracleParameter("SEARCH", searchPattern),
            new OracleParameter("SEARCH2", searchPattern),
            new OracleParameter("OFFSET", offset),
            new OracleParameter("PAGE_SIZE", page_size));

            if (emps.Count == 0) return Ok(new { success = true, total = total, data = new List<object>() });

            string empList = string.Join("','", emps.Select(x => x.EMPCD));
            string sqlAllDetails = $@"
                SELECT D.EMPCD, I.ITEM_CODE, I.ITEM_NAME, I.ITEM_TYPE, I.UNIT, D.AMOUNT, D.TEXT_VALUE
                FROM HRMS.HR_PAYROLL_ITEMS I
                LEFT JOIN HRMS.HR_PAYROLL_DATA D ON D.ITEM_ID = I.ID AND D.PERIOD_ID = :PERIOD_ID
                WHERE D.EMPCD IN ('{empList}')
                ORDER BY D.EMPCD, I.DISPLAY_ORDER";

            var allDetails = await _oracleService.ExecuteQueryAsync(sqlAllDetails, r => new {
                EMPCD = r["EMPCD"]?.ToString() ?? string.Empty,
                Detail = new PayrollDataModel {
                    ITEM_CODE = r["ITEM_CODE"]?.ToString() ?? string.Empty,
                    ITEM_NAME = r["ITEM_NAME"]?.ToString() ?? string.Empty,
                    ITEM_TYPE = r["ITEM_TYPE"]?.ToString() ?? string.Empty,
                    AMOUNT = r["AMOUNT"] == DBNull.Value ? null : Convert.ToDecimal(r["AMOUNT"]),
                    TEXT_VALUE = r["TEXT_VALUE"]?.ToString() ?? string.Empty,
                    IS_VISIBLE = 1,
                    UNIT = r["UNIT"]?.ToString()
                }
            }, new OracleParameter("PERIOD_ID", periodId));

            var list = emps.Select(e => new PayslipAdminDetailModel {
                EMPCD = e.EMPCD,
                EMP_NAME = e.CNAME,
                Details = allDetails.Where(d => d.EMPCD == e.EMPCD).Select(d => d.Detail).ToList()
            }).ToList();

            return Ok(new { success = true, total, page, page_size, total_pages = totalPages, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("admin/export")]
    public async Task<IActionResult> ExportAdminList(decimal periodId)
    {
        try
        {
            string sqlEmp = @"
                SELECT DISTINCT D.EMPCD, E.CNAME FROM HRMS.HR_PAYROLL_DATA D
                JOIN HRMS.ECM100 E ON E.EMPCD = D.EMPCD
                WHERE D.PERIOD_ID = :PERIOD_ID ORDER BY D.EMPCD";

            var emps = await _oracleService.ExecuteQueryAsync(sqlEmp, r => new {
                EMPCD = r["EMPCD"]?.ToString() ?? string.Empty,
                CNAME = r["CNAME"]?.ToString() ?? string.Empty
            }, new OracleParameter("PERIOD_ID", periodId));

            if (emps.Count == 0) return Ok(new { success = true, data = new List<object>() });

            string sqlAllDetails = @"
                SELECT D.EMPCD, I.ITEM_CODE, I.ITEM_NAME, I.ITEM_TYPE, I.UNIT, D.AMOUNT, D.TEXT_VALUE
                FROM HRMS.HR_PAYROLL_ITEMS I
                LEFT JOIN HRMS.HR_PAYROLL_DATA D ON D.ITEM_ID = I.ID AND D.PERIOD_ID = :PERIOD_ID
                ORDER BY D.EMPCD, I.DISPLAY_ORDER";

            var allDetails = await _oracleService.ExecuteQueryAsync(sqlAllDetails, r => new {
                EMPCD = r["EMPCD"]?.ToString() ?? string.Empty,
                Detail = new PayrollDataModel {
                    ITEM_CODE = r["ITEM_CODE"]?.ToString() ?? string.Empty,
                    ITEM_NAME = r["ITEM_NAME"]?.ToString() ?? string.Empty,
                    ITEM_TYPE = r["ITEM_TYPE"]?.ToString() ?? string.Empty,
                    AMOUNT = r["AMOUNT"] == DBNull.Value ? null : Convert.ToDecimal(r["AMOUNT"]),
                    TEXT_VALUE = r["TEXT_VALUE"]?.ToString() ?? string.Empty,
                    IS_VISIBLE = 1,
                    UNIT = r["UNIT"]?.ToString()
                }
            }, new OracleParameter("PERIOD_ID", periodId));

            var list = emps.Select(e => new PayslipAdminDetailModel {
                EMPCD = e.EMPCD,
                EMP_NAME = e.CNAME,
                Details = allDetails.Where(d => d.EMPCD == e.EMPCD).Select(d => d.Detail).ToList()
            }).ToList();

            return Ok(new { success = true, data = list });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
