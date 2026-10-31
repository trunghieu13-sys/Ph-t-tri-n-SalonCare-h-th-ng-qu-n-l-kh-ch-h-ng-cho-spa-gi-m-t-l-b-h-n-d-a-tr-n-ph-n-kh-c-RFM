using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("automation_rules")]
public class AutomationRule
{
    [Key]
    [Column("rule_id")]
    public int RuleId { get; set; }

    [Required]
    [StringLength(100)]
    [Column("rule_name")]
    public string RuleName { get; set; } = string.Empty;

    [StringLength(500)]
    [Column("description")]
    public string? Description { get; set; }

    [Required]
    [StringLength(20)]
    [Column("channel")]
    public string Channel { get; set; } = "SMS"; // EMAIL, SMS, ZALO

    [Required]
    [StringLength(50)]
    [Column("trigger_type")]
    public string TriggerType { get; set; } = "BEFORE_APPOINTMENT"; // BEFORE_APPOINTMENT, BIRTHDAY, AFTER_LAST_VISIT

    [Column("trigger_offset_minutes")]
    public int TriggerOffsetMinutes { get; set; } = 0;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Required]
    [Column("message_template")]
    public string MessageTemplate { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
