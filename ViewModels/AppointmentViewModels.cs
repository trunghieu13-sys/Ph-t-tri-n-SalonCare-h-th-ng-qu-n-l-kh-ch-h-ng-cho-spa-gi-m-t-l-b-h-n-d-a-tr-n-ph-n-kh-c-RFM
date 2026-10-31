using System.ComponentModel.DataAnnotations;

namespace CRM_V2.ViewModels;

public class AppointmentCreateViewModel
{
    [Required(ErrorMessage = "Vui lòng chọn khách hàng.")]
    public long CustomerId { get; set; }

    public long? EmployeeId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày hẹn.")]
    public DateTime AppointmentDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Vui lòng chọn giờ bắt đầu.")]
    public string StartTime { get; set; } = "09:00";

    [Required(ErrorMessage = "Vui lòng chọn giờ kết thúc dự kiến.")]
    public string EndTime { get; set; } = "10:00";

    [Required(ErrorMessage = "Vui lòng chọn ít nhất một dịch vụ.")]
    public List<int> ServiceIds { get; set; } = new();

    public string BookingSource { get; set; } = "Direct";

    public decimal DepositAmount { get; set; } = 0m;

    public string? Notes { get; set; }
}

public class AppointmentUpdateStatusViewModel
{
    [Required]
    public long AppointmentId { get; set; }

    [Required]
    public string NewStatus { get; set; } = string.Empty;

    public string? CancellationReason { get; set; }
}

public class AppointmentRescheduleViewModel
{
    [Required]
    public long AppointmentId { get; set; }

    [Required]
    public DateTime AppointmentDate { get; set; }

    [Required]
    public string StartTime { get; set; } = string.Empty;

    [Required]
    public string EndTime { get; set; } = string.Empty;

    public long? EmployeeId { get; set; }

    public decimal DepositAmount { get; set; } = 0m;

    public string? Notes { get; set; }

    public List<int>? ServiceIds { get; set; }
}

public class AppointmentCancelViewModel
{
    [Required]
    public long AppointmentId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do hủy lịch hẹn.")]
    public string CancellationReason { get; set; } = string.Empty;
}
