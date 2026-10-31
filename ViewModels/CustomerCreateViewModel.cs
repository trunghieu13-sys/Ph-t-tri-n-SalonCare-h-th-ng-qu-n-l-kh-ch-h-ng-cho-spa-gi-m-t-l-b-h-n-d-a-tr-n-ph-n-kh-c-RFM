using System.ComponentModel.DataAnnotations;

namespace CRM_V2.ViewModels;

public class CustomerCreateViewModel
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Họ và tên không vượt quá 100 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")]
    [RegularExpression(@"^(0|\+84)(3[2-9]|5[25689]|7[06-9]|8[1-9]|9[0-9])[0-9]{7}$", 
        ErrorMessage = "Số điện thoại Việt Nam không hợp lệ (VD: 0901234567 hoặc +84901234567).")]
    [StringLength(15, ErrorMessage = "Số điện thoại không vượt quá 15 ký tự.")]
    [Display(Name = "Số điện thoại")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ.")]
    [StringLength(100, ErrorMessage = "Email không vượt quá 100 ký tự.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(20)]
    [Display(Name = "Giới tính")]
    public string? Gender { get; set; } = "Nữ";

    [DataType(DataType.Date)]
    [Display(Name = "Ngày sinh")]
    public DateTime? DateOfBirth { get; set; }

    [StringLength(255, ErrorMessage = "Địa chỉ không vượt quá 255 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    [StringLength(50)]
    [Display(Name = "Nguồn khách")]
    public string CustomerSource { get; set; } = "Truc_tiep";

    [StringLength(30)]
    [Display(Name = "Phân loại khách")]
    public string CustomerType { get; set; } = "Moi";

    [StringLength(20)]
    [Display(Name = "Trạng thái")]
    public string CustomerStatus { get; set; } = "Hoat_dong";

    [StringLength(500, ErrorMessage = "Đường dẫn ảnh đại diện không vượt quá 500 ký tự.")]
    [Display(Name = "Ảnh đại diện")]
    public string? AvatarUrl { get; set; }

    [Display(Name = "Ghi chú ban đầu")]
    public string? Notes { get; set; }
}
