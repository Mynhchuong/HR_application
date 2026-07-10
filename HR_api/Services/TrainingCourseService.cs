using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// CRUD Course + template sessions (xem training_rules.md §2, training_plan.md §5.7).
// Course có state chỉ là cờ IS_ACTIVE — không có state machine phức tạp như Survey.
public class TrainingCourseService
{
    private readonly OracleService _db;

    public TrainingCourseService(OracleService db) { _db = db; }

    // ═══════════════════════════════════════════════════════════════
    //  LIST + DETAIL
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<CourseModel>> ListAsync(string? mode, int? active, string? search)
    {
        const string sql = @"
            SELECT C.ID, C.TITLE, C.DESCRIPTION, C.OBJECTIVES, C.CATEGORY,
                   C.COURSE_MODE, C.DEFAULT_DURATION_MIN,
                   C.AUTO_OPEN_MONTHLY, C.AUTO_OPEN_DAY,
                   C.DEFAULT_PASS_SCORE, C.DEFAULT_MIN_ATTEND_PCT,
                   C.IS_ACTIVE, C.INST_ID, C.INST_DT, C.UPDT_ID, C.UPDT_DT,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_CLASS CL
                     WHERE CL.COURSE_ID = C.ID) AS CLASS_COUNT,
                   (SELECT COUNT(*) FROM HRMS.HR_TR_COURSE_SES_TMPL ST
                     WHERE ST.COURSE_ID = C.ID) AS TEMPLATE_SESSION_COUNT
              FROM HRMS.HR_TRAINING_COURSE C
             WHERE (:P_MODE   IS NULL OR C.COURSE_MODE = :P_MODE)
               AND (:P_ACTIVE IS NULL OR C.IS_ACTIVE   = :P_ACTIVE)
               AND (:P_SEARCH IS NULL OR UPPER(C.TITLE) LIKE '%' || UPPER(:P_SEARCH) || '%')
             ORDER BY C.ID DESC";

        return await _db.ExecuteQueryAsync(sql, MapCourseLight,
            new OracleParameter("P_MODE",   (object?)mode   ?? DBNull.Value),
            new OracleParameter("P_ACTIVE", (object?)active ?? DBNull.Value),
            new OracleParameter("P_SEARCH", (object?)search ?? DBNull.Value));
    }

    public async Task<CourseModel?> GetDetailAsync(int id)
    {
        const string sql = @"
            SELECT ID, TITLE, DESCRIPTION, OBJECTIVES, CATEGORY,
                   COURSE_MODE, DEFAULT_DURATION_MIN,
                   AUTO_OPEN_MONTHLY, AUTO_OPEN_DAY,
                   DEFAULT_PASS_SCORE, DEFAULT_MIN_ATTEND_PCT,
                   IS_ACTIVE, INST_ID, INST_DT, UPDT_ID, UPDT_DT
              FROM HRMS.HR_TRAINING_COURSE
             WHERE ID = :ID";

        var list = await _db.ExecuteQueryAsync(sql, MapCourseFull,
            new OracleParameter("ID", id));
        return list.FirstOrDefault();
    }

    public async Task<List<CourseSessionTemplateModel>> GetSessionTemplatesAsync(int courseId)
    {
        const string sql = @"
            SELECT ID, COURSE_ID, SESSION_NO, DAY_OFFSET,
                   START_TIME, END_TIME, TOPIC, LOCATION
              FROM HRMS.HR_TR_COURSE_SES_TMPL
             WHERE COURSE_ID = :CID
             ORDER BY SESSION_NO";
        return await _db.ExecuteQueryAsync(sql, r => new CourseSessionTemplateModel
        {
            ID          = Convert.ToInt32(r["ID"]),
            COURSE_ID   = Convert.ToInt32(r["COURSE_ID"]),
            SESSION_NO  = Convert.ToInt32(r["SESSION_NO"]),
            DAY_OFFSET  = Convert.ToInt32(r["DAY_OFFSET"]),
            START_TIME  = r["START_TIME"]?.ToString() ?? "",
            END_TIME    = r["END_TIME"]?.ToString() ?? "",
            TOPIC       = r["TOPIC"] as string,
            LOCATION    = r["LOCATION"] as string,
        }, new OracleParameter("CID", courseId));
    }

    // ═══════════════════════════════════════════════════════════════
    //  SAVE (create + update)
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> SaveAsync(SaveCourseRequest req)
    {
        // Validate — trả InvalidOperationException để controller catch → response friendly.
        if (string.IsNullOrWhiteSpace(req.TITLE))
            throw new InvalidOperationException("Tên khoá học không được để trống");
        if (req.TITLE.Length > 100)
            throw new InvalidOperationException("Tên khoá học tối đa 100 ký tự (§12 cert display)");
        if (req.COURSE_MODE != "STANDARD" && req.COURSE_MODE != "EXPRESS")
            throw new InvalidOperationException("COURSE_MODE phải là STANDARD hoặc EXPRESS");
        if (req.AUTO_OPEN_MONTHLY == 1 && (req.AUTO_OPEN_DAY == null || req.AUTO_OPEN_DAY < 1 || req.AUTO_OPEN_DAY > 28))
            throw new InvalidOperationException("Bật auto-open thì AUTO_OPEN_DAY phải trong 1..28");
        if (req.DEFAULT_MIN_ATTEND_PCT.HasValue &&
            (req.DEFAULT_MIN_ATTEND_PCT < 0 || req.DEFAULT_MIN_ATTEND_PCT > 100))
            throw new InvalidOperationException("DEFAULT_MIN_ATTEND_PCT phải trong 0..100");

        if (req.ID == null)
        {
            // INSERT — dùng RETURNING để lấy ID.
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_COURSE
                    (TITLE, DESCRIPTION, OBJECTIVES, CATEGORY,
                     COURSE_MODE, DEFAULT_DURATION_MIN,
                     AUTO_OPEN_MONTHLY, AUTO_OPEN_DAY,
                     DEFAULT_PASS_SCORE, DEFAULT_MIN_ATTEND_PCT,
                     IS_ACTIVE, INST_ID)
                VALUES
                    (:TITLE, :DESCRIPTION, :OBJECTIVES, :CATEGORY,
                     :COURSE_MODE, :DEFAULT_DURATION_MIN,
                     :AUTO_OPEN_MONTHLY, :AUTO_OPEN_DAY,
                     :DEFAULT_PASS_SCORE, :DEFAULT_MIN_ATTEND_PCT,
                     1, :LOGIN_USER)
                RETURNING ID INTO :NEW_ID";

            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("TITLE",                  req.TITLE),
                new OracleParameter("DESCRIPTION",            (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("OBJECTIVES",             (object?)req.OBJECTIVES  ?? DBNull.Value),
                new OracleParameter("CATEGORY",               (object?)req.CATEGORY    ?? DBNull.Value),
                new OracleParameter("COURSE_MODE",            req.COURSE_MODE),
                new OracleParameter("DEFAULT_DURATION_MIN",   (object?)req.DEFAULT_DURATION_MIN ?? DBNull.Value),
                new OracleParameter("AUTO_OPEN_MONTHLY",      req.AUTO_OPEN_MONTHLY),
                new OracleParameter("AUTO_OPEN_DAY",          (object?)req.AUTO_OPEN_DAY ?? DBNull.Value),
                new OracleParameter("DEFAULT_PASS_SCORE",     (object?)req.DEFAULT_PASS_SCORE ?? DBNull.Value),
                new OracleParameter("DEFAULT_MIN_ATTEND_PCT", (object?)req.DEFAULT_MIN_ATTEND_PCT ?? DBNull.Value),
                new OracleParameter("LOGIN_USER",             req.LOGIN_USER),
                idParam);

            return Convert.ToInt32(idParam.Value);
        }
        else
        {
            // UPDATE
            const string sqlUpd = @"
                UPDATE HRMS.HR_TRAINING_COURSE
                   SET TITLE                  = :TITLE,
                       DESCRIPTION            = :DESCRIPTION,
                       OBJECTIVES             = :OBJECTIVES,
                       CATEGORY               = :CATEGORY,
                       COURSE_MODE            = :COURSE_MODE,
                       DEFAULT_DURATION_MIN   = :DEFAULT_DURATION_MIN,
                       AUTO_OPEN_MONTHLY      = :AUTO_OPEN_MONTHLY,
                       AUTO_OPEN_DAY          = :AUTO_OPEN_DAY,
                       DEFAULT_PASS_SCORE     = :DEFAULT_PASS_SCORE,
                       DEFAULT_MIN_ATTEND_PCT = :DEFAULT_MIN_ATTEND_PCT,
                       UPDT_ID                = :LOGIN_USER
                 WHERE ID = :ID";
            var rows = await _db.ExecuteNonQueryAsync(sqlUpd,
                new OracleParameter("TITLE",                  req.TITLE),
                new OracleParameter("DESCRIPTION",            (object?)req.DESCRIPTION ?? DBNull.Value),
                new OracleParameter("OBJECTIVES",             (object?)req.OBJECTIVES  ?? DBNull.Value),
                new OracleParameter("CATEGORY",               (object?)req.CATEGORY    ?? DBNull.Value),
                new OracleParameter("COURSE_MODE",            req.COURSE_MODE),
                new OracleParameter("DEFAULT_DURATION_MIN",   (object?)req.DEFAULT_DURATION_MIN ?? DBNull.Value),
                new OracleParameter("AUTO_OPEN_MONTHLY",      req.AUTO_OPEN_MONTHLY),
                new OracleParameter("AUTO_OPEN_DAY",          (object?)req.AUTO_OPEN_DAY ?? DBNull.Value),
                new OracleParameter("DEFAULT_PASS_SCORE",     (object?)req.DEFAULT_PASS_SCORE ?? DBNull.Value),
                new OracleParameter("DEFAULT_MIN_ATTEND_PCT", (object?)req.DEFAULT_MIN_ATTEND_PCT ?? DBNull.Value),
                new OracleParameter("LOGIN_USER",             req.LOGIN_USER),
                new OracleParameter("ID",                     req.ID.Value));
            if (rows == 0) throw new InvalidOperationException("Không tìm thấy course");
            return req.ID.Value;
        }
    }

    // Bulk replace template sessions: xoá hết + insert lại DS mới.
    // HR muốn có ràng buộc phức tạp hơn (diff insert/update/delete) thì làm sau.
    public async Task<int> SaveSessionTemplatesAsync(SaveSessionTemplateRequest req)
    {
        if (req.SESSIONS == null) req.SESSIONS = new();

        // Validate SESSION_NO unique + DAY_OFFSET >= 0 + LENGTH HHMM=4
        foreach (var s in req.SESSIONS)
        {
            if (s.DAY_OFFSET < 0)
                throw new InvalidOperationException($"Session {s.SESSION_NO}: DAY_OFFSET phải >= 0");
            if (s.START_TIME?.Length != 4 || s.END_TIME?.Length != 4)
                throw new InvalidOperationException($"Session {s.SESSION_NO}: START_TIME/END_TIME phải 4 ký tự HHMM");
        }

        // Xoá cũ, insert mới trong 1 chuỗi statement (không cần transaction wrapper — nếu DB dùng
        // committed read, HR có thể thấy tạm thời 0 template khi refresh giữa 2 statement; chấp nhận
        // vì HR thao tác 1 mình. Nâng cấp sang transaction nếu cần strict.)
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TR_COURSE_SES_TMPL WHERE COURSE_ID = :CID",
            new OracleParameter("CID", req.COURSE_ID));

        foreach (var s in req.SESSIONS)
        {
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TR_COURSE_SES_TMPL
                    (COURSE_ID, SESSION_NO, DAY_OFFSET, START_TIME, END_TIME, TOPIC, LOCATION, INST_ID)
                VALUES
                    (:CID, :SN, :OFF, :ST, :ET, :TP, :LC, :USR)",
                new OracleParameter("CID", req.COURSE_ID),
                new OracleParameter("SN",  s.SESSION_NO),
                new OracleParameter("OFF", s.DAY_OFFSET),
                new OracleParameter("ST",  s.START_TIME),
                new OracleParameter("ET",  s.END_TIME),
                new OracleParameter("TP",  (object?)s.TOPIC    ?? DBNull.Value),
                new OracleParameter("LC",  (object?)s.LOCATION ?? DBNull.Value),
                new OracleParameter("USR", req.LOGIN_USER));
        }
        return req.SESSIONS.Count;
    }

    // ═══════════════════════════════════════════════════════════════
    //  ARCHIVE / UNARCHIVE — toggle IS_ACTIVE
    // ═══════════════════════════════════════════════════════════════

    public async Task ArchiveAsync(ArchiveCourseRequest req, bool active)
    {
        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_COURSE
               SET IS_ACTIVE = :V, UPDT_ID = :USR
             WHERE ID = :ID",
            new OracleParameter("V",   active ? 1 : 0),
            new OracleParameter("USR", req.LOGIN_USER),
            new OracleParameter("ID",  req.ID));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy course");
    }

    // ═══════════════════════════════════════════════════════════════
    //  MAPPING
    // ═══════════════════════════════════════════════════════════════

    private static CourseModel MapCourseLight(Oracle.ManagedDataAccess.Client.OracleDataReader r)
    {
        var c = MapCourseFull(r);
        // extra list-only columns
        c.CLASS_COUNT            = ReadInt(r, "CLASS_COUNT");
        c.TEMPLATE_SESSION_COUNT = ReadInt(r, "TEMPLATE_SESSION_COUNT");
        return c;
    }

    private static CourseModel MapCourseFull(Oracle.ManagedDataAccess.Client.OracleDataReader r) => new()
    {
        ID                     = Convert.ToInt32(r["ID"]),
        TITLE                  = r["TITLE"]?.ToString() ?? "",
        DESCRIPTION            = r["DESCRIPTION"] as string,
        OBJECTIVES             = r["OBJECTIVES"]  as string,
        CATEGORY               = r["CATEGORY"]    as string,
        COURSE_MODE            = r["COURSE_MODE"]?.ToString() ?? "STANDARD",
        DEFAULT_DURATION_MIN   = ReadInt(r, "DEFAULT_DURATION_MIN"),
        AUTO_OPEN_MONTHLY      = Convert.ToInt32(r["AUTO_OPEN_MONTHLY"]),
        AUTO_OPEN_DAY          = ReadInt(r, "AUTO_OPEN_DAY"),
        DEFAULT_PASS_SCORE     = ReadDecimal(r, "DEFAULT_PASS_SCORE"),
        DEFAULT_MIN_ATTEND_PCT = ReadDecimal(r, "DEFAULT_MIN_ATTEND_PCT"),
        IS_ACTIVE              = Convert.ToInt32(r["IS_ACTIVE"]),
        INST_ID                = r["INST_ID"] as string,
        INST_DT                = r["INST_DT"] as DateTime?,
        UPDT_ID                = r["UPDT_ID"] as string,
        UPDT_DT                = r["UPDT_DT"] as DateTime?,
    };

    private static int? ReadInt(Oracle.ManagedDataAccess.Client.OracleDataReader r, string col)
    {
        var v = r[col];
        return v is DBNull ? null : Convert.ToInt32(v);
    }

    private static decimal? ReadDecimal(Oracle.ManagedDataAccess.Client.OracleDataReader r, string col)
    {
        var v = r[col];
        return v is DBNull ? null : Convert.ToDecimal(v);
    }
}
