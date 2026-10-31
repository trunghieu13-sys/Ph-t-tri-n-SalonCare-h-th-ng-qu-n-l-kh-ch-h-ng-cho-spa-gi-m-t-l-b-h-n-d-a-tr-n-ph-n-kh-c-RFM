using System.ComponentModel.DataAnnotations;

namespace CRM_V2.ViewModels;

public enum NotificationChannel
{
    Email = 1,
    Sms = 2,
    Both = 3
}

public class NotificationViewModel
{
    [Display(Name = "Kênh gửi")]
    public NotificationChannel Channel { get; set; } = NotificationChannel.Email;

    [Display(Name = "Địa chỉ Email")]
    [EmailAddress(ErrorMessage = "Định dạng Email không hợp lệ.")]
    public string? Email { get; set; }

    [Display(Name = "Số điện thoại nhận SMS")]
    [RegularExpression(@"^\+?[0-9]{9,15}$", ErrorMessage = "Số điện thoại không hợp lệ (Ví dụ: +84901234567 hoặc 0901234567).")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Tiêu đề (Chỉ áp dụng cho Email)")]
    public string? Subject { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập nội dung tin nhắn.")]
    [Display(Name = "Nội dung")]
    public string Content { get; set; } = string.Empty;
}
