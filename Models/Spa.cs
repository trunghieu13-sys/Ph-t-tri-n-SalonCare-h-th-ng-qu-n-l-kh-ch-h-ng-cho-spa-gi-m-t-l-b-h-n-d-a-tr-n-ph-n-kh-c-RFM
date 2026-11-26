using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("spas")]
public class Spa
{
    [Key]
    [Column("spa_id")]
    public long SpaId { get; set; }

    [Required]
    [MaxLength(150)]
    [Column("spa_name")]
    public string SpaName { get; set; } = string.Empty;

    [Column("owner_user_id")]
    public long OwnerUserId { get; set; }

    [ForeignKey(nameof(OwnerUserId))]
    public virtual User? OwnerUser { get; set; }

    [MaxLength(255)]
    [Column("address")]
    public string? Address { get; set; }

    [MaxLength(15)]
    [Column("phone")]
    public string? Phone { get; set; }

    [MaxLength(20)]
    [Column("status")]
    public string Status { get; set; } = "active";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<SpaInvitation> Invitations { get; set; } = new List<SpaInvitation>();
}
