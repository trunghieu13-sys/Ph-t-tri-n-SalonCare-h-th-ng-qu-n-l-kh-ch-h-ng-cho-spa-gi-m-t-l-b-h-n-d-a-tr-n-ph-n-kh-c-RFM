using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("campaigns")]
public class Campaign
{
    [Key]
    [Column("campaign_id")]
    public long CampaignId { get; set; }

    [Required]
    [StringLength(150)]
    [Column("campaign_name")]
    public string CampaignName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Column("channel")]
    public string Channel { get; set; } = "SMS"; // EMAIL, SMS

    [Required]
    [StringLength(50)]
    [Column("target_group")]
    public string TargetGroup { get; set; } = "ALL"; // ALL, VIP, Than_thiet, Moi, INACTIVE

    [Required]
    [Column("message_content")]
    public string MessageContent { get; set; } = string.Empty;

    [Column("total_recipients")]
    public int TotalRecipients { get; set; } = 0;

    [Column("success_count")]
    public int SuccessCount { get; set; } = 0;

    [Column("failed_count")]
    public int FailedCount { get; set; } = 0;

    [Required]
    [StringLength(20)]
    [Column("status")]
    public string Status { get; set; } = "PENDING"; // PENDING, PROCESSING, COMPLETED, FAILED

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
