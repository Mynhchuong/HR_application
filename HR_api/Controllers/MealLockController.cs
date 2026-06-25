using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class MealLockController : ControllerBase
{
    private readonly OracleService _db;
    public MealLockController(OracleService db) { _db = db; }

    // GET /apiHR/MealLock?from=YYYY-MM-DD&to=YYYY-MM-DD
    [HttpGet]
    public async Task<IActionResult> List(string? from = null, string? to = null)
    {
        try
        {
            DateTime dfrom = (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, out var f)) ? f : DateTime.Today.AddMonths(-1);
            DateTime dto   = (!string.IsNullOrEmpty(to)   && DateTime.TryParse(to,   out var t)) ? t : DateTime.Today.AddMonths(2);

            var rows = await _db.ExecuteQueryAsync(@"
                SELECT ID, LOCK_DATE, LOCK_LUNCH, LOCK_OT, LOCK_MAN, LOCK_NHE, LOCK_CHAY,
                       CUTOFF_DT, NOTE, IS_ACTIVE,
                       INST_ID, INST_DT, UPDT_ID, UPDT_DT
                FROM HRMS.HR_MEAL_LOCK
                WHERE LOCK_DATE BETWEEN :DF AND :DT
                ORDER BY LOCK_DATE DESC, ID DESC",
                r => new
                {
                    id         = Convert.ToDecimal(r["ID"]),
                    lockDate   = Convert.ToDateTime(r["LOCK_DATE"]),
                    lockLunch  = (r["LOCK_LUNCH"]?.ToString() ?? "N") == "Y",
                    lockOt     = (r["LOCK_OT"]?.ToString()    ?? "N") == "Y",
                    lockMan    = (r["LOCK_MAN"]?.ToString()   ?? "N") == "Y",
                    lockNhe    = (r["LOCK_NHE"]?.ToString()   ?? "N") == "Y",
                    lockChay   = (r["LOCK_CHAY"]?.ToString()  ?? "N") == "Y",
                    cutoffDt   = r["CUTOFF_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CUTOFF_DT"]),
                    note       = r["NOTE"]?.ToString(),
                    isActive   = (r["IS_ACTIVE"]?.ToString() ?? "Y") == "Y",
                    instId     = r["INST_ID"]?.ToString(),
                    instDt     = r["INST_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["INST_DT"]),
                    updtId     = r["UPDT_ID"]?.ToString(),
                    updtDt     = r["UPDT_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["UPDT_DT"]),
                },
                new OracleParameter("DF", dfrom),
                new OracleParameter("DT", dto));

            return Ok(new { success = true, data = rows });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // POST /apiHR/MealLock  — create / update
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] MealLockBody body)
    {
        try
        {
            if (body == null) return Ok(new { success = false, message = "Thiếu dữ liệu" });
            if (!DateTime.TryParse(body.LockDate, out var lockDate))
                return Ok(new { success = false, message = "Ngày khoá không hợp lệ" });
            if (!(body.LockLunch || body.LockOt))
                return Ok(new { success = false, message = "Phải chọn ít nhất khoá Trưa hoặc Tăng ca" });

            if (string.IsNullOrWhiteSpace(body.CutoffDt))
                return Ok(new { success = false, message = "Vui lòng nhập thời điểm chốt" });
            if (!DateTime.TryParse(body.CutoffDt, out var cutoff))
                return Ok(new { success = false, message = "Thời điểm chốt không hợp lệ" });

            if (string.IsNullOrWhiteSpace(body.Note))
                return Ok(new { success = false, message = "Vui lòng nhập ghi chú" });

            string actor = string.IsNullOrEmpty(body.LoginUser) ? "HR" : body.LoginUser;

            if (body.Id > 0)
            {
                int n = await _db.ExecuteNonQueryAsync(@"
                    UPDATE HRMS.HR_MEAL_LOCK
                    SET LOCK_DATE  = :LD,
                        LOCK_LUNCH = :LL,
                        LOCK_OT    = :LO,
                        LOCK_MAN   = :LM,
                        LOCK_NHE   = :LN,
                        LOCK_CHAY  = :LC,
                        CUTOFF_DT  = :CT,
                        NOTE       = :NT,
                        IS_ACTIVE  = :AC,
                        UPDT_ID    = :ACTOR,
                        UPDT_DT    = SYSDATE
                    WHERE ID = :ID",
                    new OracleParameter("LD", lockDate),
                    new OracleParameter("LL", body.LockLunch ? "Y" : "N"),
                    new OracleParameter("LO", body.LockOt    ? "Y" : "N"),
                    new OracleParameter("LM", body.LockMan   ? "Y" : "N"),
                    new OracleParameter("LN", body.LockNhe   ? "Y" : "N"),
                    new OracleParameter("LC", body.LockChay  ? "Y" : "N"),
                    new OracleParameter("CT", cutoff),
                    new OracleParameter("NT", body.Note),
                    new OracleParameter("AC", body.IsActive ? "Y" : "N"),
                    new OracleParameter("ACTOR", actor),
                    new OracleParameter("ID", body.Id));
                if (n == 0) return Ok(new { success = false, message = "Không tìm thấy rule" });
                return Ok(new { success = true, message = "Cập nhật thành công", id = body.Id });
            }
            else
            {
                await _db.ExecuteNonQueryAsync(@"
                    INSERT INTO HRMS.HR_MEAL_LOCK
                        (LOCK_DATE, LOCK_LUNCH, LOCK_OT, LOCK_MAN, LOCK_NHE, LOCK_CHAY,
                         CUTOFF_DT, NOTE, IS_ACTIVE, INST_ID, INST_DT, UPDT_ID, UPDT_DT)
                    VALUES
                        (:LD, :LL, :LO, :LM, :LN, :LC,
                         :CT, :NT, :AC, :ACTOR, SYSDATE, :ACTOR, SYSDATE)",
                    new OracleParameter("LD", lockDate),
                    new OracleParameter("LL", body.LockLunch ? "Y" : "N"),
                    new OracleParameter("LO", body.LockOt    ? "Y" : "N"),
                    new OracleParameter("LM", body.LockMan   ? "Y" : "N"),
                    new OracleParameter("LN", body.LockNhe   ? "Y" : "N"),
                    new OracleParameter("LC", body.LockChay  ? "Y" : "N"),
                    new OracleParameter("CT", cutoff),
                    new OracleParameter("NT", body.Note),
                    new OracleParameter("AC", body.IsActive ? "Y" : "N"),
                    new OracleParameter("ACTOR", actor));
                return Ok(new { success = true, message = "Tạo rule thành công" });
            }
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // DELETE /apiHR/MealLock/{id}
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        try
        {
            if (id <= 0) return Ok(new { success = false, message = "Thiếu ID" });
            int n = await _db.ExecuteNonQueryAsync(
                "DELETE FROM HRMS.HR_MEAL_LOCK WHERE ID = :ID",
                new OracleParameter("ID", id));
            if (n == 0) return Ok(new { success = false, message = "Không tìm thấy rule" });
            return Ok(new { success = true });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    // GET /apiHR/MealLock/check?date=YYYY-MM-DD&typeMeal=LUNCH|OT&targetFood=M|N|C
    //   targetFood: nếu truyền, chỉ trả locked khi rule khoá đúng loại đó.
    //               nếu không truyền → kiểm tra "có rule nào khoá ở slot này"
    //   Trả về { locked, reason, cutoffDt, note, lockMan, lockNhe, lockChay }
    [HttpGet("check")]
    public async Task<IActionResult> Check(string date, string typeMeal = "LUNCH", string? targetFood = null)
    {
        try
        {
            if (!DateTime.TryParse(date, out var target))
                return Ok(new { success = false, message = "Ngày không hợp lệ" });

            var mealCat = string.IsNullOrEmpty(typeMeal) ? "LUNCH" : typeMeal.ToUpper();
            string flagCol = mealCat == "OT" ? "LOCK_OT" : "LOCK_LUNCH";

            // Khi có nhiều rule cùng ngày + cùng scope, lấy rule khắt khe nhất:
            // - CUTOFF_DT NULL (khoá cả ngày) có ưu tiên cao nhất
            // - Sau đó là CUTOFF_DT sớm nhất (chốt sớm hơn)
            var rows = await _db.ExecuteQueryAsync(
                $@"SELECT CUTOFF_DT, NOTE, LOCK_MAN, LOCK_NHE, LOCK_CHAY FROM (
                       SELECT CUTOFF_DT, NOTE, LOCK_MAN, LOCK_NHE, LOCK_CHAY
                       FROM HRMS.HR_MEAL_LOCK
                       WHERE TRUNC(LOCK_DATE) = TRUNC(:D)
                         AND IS_ACTIVE = 'Y'
                         AND {flagCol} = 'Y'
                       ORDER BY CASE WHEN CUTOFF_DT IS NULL THEN 0 ELSE 1 END,
                                CUTOFF_DT
                   ) WHERE ROWNUM = 1",
                r => new
                {
                    cutoffDt = r["CUTOFF_DT"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["CUTOFF_DT"]),
                    note     = r["NOTE"]?.ToString(),
                    lockMan  = (r["LOCK_MAN"]?.ToString()  ?? "N") == "Y",
                    lockNhe  = (r["LOCK_NHE"]?.ToString()  ?? "N") == "Y",
                    lockChay = (r["LOCK_CHAY"]?.ToString() ?? "N") == "Y"
                },
                new OracleParameter("D", target));

            var row = rows.FirstOrDefault();
            if (row == null) return Ok(new { success = true, locked = false });

            // Nếu rule có specify type và targetFood được truyền → chỉ chặn nếu khớp
            bool typeRestricted = row.lockMan || row.lockNhe || row.lockChay;
            bool typeMatches = !typeRestricted    // không restrict = áp cho mọi loại
                || string.IsNullOrEmpty(targetFood)  // không có target = check generic
                || (targetFood == "M" && row.lockMan)
                || (targetFood == "N" && row.lockNhe)
                || (targetFood == "C" && row.lockChay);

            bool locked;
            string reason;
            if (!typeMatches)
            {
                locked = false;
                reason = "";
            }
            else if (row.cutoffDt == null)
            {
                locked = true;
                reason = "Ngày này đã bị khoá đổi món";
            }
            else if (DateTime.Now >= row.cutoffDt)
            {
                locked = true;
                reason = $"Đã quá thời gian chốt ({row.cutoffDt:HH:mm dd/MM/yyyy})";
            }
            else
            {
                locked = false;
                reason = $"Sẽ chốt lúc {row.cutoffDt:HH:mm dd/MM/yyyy}";
            }

            return Ok(new
            {
                success  = true,
                locked,
                reason,
                cutoffDt = row.cutoffDt,
                note     = row.note,
                lockMan  = row.lockMan,
                lockNhe  = row.lockNhe,
                lockChay = row.lockChay
            });
        }
        catch (Exception ex) { return Ok(new { success = false, message = ex.Message }); }
    }

    public class MealLockBody
    {
        public long    Id        { get; set; }
        public string  LockDate  { get; set; } = "";
        public bool    LockLunch { get; set; }
        public bool    LockOt    { get; set; }
        public bool    LockMan   { get; set; }
        public bool    LockNhe   { get; set; }
        public bool    LockChay  { get; set; }
        public string? CutoffDt  { get; set; }   // ISO datetime
        public string? Note      { get; set; }
        public bool    IsActive  { get; set; } = true;
        public string? LoginUser { get; set; }
    }
}
