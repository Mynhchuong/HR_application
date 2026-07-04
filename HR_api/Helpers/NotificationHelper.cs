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

    // Template cache — reload sau 10 phút hoặc khi có thay đổi
    private static Dictionary<string, NotiTemplateModel> _templateCache = new();
    private static DateTime _templateCacheTime = DateTime.MinValue;
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private const int CacheMinutes = 10;

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

    // ============================================================
    // TEMPLATE CACHE
    // ============================================================
    public static void InvalidateTemplateCache() => _templateCacheTime = DateTime.MinValue;

    private async Task<Dictionary<string, NotiTemplateModel>> GetTemplateCacheAsync()
    {
        if ((DateTime.UtcNow - _templateCacheTime).TotalMinutes < CacheMinutes)
            return _templateCache;

        await _cacheLock.WaitAsync();
        try
        {
            if ((DateTime.UtcNow - _templateCacheTime).TotalMinutes < CacheMinutes)
                return _templateCache;

            const string sql = @"
                SELECT ID, TEMPLATE_KEY, TITLE_VI, BODY_VI, TITLE_EN, BODY_EN, IS_ACTIVE
                FROM HRMS.HR_NOTI_TEMPLATES WHERE IS_ACTIVE = 1";

            var list = await _oracleService.ExecuteQueryAsync(sql, r => new NotiTemplateModel
            {
                ID           = Convert.ToInt32(r["ID"]),
                TEMPLATE_KEY = r["TEMPLATE_KEY"]?.ToString() ?? "",
                TITLE_VI     = r["TITLE_VI"]?.ToString()     ?? "",
                BODY_VI      = r["BODY_VI"]?.ToString()      ?? "",
                TITLE_EN     = r["TITLE_EN"]?.ToString(),
                BODY_EN      = r["BODY_EN"]?.ToString(),
            });

            _templateCache    = list.ToDictionary(t => t.TEMPLATE_KEY, t => t);
            _templateCacheTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationHelper] Template cache error: {ex.Message}");
        }
        finally { _cacheLock.Release(); }

        return _templateCache;
    }

    // Lấy template theo key, điền placeholder, trả (titleVI, bodyVI, titleEN, bodyEN)
    public async Task<(string titleVi, string bodyVi, string? titleEn, string? bodyEn)>
        GetTemplateAsync(string key, Dictionary<string, string>? placeholders = null)
    {
        var cache = await GetTemplateCacheAsync();
        if (!cache.TryGetValue(key, out var tmpl))
            return (key, key, null, null);

        string titleVi = tmpl.TITLE_VI;
        string bodyVi  = tmpl.BODY_VI;
        string? titleEn = tmpl.TITLE_EN;
        string? bodyEn  = tmpl.BODY_EN;

        if (placeholders != null)
        {
            foreach (var (ph, val) in placeholders)
            {
                titleVi  = titleVi.Replace($"{{{ph}}}", val);
                bodyVi   = bodyVi.Replace($"{{{ph}}}", val);
                titleEn  = titleEn?.Replace($"{{{ph}}}", val);
                bodyEn   = bodyEn?.Replace($"{{{ph}}}", val);
            }
        }
        return (titleVi, bodyVi, titleEn, bodyEn);
    }

    // ============================================================
    // SAVE NOTIFICATION TO DB + FIRE FCM PUSH (fire-and-forget)
    // ============================================================
    public async Task<decimal> SendNotificationAsync(SendNotificationRequest model)
    {
        try
        {
            string sqlInsert = @"
                INSERT INTO HRMS.HR_NOTIFICATIONS
                    (TITLE, BODY, TITLE_EN, BODY_EN, NOTI_TYPE, TARGET_VAL, LINK_ACTION, CREATED_BY, CREATED_DATE)
                VALUES
                    (:TITLE, :BODY, :TITLE_EN, :BODY_EN, :NOTI_TYPE, :TARGET_VAL, :LINK_ACTION, :CREATED_BY, SYSDATE)
                RETURNING ID INTO :OUT_ID";

            var outIdParam = new OracleParameter("OUT_ID", OracleDbType.Decimal, System.Data.ParameterDirection.Output);

            await _oracleService.ExecuteNonQueryAsync(sqlInsert,
                new OracleParameter("TITLE",      model.TITLE),
                new OracleParameter("BODY",       model.BODY),
                new OracleParameter("TITLE_EN",   (object?)model.TITLE_EN ?? DBNull.Value),
                new OracleParameter("BODY_EN",    (object?)model.BODY_EN  ?? DBNull.Value),
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

    public async Task SendFcmPublicAsync(SendNotificationRequest model) => await SendFcmAsync(model);

    // ─── FCM cho admin MULTI-target ──────────────────────────────
    public async Task SendFcmForMultiAsync(decimal notiId, SendMultiNotificationRequest model)
    {
        if (!_firebaseInitialized) return;
        try
        {
            // Resolve toàn bộ EMPCD đích từ targets
            HashSet<string> empCds = new();

            if (model.SEND_ALL_COMPANY || model.TARGETS.Count == 0)
            {
                // Toàn công ty — lấy hết EMPCD đang làm việc
                var allEmps = await _oracleService.ExecuteQueryAsync(@"
                    SELECT EMPCD FROM HRMS.ECM100
                    WHERE RETDAT IS NULL OR RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD')",
                    r => r["EMPCD"]?.ToString() ?? "");
                foreach (var e in allEmps) if (!string.IsNullOrEmpty(e)) empCds.Add(e);
            }
            else
            {
                foreach (var t in model.TARGETS)
                {
                    if (string.IsNullOrWhiteSpace(t.VAL)) continue;
                    string col = t.TYPE switch
                    {
                        "DEPT"  => "DEPTCD",
                        "LINE"  => "LINECD",
                        "WORK"  => "WORKCD",
                        "EMPCD" => "EMPCD",
                        _ => ""
                    };
                    if (string.IsNullOrEmpty(col)) continue;
                    var rows = await _oracleService.ExecuteQueryAsync(
                        $@"SELECT EMPCD FROM HRMS.ECM100
                           WHERE {col} = :V
                             AND (RETDAT IS NULL OR RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))",
                        r => r["EMPCD"]?.ToString() ?? "",
                        new OracleParameter("V", t.VAL));
                    foreach (var e in rows) if (!string.IsNullOrEmpty(e)) empCds.Add(e);
                }
            }

            if (empCds.Count == 0) return;

            // Lấy tokens cho EMPCDs (batch IN clause Oracle giới hạn 1000 → chia chunk)
            var tokens = new List<string>();
            foreach (var batch in empCds.Chunk(900))
            {
                var inClause = string.Join(",", batch.Select((_, i) => $":E{i}"));
                var parms = batch.Select((e, i) => new OracleParameter($"E{i}", e)).ToArray();
                var ts = await _oracleService.ExecuteQueryAsync(
                    $"SELECT TOKEN FROM HRMS.HR_USER_TOKENS WHERE EMPCD IN ({inClause})",
                    r => r["TOKEN"]?.ToString() ?? "",
                    parms);
                tokens.AddRange(ts.Where(t => !string.IsNullOrEmpty(t)));
            }

            if (tokens.Count == 0) return;

            var data = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(model.LINK_ACTION)) data["link_action"] = model.LINK_ACTION;
            if (!string.IsNullOrEmpty(model.PRIORITY))   data["priority"]    = model.PRIORITY!;
            if (!string.IsNullOrEmpty(model.SOURCE))     data["source"]      = model.SOURCE!;
            data["noti_id"] = notiId.ToString();

            foreach (var batch in tokens.Chunk(500))
            {
                var message = new MulticastMessage
                {
                    Notification = new Notification { Title = model.TITLE, Body = model.BODY },
                    Data   = data,
                    Tokens = batch.ToList(),
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            ChannelId = "mysamho_noti",
                            Sound     = "default"
                        }
                    },
                    Apns = new ApnsConfig { Aps = new Aps { Sound = "default" } }
                };
                await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationHelper] SendFcmForMultiAsync error: {ex.Message}");
        }
    }

    private async Task SendFcmAsync(SendNotificationRequest model)
    {
        if (!_firebaseInitialized) return;
        try
        {
            var tokens = await GetTokensForTargetAsync(model.NOTI_TYPE, model.TARGET_VAL);
            if (tokens.Count == 0) return;

            // Dùng EN nếu người nhận là Expat và EN content có sẵn
            string title = model.TITLE;
            string body  = model.BODY;
            if (model.NOTI_TYPE == "EMPCD"
                && !string.IsNullOrEmpty(model.TITLE_EN)
                && !string.IsNullOrEmpty(model.TARGET_VAL)
                && await IsExpatAsync(model.TARGET_VAL))
            {
                title = model.TITLE_EN;
                body  = model.BODY_EN ?? model.BODY;
            }

            var data = string.IsNullOrEmpty(model.LINK_ACTION)
                ? null
                : new Dictionary<string, string> { { "link_action", model.LINK_ACTION } };

            foreach (var batch in tokens.Chunk(500))
            {
                var message = new MulticastMessage
                {
                    Notification = new Notification { Title = title, Body = body },
                    Data   = data,
                    Tokens = batch.ToList(),
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            ChannelId = "mysamho_noti",
                            Sound     = "default",
                        }
                    },
                    Apns = new ApnsConfig
                    {
                        Aps = new Aps { Sound = "default" }
                    }
                };
                await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NotificationHelper] SendFcmAsync error: {ex.Message}");
        }
    }

    public async Task<string> GetEmpNameAsync(string empCd)
    {
        try
        {
            var rows = await _oracleService.ExecuteQueryAsync(
                "SELECT CNAME FROM HRMS.ECM100 WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                r => r["CNAME"]?.ToString() ?? "",
                new OracleParameter("EMPCD", empCd));
            return rows.FirstOrDefault() ?? empCd;
        }
        catch { return empCd; }
    }

    private async Task<bool> IsExpatAsync(string empCd)
    {
        try
        {
            var rows = await _oracleService.ExecuteQueryAsync(
                @"SELECT 1 FROM HRMS.HR_USERS U
                  JOIN HRMS.HR_ROLES R ON R.ID = U.ROLE_ID
                  WHERE U.EMPCD = :EMPCD AND R.ROLE_NAME = 'Expat' AND ROWNUM = 1",
                r => 1,
                new OracleParameter("EMPCD", empCd));
            return rows.Count > 0;
        }
        catch { return false; }
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

                "EMPCD" when !string.IsNullOrEmpty(targetVal) => await _oracleService.ExecuteQueryAsync(
                    "SELECT TOKEN FROM HRMS.HR_USER_TOKENS WHERE EMPCD = :VAL",
                    r => r["TOKEN"]?.ToString() ?? "",
                    new OracleParameter("VAL", targetVal)),

                // SURVEY: chỉ push tới người trong HR_SURVEY_RECIPIENT snapshot
                "SURVEY" when !string.IsNullOrEmpty(targetVal) && int.TryParse(targetVal, out var sid)
                    => await _oracleService.ExecuteQueryAsync(@"
                    SELECT T.TOKEN
                      FROM HRMS.HR_USER_TOKENS T
                      JOIN HRMS.HR_SURVEY_RECIPIENT R ON R.EMPCD = T.EMPCD
                     WHERE R.SURVEY_ID = :VAL",
                    r => r["TOKEN"]?.ToString() ?? "",
                    new OracleParameter("VAL", sid)),

                _ => new()
            };
        }
        catch { return new(); }
    }
}
