using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Theo dõi tiến độ xem video từng buổi (đào tạo online). Xem dở thoát ra không tính đã học —
// IS_COMPLETED chỉ set khi đã xem tới >=95% (chừa sai số buffer cuối video) hoặc client báo 'ended'.
// Không cho tua lùi làm giảm tiến độ đã ghi nhận (LAST_POSITION_SEC chỉ tăng).
public class TrainingVideoProgressService
{
    private readonly OracleService _db;

    public TrainingVideoProgressService(OracleService db) { _db = db; }

    public async Task UpdateProgressAsync(UpdateVideoProgressRequest req)
    {
        if (req.DURATION_SEC <= 0) throw new InvalidOperationException("DURATION_SEC không hợp lệ");
        var isCompleted = req.IS_ENDED || (double)req.POSITION_SEC / req.DURATION_SEC >= 0.95;

        await _db.ExecuteNonQueryAsync(@"
            MERGE INTO HRMS.HR_TRAINING_VIDEO_PROGRESS P
            USING (SELECT :MID AS MATERIAL_ID, :EMP AS EMPCD FROM DUAL) SRC
               ON (P.MATERIAL_ID = SRC.MATERIAL_ID AND P.EMPCD = SRC.EMPCD)
             WHEN MATCHED THEN UPDATE SET
                    LAST_POSITION_SEC = GREATEST(P.LAST_POSITION_SEC, :POS),
                    DURATION_SEC      = :DUR,
                    IS_COMPLETED      = CASE WHEN P.IS_COMPLETED = 1 OR :DONE = 1 THEN 1 ELSE 0 END,
                    COMPLETED_DT      = CASE WHEN P.IS_COMPLETED = 0 AND :DONE = 1 THEN SYSDATE ELSE P.COMPLETED_DT END,
                    UPDT_DT           = SYSDATE
             WHEN NOT MATCHED THEN INSERT
                    (MATERIAL_ID, EMPCD, LAST_POSITION_SEC, DURATION_SEC, IS_COMPLETED, COMPLETED_DT, UPDT_DT)
                    VALUES (:MID, :EMP, :POS, :DUR, :DONE, CASE WHEN :DONE = 1 THEN SYSDATE ELSE NULL END, SYSDATE)",
            new OracleParameter("MID",  req.MATERIAL_ID),
            new OracleParameter("EMP",  req.EMPCD),
            new OracleParameter("POS",  req.POSITION_SEC),
            new OracleParameter("DUR",  req.DURATION_SEC),
            new OracleParameter("DONE", isCompleted ? 1 : 0));
    }

    // Dùng để gate làm bài test (TrainingAttemptService) — mọi video bắt buộc gắn theo session
    // (không CANCELLED, đúng scope GROUP_ID) của lớp phải đã IS_COMPLETED=1 với empcd này.
    public async Task<(bool AllWatched, int MissingCount)> IsClassFullyWatchedAsync(int classId, string empcd, int? groupId)
    {
        const string sql = @"
            SELECT COUNT(*) AS MISSING
              FROM HRMS.HR_TRAINING_MATERIAL M
              JOIN HRMS.HR_TRAINING_SESSION S ON S.ID = M.SESSION_ID
              LEFT JOIN HRMS.HR_TRAINING_VIDEO_PROGRESS P
                     ON P.MATERIAL_ID = M.ID AND P.EMPCD = :EMP
             WHERE S.CLASS_ID = :CID
               AND S.STATUS != 'CANCELLED'
               AND (S.GROUP_ID IS NULL OR S.GROUP_ID = :GID)
               AND M.MATERIAL_LEVEL = 'SESSION'
               AND M.FILE_TYPE = 'MP4'
               AND M.IS_REQUIRED = 1
               AND NVL(P.IS_COMPLETED, 0) = 0";
        var missing = (await _db.ExecuteQueryAsync(sql,
            r => Convert.ToInt32(r["MISSING"]),
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd),
            new OracleParameter("GID", (object?)groupId ?? DBNull.Value))).FirstOrDefault();
        return (missing == 0, missing);
    }
}
