using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;
using HR_api.Data;
using HR_api.Models.Account;

namespace HR_api.Controllers;

[ApiController]
[Route("apiHR/[controller]")]
public class AccountController : ControllerBase
{
    private readonly OracleService _oracleService;

    public AccountController(OracleService oracleService)
    {
        _oracleService = oracleService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var empcd = request.EmpCd;
        var password = request.Password;
        if (string.IsNullOrEmpty(empcd) || string.IsNullOrEmpty(password))
            return BadRequest(new { success = false, message = "Thiếu thông tin đăng nhập" });

        string sql = @"
            SELECT 
                E.EMPCD AS ECM_EMPCD,
                E.CNAME AS ECM_NAME,
                E.JEAJIKGB,
                U.ID,
                U.EMPCD,
                U.PASSWORD,
                U.FULL_NAME,                      
                U.ROLE_ID,
                R.ROLE_NAME,
                U.IS_ACTIVE,
                U.LASTED_LOGIN,
                U.SIGNATUREBLOB
            FROM HRMS.ECM100 E
            LEFT JOIN HRMS.HR_USERS U ON E.EMPCD = U.EMPCD
            LEFT JOIN HRMS.HR_ROLES R ON U.ROLE_ID = R.ID
            WHERE E.EMPCD = :EMPCD";

        var checkResults = await _oracleService.ExecuteQueryAsync(sql, reader => new 
        {
            EcmEmpCd = reader["ECM_EMPCD"]?.ToString(),
            EcmName = reader["ECM_NAME"]?.ToString(),
            Jeajikgb = reader["JEAJIKGB"]?.ToString(),
            Id = reader["ID"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ID"]),
            EmpCd = reader["EMPCD"]?.ToString(),
            Password = reader["PASSWORD"]?.ToString(),
            FullName = reader["FULL_NAME"]?.ToString(),
            RoleId = reader["ROLE_ID"] == DBNull.Value ? null : (int?)Convert.ToInt32(reader["ROLE_ID"]),
            RoleName = reader["ROLE_NAME"]?.ToString(),
            IsActive = reader["IS_ACTIVE"] == DBNull.Value ? 1 : Convert.ToInt32(reader["IS_ACTIVE"]),
            LastedLogin = reader["LASTED_LOGIN"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(reader["LASTED_LOGIN"]),
            SignatureBlob = reader["SIGNATUREBLOB"]?.ToString()
        },
        new OracleParameter("EMPCD", empcd));

        var userCheck = checkResults.FirstOrDefault();

        // Có trong ECM100 nhưng đã nghỉ việc (JEAJIKGB != "Y") → chặn login + tự động disable
        if (userCheck != null && userCheck.Jeajikgb != "Y")
        {
            await _oracleService.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_USERS SET IS_ACTIVE = 0, UPDT_ID = 'SYSTEM', UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD AND IS_ACTIVE = 1",
                new OracleParameter("EMPCD", empcd));
            return Ok(new { success = false, message = "Tài khoản này đã nghỉ việc" });
        }

        // Fallback: không có trong ECM100 → thử check thẳng HR_USERS (tài khoản hệ thống như admin)
        if (userCheck == null)
        {
            var directUser = await _oracleService.ExecuteQueryAsync(@"
                SELECT U.ID, U.EMPCD, U.PASSWORD, U.FULL_NAME, U.ROLE_ID, R.ROLE_NAME,
                       U.IS_ACTIVE, U.LASTED_LOGIN, U.SIGNATUREBLOB
                FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES R ON U.ROLE_ID = R.ID
                WHERE U.EMPCD = :EMPCD",
                r => new {
                    Id = Convert.ToInt32(r["ID"]),
                    EmpCd = r["EMPCD"]?.ToString(),
                    Password = r["PASSWORD"]?.ToString(),
                    FullName = r["FULL_NAME"]?.ToString(),
                    RoleId = r["ROLE_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["ROLE_ID"]),
                    RoleName = r["ROLE_NAME"]?.ToString(),
                    IsActive = r["IS_ACTIVE"] == DBNull.Value ? 1 : Convert.ToInt32(r["IS_ACTIVE"]),
                    LastedLogin = r["LASTED_LOGIN"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LASTED_LOGIN"]),
                    SignatureBlob = r["SIGNATUREBLOB"]?.ToString()
                },
                new OracleParameter("EMPCD", empcd));

            var du = directUser.FirstOrDefault();
            if (du == null || du.Password != password)
                return Ok(new { success = false, message = "Sai tài khoản hoặc mật khẩu" });
            if (du.IsActive == 0)
                return Ok(new { success = false, message = "Tài khoản này đã nghỉ việc" });

            await _oracleService.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_USERS SET LASTED_LOGIN = SYSDATE WHERE EMPCD = :EMPCD",
                new OracleParameter("EMPCD", empcd));

            return Ok(new { success = true, data = new UserInfoModel {
                Id = du.Id, EmpCd = du.EmpCd, FullName = du.FullName,
                RoleId = du.RoleId, RoleName = du.RoleName,
                IsActive = du.IsActive, LastedLogin = du.LastedLogin,
                SIGNATUREBLOB = du.SignatureBlob ?? "N"
            }});
        }

        UserInfoModel result = null;

        if (string.IsNullOrEmpty(userCheck.EmpCd))
        {
            // Có trong ERP nhưng CHƯA có trong HR_USERS
            if (password == "123456")
            {
                // Tự động insert vào HR_USERS với RoleId = 1
                string insertSql = @"
                    INSERT INTO HRMS.HR_USERS (EMPCD, PASSWORD, FULL_NAME, ROLE_ID, INST_ID)
                    VALUES (:EMPCD, :PASSWORD, :FULL_NAME, 1, 'SYSTEM')";

                await _oracleService.ExecuteNonQueryAsync(insertSql,
                    new OracleParameter("EMPCD", empcd),
                    new OracleParameter("PASSWORD", "123456"),
                    new OracleParameter("FULL_NAME", userCheck.EcmName));
                
                // Fetch lại ID sau khi insert
                var newInserted = await _oracleService.ExecuteQueryAsync(@"
                    SELECT U.ID, R.ROLE_NAME 
                    FROM HRMS.HR_USERS U 
                    LEFT JOIN HRMS.HR_ROLES R ON U.ROLE_ID = R.ID 
                    WHERE U.EMPCD = :EMPCD", 
                    r => new { Id = Convert.ToInt32(r["ID"]), RoleName = r["ROLE_NAME"]?.ToString() },
                    new OracleParameter("EMPCD", empcd));

                var insertedUser = newInserted.FirstOrDefault();

                result = new UserInfoModel
                {
                    Id = insertedUser?.Id ?? 0,
                    EmpCd = empcd,
                    FullName = userCheck.EcmName,
                    RoleId = 1,
                    RoleName = insertedUser?.RoleName ?? "Nhân viên",
                    IsActive = 1,
                    SIGNATUREBLOB = "N"
                };
            }
            else
            {
                // Nhập sai mật khẩu 123456 đối với user chưa có tài khoản
                return Ok(new { success = false, message = "Sai tài khoản hoặc mật khẩu" });
            }
        }
        else
        {
            // Đã có trong HR_USERS -> So sánh password
            if (userCheck.Password != password)
            {
                return Ok(new { success = false, message = "Sai tài khoản hoặc mật khẩu" });
            }

            if (userCheck.IsActive == 0)
            {
                return Ok(new { success = false, message = "Tài khoản đã bị khóa" });
            }

            result = new UserInfoModel
            {
                Id = userCheck.Id,
                EmpCd = userCheck.EmpCd,
                FullName = userCheck.FullName,
                RoleId = userCheck.RoleId,
                RoleName = userCheck.RoleName,
                IsActive = userCheck.IsActive,
                LastedLogin = userCheck.LastedLogin,
                SIGNATUREBLOB = userCheck.SignatureBlob
            };
        }

        string updateSql = @"UPDATE HRMS.HR_USERS SET LASTED_LOGIN = SYSDATE WHERE EMPCD = :EMPCD";
        await _oracleService.ExecuteNonQueryAsync(updateSql, new OracleParameter("EMPCD", empcd));

        // Populate filter codes 1 lần lúc login cho Supervisor / DeputyManager / Manager / Assistant
        var rolesNeedFilter = new[] { "Supervisor", "DeputyManager", "Manager", "Assistant" };
        if (rolesNeedFilter.Any(r => string.Equals(result.RoleName, r, StringComparison.OrdinalIgnoreCase)))
        {
            bool isSupervisor = string.Equals(result.RoleName, "Supervisor", StringComparison.OrdinalIgnoreCase);

            if (isSupervisor)
            {
                // Supervisor: lấy WORKCD + LINECD (để filter hẹp hơn khi work code span nhiều line)
                string supSql = "SELECT DISTINCT WORKCD, LINECD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD2";
                var rows = await _oracleService.ExecuteQueryAsync(supSql,
                    r => new { Work = r["WORKCD"]?.ToString() ?? "", Line = r["LINECD"]?.ToString() ?? "" },
                    new OracleParameter("EMPCD2", empcd));
                result.FilterType      = "work";
                result.FilterCodes     = rows.Select(r => r.Work).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
                result.FilterLineCodes = rows.Select(r => r.Line).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            }
            else
            {
                // Manager/Assistant: lấy DEPTCD + LINECD
                string mgrSql = "SELECT DISTINCT DEPTCD, LINECD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD2";
                var rows = await _oracleService.ExecuteQueryAsync(mgrSql,
                    r => new { Dept = r["DEPTCD"]?.ToString() ?? "", Line = r["LINECD"]?.ToString() ?? "" },
                    new OracleParameter("EMPCD2", empcd));
                result.FilterType      = "dept";
                result.FilterCodes     = rows.Select(r => r.Dept).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
                result.FilterLineCodes = rows.Select(r => r.Line).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

                // Fallback: HR_USERS_DEPT không có LINECD → lấy LINECD của chính employee từ ECM100
                if (result.FilterLineCodes.Count == 0)
                {
                    var ecmRows = await _oracleService.ExecuteQueryAsync(
                        "SELECT LINECD FROM HRMS.ECM100 WHERE EMPCD = :EMPCD3 AND ROWNUM = 1",
                        r => r["LINECD"]?.ToString() ?? "",
                        new OracleParameter("EMPCD3", empcd));
                    result.FilterLineCodes = ecmRows.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
                }
            }
        }

        return Ok(new { success = true, data = result });
    }

    [HttpPost("create-user")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserModel model)
    {
        if (model == null || string.IsNullOrEmpty(model.EmpCd))
            return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ" });

        var check = await _oracleService.ExecuteQueryAsync(
            "SELECT 1 FROM HRMS.HR_USERS WHERE EMPCD = :EMPCD",
            r => 1,
            new OracleParameter("EMPCD", model.EmpCd));

        if (check.Count > 0)
            return Ok(new { success = false, message = "User đã tồn tại" });

        string sql = @"
            INSERT INTO HRMS.HR_USERS (EMPCD, PASSWORD, FULL_NAME, ROLE_ID, INST_ID)
            VALUES (:EMPCD, :PASSWORD, :FULL_NAME, :ROLE_ID, :LOGIN_USER)";

        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("EMPCD", model.EmpCd),
            new OracleParameter("PASSWORD", model.Password),
            new OracleParameter("FULL_NAME", model.FullName),
            new OracleParameter("ROLE_ID", model.RoleId),
            new OracleParameter("LOGIN_USER", model.LoginUser));

        return Ok(new { success = rows > 0 });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] DisableUserRequest req)
    {
        if (string.IsNullOrEmpty(req.EmpCd))
            return BadRequest(new { success = false, message = "EMPCD is required" });

        string sql = "UPDATE HRMS.HR_USERS SET PASSWORD = '123456', UPDT_ID = :LOGIN_USER, UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD";
        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("LOGIN_USER", req.LoginUser),
            new OracleParameter("EMPCD", req.EmpCd));

        if (rows == 0)
            return Ok(new { success = false, message = "User không tồn tại" });

        return Ok(new { success = true, message = "Reset password thành công" });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrEmpty(req.EmpCd) || string.IsNullOrEmpty(req.OldPassword) || string.IsNullOrEmpty(req.NewPassword))
            return BadRequest(new { success = false, message = "Thiếu dữ liệu" });

        if (req.NewPassword == "123456")
            return Ok(new { success = false, message = "Không được dùng mật khẩu mặc định 123456" });

        if (req.NewPassword == req.OldPassword)
            return Ok(new { success = false, message = "Mật khẩu mới phải khác mật khẩu cũ" });

        string sql = @"UPDATE HRMS.HR_USERS SET PASSWORD = :NEW_PASSWORD, UPDT_ID = :EMPCD, UPDT_DT = SYSDATE 
                       WHERE EMPCD = :EMPCD AND PASSWORD = :OLD_PASSWORD";

        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("NEW_PASSWORD", req.NewPassword),
            new OracleParameter("EMPCD", req.EmpCd),
            new OracleParameter("OLD_PASSWORD", req.OldPassword));

        if (rows == 0)
            return Ok(new { success = false, message = "Sai mật khẩu cũ hoặc user không tồn tại" });

        return Ok(new { success = true, message = "Đổi mật khẩu thành công" });
    }

    // ─────────────────────────────────────────────────────────────────
    // POST /apiHR/Account/forgot-password
    //  Body: { Empcd, Juminno (CCCD), JuminnoDate (YYYYMMDD), NewPassword }
    //  Logic:
    //   1. Match ECM100.JUMINNO + JUMINNO_DATE với input
    //   2. Check HR_USERS.LAST_PWD_RESET — nếu < 7 ngày → reject
    //   3. Update PASSWORD + LAST_PWD_RESET = SYSDATE
    // ─────────────────────────────────────────────────────────────────
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        try
        {
            if (req == null
                || string.IsNullOrWhiteSpace(req.Empcd)
                || string.IsNullOrWhiteSpace(req.Juminno)
                || string.IsNullOrWhiteSpace(req.JuminnoDate)
                || string.IsNullOrWhiteSpace(req.NewPassword))
                return Ok(new { success = false, message = "Vui lòng nhập đủ thông tin" });

            if (req.NewPassword.Length < 6)
                return Ok(new { success = false, message = "Mật khẩu mới phải ít nhất 6 ký tự" });
            if (req.NewPassword == "123456")
                return Ok(new { success = false, message = "Không được dùng mật khẩu mặc định 123456" });

            // Chuẩn hoá JUMINNO_DATE về dạng YYYYMMDD nếu user nhập dd/MM/yyyy hoặc yyyy-MM-dd
            string juminnoDate = req.JuminnoDate.Trim()
                .Replace("/", "").Replace("-", "").Replace(".", "");
            if (juminnoDate.Length == 8 && juminnoDate.All(char.IsDigit))
            {
                // Nếu gửi dạng dd/MM/yyyy → chuyển sang yyyy/MM/dd
                if (DateTime.TryParseExact(req.JuminnoDate.Trim(), new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" },
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None,
                        out var dt))
                {
                    juminnoDate = dt.ToString("yyyyMMdd");
                }
            }
            else
            {
                return Ok(new { success = false, message = "Ngày cấp CCCD không hợp lệ" });
            }

            // 1) Verify CCCD + ngày cấp khớp với ECM100
            // Normalize cả 2 đầu (loại '/', '-', '.', space) để khỏi sai format
            var verify = await _oracleService.ExecuteQueryAsync(@"
                SELECT EMPCD, JUMINNO, JUMINNO_DATE
                FROM HRMS.ECM100
                WHERE EMPCD = :EMPCD
                  AND TRIM(JUMINNO) = TRIM(:JUMINNO)
                  AND REPLACE(REPLACE(REPLACE(REPLACE(TRIM(JUMINNO_DATE),'/',''),'-',''),'.',''),' ','')
                      = :JUMINNO_DATE
                  AND ROWNUM = 1",
                r => r["EMPCD"]?.ToString() ?? "",
                new OracleParameter("EMPCD", req.Empcd),
                new OracleParameter("JUMINNO", req.Juminno.Trim()),
                new OracleParameter("JUMINNO_DATE", juminnoDate));

            if (verify.Count == 0)
                return Ok(new { success = false, message = "CCCD hoặc ngày cấp không khớp với hồ sơ. Vui lòng kiểm tra lại hoặc liên hệ phòng Nhân sự." });

            // 2) Verify HR_USERS có tồn tại + check rate limit
            var userRows = await _oracleService.ExecuteQueryAsync(@"
                SELECT NVL(IS_ACTIVE, 1) IS_ACTIVE,
                       LAST_PWD_RESET,
                       CASE WHEN LAST_PWD_RESET IS NULL THEN NULL
                            ELSE ROUND(SYSDATE - LAST_PWD_RESET, 2)
                       END DAYS_SINCE_LAST
                FROM HRMS.HR_USERS
                WHERE EMPCD = :EMPCD AND ROWNUM = 1",
                r => new
                {
                    isActive = Convert.ToInt32(r["IS_ACTIVE"]),
                    lastReset = r["LAST_PWD_RESET"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LAST_PWD_RESET"]),
                    daysSince = r["DAYS_SINCE_LAST"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["DAYS_SINCE_LAST"])
                },
                new OracleParameter("EMPCD", req.Empcd));

            var user = userRows.FirstOrDefault();
            if (user == null)
                return Ok(new { success = false, message = "Tài khoản chưa tồn tại trên hệ thống. Vui lòng đăng nhập lần đầu hoặc liên hệ HR." });
            if (user.isActive == 0)
                return Ok(new { success = false, message = "Tài khoản đang bị khoá. Vui lòng liên hệ HR." });

            if (user.daysSince.HasValue && user.daysSince.Value < 7)
            {
                var remain = Math.Ceiling(7 - user.daysSince.Value);
                return Ok(new
                {
                    success = false,
                    code    = "RATE_LIMIT",
                    message = $"Bạn chỉ được tự đặt lại mật khẩu 1 lần / tuần. Lần cuối: {user.lastReset:dd/MM/yyyy HH:mm}. Vui lòng thử lại sau {remain} ngày hoặc liên hệ phòng Nhân sự."
                });
            }

            // 3) Update password + LAST_PWD_RESET
            int n = await _oracleService.ExecuteNonQueryAsync(@"
                UPDATE HRMS.HR_USERS
                SET PASSWORD       = :NEW_PASSWORD,
                    LAST_PWD_RESET = SYSDATE,
                    UPDT_ID        = :EMPCD,
                    UPDT_DT        = SYSDATE
                WHERE EMPCD = :EMPCD",
                new OracleParameter("NEW_PASSWORD", req.NewPassword),
                new OracleParameter("EMPCD", req.Empcd));

            if (n == 0) return Ok(new { success = false, message = "Cập nhật mật khẩu thất bại" });
            return Ok(new { success = true, message = "Đã đặt lại mật khẩu thành công. Vui lòng đăng nhập với mật khẩu mới." });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("disable-user")]
    public async Task<IActionResult> DisableUser([FromBody] DisableUserRequest req)
    {
        if (string.IsNullOrEmpty(req.EmpCd))
            return BadRequest(new { success = false, message = "EMPCD is required" });

        string sql = "UPDATE HRMS.HR_USERS SET IS_ACTIVE = 0, UPDT_ID = :LOGIN_USER, UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD";
        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("LOGIN_USER", req.LoginUser),
            new OracleParameter("EMPCD", req.EmpCd));

        if (rows == 0)
            return Ok(new { success = false, message = "User không tồn tại" });

        return Ok(new { success = true, message = "Đã khóa tài khoản" });
    }

    [HttpPost("enable-user")]
    public async Task<IActionResult> EnableUser([FromBody] DisableUserRequest req)
    {
        if (string.IsNullOrEmpty(req.EmpCd))
            return BadRequest(new { success = false, message = "EMPCD is required" });

        string sql = "UPDATE HRMS.HR_USERS SET IS_ACTIVE = 1, UPDT_ID = :LOGIN_USER, UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD";
        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("LOGIN_USER", req.LoginUser),
            new OracleParameter("EMPCD", req.EmpCd));

        if (rows == 0)
            return Ok(new { success = false, message = "User không tồn tại" });

        return Ok(new { success = true, message = "Đã mở khóa tài khoản" });
    }

    [HttpGet("user-list")]
    public async Task<IActionResult> GetUsers(
        string? deptcd = null,
        string? linecd = null,
        string? workcd = null,
        int? roleId = null,
        string? empCd = null,
        string? fullName = null,
        bool pwdResetToday = false,
        int page = 1,
        int pageSize = 50)
    {
        try
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 9999) pageSize = 9999;

            string where = " WHERE 1=1 ";
            var parameters = new List<OracleParameter>();

            if (!string.IsNullOrWhiteSpace(deptcd))
            {
                where += " AND A.DEPTCD = :DEPTCD";
                parameters.Add(new OracleParameter("DEPTCD", deptcd.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(linecd))
            {
                where += " AND A.LINECD = :LINECD";
                parameters.Add(new OracleParameter("LINECD", linecd.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(workcd))
            {
                where += " AND A.WORKCD = :WORKCD";
                parameters.Add(new OracleParameter("WORKCD", workcd.Trim()));
            }
            if (roleId.HasValue)
            {
                where += " AND U.ROLE_ID = :ROLE_ID";
                parameters.Add(new OracleParameter("ROLE_ID", roleId.Value));
            }
            if (!string.IsNullOrWhiteSpace(empCd))
            {
                where += " AND UPPER(TRIM(U.EMPCD)) LIKE UPPER(:EMPCD)";
                parameters.Add(new OracleParameter("EMPCD", $"%{empCd.Trim()}%"));
            }
            if (!string.IsNullOrWhiteSpace(fullName))
            {
                where += " AND UPPER(TRIM(U.FULL_NAME)) LIKE UPPER(:FULL_NAME)";
                parameters.Add(new OracleParameter("FULL_NAME", $"%{fullName.Trim()}%"));
            }
            if (pwdResetToday)
            {
                where += " AND TRUNC(U.LAST_PWD_RESET) = TRUNC(SYSDATE)";
            }

            string countSql = $@"
                SELECT COUNT(1) FROM HRMS.HR_USERS U
                LEFT JOIN HRMS.HR_ROLES R ON U.ROLE_ID = R.ID
                LEFT JOIN HRMS.ECM100 A ON U.EMPCD = A.EMPCD
                LEFT JOIN HRMS.EAM410 B ON A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD 
                                        AND A.WORKCD = B.WORKCD AND A.JEAJIKGB = 'Y' {where}";

            var countResults = await _oracleService.ExecuteQueryAsync(countSql, r => Convert.ToInt32(r[0]), parameters.Select(p => (OracleParameter)p.Clone()).ToArray());
            int total = countResults.FirstOrDefault();

            int minRow = (page - 1) * pageSize + 1;
            int maxRow = page * pageSize;

            var dataParams = parameters.Select(p => (OracleParameter)p.Clone()).ToList();
            dataParams.Add(new OracleParameter("MAX_ROW", maxRow));
            dataParams.Add(new OracleParameter("MIN_ROW", minRow));

            string sql = $@"
                SELECT * FROM (
                    SELECT A1.*, ROWNUM rnum FROM (
                        SELECT U.ID, U.EMPCD, U.FULL_NAME, B.DEPTNM AS DEPT_NAME, B.TEAMNM AS LINE_NAME,
                               B.WORKNM AS WORK_NAME, U.ROLE_ID, R.ROLE_NAME, U.IS_ACTIVE, U.LASTED_LOGIN,
                               U.LAST_PWD_RESET
                        FROM HRMS.HR_USERS U
                        LEFT JOIN HRMS.HR_ROLES R ON U.ROLE_ID = R.ID
                        LEFT JOIN HRMS.ECM100 A ON U.EMPCD = A.EMPCD
                        LEFT JOIN HRMS.EAM410 B ON A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD 
                                                AND A.WORKCD = B.WORKCD AND A.JEAJIKGB = 'Y' {where}
                        ORDER BY U.ID
                    ) A1 WHERE ROWNUM <= :MAX_ROW
                ) WHERE rnum >= :MIN_ROW";

            var data = await _oracleService.ExecuteQueryAsync(sql, reader => new UserInfoModel
            {
                Id = Convert.ToInt32(reader["ID"]),
                EmpCd = reader["EMPCD"]?.ToString() ?? string.Empty,
                FullName = reader["FULL_NAME"]?.ToString() ?? string.Empty,
                DeptCd = reader["DEPT_NAME"]?.ToString(),
                LineCd = reader["LINE_NAME"]?.ToString(),
                WorkCd = reader["WORK_NAME"]?.ToString(),
                RoleId = reader["ROLE_ID"] == DBNull.Value ? null : (int?)Convert.ToInt32(reader["ROLE_ID"]),
                RoleName = reader["ROLE_NAME"]?.ToString(),
                IsActive = Convert.ToInt32(reader["IS_ACTIVE"]),
                LastedLogin  = reader["LASTED_LOGIN"]   == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(reader["LASTED_LOGIN"]),
                LastPwdReset = reader["LAST_PWD_RESET"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(reader["LAST_PWD_RESET"])
            }, dataParams.ToArray());

            return Ok(new { data, total, page, pageSize, totalPage = (int)Math.Ceiling((double)total / pageSize) });
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
    }

    [HttpGet("user-dropdown")]
    public async Task<IActionResult> GetUserDropdown()
    {
        string sql = @"SELECT ID, EMPCD || ' - ' || FULL_NAME AS DISPLAY_NAME, WORKCD, LINECD, DEPTCD
                       FROM HRMS.HR_USERS WHERE IS_ACTIVE = 1 ORDER BY FULL_NAME";

        var result = await _oracleService.ExecuteQueryAsync(sql, reader => new UserDropdownModel
        {
            Id = Convert.ToInt32(reader["ID"]),
            DisplayName = reader["DISPLAY_NAME"]?.ToString() ?? string.Empty,
            WorkCd = reader["WORKCD"]?.ToString(),
            LineCd = reader["LINECD"]?.ToString(),
            DeptCd = reader["DEPTCD"]?.ToString()
        });

        return Ok(result);
    }

    [HttpGet("dropdown/work")]
    public async Task<IActionResult> GetWorkDropdown()
    {
        string sql = @"SELECT DISTINCT WORKCD, WORKNM FROM HRMS.EAM410 
                       WHERE WORKCD IS NOT NULL AND USEYN = 'Y' ORDER BY WORKNM";

        var result = await _oracleService.ExecuteQueryAsync(sql, reader => new
        {
            id = reader["WORKCD"]?.ToString(),
            text = reader["WORKNM"]?.ToString()
        });

        return Ok(result);
    }

    [HttpGet("dropdown/dept")]
    public async Task<IActionResult> GetDeptDropdown()
    {
        string sql = @"SELECT DISTINCT DEPTCD, DEPTNM FROM HRMS.EAM410 
                       WHERE DEPTCD IS NOT NULL AND USEYN = 'Y' ORDER BY DEPTNM";

        var result = await _oracleService.ExecuteQueryAsync(sql, reader => new
        {
            id = reader["DEPTCD"]?.ToString(),
            text = reader["DEPTNM"]?.ToString()
        });

        return Ok(result);
    }

    [HttpGet("dropdown/line")]
    public async Task<IActionResult> GetLineDropdown()
    {
        string sql = @"SELECT DISTINCT LINECD, TEAMNM FROM HRMS.EAM410
                       WHERE LINECD IS NOT NULL AND USEYN = 'Y' ORDER BY TEAMNM";

        var result = await _oracleService.ExecuteQueryAsync(sql, reader => new
        {
            id = reader["LINECD"]?.ToString(),
            text = reader["TEAMNM"]?.ToString()
        });

        return Ok(result);
    }

    [HttpGet("dropdown/line-by-dept")]
    public async Task<IActionResult> GetLineByDept(string? deptcd)
    {
        if (string.IsNullOrEmpty(deptcd)) return Ok(new List<object>());
        var result = await _oracleService.ExecuteQueryAsync(
            @"SELECT DISTINCT LINECD, TEAMNM FROM HRMS.EAM410
              WHERE DEPTCD = :DEPTCD AND LINECD IS NOT NULL AND USEYN = 'Y' ORDER BY TEAMNM",
            r => new { id = r["LINECD"]?.ToString(), text = r["TEAMNM"]?.ToString() },
            new OracleParameter("DEPTCD", deptcd));
        return Ok(result);
    }

    [HttpGet("dropdown/work-by-line")]
    public async Task<IActionResult> GetWorkByLine(string? lineCd, string? deptCd = null)
    {
        if (string.IsNullOrEmpty(lineCd) && string.IsNullOrEmpty(deptCd)) return Ok(new List<object>());
        // LINECD chỉ unique trong từng DEPTCD (PK EAM410: COMPYCD+DEPTCD+LINECD+WORKCD)
        // — thiếu lọc DEPTCD sẽ lộ work của bộ phận khác trùng mã line.
        var conds = new List<string> { "WORKCD IS NOT NULL", "USEYN = 'Y'" };
        var ps    = new List<OracleParameter>();
        if (!string.IsNullOrEmpty(lineCd)) { conds.Add("LINECD = :LINECD"); ps.Add(new OracleParameter("LINECD", lineCd)); }
        if (!string.IsNullOrEmpty(deptCd)) { conds.Add("DEPTCD = :DEPTCD"); ps.Add(new OracleParameter("DEPTCD", deptCd)); }
        var result = await _oracleService.ExecuteQueryAsync(
            $"SELECT DISTINCT WORKCD, WORKNM FROM HRMS.EAM410 WHERE {string.Join(" AND ", conds)} ORDER BY WORKNM",
            r => new { id = r["WORKCD"]?.ToString(), text = r["WORKNM"]?.ToString() },
            ps.ToArray());
        return Ok(result);
    }

    [HttpGet("dropdown/role")]
    public async Task<IActionResult> GetRoleDropdown()
    {
        string sql = @"SELECT ID, ROLE_NAME FROM HRMS.HR_ROLES ORDER BY ROLE_NAME";

        var result = await _oracleService.ExecuteQueryAsync(sql, reader => new
        {
            id = Convert.ToInt32(reader["ID"]),
            text = reader["ROLE_NAME"]?.ToString()
        });

        return Ok(result);
    }

    // Scoped dropdowns — chỉ trả về dept/line/work mà user được phân quyền (HR_USERS_DEPT)
    [HttpGet("dropdown/dept-by-scope")]
    public async Task<IActionResult> GetDeptByScope(string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new List<object>());
        string sql = @"SELECT DISTINCT DEPTCD, DEPTNM FROM HRMS.EAM410
                       WHERE (DEPTCD, LINECD, WORKCD) IN (
                           SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD
                       ) ORDER BY DEPTNM";
        var result = await _oracleService.ExecuteQueryAsync(sql,
            r => new { id = r["DEPTCD"]?.ToString(), text = r["DEPTNM"]?.ToString() },
            new OracleParameter("EMPCD", empcd));
        return Ok(result);
    }

    [HttpGet("dropdown/line-by-scope")]
    public async Task<IActionResult> GetLineByScope(string empcd, string? deptCd = null)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new List<object>());
        string sql = string.IsNullOrEmpty(deptCd)
            ? @"SELECT DISTINCT LINECD, TEAMNM FROM HRMS.EAM410
                WHERE (DEPTCD, LINECD, WORKCD) IN (
                    SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD
                ) ORDER BY TEAMNM"
            : @"SELECT DISTINCT LINECD, TEAMNM FROM HRMS.EAM410
                WHERE DEPTCD = :DEPTCD AND (DEPTCD, LINECD, WORKCD) IN (
                    SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD
                ) ORDER BY TEAMNM";
        var ps = string.IsNullOrEmpty(deptCd)
            ? new[] { new OracleParameter("EMPCD", empcd) }
            : new[] { new OracleParameter("EMPCD", empcd), new OracleParameter("DEPTCD", deptCd) };
        var result = await _oracleService.ExecuteQueryAsync(sql,
            r => new { id = r["LINECD"]?.ToString(), text = r["TEAMNM"]?.ToString() }, ps);
        return Ok(result);
    }

    [HttpGet("dropdown/work-by-scope")]
    public async Task<IActionResult> GetWorkByScope(string empcd, string? lineCd = null, string? deptCd = null)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new List<object>());
        // Cùng lý do work-by-line: LINECD trùng mã giữa các dept nên phải lọc kèm DEPTCD.
        var conds = new List<string> {
            @"(DEPTCD, LINECD, WORKCD) IN (
                SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD
            )" };
        var ps = new List<OracleParameter> { new OracleParameter("EMPCD", empcd) };
        if (!string.IsNullOrEmpty(lineCd)) { conds.Add("LINECD = :LINECD"); ps.Add(new OracleParameter("LINECD", lineCd)); }
        if (!string.IsNullOrEmpty(deptCd)) { conds.Add("DEPTCD = :DEPTCD"); ps.Add(new OracleParameter("DEPTCD", deptCd)); }
        var result = await _oracleService.ExecuteQueryAsync(
            $"SELECT DISTINCT WORKCD, WORKNM FROM HRMS.EAM410 WHERE {string.Join(" AND ", conds)} ORDER BY WORKNM",
            r => new { id = r["WORKCD"]?.ToString(), text = r["WORKNM"]?.ToString() }, ps.ToArray());
        return Ok(result);
    }

    [HttpGet("dropdown/emp")]
    public async Task<IActionResult> GetEmpDropdown(string? term)
    {
        if (string.IsNullOrEmpty(term) || term.Length < 2) return Ok(new List<object>());
        string sql = @"SELECT * FROM (
                         SELECT EMPCD, CNAME FROM HRMS.ECM100
                          WHERE JEAJIKGB = 'Y'
                            AND (UPPER(EMPCD) LIKE :TERM1 OR UPPER(CNAME) LIKE :TERM2)
                          ORDER BY CNAME
                       ) WHERE ROWNUM <= 30";
        string like = "%" + term.ToUpper() + "%";
        var result = await _oracleService.ExecuteQueryAsync(sql,
            r => new { id = r["EMPCD"]?.ToString(), text = $"{r["EMPCD"]} - {r["CNAME"]}" },
            new OracleParameter("TERM1", OracleDbType.Varchar2) { Value = like },
            new OracleParameter("TERM2", OracleDbType.Varchar2) { Value = like });
        return Ok(result);
    }

    [HttpGet("dropdown/emp-by-scope")]
    public async Task<IActionResult> GetEmpByScope(string empcd)
    {
        if (string.IsNullOrEmpty(empcd)) return Ok(new List<object>());
        string sql = @"
            SELECT EC.EMPCD, EC.CNAME,
                   EA.DEPTNM, EA.TEAMNM, EA.WORKNM
            FROM HRMS.ECM100 EC
            LEFT JOIN HRMS.EAM410 EA ON EA.DEPTCD = EC.DEPTCD AND EA.LINECD = EC.LINECD AND EA.WORKCD = EC.WORKCD
            WHERE EC.JEAJIKGB = 'Y'
              AND (EC.RETDAT IS NULL OR EC.RETDAT > TO_CHAR(SYSDATE,'YYYYMMDD'))
              AND (EC.DEPTCD, EC.LINECD, EC.WORKCD) IN (
                  SELECT DEPTCD, LINECD, WORKCD FROM HRMS.HR_USERS_DEPT WHERE EMPCD = :EMPCD
              )
            ORDER BY EA.DEPTNM, EA.TEAMNM, EA.WORKNM, EC.CNAME";
        var result = await _oracleService.ExecuteQueryAsync(sql,
            r => new {
                empcd     = r["EMPCD"]?.ToString(),
                name      = r["CNAME"]?.ToString(),
                dept_name = r["DEPTNM"]?.ToString(),
                line_name = r["TEAMNM"]?.ToString(),
                work_name = r["WORKNM"]?.ToString()
            },
            new OracleParameter("EMPCD", empcd));
        return Ok(result);
    }

    [HttpGet("user-detail")]
    public async Task<IActionResult> GetUserDetail(string empCd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empCd)) return BadRequest(new { error = "empCd is required" });

            string sql = @"
                SELECT B.DEPTNM, B.TEAMNM, B.WORKNM, A.CNAME, A.BIRTHDAT, A.SEXGB, A.MARRGB,
                       A.HOMETEL AS PHONE, A.JUMINNO_PLACE AS HOMETOWN,
                       A.CONTRACT_TYPE, A.CONTRACT_DATE,
                       A.JUMINNO, A.JUMINNO_DATE, A.IGENTDAT,
                       A.ADDRESS_ETC, C.CODE_NAME1_N, C.CODE_NAME3_N,
                       FLOOR(MONTHS_BETWEEN(SYSDATE, TO_DATE(
                           CASE WHEN TO_NUMBER(SUBSTR(A.EMPCD,1,2)) > TO_NUMBER(TO_CHAR(SYSDATE,'YY'))
                                THEN '19' ELSE '20' END || SUBSTR(A.EMPCD,1,4) || '01', 'YYYYMMDD'
                       )) / 12) || ' năm ' ||
                       MOD(FLOOR(MONTHS_BETWEEN(SYSDATE, TO_DATE(
                           CASE WHEN TO_NUMBER(SUBSTR(A.EMPCD,1,2)) > TO_NUMBER(TO_CHAR(SYSDATE,'YY'))
                                THEN '19' ELSE '20' END || SUBSTR(A.EMPCD,1,4) || '01', 'YYYYMMDD'
                       ))), 12) || ' tháng' AS SENIORITY_DESC
                FROM HRMS.ECM100 A
                JOIN HRMS.EAM410 B ON A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD AND A.WORKCD = B.WORKCD AND B.USEYN = 'Y'
                LEFT JOIN HRMS.EAM510 C ON A.BONADDR3 = C.CODE1 AND A.BONADDR1 = C.CODE3
                WHERE A.EMPCD = :EMPCD";

            var results = await _oracleService.ExecuteQueryAsync(sql, reader => new UserDetailModel
            {
                DeptName = reader["DEPTNM"]?.ToString(),
                LineName = reader["TEAMNM"]?.ToString(),
                WorkName = reader["WORKNM"]?.ToString(),
                FullName = reader["CNAME"]?.ToString() ?? string.Empty,
                BirthDate = SafeToDate(reader["BIRTHDAT"]),
                Sex = reader["SEXGB"]?.ToString(),
                MaritalStatus = reader["MARRGB"]?.ToString(),
                Phone = reader["PHONE"]?.ToString(),
                Seniority = reader["SENIORITY_DESC"]?.ToString(),
                HomeTown = reader["HOMETOWN"]?.ToString(),
                ContractType = reader["CONTRACT_TYPE"]?.ToString(),
                ContractDate = SafeToDate(reader["CONTRACT_DATE"]),
                Juminno      = reader["JUMINNO"]?.ToString(),
                JuminnoDate  = reader["JUMINNO_DATE"]?.ToString(),
                HireDate     = SafeToDate(reader["IGENTDAT"]),
                Address = string.Join(", ",
                    new[] {
                        reader["ADDRESS_ETC"]?.ToString()?.Trim(),
                        reader["CODE_NAME3_N"]?.ToString()?.Trim(),
                        reader["CODE_NAME1_N"]?.ToString()?.Trim()
                    }.Where(s => !string.IsNullOrEmpty(s)))
            }, new OracleParameter("EMPCD", empCd.Trim()));

            var r = results.FirstOrDefault();
            Console.WriteLine($"[user-detail] empCd={empCd} | addr_etc={r?.Address} | rows={results.Count}");
            return Ok(r);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[user-detail] EXCEPTION: {ex.Message}");
            return Ok(new { error = ex.Message });
        }
    }

    [HttpPost("bulk-update-role")]
    public async Task<IActionResult> BulkUpdateRole([FromBody] List<BulkUpdateRoleItem> items)
    {
        if (items == null || items.Count == 0)
            return BadRequest(new { success = false, message = "Không có dữ liệu" });

        var allRoles = await _oracleService.ExecuteQueryAsync(
            "SELECT ID, ROLE_NAME FROM HRMS.HR_ROLES",
            r => new { Id = Convert.ToInt32(r["ID"]), Name = r["ROLE_NAME"]?.ToString() ?? "" });
        var roleMap = allRoles.ToDictionary(r => r.Id, r => r.Name);

        var results = new List<BulkUpdateRoleResult>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.EmpCd)) continue;

            if (!roleMap.TryGetValue(item.RoleId, out var roleName))
            {
                results.Add(new BulkUpdateRoleResult { EmpCd = item.EmpCd, Success = false, Message = $"Role ID {item.RoleId} không tồn tại" });
                continue;
            }
            if (roleName == "Admin")
            {
                results.Add(new BulkUpdateRoleResult { EmpCd = item.EmpCd, Success = false, Message = "Không thể cấp quyền Admin" });
                continue;
            }

            var exists = await _oracleService.ExecuteQueryAsync(
                "SELECT 1 FROM HRMS.HR_USERS WHERE EMPCD = :EMPCD",
                r => 1,
                new OracleParameter("EMPCD", item.EmpCd.Trim()));

            if (exists.Count == 0)
            {
                results.Add(new BulkUpdateRoleResult { EmpCd = item.EmpCd, Success = false, Message = "Chưa đăng nhập vào hệ thống, không thể cập nhật" });
                continue;
            }

            int rows = await _oracleService.ExecuteNonQueryAsync(
                "UPDATE HRMS.HR_USERS SET ROLE_ID = :ROLE_ID, UPDT_ID = :LOGIN_USER, UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD",
                new OracleParameter("ROLE_ID", item.RoleId),
                new OracleParameter("LOGIN_USER", item.LoginUser ?? "SYSTEM"),
                new OracleParameter("EMPCD", item.EmpCd.Trim()));

            results.Add(new BulkUpdateRoleResult { EmpCd = item.EmpCd, Success = rows > 0, Message = rows > 0 ? "Thành công" : "Không tìm thấy user" });
        }

        return Ok(new { success = true, data = results });
    }

    [HttpPost("update-role")]
    public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.EmpCd) || req.RoleId <= 0)
            return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ" });

        var roleCheck = await _oracleService.ExecuteQueryAsync(
            "SELECT ROLE_NAME FROM HRMS.HR_ROLES WHERE ID = :ROLE_ID",
            r => r["ROLE_NAME"]?.ToString(),
            new OracleParameter("ROLE_ID", req.RoleId));

        if (roleCheck.Count == 0)
            return Ok(new { success = false, message = "Role không tồn tại" });

        if (roleCheck[0] == "Admin")
            return Ok(new { success = false, message = "Không thể cấp quyền Admin" });

        string sql = "UPDATE HRMS.HR_USERS SET ROLE_ID = :ROLE_ID, UPDT_ID = :LOGIN_USER, UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD";
        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("ROLE_ID", req.RoleId),
            new OracleParameter("LOGIN_USER", req.LoginUser),
            new OracleParameter("EMPCD", req.EmpCd));

        if (rows == 0)
            return Ok(new { success = false, message = "User không tồn tại" });

        return Ok(new { success = true, message = $"Đã cập nhật role thành công" });
    }

    [HttpPost("update-signature-flag")]
    public async Task<IActionResult> UpdateSignatureFlag([FromBody] UpdateSignatureRequest req)
    {
        if (req == null || string.IsNullOrEmpty(req.EmpCd)) return BadRequest("Thiếu EMPCD");
        if (string.IsNullOrEmpty(req.Flag) || (req.Flag != "Y" && req.Flag != "N")) return BadRequest("Flag phải là 'Y' hoặc 'N'");

        string sql = @"UPDATE HRMS.HR_USERS SET SIGNATUREBLOB = :FLAG, UPDT_ID = :LOGIN_USER, UPDT_DT = SYSDATE WHERE EMPCD = :EMPCD";
        int rows = await _oracleService.ExecuteNonQueryAsync(sql,
            new OracleParameter("FLAG", req.Flag),
            new OracleParameter("LOGIN_USER", req.LoginUser ?? "SYSTEM"),
            new OracleParameter("EMPCD", req.EmpCd));

        if (rows == 0) return Ok(new { success = false, message = "User không tồn tại" });
        return Ok(new { success = true, message = "Cập nhật chữ ký thành công" });
    }

    // ─────────────────────────────────────────────
    // GET: account/check-active?empcd=xxx
    // Kiểm tra giữa phiên: tài khoản còn active + còn đi làm không.
    // Dùng bởi HR_web (CookieAuthenticationEvents.OnValidatePrincipal) để tự
    // logout ngay khi HR cho nghỉ việc, không cần chờ tới hạn hết cookie.
    // ─────────────────────────────────────────────
    [HttpGet("check-active")]
    public async Task<IActionResult> CheckActive(string empcd)
    {
        if (string.IsNullOrWhiteSpace(empcd))
            return Ok(new { success = true, active = false });

        var rows = await _oracleService.ExecuteQueryAsync(@"
            SELECT U.IS_ACTIVE, E.JEAJIKGB
            FROM HRMS.HR_USERS U
            LEFT JOIN HRMS.ECM100 E ON E.EMPCD = U.EMPCD
            WHERE U.EMPCD = :EMPCD",
            r => new
            {
                IsActive = r["IS_ACTIVE"] == DBNull.Value ? 1 : Convert.ToInt32(r["IS_ACTIVE"]),
                Jeajikgb = r["JEAJIKGB"] == DBNull.Value ? null : r["JEAJIKGB"].ToString()
            },
            new OracleParameter("EMPCD", empcd));

        var u = rows.FirstOrDefault();
        // Không có trong HR_USERS nữa (bị xoá) -> coi như không active
        // Có JEAJIKGB (là nhân viên ECM100) nhưng khác 'Y' -> đã nghỉ việc
        bool active = u != null && u.IsActive == 1 && (u.Jeajikgb == null || u.Jeajikgb == "Y");
        return Ok(new { success = true, active });
    }

    // ─────────────────────────────────────────────
    // POST: account/sync-resigned
    // Tự động disable HR_USERS cho nhân viên đã nghỉ (JEAJIKGB != "Y" trong ECM100)
    // ─────────────────────────────────────────────
    [HttpPost("sync-resigned")]
    public async Task<IActionResult> SyncResignedUsers()
    {
        try
        {
            string sql = @"
                UPDATE HRMS.HR_USERS SET IS_ACTIVE = 0, UPDT_ID = 'SYSTEM', UPDT_DT = SYSDATE
                WHERE IS_ACTIVE = 1
                  AND EMPCD IN (SELECT EMPCD FROM HRMS.ECM100 WHERE NVL(JEAJIKGB, 'N') != 'Y')";
            int updated = await _oracleService.ExecuteNonQueryAsync(sql);
            return Ok(new { success = true, updated });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }

    private DateTime? SafeToDate(object value)
    {
        if (value == null || value == DBNull.Value) return null;
        var str = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(str)) return null;

        if (DateTime.TryParseExact(str, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
            return dt;
        if (DateTime.TryParse(str, out dt))
            return dt;
        return null;
    }
}
