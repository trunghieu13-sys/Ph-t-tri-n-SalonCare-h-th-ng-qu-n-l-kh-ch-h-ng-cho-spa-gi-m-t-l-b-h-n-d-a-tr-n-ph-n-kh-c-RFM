using System.Text.Json.Serialization;

namespace CRM_V2.Services;

public interface IStaffService
{
    Task<List<StaffDto>> GetStaffListAsync(string? search = null, int? roleId = null, string? status = null);
    Task<StaffDto?> GetStaffByIdAsync(long userId);
    Task<(bool Success, string Message, long? UserId)> CreateStaffAsync(CreateStaffDto dto);
    Task<(bool Success, string Message)> UpdateRoleAsync(long userId, int newRoleId);
    Task<(bool Success, string Message)> UpdateSpecialtyAsync(long userId, string specialty);
    Task<(bool Success, string Message, string NewStatus)> ToggleStatusAsync(long userId);
    Task<(bool Success, string Message)> DeleteStaffAsync(long userId);
    List<RolePermissionInfo> GetRolePermissionsMatrix();

    // ===== Mời nhân viên vào đội ngũ Spa (Multi-Tenant) =====
    Task<List<StaffInviteSearchItemDto>> SearchStaffForInvitationAsync(string? query);
    Task<(bool Success, string Message)> SendInvitationAsync(long inviteeUserId);
    Task<(bool Success, string Message)> RemoveFromTeamAsync(long userId);
    Task<List<MyInvitationDto>> GetMyPendingInvitationsAsync(long userId);
    Task<(bool Success, string Message, long? SpaId)> AcceptInvitationAsync(long invitationId, long userId);
    Task<(bool Success, string Message)> RejectInvitationAsync(long invitationId, long userId);
}

public class StaffInviteSearchItemDto
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>NoSpa (có thể mời) | Pending (đang chờ phản hồi) | InTeam (đã thuộc đội ngũ) | OtherSpa (thuộc quán khác)</summary>
    public string InviteStatus { get; set; } = "NoSpa";
}

public class MyInvitationDto
{
    public long InvitationId { get; set; }
    public long SpaId { get; set; }
    public string SpaName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class InvitationRequest
{
    [JsonPropertyName("inviteeUserId")]
    public long InviteeUserId { get; set; }
}

public class InvitationResponseRequest
{
    [JsonPropertyName("invitationId")]
    public long InvitationId { get; set; }
}

public class RemoveFromTeamRequest
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }
}

public class StaffDto
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Specialty { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string RoleDisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public int AppointmentsCount { get; set; }
    public decimal Rating { get; set; } = 5.0m;
}

public class CreateStaffDto
{
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("specialty")]
    public string? Specialty { get; set; }

    [JsonPropertyName("roleId")]
    public int RoleId { get; set; } = 3; // Default: Staff

    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }
}

public class UpdateRoleRequest
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("newRoleId")]
    public int NewRoleId { get; set; }
}

public class UpdateSpecialtyRequest
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("specialty")]
    public string Specialty { get; set; } = string.Empty;
}

public class RolePermissionInfo
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ColorBadge { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<PermissionModuleItem> Permissions { get; set; } = new();
}

public class PermissionModuleItem
{
    public string ModuleName { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public string Note { get; set; } = string.Empty;
}
