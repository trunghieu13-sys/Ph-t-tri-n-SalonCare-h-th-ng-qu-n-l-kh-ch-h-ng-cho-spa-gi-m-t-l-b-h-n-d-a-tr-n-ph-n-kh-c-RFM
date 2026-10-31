using System.ComponentModel.DataAnnotations;

namespace CRM_V2.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [Display(Name = "Email đăng ký")]
    public string Email { get; set; } = string.Empty;
}
