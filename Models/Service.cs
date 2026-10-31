using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("services")]
public class Service
{
    [Key]
    [Column("service_id")]
    public int ServiceId { get; set; }

    [Required]
    [MaxLength(150)]
    [Column("service_name")]
    public string ServiceName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Column("category_name")]
    public string CategoryName { get; set; } = "Chăm sóc da";

    // Alias tương thích ngược cho các module đang gọi s.Category
    [NotMapped]
    public string? Category
    {
        get => CategoryName;
        set => CategoryName = string.IsNullOrWhiteSpace(value) ? "Chăm sóc da" : value;
    }

    [Column("duration_minutes")]
    public int DurationMinutes { get; set; } = 60;

    [Column("price", TypeName = "decimal(12, 2)")]
    public decimal Price { get; set; } = 0.00m;

    [Column("description")]
    public string? Description { get; set; }

    [MaxLength(50)]
    [Column("icon_class")]
    public string IconClass { get; set; } = "fa-spa";

    [MaxLength(30)]
    [Column("bg_color")]
    public string BgColor { get; set; } = "bg-purple-500";

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public virtual ICollection<AppointmentService> AppointmentServices { get; set; } = new List<AppointmentService>();
}
