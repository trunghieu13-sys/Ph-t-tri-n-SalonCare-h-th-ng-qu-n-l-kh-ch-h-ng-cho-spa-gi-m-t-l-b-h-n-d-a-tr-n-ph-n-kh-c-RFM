using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM_V2.Models;

[Table("spa_invitations")]
public class SpaInvitation
{
    [Key]
    [Column("invitation_id")]
    public long InvitationId { get; set; }

    [Column("spa_id")]
    public long SpaId { get; set; }

    [ForeignKey(nameof(SpaId))]
    public virtual Spa? Spa { get; set; }

    [Column("inviter_user_id")]
    public long InviterUserId { get; set; }

    [ForeignKey(nameof(InviterUserId))]
    public virtual User? InviterUser { get; set; }

    [Column("invitee_user_id")]
    public long InviteeUserId { get; set; }

    [ForeignKey(nameof(InviteeUserId))]
    public virtual User? InviteeUser { get; set; }

    [MaxLength(20)]
    [Column("status")]
    public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column("responded_at")]
    public DateTime? RespondedAt { get; set; }
}
