using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("customers")]
public class Customer
{
    [Key]
    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("full_name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)]
    [Column("phone")]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(100)]
    [EmailAddress]
    [Column("email")]
    public string? Email { get; set; }

    [MaxLength(20)]
    [Column("gender")]
    public string? Gender { get; set; }

    [Column("date_of_birth", TypeName = "date")]
    public DateTime? DateOfBirth { get; set; }

    [MaxLength(255)]
    [Column("address")]
    public string? Address { get; set; }

    [MaxLength(500)]
    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [MaxLength(500)]
    [Column("customer_source")]
    public string CustomerSource { get; set; } = "Truc_tiep";

    [MaxLength(30)]
    [Column("customer_type")]
    public string CustomerType { get; set; } = "Moi"; // Moi, Than_thiet, VIP

    [MaxLength(20)]
    [Column("customer_status")]
    public string CustomerStatus { get; set; } = "Hoat_dong"; // Hoat_dong, Ngung_hoat_dong

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Nhóm thông tin tổng hợp CRM Analytics
    [Column("last_visit_at")]
    public DateTime? LastVisitAt { get; set; }

    [Column("total_visits")]
    public int TotalVisits { get; set; } = 0;

    [Column("total_spent", TypeName = "decimal(18,2)")]
    public decimal TotalSpent { get; set; } = 0.00m;

    [Column("last_booking_at")]
    public DateTime? LastBookingAt { get; set; }

    [Column("no_show_count")]
    public int NoShowCount { get; set; } = 0;
}
