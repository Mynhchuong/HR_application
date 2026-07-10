using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Material CRUD + track view (§8).
// File upload thật (multipart, network share) dùng existing pattern ImageController — service này chỉ metadata.
public class TrainingMaterialService
{
    private readonly OracleService _db;

    public TrainingMaterialService(OracleService db) { _db = db; }

    // List materials theo Class (bao gồm level=CLASS + level=COURSE của Course parent)
    public async Task<List<MaterialModel>> ListByClassAsync(int classId, string? currentEmpcd = null)
    {
        const string sql = @"
            SELECT M.ID, M.MATERIAL_LEVEL, M.COURSE_ID, M.CLASS_ID,
                   M.TITLE, M.FILE_NAME, M.FILE_TYPE, M.FILE_URL,
                   M.IS_REQUIRED, M.DISPLAY_ORDER,
                   M.INST_ID, M.INST_DT,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_MATERIAL_VIEW V
                     WHERE V.MATERIAL_ID = M.ID) AS VIEW_COUNT,
                   CASE WHEN :EMP IS NULL THEN NULL
                        WHEN EXISTS (
                             SELECT 1 FROM HRMS.HR_TRAINING_MATERIAL_VIEW V
                              WHERE V.MATERIAL_ID = M.ID AND V.EMPCD = :EMP
                        ) THEN 1 ELSE 0 END AS HAS_VIEWED
              FROM HRMS.HR_TRAINING_MATERIAL M
             WHERE (M.MATERIAL_LEVEL = 'CLASS' AND M.CLASS_ID = :CID)
                OR (M.MATERIAL_LEVEL = 'COURSE'
                    AND M.COURSE_ID = (SELECT COURSE_ID FROM HRMS.HR_TRAINING_CLASS WHERE ID = :CID))
             ORDER BY M.DISPLAY_ORDER, M.ID";
        return await _db.ExecuteQueryAsync(sql, MapMaterial,
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", (object?)currentEmpcd ?? DBNull.Value));
    }

    public async Task<List<MaterialModel>> ListByCourseAsync(int courseId)
    {
        const string sql = @"
            SELECT M.ID, M.MATERIAL_LEVEL, M.COURSE_ID, M.CLASS_ID,
                   M.TITLE, M.FILE_NAME, M.FILE_TYPE, M.FILE_URL,
                   M.IS_REQUIRED, M.DISPLAY_ORDER,
                   M.INST_ID, M.INST_DT,
                   NULL AS VIEW_COUNT,
                   NULL AS HAS_VIEWED
              FROM HRMS.HR_TRAINING_MATERIAL M
             WHERE M.MATERIAL_LEVEL = 'COURSE' AND M.COURSE_ID = :CID
             ORDER BY M.DISPLAY_ORDER, M.ID";
        return await _db.ExecuteQueryAsync(sql, MapMaterial,
            new OracleParameter("CID", courseId));
    }

    public async Task<int> SaveAsync(SaveMaterialRequest req)
    {
        // Validate — DDL đã CHECK level+id consistency, nhưng validate sớm cho UX message
        if (req.MATERIAL_LEVEL != "COURSE" && req.MATERIAL_LEVEL != "CLASS")
            throw new InvalidOperationException("MATERIAL_LEVEL phải COURSE hoặc CLASS");
        if (req.MATERIAL_LEVEL == "COURSE" && (req.COURSE_ID == null || req.CLASS_ID != null))
            throw new InvalidOperationException("Level COURSE cần COURSE_ID + CLASS_ID = null");
        if (req.MATERIAL_LEVEL == "CLASS" && (req.CLASS_ID == null || req.COURSE_ID != null))
            throw new InvalidOperationException("Level CLASS cần CLASS_ID + COURSE_ID = null");
        if (string.IsNullOrWhiteSpace(req.TITLE)) throw new InvalidOperationException("TITLE required");
        if (string.IsNullOrWhiteSpace(req.FILE_NAME)) throw new InvalidOperationException("FILE_NAME required");
        var okTypes = new[] { "PDF", "DOCX", "MP4", "IMG", "LINK" };
        if (!okTypes.Contains(req.FILE_TYPE))
            throw new InvalidOperationException("FILE_TYPE phải PDF|DOCX|MP4|IMG|LINK");

        if (req.ID == null)
        {
            const string sqlIns = @"
                INSERT INTO HRMS.HR_TRAINING_MATERIAL
                    (MATERIAL_LEVEL, COURSE_ID, CLASS_ID,
                     TITLE, FILE_NAME, FILE_TYPE, FILE_URL,
                     IS_REQUIRED, DISPLAY_ORDER, INST_ID)
                VALUES
                    (:LV, :COID, :CLID, :TT, :FN, :FT, :FU, :REQ, :ORD, :USR)
                RETURNING ID INTO :NEW_ID";
            var idParam = new OracleParameter("NEW_ID", OracleDbType.Int32)
            {
                Direction = System.Data.ParameterDirection.Output
            };
            await _db.ExecuteNonQueryAsync(sqlIns,
                new OracleParameter("LV",   req.MATERIAL_LEVEL),
                new OracleParameter("COID", (object?)req.COURSE_ID ?? DBNull.Value),
                new OracleParameter("CLID", (object?)req.CLASS_ID  ?? DBNull.Value),
                new OracleParameter("TT",   req.TITLE),
                new OracleParameter("FN",   req.FILE_NAME),
                new OracleParameter("FT",   req.FILE_TYPE),
                new OracleParameter("FU",   (object?)req.FILE_URL ?? DBNull.Value),
                new OracleParameter("REQ",  req.IS_REQUIRED),
                new OracleParameter("ORD",  req.DISPLAY_ORDER),
                new OracleParameter("USR",  req.LOGIN_USER),
                idParam);
            return Convert.ToInt32(idParam.Value);
        }
        else
        {
            await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_MATERIAL
                   SET TITLE         = :TT,
                       FILE_NAME     = :FN,
                       FILE_TYPE     = :FT,
                       FILE_URL      = :FU,
                       IS_REQUIRED   = :REQ,
                       DISPLAY_ORDER = :ORD,
                       UPDT_ID       = :USR
                 WHERE ID = :ID",
                new OracleParameter("TT",  req.TITLE),
                new OracleParameter("FN",  req.FILE_NAME),
                new OracleParameter("FT",  req.FILE_TYPE),
                new OracleParameter("FU",  (object?)req.FILE_URL ?? DBNull.Value),
                new OracleParameter("REQ", req.IS_REQUIRED),
                new OracleParameter("ORD", req.DISPLAY_ORDER),
                new OracleParameter("USR", req.LOGIN_USER),
                new OracleParameter("ID",  req.ID.Value));
            return req.ID.Value;
        }
    }

    public async Task DeleteAsync(DeleteMaterialRequest req)
    {
        // Xoá luôn view rows để không orphan (nếu cần audit thì đổi thành soft delete sau).
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_MATERIAL_VIEW WHERE MATERIAL_ID = :ID",
            new OracleParameter("ID", req.ID));
        var rows = await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_MATERIAL WHERE ID = :ID",
            new OracleParameter("ID", req.ID));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy material");
    }

    // Track view — insert first-time only. PK (MATERIAL_ID, EMPCD) → dupe ignored.
    public async Task<bool> TrackViewAsync(MaterialViewRequest req)
    {
        try
        {
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO HRMS.HR_TRAINING_MATERIAL_VIEW (MATERIAL_ID, EMPCD, FIRST_VIEW_DT)
                VALUES (:MID, :EMP, SYSDATE)",
                new OracleParameter("MID", req.MATERIAL_ID),
                new OracleParameter("EMP", req.EMPCD));
            return true;   // new view
        }
        catch (OracleException ex) when (ex.Number == 1)
        {
            return false;  // đã view rồi
        }
    }

    private static MaterialModel MapMaterial(OracleDataReader r) => new()
    {
        ID             = Convert.ToInt32(r["ID"]),
        MATERIAL_LEVEL = r["MATERIAL_LEVEL"]?.ToString() ?? "CLASS",
        COURSE_ID      = r["COURSE_ID"] is DBNull ? null : Convert.ToInt32(r["COURSE_ID"]),
        CLASS_ID       = r["CLASS_ID"]  is DBNull ? null : Convert.ToInt32(r["CLASS_ID"]),
        TITLE          = r["TITLE"]?.ToString() ?? "",
        FILE_NAME      = r["FILE_NAME"]?.ToString() ?? "",
        FILE_TYPE      = r["FILE_TYPE"]?.ToString() ?? "PDF",
        FILE_URL       = r["FILE_URL"] as string,
        IS_REQUIRED    = Convert.ToInt32(r["IS_REQUIRED"]),
        DISPLAY_ORDER  = Convert.ToInt32(r["DISPLAY_ORDER"]),
        INST_ID        = r["INST_ID"] as string,
        INST_DT        = r["INST_DT"] as DateTime?,
        VIEW_COUNT     = r["VIEW_COUNT"] is DBNull ? null : Convert.ToInt32(r["VIEW_COUNT"]),
        HAS_VIEWED     = r["HAS_VIEWED"] is DBNull ? null : Convert.ToInt32(r["HAS_VIEWED"]),
    };
}
