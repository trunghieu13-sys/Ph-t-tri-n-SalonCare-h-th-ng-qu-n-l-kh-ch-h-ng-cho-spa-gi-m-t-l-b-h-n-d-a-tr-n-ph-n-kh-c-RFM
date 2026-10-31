using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("customer_interactions")]
public class CustomerInteraction
{
    [Key]
    [Column("interaction_id")]
    public long InteractionId { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("rule_id")]
    public int? RuleId { get; set; }

    [Column("campaign_id")]
    public long? CampaignId { get; set; }

    [Required]
    [StringLength(20)]
    [Column("channel")]
    public string Channel { get; set; } = "SMS"; // EMAIL, SMS, ZALO

    [Required]
    [StringLength(100)]
    [Column("recipient")]
    public string Recipient { get; set; } = string.Empty;

    [Required]
    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Column("status")]
    public string Status { get; set; } = "PENDING"; // SUCCESS, FAILED, PENDING

    [Column("sent_at")]
    public DateTime SentAt { get; set; } = DateTime.Now;

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    // Navigation properties
    [ForeignKey(nameof(CustomerId))]
    public Customer? Customer { get; set; }

    [ForeignKey(nameof(RuleId))]
    public AutomationRule? Rule { get; set; }

    [ForeignKey(nameof(CampaignId))]
    public Campaign? Campaign { get; set; }
}
