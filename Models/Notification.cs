using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("notifications")]
public class Notification
{
    [Key]
    [Column("notification_id")]
    public long NotificationId { get; set; }

    [Column("user_id")]
    public long? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [Required]
    [MaxLength(150)]
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    [Column("type")]
    public string Type { get; set; } = "SYSTEM"; // APPOINTMENT_REMINDER, BIRTHDAY_REMINDER, SYSTEM, CANCEL_ALERT

    [Column("reference_id")]
    public long? ReferenceId { get; set; }

    [MaxLength(30)]
    [Column("reference_type")]
    public string? ReferenceType { get; set; } // CUSTOMER, APPOINTMENT, SERVICE, SYSTEM

    [Column("is_read")]
    public bool IsRead { get; set; } = false;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
