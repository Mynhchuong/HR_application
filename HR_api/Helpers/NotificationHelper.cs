using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Notification;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace HR_api.Helpers;

public class NotificationHelper
{
    private readonly OracleService _oracleService;
    private readonly IConfiguration _config;
    private static volatile bool _firebaseInitialized = false;
    private static readonly object _fbLock = new();

    public NotificationHelper(OracleService oracleService, IConfiguration config)
    {
        _oracleService = oracleService;
        _config = config;
        EnsureFirebaseInitialized();
    }

    private void EnsureFirebaseInitialized()
    {
        if (_firebaseInitialized) return;
        lock (_fbLock)
        {
            if (_firebaseInitialized) return;
            try
            {
                var keyPath = _config["Firebase:ServiceAccountKeyPath"];
                if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
                {
                    if (FirebaseApp.DefaultInstance == null)
                    {
                        FirebaseApp.Create(new AppOptions
                        {
                            Credential = GoogleCredential.FromFile(keyPath)
                        });
                    }
                    _firebaseInitialized = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Firebase] Init failed: {ex.Message}");
            }
        }
    }

    // Save notification to DB + fire FCM push (fire-and-forget)
    public async Task<decimal> SendNotificationAsync(SendNotificationRequest model)
    {
        try
        {
            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS (TITLE, BODY, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE)
                VALUES (:TITLE, :BODY, :NOTI_TYPE, :TARGET_VAL, :LINK_ACTION, :CREATED_BY, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);

            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE",      model.TITLE),
                new OracleParameter("BODY",       model.BODY),
                new OracleParameter("NOTI_TYPE",  model.NOTI_TYPE),
                new OracleParameter("TARGET_VAL", model.TARGET_VAL),
                new OracleParameter("LINK_ACTION",(object?)model.LINK_ACTION ?? DBNull.Value),
                new OracleParameter("CREATED_BY", (object?)model.CREATED_BY  ?? DBNull.Value),
                outIdParam);

            decimal notiId = outIdParam.Value is Oracle.ManagedDataAccess.Types.OracleDecimal od && !od.IsNull
                ? od.Value : 0;

            _ = Task.Run(() => SendFcmAsync(model));

            return notiId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationHelper] SendNotificationAsync error: {ex.Message}");
            return -1;
        }
    }

    // Find all supervisor/deputy/manager/expat who manage requester's dept/line/work
    public async Task<List<string>> GetApproverEmpCdsAsync(string requesterEmpCd)
    {
        string sql = @"
            SELECT DISTINCT D.EMPCD
            FROM HRMS.HR_USERS_DEPT D
            JOIN HRMS.HR_USERS U ON U.EMPCD = D.EMPCD
            JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID
            WHERE R.ROLE_NAME IN ('Supervisor', 'DeputyManager', 'Manager', 'Expat')
              AND U.IS_ACTIVE = 1
              AND (D.DEPTCD, D.LINECD, D.WORKCD) IN (
                  SELECT EC.DEPTCD, EC.LINECD, EC.WORKCD
                  FROM HRMS.ECM100 EC
                  WHERE EC.EMPCD = :EMPCD AND ROWNUM = 1
              )";
        try
        {
            return await _oracleService.ExecuteQueryAsync(sql,
                r => r["EMPCD"]?.ToString() ?? "",
                new OracleParameter("EMPCD", requesterEmpCd));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationHelper] GetApproverEmpCdsAsync error: {ex.Message}");
            return new();
        }
    }

    private async Task SendFcmAsync(SendNotificationRequest model)
    {
        if (!_firebaseInitialized) return;
        try
        {
            var tokens = await GetTokensForTargetAsync(model.NOTI_TYPE, model.TARGET_VAL);
            if (tokens.Count == 0) return;

            var data = string.IsNullOrEmpty(model.LINK_ACTION)
                ? null
                : new Dictionary<string, string> { { "link_action", model.LINK_ACTION } };

            foreach (var batch in tokens.Chunk(500))
            {
                var message = new MulticastMessage
                {
                    Notification = new Notification { Title = model.TITLE, Body = model.BODY },
                    Data   = data,
                    Tokens = batch.ToList()
                };
                await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationHelper] SendFcmAsync error: {ex.Message}");
        }
    }

    private async Task<List<string>> GetTokensForTargetAsync(string? notiType, string? targetVal)
    {
        if (string.IsNullOrEmpty(notiType)) return new();

        try
        {
            return notiType switch
            {
                "COMPANY" => await _oracleService.ExecuteQueryAsync(
                    "SELECT TOKEN FROM HRMS.HR_USER_TOKENS",
                    r => r["TOKEN"]?.ToString() ?? ""),

                "DEPT" when !string.IsNullOrEmpty(targetVal) => await _oracleService.ExecuteQueryAsync(@"
                    SELECT T.TOKEN FROM HRMS.HR_USER_TOKENS T
                    JOIN HRMS.ECM100 EC ON EC.EMPCD = T.EMPCD
                    WHERE EC.DEPTCD = :VAL
                      AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))",
                    r => r["TOKEN"]?.ToString() ?? "",
                    new OracleParameter("VAL", targetVal)),

                _ when !string.IsNullOrEmpty(targetVal) => await _oracleService.ExecuteQueryAsync(
                    "SELECT TOKEN FROM HRMS.HR_USER_TOKENS WHERE EMPCD = :VAL",
                    r => r["TOKEN"]?.ToString() ?? "",
                    new OracleParameter("VAL", targetVal)),

                _ => new()
            };
        }
        catch { return new(); }
    }
}
