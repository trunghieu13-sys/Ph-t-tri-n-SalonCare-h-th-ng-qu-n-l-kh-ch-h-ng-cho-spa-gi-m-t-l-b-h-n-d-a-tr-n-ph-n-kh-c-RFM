using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("audit_logs")]
public class AuditLog
{
    [Key]
    [Column("log_id")]
    public long LogId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("module_name")]
    public string ModuleName { get; set; } = string.Empty; // CUSTOMERS, APPOINTMENTS, SERVICES, STAFF, CUSTOMER_CARE, REPORTS, SETTINGS, AUTH

    [Required]
    [MaxLength(20)]
    [Column("action_type")]
    public string ActionType { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, LOGIN, EXPORT

    [Required]
    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [MaxLength(50)]
    [Column("entity_id")]
    public string? EntityId { get; set; }

    [MaxLength(50)]
    [Column("ip_address")]
    public string? IpAddress { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
