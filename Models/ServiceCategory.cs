using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("service_categories")]
public class ServiceCategory
{
    [Key]
    [Column("category_id")]
    public int CategoryId { get; set; }

    [Required]
    [MaxLength(50)]
    [Column("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    [MaxLength(50)]
    [Column("default_icon")]
    public string DefaultIcon { get; set; } = "fa-spa";

    [MaxLength(30)]
    [Column("default_bg_color")]
    public string DefaultBgColor { get; set; } = "bg-purple-500";

    [Column("display_order")]
    public int DisplayOrder { get; set; } = 0;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
