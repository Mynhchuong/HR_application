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

        // Kiểm tra xem nhân viên có tồn tại trên ERP và còn làm việc hay không
        if (userCheck == null || userCheck.Jeajikgb != "Y")
        {
            return Ok(new { success = false, message = "Sai tài khoản hoặc mật khẩu" });
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
        int page = 1,
        int pageSize = 50)
    {
        try
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

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
                               B.WORKNM AS WORK_NAME, U.ROLE_ID, R.ROLE_NAME, U.IS_ACTIVE, U.LASTED_LOGIN
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
                LastedLogin = reader["LASTED_LOGIN"] == DBNull.Value ? null : (DateTime?)Convert.ToDateTime(reader["LASTED_LOGIN"])
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

    [HttpGet("user-detail")]
    public async Task<IActionResult> GetUserDetail(string empCd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(empCd)) return BadRequest(new { error = "empCd is required" });

            string sql = @"
                SELECT B.DEPTNM, B.TEAMNM, B.WORKNM, A.CNAME, A.BIRTHDAT, A.SEXGB, A.MARRGB,
                       A.HOMETEL AS PHONE, A.SENIORITY AS SENIORITY_DESC, A.JUMINNO_PLACE AS HOMETOWN,
                       A.CONTRACT_TYPE, A.CONTRACT_DATE
                FROM HRMS.ECM100 A
                JOIN HRMS.EAM410 B ON A.DEPTCD = B.DEPTCD AND A.LINECD = B.LINECD AND A.WORKCD = B.WORKCD AND B.USEYN = 'Y'
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
                ContractDate = SafeToDate(reader["CONTRACT_DATE"])
            }, new OracleParameter("EMPCD", empCd.Trim()));

            return Ok(results.FirstOrDefault());
        }
        catch (Exception ex)
        {
            return Ok(new { error = ex.Message });
        }
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
