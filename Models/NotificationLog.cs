using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("notification_logs")]
public class NotificationLog
{
    [Key]
    [Column("log_id")]
    public long LogId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    [Column("appointment_id")]
    public long? AppointmentId { get; set; }

    [ForeignKey(nameof(AppointmentId))]
    public virtual Appointment? Appointment { get; set; }

    [Required]
    [MaxLength(30)]
    [Column("channel")]
    public string Channel { get; set; } = "ZaloOA"; // ZaloOA, Email, SMS

    [Required]
    [MaxLength(30)]
    [Column("message_type")]
    public string MessageType { get; set; } = "Reminder24h"; // Reminder24h, Reminder2h, WinBackPromo

    [Required]
    [MaxLength(30)]
    [Column("segment_at_send_time")]
    public string SegmentAtSendTime { get; set; } = "New"; // VIP, Loyal, AtRisk, New

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    [Column("status")]
    public string Status { get; set; } = "Sent"; // Sent, Failed, ConfirmedByCustomer, CanceledByCustomer

    [MaxLength(100)]
    [Column("external_message_id")]
    public string? ExternalMessageId { get; set; }

    [Column("payload_json")]
    public string? PayloadJson { get; set; }

    [MaxLength(500)]
    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("sent_at")]
    public DateTime SentAt { get; set; } = DateTime.Now;
}
