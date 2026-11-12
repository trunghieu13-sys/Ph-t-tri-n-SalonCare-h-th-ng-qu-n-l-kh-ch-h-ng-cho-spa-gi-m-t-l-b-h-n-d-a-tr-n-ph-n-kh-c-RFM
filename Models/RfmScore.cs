using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("rfm_scores")]
public class RfmScore
{
    [Key]
    [Column("customer_id")]
    public long CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    [Column("recency_days")]
    public int RecencyDays { get; set; } = 999;

    [Column("frequency_count")]
    public int FrequencyCount { get; set; } = 0;

    [Column("monetary_total", TypeName = "decimal(18,2)")]
    public decimal MonetaryTotal { get; set; } = 0.00m;

    [Column("no_show_count")]
    public int NoShowCount { get; set; } = 0;

    [Required]
    [MaxLength(30)]
    [Column("segment_group")]
    public string SegmentGroup { get; set; } = "New"; // VIP, Loyal, AtRisk, New

    [Column("is_no_show_risk")]
    public bool IsNoShowRisk { get; set; } = false;

    [Required]
    [MaxLength(1000)]
    [Column("reason_text")]
    public string ReasonText { get; set; } = string.Empty;

    [Column("last_calculated_at")]
    public DateTime LastCalculatedAt { get; set; } = DateTime.Now;
}
