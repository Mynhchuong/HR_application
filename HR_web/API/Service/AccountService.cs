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
        var formData = new Dictionary<string, string>
        {
            { "empcd", empcd },
            { "password", password }
        };
        var response = await _api.PostFormAsync(LoginEndpoint, formData);
        var result = await ParseResponse<UserInfoModel>(response);
        return (result != null && result.success) ? result.data : null;
    }
    #endregion

    #region USER LIST
    public async Task<UserInfoPagedViewModel> GetUserListAsync(
        string? fullName = null, string? deptCd = null, string? lineCd = null,
        string? workCd = null, int? roleId = null, string? empCd = null,
        int page = 1, int pageSize = 50)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(fullName)) queryParams.Add($"fullName={Uri.EscapeDataString(fullName)}");
        if (!string.IsNullOrEmpty(deptCd)) queryParams.Add($"deptcd={Uri.EscapeDataString(deptCd)}");
        if (!string.IsNullOrEmpty(lineCd)) queryParams.Add($"linecd={Uri.EscapeDataString(lineCd)}");
        if (!string.IsNullOrEmpty(workCd)) queryParams.Add($"workcd={Uri.EscapeDataString(workCd)}");
        if (!string.IsNullOrEmpty(empCd)) queryParams.Add($"empCd={Uri.EscapeDataString(empCd)}");
        if (roleId.HasValue) queryParams.Add($"roleId={roleId.Value}");
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
        var response = await _api.PostFormAsync("Account/reset-password", new Dictionary<string, string>
        {
            { "empcd", empCd }, { "loginUser", loginUser }
        });
        var result = await ParseResponse<object>(response);
        return result != null && result.success;
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
    #endregion
}
