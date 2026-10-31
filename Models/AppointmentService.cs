using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("appointment_services")]
public class AppointmentService
{
    [Column("appointment_id")]
    public long AppointmentId { get; set; }

    [ForeignKey(nameof(AppointmentId))]
    public virtual Appointment? Appointment { get; set; }

    [Column("service_id")]
    public int ServiceId { get; set; }

    [ForeignKey(nameof(ServiceId))]
    public virtual Service? Service { get; set; }

    [Column("price", TypeName = "decimal(12, 2)")]
    public decimal Price { get; set; } = 0.00m;

    [Column("employee_id")]
    public long? EmployeeId { get; set; }

    [ForeignKey(nameof(EmployeeId))]
    public virtual User? Employee { get; set; }

    [Column("is_hidden")]
    public bool IsHidden { get; set; } = false;
}
