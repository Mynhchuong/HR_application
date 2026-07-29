using HR_web.Models.Account;
using Newtonsoft.Json;


namespace HR_web.API.Service;

public class AccountService
{
    private readonly ApiService _api;

    private const string LoginEndpoint = "Account/login";
    private const string UserListEndpoint = "Account/user-list";
    private const string UserDetailEndpoint = "Account/user-detail";
    private const string CreateUserEndpoint = "Account/create-user";
    private const string UpdateSignatureEndpoint = "Account/update-signature-flag";

    public AccountService(ApiService api)
    {
        _api = api;
    }

    #region COMMON RESPONSE
    public class ApiResponse<T>
    {
        public bool success { get; set; }
        public T? data { get; set; }
        public string? message { get; set; }
    }

    private async Task<ApiResponse<T>?> ParseResponse<T>(HttpResponseMessage? response)
    {
        if (response == null) return null;
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonConvert.DeserializeObject<ApiResponse<T>>(json); }
        catch { return null; }
    }
    #endregion

    #region LOGIN
    public async Task<UserInfoModel?> LoginAsync(string empcd, string password)
    {
        var payload = new { EmpCd = empcd, Password = password };
        var response = await _api.PostAsync(LoginEndpoint, payload);
        var result = await ParseResponse<UserInfoModel>(response);
        return (result != null && result.success) ? result.data : null;
    }

    private class CheckActiveResponse
    {
        public bool success { get; set; }
        public bool active { get; set; }
    }

    // Kiểm tra giữa phiên: tài khoản còn active + còn đi làm không (JEAJIKGB/IS_ACTIVE).
    // Lỗi kết nối -> coi như còn active (fail-open), tránh văng logout hàng loạt khi API tạm gián đoạn.
    public async Task<bool> CheckActiveAsync(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return false;
        var result = await _api.GetAsync<CheckActiveResponse>("Account/check-active", $"empcd={Uri.EscapeDataString(empCd)}");
        return result?.success != true || result.active;
    }
    #endregion

    #region USER LIST
    public async Task<UserInfoPagedViewModel> GetUserListAsync(
        string? fullName = null, string? deptCd = null, string? lineCd = null,
        string? workCd = null, int? roleId = null, string? empCd = null,
        bool pwdResetToday = false,
        int page = 1, int pageSize = 50)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(fullName)) queryParams.Add($"fullName={Uri.EscapeDataString(fullName)}");
        if (!string.IsNullOrEmpty(deptCd)) queryParams.Add($"deptcd={Uri.EscapeDataString(deptCd)}");
        if (!string.IsNullOrEmpty(lineCd)) queryParams.Add($"linecd={Uri.EscapeDataString(lineCd)}");
        if (!string.IsNullOrEmpty(workCd)) queryParams.Add($"workcd={Uri.EscapeDataString(workCd)}");
        if (!string.IsNullOrEmpty(empCd)) queryParams.Add($"empCd={Uri.EscapeDataString(empCd)}");
        if (roleId.HasValue) queryParams.Add($"roleId={roleId.Value}");
        if (pwdResetToday) queryParams.Add("pwdResetToday=true");
        queryParams.Add($"page={page}");
        queryParams.Add($"pageSize={pageSize}");

        var result = await _api.GetAsync<UserInfoPagedViewModel>(UserListEndpoint, string.Join("&", queryParams));
        return result ?? new UserInfoPagedViewModel();
    }
    #endregion

    #region USER DETAIL
    public async Task<UserDetailModel?> GetUserDetailAsync(string empCd)
    {
        if (string.IsNullOrWhiteSpace(empCd)) return null;
        return await _api.GetAsync<UserDetailModel>(UserDetailEndpoint, $"empCd={Uri.EscapeDataString(empCd)}");
    }
    #endregion

    #region USER ACTIONS
    public async Task<bool> ResetPasswordAsync(string empCd, string loginUser)
    {
        var response = await _api.PostAsync("Account/reset-password", new { EmpCd = empCd, LoginUser = loginUser });
        var result = await ParseResponse<object>(response);
        return result != null && result.success;
    }

    public async Task<(bool ok, string message)> ForgotPasswordAsync(string empcd, string juminno, string juminnoDate, string newPassword)
    {
        try
        {
            var response = await _api.PostAsync("Account/forgot-password", new {
                Empcd       = empcd,
                Juminno     = juminno,
                JuminnoDate = juminnoDate,
                NewPassword = newPassword
            });
            if (response?.IsSuccessStatusCode == true)
            {
                var json = await response.Content.ReadAsStringAsync();
                var obj  = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                bool ok  = obj?.success == true;
                string msg = (string?)obj?.message ?? "";
                return (ok, msg);
            }
            return (false, "Lỗi kết nối server");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<bool> ChangePasswordAsync(string empCd, string oldPassword, string newPassword)
    {
        var response = await _api.PostAsync("Account/change-password", new { EmpCd = empCd, OldPassword = oldPassword, NewPassword = newPassword });
        var result = await ParseResponse<object>(response);
        return result != null && result.success;
    }

    public async Task<bool> DisableUserAsync(string empCd, string loginUser)
    {
        var response = await _api.PostAsync("Account/disable-user", new Dictionary<string, string> { { "empcd", empCd }, { "loginUser", loginUser } });
        var result = await ParseResponse<object>(response);
        return result != null && result.success;
    }

    public async Task<bool> EnableUserAsync(string empCd, string loginUser)
    {
        var response = await _api.PostAsync("Account/enable-user", new Dictionary<string, string> { { "empcd", empCd }, { "loginUser", loginUser } });
        var result = await ParseResponse<object>(response);
        return result != null && result.success;
    }

    public async Task<(bool success, string message)> CreateUserAsync(CreateUserModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.EmpCd))
            return (false, "Dữ liệu không hợp lệ");
        try
        {
            var response = await _api.PostAsync(CreateUserEndpoint, model);
            var result = await ParseResponse<object>(response);
            if (result == null) return (false, "Không nhận được phản hồi từ server");
            return (result.success, result.message ?? string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public class BulkUpdateRoleResult
    {
        public string EmpCd { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public async Task<List<BulkUpdateRoleResult>> BulkUpdateRoleAsync(List<(string empCd, int roleId)> items, string loginUser)
    {
        var payload = items.Select(x => new { EmpCd = x.empCd, RoleId = x.roleId, LoginUser = loginUser });
        var response = await _api.PostAsync("Account/bulk-update-role", payload);
        var result = await ParseResponse<List<BulkUpdateRoleResult>>(response);
        return result?.data ?? new List<BulkUpdateRoleResult>();
    }

    public async Task<(bool success, string message)> UpdateRoleAsync(string empCd, int roleId, string loginUser)
    {
        var response = await _api.PostAsync("Account/update-role", new { EmpCd = empCd, RoleId = roleId, LoginUser = loginUser });
        var result = await ParseResponse<object>(response);
        if (result == null) return (false, "Không nhận được phản hồi từ server");
        return (result.success, result.message ?? string.Empty);
    }

    public async Task<bool> UpdateSignatureFlagAsync(string empCd, bool hasSignature, string loginUser)
    {
        var response = await _api.PostAsync(UpdateSignatureEndpoint, new
        {
            EmpCd = empCd,
            Flag = hasSignature ? "Y" : "N",
            LoginUser = loginUser
        });
        var result = await ParseResponse<object>(response);
        return result != null && result.success;
    }
    public async Task SyncResignedUsersAsync()
    {
        try { await _api.PostAsync("Account/sync-resigned", new { }); }
        catch { /* silent — không block UI */ }
    }
    #endregion
}
