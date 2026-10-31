using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("appointments")]
public class Appointment
{
    [Key]
    [Column("appointment_id")]
    public long AppointmentId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    [Column("employee_id")]
    public long? EmployeeId { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    public virtual User? Employee { get; set; }

    [Column("appointment_date", TypeName = "date")]
    public DateTime AppointmentDate { get; set; }

    [Column("start_time", TypeName = "time")]
    public TimeSpan StartTime { get; set; }

    [Column("end_time", TypeName = "time")]
    public TimeSpan EndTime { get; set; }

    [Required]
    [MaxLength(30)]
    [Column("status")]
    public string Status { get; set; } = "Pending";

    [Required]
    [MaxLength(30)]
    [Column("booking_source")]
    public string BookingSource { get; set; } = "Direct";

    [Column("deposit_amount", TypeName = "decimal(12, 2)")]
    public decimal DepositAmount { get; set; } = 0.00m;

    [Column("total_price", TypeName = "decimal(12, 2)")]
    public decimal TotalPrice { get; set; } = 0.00m;

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("confirmed_at")]
    public DateTime? ConfirmedAt { get; set; }

    [Column("check_in_at")]
    public DateTime? CheckInAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("cancelled_at")]
    public DateTime? CancelledAt { get; set; }

    [Column("cancellation_reason")]
    public string? CancellationReason { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    public virtual User? Creator { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [Column("is_hidden_from_board")]
    public bool IsHiddenFromBoard { get; set; } = false;

    [Column("hidden_at")]
    public DateTime? HiddenAt { get; set; }

    public virtual ICollection<AppointmentService> AppointmentServices { get; set; } = new List<AppointmentService>();
}
