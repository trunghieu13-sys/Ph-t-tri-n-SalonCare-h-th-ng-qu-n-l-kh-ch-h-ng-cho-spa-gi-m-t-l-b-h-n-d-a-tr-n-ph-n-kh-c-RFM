using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("system_configs")]
public class SystemConfig
{
    [Key]
    [MaxLength(100)]
    [Column("config_key")]
    public string ConfigKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    [Column("config_value")]
    public string ConfigValue { get; set; } = string.Empty;

    [MaxLength(500)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
