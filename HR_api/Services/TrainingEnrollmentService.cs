using HR_api.Data;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// Enrollment: bulk assign, self-register, pre-assign mandatory, approve/reject, group assignment.
// "Là mandatory?" = SOURCE='ASSIGNED' (§3.3) — không cần cột riêng.
public class TrainingEnrollmentService
{
    private readonly OracleService _db;
    private readonly TrainingNotificationService _noti;

    public TrainingEnrollmentService(OracleService db, TrainingNotificationService noti)
    {
        _db = db;
        _noti = noti;
    }

    // Enqueue helper — fetch Class + Course info cho placeholders
    private async Task<Dictionary<string, string>> BuildClassPlaceholdersAsync(int classId)
    {
        return (await _db.ExecuteQueryAsync(@"
            SELECT CL.CLASS_NAME, TO_CHAR(CL.START_DATE,'DD/MM/YYYY') START_DATE_STR
              FROM HRMS.HR_TRAINING_CLASS CL
             WHERE CL.ID = :ID",
            r => new Dictionary<string, string>
            {
                ["className"] = r["CLASS_NAME"]?.ToString() ?? "",
                ["startDate"] = r["START_DATE_STR"]?.ToString() ?? "",
            }, new OracleParameter("ID", classId))).FirstOrDefault() ?? new();
    }

    // ═══════════════════════════════════════════════════════════════
    //  LIST enrollment của Class
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<EnrollmentModel>> ListAsync(int classId, string? status, int? groupId)
    {
        const string sql = @"
            SELECT E.CLASS_ID, E.EMPCD, EC.CNAME AS EMP_NAME,
                   EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                   E.SOURCE, E.STATUS, E.DROP_REASON,
                   E.GROUP_ID, G.GROUP_NAME,
                   E.FINAL_SCORE, E.ATTENDANCE_PERCENT, E.IS_CERTIFIED, E.COMPLETION_DATE,
                   E.INST_ID, E.INST_DT, E.UPDT_ID, E.UPDT_DT
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = E.GROUP_ID
             WHERE E.CLASS_ID = :CID
               AND (:P_STATUS IS NULL OR E.STATUS   = :P_STATUS)
               AND (:P_GROUP  IS NULL OR E.GROUP_ID = :P_GROUP)
             ORDER BY E.STATUS, E.EMPCD";

        return await _db.ExecuteQueryAsync(sql, MapEnrollment,
            new OracleParameter("CID",       classId),
            new OracleParameter("P_STATUS", (object?)status  ?? DBNull.Value),
            new OracleParameter("P_GROUP",  (object?)groupId ?? DBNull.Value));
    }

    // 1 enrollment cụ thể — student class detail dùng để biết nhóm của chính mình (GV nhóm nào dạy mình)
    public async Task<EnrollmentModel?> GetOneAsync(int classId, string empcd)
    {
        return (await _db.ExecuteQueryAsync(@"
            SELECT E.CLASS_ID, E.EMPCD, EC.CNAME AS EMP_NAME,
                   EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                   E.SOURCE, E.STATUS, E.DROP_REASON,
                   E.GROUP_ID, G.GROUP_NAME,
                   E.FINAL_SCORE, E.ATTENDANCE_PERCENT, E.IS_CERTIFIED, E.COMPLETION_DATE,
                   E.INST_ID, E.INST_DT, E.UPDT_ID, E.UPDT_DT
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = E.GROUP_ID
             WHERE E.CLASS_ID = :CID AND E.EMPCD = :EMP",
            MapEnrollment,
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd))).FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════
    //  BULK ASSIGN (mode ASSIGNED hoặc thêm vào Class ASSIGNED đã publish)
    //  Semantic: SOURCE='ASSIGNED', STATUS='ENROLLED' luôn. MERGE để idempotent.
    // ═══════════════════════════════════════════════════════════════

    // Chạy toàn bộ trong 1 transaction thật (OracleTxScope) — khoá HR_TRAINING_CLASS bằng
    // FOR UPDATE trước khi check cap + loop insert/upgrade, rollback hết nếu có bước fail giữa chừng
    // (all-or-nothing đúng yêu cầu §3.3). Cùng lock với SelfRegisterAsync nên 2 luồng
    // (HR bulk/pre-assign và NV tự đăng ký) không thể cùng đọc count "cũ" của nhau.
    public async Task<BulkAssignResult> BulkAssignAsync(BulkAssignRequest req)
    {
        var res = new BulkAssignResult { TOTAL_INPUT = req.EMPCDS?.Count ?? 0 };
        if (req.EMPCDS == null || req.EMPCDS.Count == 0) return res;

        var distinctIn = req.EMPCDS.Distinct().ToList();
        var newAssigned = new List<string>();
        var classStatus = "";

        await using var scope = await _db.BeginTransactionAsync();
        try
        {
            // Khoá Class row — validate status + đọc mode/cap trong cùng transaction.
            var meta = (await _db.ExecuteQueryAsync(scope, @"
                SELECT STATUS, MAX_STUDENTS FROM HRMS.HR_TRAINING_CLASS
                 WHERE ID = :CID FOR UPDATE",
                r => new
                {
                    ST  = r["STATUS"]?.ToString() ?? "",
                    MAX = r["MAX_STUDENTS"] is DBNull ? (int?)null : Convert.ToInt32(r["MAX_STUDENTS"]),
                }, new OracleParameter("CID", req.CLASS_ID))).FirstOrDefault();

            if (meta == null) throw new InvalidOperationException("Không tìm thấy Class");
            if (meta.ST is "CLOSED" or "CANCELLED" or "COMPLETED")
                throw new InvalidOperationException($"Class đang {meta.ST}, không assign được");
            classStatus = meta.ST;

            // Cap check (nếu Class OPEN có MAX_STUDENTS): occupied + newInput - existingInput.
            // An toàn tuyệt đối vì Class row đã bị khoá FOR UPDATE ở trên.
            if (meta.MAX.HasValue)
            {
                var occupied = (await _db.ExecuteQueryAsync(scope, @"
                    SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
                     WHERE CLASS_ID = :CID AND STATUS IN ('ENROLLED','PENDING_APPROVAL')",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("CID", req.CLASS_ID))).First();

                int existing = 0;
                const int chunkSize = 500;
                for (int i = 0; i < distinctIn.Count; i += chunkSize)
                {
                    var chunk = distinctIn.Skip(i).Take(chunkSize).ToList();
                    var placeholders = string.Join(",", chunk.Select((_, idx) => $":E{idx}"));
                    var sql = $@"
                        SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
                         WHERE CLASS_ID = :CID AND EMPCD IN ({placeholders})
                           AND STATUS IN ('ENROLLED','PENDING_APPROVAL')";
                    var ps = new List<OracleParameter> { new OracleParameter("CID", req.CLASS_ID) };
                    for (int idx = 0; idx < chunk.Count; idx++)
                        ps.Add(new OracleParameter($"E{idx}", chunk[idx]));
                    existing += (await _db.ExecuteQueryAsync(scope, sql, r => Convert.ToInt32(r["CNT"]), ps.ToArray())).First();
                }

                int netNew = distinctIn.Count - existing;
                if (occupied + netNew > meta.MAX.Value)
                {
                    var over = occupied + netNew - meta.MAX.Value;
                    throw new InvalidOperationException(
                        $"Vượt cap {over} slot (occupied={occupied}, cap={meta.MAX}). Tăng MAX_STUDENTS hoặc bỏ bớt.");
                }
            }

            foreach (var empcd in distinctIn)
            {
                // Check nhân viên tồn tại trong ECM100
                var count = await _db.ExecuteQueryAsync(scope,
                    "SELECT COUNT(*) CNT FROM HRMS.ECM100 WHERE EMPCD = :EMP",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("EMP", empcd));

                if (count.FirstOrDefault() == 0)
                {
                    res.FAILED_EMPCDS.Add(empcd);
                    continue;
                }

                var kind = await UpsertEnrollmentAsync(scope, req.CLASS_ID, empcd, req.LOGIN_USER);
                switch (kind)
                {
                    case UpsertKind.Inserted:
                        res.NEW_INSERTED++;
                        newAssigned.Add(empcd);
                        break;
                    case UpsertKind.Upgraded:       res.UPGRADED++;  newAssigned.Add(empcd); break;
                    case UpsertKind.SkippedDropped: res.SKIPPED_DROPPED++; break;
                    case UpsertKind.SkippedExisted: res.SKIPPED_EXISTED++; break;
                }
            }

            await scope.CommitAsync();
        }
        catch
        {
            await scope.RollbackAsync();
            throw;
        }

        // Enqueue TRAINING_ASSIGNED cho các NV mới được assign / upgrade (§13) — làm SAU khi commit
        // thành công. Không cần chung transaction với enrollment (noti queue là hệ thống riêng, retry được).
        // CHỈ gửi ngay khi lớp ĐÃ chốt lịch (thêm người giữa chừng). Lớp còn DRAFT/OPEN_FOR_REGISTRATION:
        // dồn đến khi HR bấm "Lên lịch học" (FinalizeEnrollmentAsync gửi toàn bộ ENROLLED có dedup) —
        // tránh báo nhầm cho NV khi HR còn đang thêm/xóa nháp danh sách.
        if (newAssigned.Count > 0 && classStatus is "SCHEDULED" or "IN_PROGRESS")
        {
            var ph = await BuildClassPlaceholdersAsync(req.CLASS_ID);
            await _noti.EnqueueBulkAsync("TRAINING_ASSIGNED", newAssigned, ph, classId: req.CLASS_ID);
        }
        return res;
    }

    // Pre-assign identical logic đến bulk assign — semantically dùng cho OPEN mode.
    public Task<BulkAssignResult> BulkPreAssignAsync(BulkPreAssignRequest req)
        => BulkAssignAsync(new BulkAssignRequest
        {
            CLASS_ID   = req.CLASS_ID,
            EMPCDS     = req.EMPCDS,
            LOGIN_USER = req.LOGIN_USER,
        });

    // Result kind for internal MERGE-lite
    private enum UpsertKind { Inserted, Upgraded, SkippedDropped, SkippedExisted }

    private async Task<UpsertKind> UpsertEnrollmentAsync(OracleTxScope scope, int classId, string empcd, string actor)
    {
        // Check row cũ
        var cur = (await _db.ExecuteQueryAsync(scope,
            "SELECT SOURCE, STATUS FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            r => new { SOURCE = r["SOURCE"]?.ToString() ?? "", STATUS = r["STATUS"]?.ToString() ?? "" },
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd))).FirstOrDefault();

        if (cur == null)
        {
            await _db.ExecuteNonQueryAsync(scope, @"
                INSERT INTO HRMS.HR_TRAINING_ENROLLMENT
                    (CLASS_ID, EMPCD, SOURCE, STATUS, INST_ID)
                VALUES (:CID, :EMP, 'ASSIGNED', 'ENROLLED', :USR)",
                new OracleParameter("CID", classId),
                new OracleParameter("EMP", empcd),
                new OracleParameter("USR", actor));
            return UpsertKind.Inserted;
        }

        if (cur.STATUS == "DROPPED") return UpsertKind.SkippedDropped;

        if (cur.SOURCE == "ASSIGNED" && cur.STATUS == "ENROLLED")
            return UpsertKind.SkippedExisted;

        // Upgrade: SELF_REGISTER PENDING/ENROLLED, hoặc REJECTED → ASSIGNED/ENROLLED
        await _db.ExecuteNonQueryAsync(scope, @"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET SOURCE = 'ASSIGNED', STATUS = 'ENROLLED', UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            new OracleParameter("USR", actor),
            new OracleParameter("CID", classId),
            new OracleParameter("EMP", empcd));
        return UpsertKind.Upgraded;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SELF-REGISTER (NV bấm Đăng ký) — mode OPEN chỉ
    //  Race safe: SELECT FOR UPDATE trên Class + count trong same transaction.
    //  Dùng OracleService không có transaction API — dùng optimistic check + PK violation làm safety net.
    // ═══════════════════════════════════════════════════════════════

    // Race-safe (§3.2): SELECT ... FOR UPDATE trên HR_TRAINING_CLASS trước khi check cap + insert,
    // tất cả trong CÙNG 1 transaction/connection (OracleTxScope) — khoá row Class cho tới commit/rollback
    // nên request thứ 2 (self-register khác, hoặc bulk/pre-assign HR) phải đợi request đầu commit xong
    // mới đọc được count mới nhất. Trước đây dùng 1 câu INSERT...SELECT không giữ lock nào → có thể
    // vượt cap khi 2 request tới gần như đồng thời (đã ghi nhận ở audit).
    public async Task<(bool ok, string? error)> SelfRegisterAsync(SelfRegisterRequest req)
    {
        await using var scope = await _db.BeginTransactionAsync();
        try
        {
            var meta = (await _db.ExecuteQueryAsync(scope, @"
                SELECT STATUS, REGISTRATION_MODE, MAX_STUDENTS, REGISTRATION_DEADLINE
                  FROM HRMS.HR_TRAINING_CLASS
                 WHERE ID = :ID
                   FOR UPDATE",
                r => new
                {
                    ST       = r["STATUS"]?.ToString() ?? "",
                    MODE     = r["REGISTRATION_MODE"]?.ToString() ?? "",
                    MAX      = r["MAX_STUDENTS"] is DBNull ? (int?)null : Convert.ToInt32(r["MAX_STUDENTS"]),
                    DEADLINE = r["REGISTRATION_DEADLINE"] as DateTime?,
                }, new OracleParameter("ID", req.CLASS_ID))).FirstOrDefault();

            if (meta == null) { await scope.RollbackAsync(); return (false, "Không tìm thấy Class"); }
            if (meta.ST != "OPEN_FOR_REGISTRATION") { await scope.RollbackAsync(); return (false, "Lớp không mở đăng ký"); }
            if (meta.MODE != "OPEN") { await scope.RollbackAsync(); return (false, "Lớp không hỗ trợ chế độ tự đăng ký"); }
            if (meta.DEADLINE.HasValue && DateTime.Now > meta.DEADLINE.Value)
            {
                await scope.RollbackAsync();
                return (false, "Đã hết hạn đăng ký");
            }

            // Đã đăng ký rồi? (double-click / gọi lại)
            var already = (await _db.ExecuteQueryAsync(scope,
                "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND EMPCD = :EMP",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("CID", req.CLASS_ID),
                new OracleParameter("EMP", req.EMPCD))).First();
            if (already > 0)
            {
                await scope.RollbackAsync();
                return (false, "Bạn đã đăng ký lớp này rồi");
            }

            // Cap check — an toàn vì Class row đã bị khoá FOR UPDATE, không request nào khác
            // (self-register khác hoặc bulk/pre-assign HR) đọc được count "cũ" trong lúc này.
            // MAX_STUDENTS NULL = unlimited (§3.2).
            if (meta.MAX.HasValue)
            {
                var occupied = (await _db.ExecuteQueryAsync(scope, @"
                    SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
                     WHERE CLASS_ID = :CID AND STATUS IN ('ENROLLED','PENDING_APPROVAL')",
                    r => Convert.ToInt32(r["CNT"]),
                    new OracleParameter("CID", req.CLASS_ID))).First();
                if (occupied >= meta.MAX.Value)
                {
                    await scope.RollbackAsync();
                    return (false, "Lớp học đã đủ, không nhận đăng ký nữa");
                }
            }

            try
            {
                await _db.ExecuteNonQueryAsync(scope, @"
                    INSERT INTO HRMS.HR_TRAINING_ENROLLMENT
                        (CLASS_ID, EMPCD, SOURCE, STATUS, INST_ID)
                    VALUES (:CID, :EMP, 'SELF_REGISTER', 'PENDING_APPROVAL', :EMP)",
                    new OracleParameter("CID", req.CLASS_ID),
                    new OracleParameter("EMP", req.EMPCD));
            }
            catch (OracleException ex) when (ex.Number == 1)
            {
                // An toàn lưới cuối — về lý thuyết không xảy ra nhờ FOR UPDATE, giữ lại phòng hờ.
                await scope.RollbackAsync();
                return (false, "Bạn đã đăng ký lớp này rồi");
            }

            await scope.CommitAsync();
            return (true, null);
        }
        finally
        {
            // OracleTxScope.DisposeAsync tự rollback nếu Commit/Rollback chưa được gọi (VD exception lạ).
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  APPROVE / REJECT (PENDING_APPROVAL → ENROLLED / REJECTED)
    // ═══════════════════════════════════════════════════════════════

    public async Task ApproveAsync(ApproveEnrollmentRequest req)
    {
        // Cap check trước khi approve (nếu OPEN có MAX)
        var status = await GetClassStatusAsync(req.CLASS_ID)
            ?? throw new InvalidOperationException("Không tìm thấy Class");
        if (status is "CLOSED" or "CANCELLED" or "COMPLETED")
            throw new InvalidOperationException($"Class đang {status}, không approve được");

        await ThrowIfExceedsCapAsync(req.CLASS_ID, new List<string> { req.EMPCD });

        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET STATUS = 'ENROLLED', UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND EMPCD = :EMP
               AND STATUS = 'PENDING_APPROVAL'",
            new OracleParameter("USR", req.LOGIN_USER),
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy đơn PENDING_APPROVAL");

        var ph = await BuildClassPlaceholdersAsync(req.CLASS_ID);
        await _noti.EnqueueAsync("TRAINING_REGISTER_APPROVED", req.EMPCD, ph, classId: req.CLASS_ID);
    }

    public async Task RejectAsync(RejectEnrollmentRequest req)
    {
        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET STATUS = 'REJECTED', UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND EMPCD = :EMP
               AND STATUS = 'PENDING_APPROVAL'",
            new OracleParameter("USR", req.LOGIN_USER),
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));
        if (rows == 0) throw new InvalidOperationException("Không tìm thấy đơn PENDING_APPROVAL");

        var ph = await BuildClassPlaceholdersAsync(req.CLASS_ID);
        await _noti.EnqueueAsync("TRAINING_REGISTER_REJECTED", req.EMPCD, ph, classId: req.CLASS_ID);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ASSIGN GROUP (§5b — bulk gán GROUP_ID cho DS EMPCD)
    // ═══════════════════════════════════════════════════════════════

    // §16 Recover — HR chuyển DROPPED → ENROLLED (case đá nhầm hoặc cứu xét)
    public async Task RecoverAsync(RecoverEnrollmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.REASON))
            throw new InvalidOperationException("REASON required cho audit");

        // Check cap trước khi recover (occupied + 1 <= MAX)
        await ThrowIfExceedsCapAsync(req.CLASS_ID, new List<string> { req.EMPCD });

        var rows = await _db.ExecuteNonQueryAsync(@"
            UPDATE HRMS.HR_TRAINING_ENROLLMENT
               SET STATUS = 'ENROLLED',
                   DROP_REASON = NULL,
                   UPDT_ID = :USR
             WHERE CLASS_ID = :CID AND EMPCD = :EMP AND STATUS = 'DROPPED'",
            new OracleParameter("USR", req.LOGIN_USER),
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));
        if (rows == 0)
            throw new InvalidOperationException("Không tìm thấy enrollment DROPPED");

        // Audit log — INSERT vào HR_TRAINING_AUDIT
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO HRMS.HR_TRAINING_AUDIT
                (ACTION, CLASS_ID, AFFECTED_COUNT, PAYLOAD, ACTOR_EMPCD)
            VALUES ('RECOVER_ENROLLMENT', :CID, 1, :PL, :USR)",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("PL",  System.Text.Json.JsonSerializer.Serialize(new
            {
                empcd  = req.EMPCD,
                reason = req.REASON,
            })),
            new OracleParameter("USR", req.LOGIN_USER));

        // Cùng rule với BulkAssign: lớp chưa chốt lịch thì không báo — FinalizeEnrollment sẽ gửi.
        var st = (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_CLASS WHERE ID = :CID",
            r => r["STATUS"]?.ToString() ?? "",
            new OracleParameter("CID", req.CLASS_ID))).FirstOrDefault();
        if (st is "SCHEDULED" or "IN_PROGRESS")
        {
            var ph = await BuildClassPlaceholdersAsync(req.CLASS_ID);
            await _noti.EnqueueAsync("TRAINING_ASSIGNED", req.EMPCD, ph, classId: req.CLASS_ID);
        }
    }

    public async Task<int> AssignGroupAsync(AssignGroupRequest req)
    {
        if (req.EMPCDS == null || req.EMPCDS.Count == 0) return 0;

        // Verify group thuộc Class (§16 ràng buộc code layer)
        if (req.GROUP_ID.HasValue)
        {
            var ok = (await _db.ExecuteQueryAsync(
                "SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_CLASS_GROUP WHERE ID = :GID AND CLASS_ID = :CID",
                r => Convert.ToInt32(r["CNT"]),
                new OracleParameter("GID", req.GROUP_ID.Value),
                new OracleParameter("CID", req.CLASS_ID))).First();
            if (ok == 0) throw new InvalidOperationException("Group không thuộc Class này");
        }

        int updated = 0;
        foreach (var empcd in req.EMPCDS.Distinct())
        {
            // §16: cấm reassign DROPPED
            var rows = await _db.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_TRAINING_ENROLLMENT
                   SET GROUP_ID = :GID, UPDT_ID = :USR
                 WHERE CLASS_ID = :CID
                   AND EMPCD = :EMP
                   AND STATUS <> 'DROPPED'",
                new OracleParameter("GID", (object?)req.GROUP_ID ?? DBNull.Value),
                new OracleParameter("USR", req.LOGIN_USER),
                new OracleParameter("CID", req.CLASS_ID),
                new OracleParameter("EMP", empcd));
            updated += rows;
        }
        return updated;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MY CLASSES — cho user (student view)
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<EnrollmentModel>> GetMyClassesAsync(string empcd)
    {
        // JOIN thêm Class + Course để user thấy tên lớp, ngày học (§4.1 my-classes view).
        const string sql = @"
            SELECT E.CLASS_ID, E.EMPCD, EC.CNAME AS EMP_NAME,
                   EC.DEPTCD, EC.LINECD, EC.WORKCD,
                   B.DEPTNM DEPT_NAME, B.TEAMNM LINE_NAME, B.WORKNM WORK_NAME,
                   E.SOURCE, E.STATUS, E.DROP_REASON,
                   E.GROUP_ID, G.GROUP_NAME,
                   E.FINAL_SCORE, E.ATTENDANCE_PERCENT, E.IS_CERTIFIED, E.COMPLETION_DATE,
                   E.INST_ID, E.INST_DT, E.UPDT_ID, E.UPDT_DT,
                   CL.CLASS_NAME, CL.STATUS AS CLASS_STATUS,
                   CL.START_DATE AS CLASS_START_DATE, CL.END_DATE AS CLASS_END_DATE,
                   CO.TITLE AS COURSE_TITLE, CO.COURSE_MODE AS CLASS_MODE,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_SESSION S
                     WHERE S.CLASS_ID = E.CLASS_ID
                       AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)) AS TOTAL_SESSIONS,
                   (SELECT COUNT(*) FROM HRMS.HR_TRAINING_ATTENDANCE A
                     WHERE A.EMPCD = E.EMPCD
                       AND A.STATUS IN ('PRESENT', 'LATE')
                       AND A.TEACHER_CONFIRMED = 1
                       AND A.SESSION_ID IN (
                           SELECT S.ID FROM HRMS.HR_TRAINING_SESSION S
                            WHERE S.CLASS_ID = E.CLASS_ID
                              AND S.STATUS = 'COMPLETED'
                              AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)
                       )) AS COMPLETED_SESSIONS,
                   (SELECT T_EC.CNAME FROM HRMS.HR_TRAINING_CLASS_TEACHER T
                      LEFT JOIN HRMS.ECM100 T_EC ON T_EC.EMPCD = T.EMPCD
                     WHERE T.CLASS_ID = E.CLASS_ID AND T.IS_PRIMARY = 1 AND ROWNUM = 1) AS PRIMARY_TEACHER_NAME
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              JOIN HRMS.HR_TRAINING_CLASS CL ON CL.ID = E.CLASS_ID
              JOIN HRMS.HR_TRAINING_COURSE CO ON CO.ID = CL.COURSE_ID
              LEFT JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.EAM410 B ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = E.GROUP_ID
             WHERE E.EMPCD = :EMP
               AND E.STATUS IN ('ENROLLED','PENDING_APPROVAL','COMPLETED','FAILED')
               AND CL.STATUS != 'CANCELLED'
             ORDER BY E.INST_DT DESC";
        return await _db.ExecuteQueryAsync(sql, MapEnrollmentWithClass, new OracleParameter("EMP", empcd));
    }

    // ═══════════════════════════════════════════════════════════════
    //  INTERNAL helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<string?> GetClassStatusAsync(int classId)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => r["STATUS"]?.ToString(),
            new OracleParameter("ID", classId))).FirstOrDefault();
    }

    // Nếu Class OPEN có MAX_STUDENTS: check `occupied + newInsertCount > MAX` → throw.
    // ASSIGNED mode + OPEN mode unlimited (MAX NULL) → skip.
    // Chunk 500 EMPCDs / query để tránh Oracle IN limit (1000 entries max, để buffer).
    private async Task ThrowIfExceedsCapAsync(int classId, List<string> incoming)
    {
        var meta = (await _db.ExecuteQueryAsync(
            "SELECT REGISTRATION_MODE, MAX_STUDENTS FROM HRMS.HR_TRAINING_CLASS WHERE ID = :ID",
            r => new
            {
                MODE = r["REGISTRATION_MODE"]?.ToString() ?? "",
                MAX  = r["MAX_STUDENTS"] is DBNull ? (int?)null : Convert.ToInt32(r["MAX_STUDENTS"]),
            }, new OracleParameter("ID", classId))).FirstOrDefault();
        if (meta == null || meta.MODE == "ASSIGNED" || !meta.MAX.HasValue) return;

        var occupied = (await _db.ExecuteQueryAsync(@"
            SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
             WHERE CLASS_ID = :CID
               AND STATUS IN ('ENROLLED','PENDING_APPROVAL')",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("CID", classId))).First();

        // Đếm EMPCD input đã có ENROLLED/PENDING (không tăng cap) — batch bằng IN chunks.
        var distinctIn = incoming.Distinct().ToList();
        int existing = 0;
        const int chunkSize = 500;
        for (int i = 0; i < distinctIn.Count; i += chunkSize)
        {
            var chunk = distinctIn.Skip(i).Take(chunkSize).ToList();
            var placeholders = string.Join(",", chunk.Select((_, idx) => $":E{idx}"));
            var sql = $@"
                SELECT COUNT(*) CNT FROM HRMS.HR_TRAINING_ENROLLMENT
                 WHERE CLASS_ID = :CID
                   AND EMPCD IN ({placeholders})
                   AND STATUS IN ('ENROLLED','PENDING_APPROVAL')";
            var ps = new List<OracleParameter> { new OracleParameter("CID", classId) };
            for (int idx = 0; idx < chunk.Count; idx++)
                ps.Add(new OracleParameter($"E{idx}", chunk[idx]));
            var cnt = (await _db.ExecuteQueryAsync(sql,
                r => Convert.ToInt32(r["CNT"]),
                ps.ToArray())).First();
            existing += cnt;
        }

        int netNew = distinctIn.Count - existing;
        if (occupied + netNew > meta.MAX.Value)
        {
            var over = occupied + netNew - meta.MAX.Value;
            throw new InvalidOperationException($"Vượt cap {over} slot (occupied={occupied}, cap={meta.MAX}). Tăng MAX_STUDENTS hoặc bỏ bớt.");
        }
    }

    private static EnrollmentModel MapEnrollmentWithClass(OracleDataReader r)
    {
        var e = MapEnrollment(r);
        e.CLASS_NAME           = r["CLASS_NAME"] as string;
        e.CLASS_STATUS         = r["CLASS_STATUS"] as string;
        e.CLASS_START_DATE     = r["CLASS_START_DATE"] as DateTime?;
        e.CLASS_END_DATE       = r["CLASS_END_DATE"] as DateTime?;
        e.COURSE_TITLE         = r["COURSE_TITLE"] as string;
        e.CLASS_MODE           = r["CLASS_MODE"] as string;
        e.TOTAL_SESSIONS       = r["TOTAL_SESSIONS"] is DBNull ? 0 : Convert.ToInt32(r["TOTAL_SESSIONS"]);
        e.COMPLETED_SESSIONS   = r["COMPLETED_SESSIONS"] is DBNull ? 0 : Convert.ToInt32(r["COMPLETED_SESSIONS"]);
        e.PRIMARY_TEACHER_NAME = r["PRIMARY_TEACHER_NAME"] as string;
        return e;
    }

    private static EnrollmentModel MapEnrollment(OracleDataReader r) => new()
    {
        CLASS_ID           = Convert.ToInt32(r["CLASS_ID"]),
        EMPCD              = r["EMPCD"]?.ToString() ?? "",
        EMP_NAME           = r["EMP_NAME"] as string,
        DEPTCD             = r["DEPTCD"] as string,
        LINECD             = r["LINECD"] as string,
        WORKCD             = r["WORKCD"] as string,
        DEPT_NAME          = r["DEPT_NAME"] as string,
        LINE_NAME          = r["LINE_NAME"] as string,
        WORK_NAME          = r["WORK_NAME"] as string,
        SOURCE             = r["SOURCE"]?.ToString() ?? "",
        STATUS             = r["STATUS"]?.ToString() ?? "",
        DROP_REASON        = r["DROP_REASON"] as string,
        GROUP_ID           = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
        GROUP_NAME         = r["GROUP_NAME"] as string,
        FINAL_SCORE        = r["FINAL_SCORE"] is DBNull ? null : Convert.ToDecimal(r["FINAL_SCORE"]),
        ATTENDANCE_PERCENT = r["ATTENDANCE_PERCENT"] is DBNull ? null : Convert.ToDecimal(r["ATTENDANCE_PERCENT"]),
        IS_CERTIFIED       = Convert.ToInt32(r["IS_CERTIFIED"]),
        COMPLETION_DATE    = r["COMPLETION_DATE"] as DateTime?,
        INST_ID            = r["INST_ID"] as string,
        INST_DT            = r["INST_DT"] as DateTime?,
        UPDT_ID            = r["UPDT_ID"] as string,
        UPDT_DT            = r["UPDT_DT"] as DateTime?,
    };

    // ═══════════════════════════════════════════════════════════════
    //  REMOVE — xóa học viên khỏi lớp (admin)
    // ═══════════════════════════════════════════════════════════════
    public async Task RemoveAsync(RemoveEnrollmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.EMPCD))
            throw new InvalidOperationException("Mã nhân viên không hợp lệ.");

        // Không cho xóa nếu học viên đã hoàn thành/thi đạt
        var exists = (await _db.ExecuteQueryAsync(
            "SELECT STATUS FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            r => r["STATUS"]?.ToString() ?? "",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD))).FirstOrDefault();

        if (exists == null)
            throw new InvalidOperationException("Không tìm thấy học viên trong lớp.");
        if (exists == "COMPLETED")
            throw new InvalidOperationException("Không thể xóa học viên đã hoàn thành khóa học.");

        await _db.ExecuteNonQueryAsync(
            "DELETE FROM HRMS.HR_TRAINING_ENROLLMENT WHERE CLASS_ID = :CID AND EMPCD = :EMP",
            new OracleParameter("CID", req.CLASS_ID),
            new OracleParameter("EMP", req.EMPCD));
    }
}
