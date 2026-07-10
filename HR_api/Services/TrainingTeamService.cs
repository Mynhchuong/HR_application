using HR_api.Data;
using HR_api.Helpers;
using HR_api.Models.Training;
using Oracle.ManagedDataAccess.Client;

namespace HR_api.Services;

// §14.5 — Team training schedule view cho Manager/Supervisor/Clerk.
// Scope filter reuse HR_USERS_DEPT pattern (giống Leave/GatePass/OT).
// Session có GROUP_ID: chỉ list session cùng group với học viên (§5b.3 filter).
public class TrainingTeamService
{
    private readonly OracleService _db;

    public TrainingTeamService(OracleService db) { _db = db; }

    // Check user có row HR_USERS_DEPT — dùng cho menu builder + guard endpoint.
    public async Task<bool> HasScopeAsync(string empcd)
    {
        return (await _db.ExecuteQueryAsync(
            "SELECT COUNT(*) CNT FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :E AND ROWNUM = 1",
            r => Convert.ToInt32(r["CNT"]),
            new OracleParameter("E", empcd))).First() > 0;
    }

    // Full schedule cho date range (default = 30 ngày tới). status = 'UPCOMING' | 'ONGOING' | 'COMPLETED' | 'ALL'.
    public async Task<List<TeamScheduleItem>> GetScheduleAsync(
        string empcd, DateTime? from, DateTime? to, string? status)
    {
        var dfrom = from ?? DateTime.Today;
        var dto   = to   ?? DateTime.Today.AddDays(30);

        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "SC");

        // Filter session status. 'ALL' hoặc null → skip filter.
        string statusFilter = "";
        var ps = new List<OracleParameter>
        {
            new OracleParameter("D_FROM", OracleDbType.Date) { Value = dfrom },
            new OracleParameter("D_TO",   OracleDbType.Date) { Value = dto },
        };
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            statusFilter = " AND S.STATUS = :ST";
            ps.Add(new OracleParameter("ST", status));
        }
        ps.AddRange(scope.Params);

        string sql = $@"
            SELECT EC.EMPCD, EC.CNAME EMP_NAME,
                   EC.DEPTCD, B.DEPTNM DEPT_NAME,
                   EC.LINECD, B.TEAMNM LINE_NAME,
                   EC.WORKCD, B.WORKNM WORK_NAME,
                   CL.ID CLASS_ID, CL.CLASS_NAME,
                   CO.TITLE COURSE_TITLE,
                   S.ID SESSION_ID, S.SESSION_NO, S.SESSION_DATE,
                   S.START_TIME, S.END_TIME, S.TOPIC, S.LOCATION, S.STATUS SESSION_STATUS,
                   S.GROUP_ID, G.GROUP_NAME
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              JOIN HRMS.HR_TRAINING_CLASS   CL ON CL.ID = E.CLASS_ID
              JOIN HRMS.HR_TRAINING_COURSE  CO ON CO.ID = CL.COURSE_ID
              JOIN HRMS.HR_TRAINING_SESSION S  ON S.CLASS_ID = CL.ID
                                              AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)
              JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
              LEFT JOIN HRMS.EAM410 B  ON B.DEPTCD = EC.DEPTCD AND B.LINECD = EC.LINECD AND B.WORKCD = EC.WORKCD
              LEFT JOIN HRMS.HR_TRAINING_CLASS_GROUP G ON G.ID = E.GROUP_ID
             WHERE E.STATUS = 'ENROLLED'
               AND S.SESSION_DATE BETWEEN :D_FROM AND :D_TO
               AND S.STATUS <> 'CANCELLED'
               AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
               {statusFilter}
               {scope.SqlClause}
             ORDER BY S.SESSION_DATE, S.START_TIME, EC.EMPCD";

        return await _db.ExecuteQueryAsync(sql, MapItem, ps.ToArray());
    }

    // Count NV có tiết học hôm nay trong scope (dùng cho Home summary card)
    public async Task<int> CountTodayInScopeAsync(string empcd)
    {
        var scope = OTScopeFilterHelper.ForScopeByTuple(empcd, empAlias: "EC", prefix: "TT");
        string sql = $@"
            SELECT COUNT(DISTINCT E.EMPCD) CNT
              FROM HRMS.HR_TRAINING_ENROLLMENT E
              JOIN HRMS.HR_TRAINING_SESSION S ON S.CLASS_ID = E.CLASS_ID
                                            AND (S.GROUP_ID IS NULL OR S.GROUP_ID = E.GROUP_ID)
              JOIN HRMS.ECM100 EC ON EC.EMPCD = E.EMPCD
             WHERE E.STATUS = 'ENROLLED'
               AND S.SESSION_DATE = TRUNC(SYSDATE)
               AND S.STATUS <> 'CANCELLED'
               AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
               {scope.SqlClause}";
        var rows = await _db.ExecuteQueryAsync(sql, r => Convert.ToInt32(r["CNT"]), scope.Params.ToArray());
        return rows.FirstOrDefault();
    }

    private static TeamScheduleItem MapItem(OracleDataReader r) => new()
    {
        EMPCD          = r["EMPCD"]?.ToString() ?? "",
        EMP_NAME       = r["EMP_NAME"] as string,
        DEPTCD         = r["DEPTCD"] as string,
        DEPT_NAME      = r["DEPT_NAME"] as string,
        LINECD         = r["LINECD"] as string,
        LINE_NAME      = r["LINE_NAME"] as string,
        WORKCD         = r["WORKCD"] as string,
        WORK_NAME      = r["WORK_NAME"] as string,
        CLASS_ID       = Convert.ToInt32(r["CLASS_ID"]),
        CLASS_NAME     = r["CLASS_NAME"]?.ToString()   ?? "",
        COURSE_TITLE   = r["COURSE_TITLE"]?.ToString() ?? "",
        SESSION_ID     = Convert.ToInt32(r["SESSION_ID"]),
        SESSION_NO     = Convert.ToInt32(r["SESSION_NO"]),
        SESSION_DATE   = Convert.ToDateTime(r["SESSION_DATE"]),
        START_TIME     = r["START_TIME"]?.ToString() ?? "",
        END_TIME       = r["END_TIME"]?.ToString() ?? "",
        TOPIC          = r["TOPIC"] as string,
        LOCATION       = r["LOCATION"] as string,
        SESSION_STATUS = r["SESSION_STATUS"]?.ToString() ?? "",
        GROUP_ID       = r["GROUP_ID"] is DBNull ? null : Convert.ToInt32(r["GROUP_ID"]),
        GROUP_NAME     = r["GROUP_NAME"] as string,
    };
}
