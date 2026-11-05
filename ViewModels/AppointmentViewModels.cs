using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CRM_V2.Models;

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

    public string? EndTime { get; set; } = "10:00";

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

public class EmployeeAvailabilityDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("employee_id")]
    public long EmployeeId => Id;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("specialty")]
    public string Specialty { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "staff";

    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("isAvailable")]
    public bool IsAvailableCamel => IsAvailable;

    [JsonPropertyName("conflict_appointment_id")]
    public long? ConflictAppointmentId { get; set; }

    [JsonPropertyName("conflict_start_time")]
    public string? ConflictStartTime { get; set; }

    [JsonPropertyName("conflict_end_time")]
    public string? ConflictEndTime { get; set; }

    [JsonPropertyName("conflict_status")]
    public string? ConflictStatus { get; set; }

    [JsonPropertyName("conflict_customer_name")]
    public string? ConflictCustomerName { get; set; }

    [JsonPropertyName("display_label")]
    public string DisplayLabel => IsAvailable
        ? $"🟢 {Name} (Sẵn sàng)"
        : $"🔴 {Name} (Đã có lịch {ConflictStartTime} - {ConflictEndTime})";
}

public class AvailableEmployeesResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("calculated_end_time")]
    public string CalculatedEndTime => EndTime;

    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("totalEmployees")]
    public int TotalEmployees { get; set; }

    [JsonPropertyName("availableCount")]
    public int AvailableCount { get; set; }

    [JsonPropertyName("busyCount")]
    public int BusyCount { get; set; }

    [JsonPropertyName("all_busy")]
    public bool AllBusy { get; set; }

    [JsonPropertyName("allBusy")]
    public bool AllBusyCamel => AllBusy;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("employees")]
    public List<EmployeeAvailabilityDto> Employees { get; set; } = new();

    [JsonPropertyName("suggested_slots")]
    public List<string> SuggestedSlots { get; set; } = new();

    [JsonPropertyName("suggestedSlots")]
    public List<string> SuggestedSlotsCamel => SuggestedSlots;
}

public class AppointmentOperationResult
{
    public bool Success { get; set; }
    public string Code { get; set; } = "OK";
    public string Message { get; set; } = string.Empty;
    public Appointment? Appointment { get; set; }
    public string CalculatedStartTime { get; set; } = string.Empty;
    public string CalculatedEndTime { get; set; } = string.Empty;
    public int TotalDurationMinutes { get; set; }
    public List<string> SuggestedSlots { get; set; } = new();
    public List<EmployeeAvailabilityDto> AvailableEmployees { get; set; } = new();
}
