using System.ComponentModel.DataAnnotations;

namespace HR_web.Models.Account;

public class ForgotPasswordModel
{
    [Required(ErrorMessage = "Vui lòng nhập mã nhân viên")]
    [Display(Name = "Mã số nhân viên")]
    public string EMPCD { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số CCCD")]
    [Display(Name = "Số CCCD/CMND")]
    public string Juminno { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập ngày cấp")]
    [Display(Name = "Ngày cấp CCCD")]
    public string JuminnoDate { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải ít nhất 6 ký tự")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
    [DataType(DataType.Password)]
    [Display(Name = "Xác nhận mật khẩu")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
