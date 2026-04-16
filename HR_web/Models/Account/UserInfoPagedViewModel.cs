namespace HR_web.Models.Account;

/// <summary>
/// ViewModel trả về từ API user-list (có phân trang).
/// AccountService.GetUserListAsync() trả về kiểu này.
/// </summary>
public class UserInfoPagedViewModel
{
    public List<UserInfoModel> Data { get; set; } = new();
    public int Total { get; set; }
    public int Page  { get; set; }
    public int PageSize { get; set; }
}
