using System.ComponentModel.DataAnnotations;

namespace HR_web.Models.Account;

public class LoginModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã nhân viên")]
    public string EMPCD { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}
