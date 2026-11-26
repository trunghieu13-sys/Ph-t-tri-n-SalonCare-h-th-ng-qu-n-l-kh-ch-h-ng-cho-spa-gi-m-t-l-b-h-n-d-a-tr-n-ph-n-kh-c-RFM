using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class StaffService : IStaffService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<StaffService> _logger;

    public StaffService(
        ApplicationDbContext context,
        ILogger<StaffService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _context = context;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    private TenantResolution ResolveCurrentTenant()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        return TenantContext.Resolve(httpContext?.User, _context, httpContext);
    }

    public async Task<List<StaffDto>> GetStaffListAsync(string? search = null, int? roleId = null, string? status = null)
    {
        var tenant = ResolveCurrentTenant();
        if (tenant.IsUnassignedTenantUser)
        {
            return new List<StaffDto>();
        }

        var query = _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.Status != "resigned");

        if (!tenant.IsAdminOrUnscoped && tenant.SpaId.HasValue)
        {
            query = query.Where(u => u.SpaId == tenant.SpaId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(s) ||
                u.Username.ToLower().Contains(s) ||
                (u.Phone != null && u.Phone.Contains(s)) ||
                u.Email.ToLower().Contains(s) ||
                (u.Specialty != null && u.Specialty.ToLower().Contains(s)));
        }

        if (roleId.HasValue && roleId.Value > 0)
        {
            query = query.Where(u => u.RoleId == roleId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.Status == status);
        }

        var users = await query
            .OrderBy(u => u.RoleId)
            .ThenBy(u => u.FullName)
            .ToListAsync();

        // Lấy số lượng lịch hẹn đã phụ trách cho từng nhân viên
        var userIds = users.Select(u => u.UserId).ToList();
        var apptCounts = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.EmployeeId.HasValue && userIds.Contains(a.EmployeeId.Value))
            .GroupBy(a => a.EmployeeId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        return users.Select(u =>
        {
            var roleName = u.Role?.RoleName?.ToLower() ?? "staff";
            string roleDisplayName = roleName switch
            {
                "admin" => "Quản trị viên (Admin)",
                "owner" => "Chủ Spa (Owner)",
                _ => "Kỹ thuật viên (Staff)"
            };

            var apptCount = apptCounts.TryGetValue(u.UserId, out var c) ? c : 0;

            return new StaffDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Username = u.Username,
                Email = u.Email,
                Phone = u.Phone,
                Specialty = string.IsNullOrWhiteSpace(u.Specialty) ? "Chăm sóc & Phục vụ tổng hợp" : u.Specialty,
                RoleId = u.RoleId,
                RoleName = roleName,
                RoleDisplayName = roleDisplayName,
                AvatarUrl = string.IsNullOrWhiteSpace(u.AvatarUrl)
                    ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(u.FullName)}&background=7C3AED&color=fff&size=150"
                    : u.AvatarUrl,
                Status = u.Status,
                CreatedAt = u.CreatedAt,
                AppointmentsCount = apptCount,
                Rating = 5.0m
            };
        }).ToList();
    }

    public async Task<StaffDto?> GetStaffByIdAsync(long userId)
    {
        var u = await _context.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (u == null) return null;

        var roleName = u.Role?.RoleName?.ToLower() ?? "staff";
        string roleDisplayName = roleName switch
        {
            "admin" => "Quản trị viên (Admin)",
            "owner" => "Chủ Spa (Owner)",
            _ => "Kỹ thuật viên (Staff)"
        };

        var apptCount = await _context.Appointments
            .AsNoTracking()
            .CountAsync(a => a.EmployeeId == u.UserId);

        return new StaffDto
        {
            UserId = u.UserId,
            FullName = u.FullName,
            Username = u.Username,
            Email = u.Email,
            Phone = u.Phone,
            Specialty = u.Specialty,
            RoleId = u.RoleId,
            RoleName = roleName,
            RoleDisplayName = roleDisplayName,
            AvatarUrl = u.AvatarUrl,
            Status = u.Status,
            CreatedAt = u.CreatedAt,
            AppointmentsCount = apptCount,
            Rating = 5.0m
        };
    }

    public async Task<(bool Success, string Message, long? UserId)> CreateStaffAsync(CreateStaffDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return (false, "Vui lòng nhập họ và tên nhân viên.", null);

            if (string.IsNullOrWhiteSpace(dto.Username))
                return (false, "Vui lòng nhập tên đăng nhập.", null);

            if (string.IsNullOrWhiteSpace(dto.Email))
                return (false, "Vui lòng nhập email nhân viên.", null);

            if (string.IsNullOrWhiteSpace(dto.Phone))
                return (false, "Vui lòng nhập số điện thoại.", null);

            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
                return (false, "Mật khẩu phải có ít nhất 6 ký tự.", null);

            var cleanUsername = dto.Username.Trim().ToLower();
            var cleanEmail = dto.Email.Trim().ToLower();

            // Check duplicate
            var userExists = await _context.Users.AnyAsync(u => u.Username.ToLower() == cleanUsername);
            if (userExists)
                return (false, $"Tên đăng nhập '{dto.Username}' đã được sử dụng.", null);

            var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == cleanEmail);
            if (emailExists)
                return (false, $"Email '{dto.Email}' đã được đăng ký.", null);

            // Hash password bằng BCrypt
            var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password.Trim());

            var defaultAvatar = string.IsNullOrWhiteSpace(dto.AvatarUrl)
                ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(dto.FullName)}&background=7C3AED&color=fff&size=150"
                : dto.AvatarUrl.Trim();

            var tenant = ResolveCurrentTenant();
            var newStaff = new User
            {
                FullName = dto.FullName.Trim(),
                Username = cleanUsername,
                Email = cleanEmail,
                Phone = dto.Phone.Trim(),
                PasswordHash = hash,
                RoleId = dto.RoleId is >= 1 and <= 3 ? dto.RoleId : 3,
                SpaId = tenant.SpaId,
                Specialty = string.IsNullOrWhiteSpace(dto.Specialty) ? "Chăm sóc da & Trị liệu" : dto.Specialty.Trim(),
                AvatarUrl = defaultAvatar,
                Status = "active",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.Users.Add(newStaff);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Đã thêm thành công nhân viên mới: {FullName} ({Username}), Role: {RoleId}", newStaff.FullName, newStaff.Username, newStaff.RoleId);
            return (true, "Thêm nhân viên mới thành công!", newStaff.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi thêm nhân viên mới: {Message}", ex.Message);
            return (false, "Lỗi máy chủ khi tạo nhân viên: " + ex.Message, null);
        }
    }

    public async Task<(bool Success, string Message)> UpdateRoleAsync(long userId, int newRoleId)
    {
        try
        {
            if (newRoleId is < 1 or > 3)
                return (false, "Vai trò được chọn không hợp lệ (Chỉ chấp nhận Admin, Owner hoặc Staff).");

            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Status == "resigned")
                return (false, "Không tìm thấy tài khoản nhân viên.");

            var oldRoleId = user.RoleId;
            user.RoleId = newRoleId;
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            string roleText = newRoleId switch
            {
                1 => "Quản trị viên (Admin)",
                2 => "Chủ Spa (Owner)",
                _ => "Nhân viên Kỹ thuật (Staff)"
            };

            _logger.LogInformation("Phân quyền thành công: UserId {UserId} từ Role {OldRole} -> Role {NewRole}", userId, oldRoleId, newRoleId);
            return (true, $"Đã cập nhật vai trò nhân viên '{user.FullName}' thành '{roleText}'. Quyền hạn mới sẽ áp dụng ngay lập tức!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật vai trò nhân viên: {Message}", ex.Message);
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> UpdateSpecialtyAsync(long userId, string specialty)
    {
        try
        {
            if (userId <= 0)
                return (false, "Mã nhân viên không hợp lệ.");

            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Status == "resigned")
                return (false, "Không tìm thấy tài khoản nhân viên.");

            var cleanSpecialty = string.IsNullOrWhiteSpace(specialty)
                ? "Chăm sóc & Phục vụ tổng hợp"
                : specialty.Trim();

            user.Specialty = cleanSpecialty;
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Cập nhật chuyên môn thành công: UserId {UserId} ({FullName}) -> {Specialty}", userId, user.FullName, cleanSpecialty);
            return (true, $"Đã cập nhật chuyên môn cho nhân viên '{user.FullName}' thành '{cleanSpecialty}'!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật chuyên môn nhân viên: {Message}", ex.Message);
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message, string NewStatus)> ToggleStatusAsync(long userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Status == "resigned")
                return (false, "Không tìm thấy tài khoản nhân viên.", "");

            var newStatus = user.Status == "active" ? "locked" : "active";
            user.Status = newStatus;
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            string msg = newStatus == "active"
                ? $"Đã mở khóa tài khoản của '{user.FullName}'."
                : $"Đã tạm khóa tài khoản của '{user.FullName}'. Nhân viên sẽ không thể đăng nhập.";

            return (true, msg, newStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi khóa/mở khóa nhân viên: {Message}", ex.Message);
            return (false, "Lỗi máy chủ: " + ex.Message, "");
        }
    }

    public async Task<(bool Success, string Message)> DeleteStaffAsync(long userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return (false, "Không tìm thấy tài khoản nhân viên.");

            // Soft delete
            user.Status = "resigned";
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return (true, $"Đã cập nhật trạng thái nhân viên '{user.FullName}' thành 'Đã nghỉ việc'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa nhân viên: {Message}", ex.Message);
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    // =========================================================================
    // MỜI NHÂN VIÊN VÀO ĐỘI NGŨ SPA
    // =========================================================================
    public async Task<List<StaffInviteSearchItemDto>> SearchStaffForInvitationAsync(string? query)
    {
        var tenant = ResolveCurrentTenant();
        if (!tenant.SpaId.HasValue || string.IsNullOrWhiteSpace(query))
            return new List<StaffInviteSearchItemDto>();

        var spaId = tenant.SpaId.Value;
        var kw = query.Trim().ToLower();

        var users = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.Role != null && u.Role.RoleName == "staff" && u.Status == "active" &&
                        (u.FullName.ToLower().Contains(kw) ||
                         u.Username.ToLower().Contains(kw) ||
                         u.Email.ToLower().Contains(kw)))
            .OrderBy(u => u.FullName)
            .Take(10)
            .ToListAsync();

        var ids = users.Select(u => u.UserId).ToList();
        var pendingIds = (await _context.SpaInvitations
            .AsNoTracking()
            .Where(i => i.SpaId == spaId && i.Status == "Pending" && ids.Contains(i.InviteeUserId))
            .Select(i => i.InviteeUserId)
            .ToListAsync()).ToHashSet();

        return users.Select(u => new StaffInviteSearchItemDto
        {
            UserId = u.UserId,
            FullName = u.FullName,
            Username = u.Username,
            Email = u.Email,
            Phone = u.Phone,
            AvatarUrl = string.IsNullOrWhiteSpace(u.AvatarUrl)
                ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(u.FullName)}&background=7C3AED&color=fff&size=150"
                : u.AvatarUrl,
            InviteStatus = u.SpaId == spaId ? "InTeam"
                         : u.SpaId.HasValue ? "OtherSpa"
                         : pendingIds.Contains(u.UserId) ? "Pending"
                         : "NoSpa"
        }).ToList();
    }

    public async Task<(bool Success, string Message)> SendInvitationAsync(long inviteeUserId)
    {
        try
        {
            var tenant = ResolveCurrentTenant();
            if (!tenant.SpaId.HasValue || !tenant.UserId.HasValue)
                return (false, "Tài khoản của bạn chưa gắn với quán Spa nào nên không thể gửi lời mời.");

            var spa = await _context.Spas.FirstOrDefaultAsync(s => s.SpaId == tenant.SpaId.Value);
            if (spa == null || spa.Status != "active")
                return (false, "Quán Spa không tồn tại hoặc đang ngừng hoạt động.");

            var inviter = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == tenant.UserId.Value);
            var invitee = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == inviteeUserId);

            if (invitee == null || invitee.Role?.RoleName != "staff" || invitee.Status != "active")
                return (false, "Chỉ có thể mời tài khoản Nhân viên (staff) đang hoạt động.");

            if (invitee.SpaId == spa.SpaId)
                return (false, $"{invitee.FullName} đã thuộc đội ngũ của quán.");

            if (invitee.SpaId.HasValue)
                return (false, $"{invitee.FullName} đã thuộc một quán Spa khác.");

            var pendingExists = await _context.SpaInvitations.AnyAsync(i =>
                i.SpaId == spa.SpaId && i.InviteeUserId == inviteeUserId && i.Status == "Pending");
            if (pendingExists)
                return (false, $"Đã gửi lời mời cho {invitee.FullName} và đang chờ phản hồi.");

            var invitation = new SpaInvitation
            {
                SpaId = spa.SpaId,
                InviterUserId = tenant.UserId.Value,
                InviteeUserId = inviteeUserId,
                Status = "Pending",
                CreatedAt = DateTime.Now
            };
            _context.SpaInvitations.Add(invitation);
            await _context.SaveChangesAsync();

            var ownerName = inviter?.FullName ?? "Chủ Spa";
            _context.Notifications.Add(new Notification
            {
                UserId = inviteeUserId,
                SpaId = spa.SpaId,
                Title = "Lời mời gia nhập đội ngũ Spa",
                Message = $"Chủ Spa {ownerName} mời bạn tham gia đội ngũ {spa.SpaName}. Vào Tổng quan để Đồng ý hoặc Từ chối.",
                Type = "SPA_INVITATION",
                ReferenceId = invitation.InvitationId,
                ReferenceType = "SPA_INVITATION",
                IsRead = false,
                CreatedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();

            return (true, $"Đã gửi lời mời tới {invitee.FullName}. Đang chờ nhân viên phản hồi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi lời mời nhân viên");
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> RemoveFromTeamAsync(long userId)
    {
        try
        {
            var tenant = ResolveCurrentTenant();
            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return (false, "Không tìm thấy nhân viên.");

            if (!tenant.IsAdminOrUnscoped && user.SpaId != tenant.SpaId)
                return (false, "Nhân viên này không thuộc đội ngũ của quán bạn.");

            if (!user.SpaId.HasValue)
                return (false, "Nhân viên này hiện không thuộc quán nào.");

            if (user.UserId == tenant.UserId)
                return (false, "Bạn không thể tự xóa chính mình khỏi đội ngũ.");

            var isSpaOwner = await _context.Spas.AnyAsync(s => s.SpaId == user.SpaId && s.OwnerUserId == user.UserId);
            if (isSpaOwner)
                return (false, "Không thể xóa Chủ Spa khỏi quán của chính họ.");

            user.SpaId = null;
            user.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return (true, $"Đã mời '{user.FullName}' ra khỏi đội ngũ. Tài khoản này sẽ không còn thấy dữ liệu của quán.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa nhân viên khỏi đội ngũ");
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    public async Task<List<MyInvitationDto>> GetMyPendingInvitationsAsync(long userId)
    {
        return await _context.SpaInvitations
            .AsNoTracking()
            .Where(i => i.InviteeUserId == userId && i.Status == "Pending")
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new MyInvitationDto
            {
                InvitationId = i.InvitationId,
                SpaId = i.SpaId,
                SpaName = i.Spa != null ? i.Spa.SpaName : "Spa",
                OwnerName = i.InviterUser != null ? i.InviterUser.FullName : "Chủ Spa",
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<(bool Success, string Message, long? SpaId)> AcceptInvitationAsync(long invitationId, long userId)
    {
        try
        {
            var invitation = await _context.SpaInvitations
                .Include(i => i.Spa)
                .FirstOrDefaultAsync(i => i.InvitationId == invitationId && i.InviteeUserId == userId);

            if (invitation == null)
                return (false, "Không tìm thấy lời mời.", null);

            if (invitation.Status != "Pending")
                return (false, "Lời mời này đã được xử lý trước đó.", null);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return (false, "Không tìm thấy tài khoản.", null);

            if (user.SpaId.HasValue)
                return (false, "Bạn đã thuộc một đội ngũ Spa khác.", null);

            if (invitation.Spa == null || invitation.Spa.Status != "active")
                return (false, "Quán Spa không còn hoạt động.", null);

            invitation.Status = "Accepted";
            invitation.RespondedAt = DateTime.Now;
            user.SpaId = invitation.SpaId;
            user.UpdatedAt = DateTime.Now;

            // Các lời mời Pending còn lại của nhân viên này không còn hiệu lực
            var others = await _context.SpaInvitations
                .Where(i => i.InviteeUserId == userId && i.Status == "Pending" && i.InvitationId != invitationId)
                .ToListAsync();
            foreach (var o in others)
            {
                o.Status = "Rejected";
                o.RespondedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return (true, $"Bạn đã gia nhập đội ngũ {invitation.Spa.SpaName}!", invitation.SpaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi chấp nhận lời mời");
            return (false, "Lỗi máy chủ: " + ex.Message, null);
        }
    }

    public async Task<(bool Success, string Message)> RejectInvitationAsync(long invitationId, long userId)
    {
        try
        {
            var invitation = await _context.SpaInvitations
                .FirstOrDefaultAsync(i => i.InvitationId == invitationId && i.InviteeUserId == userId);

            if (invitation == null)
                return (false, "Không tìm thấy lời mời.");

            if (invitation.Status != "Pending")
                return (false, "Lời mời này đã được xử lý trước đó.");

            invitation.Status = "Rejected";
            invitation.RespondedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return (true, "Đã từ chối lời mời.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi từ chối lời mời");
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    public List<RolePermissionInfo> GetRolePermissionsMatrix()
    {
        return new List<RolePermissionInfo>
        {
            new()
            {
                RoleId = 1,
                RoleName = "admin",
                DisplayName = "Quản trị viên (Admin)",
                ColorBadge = "bg-rose-500/20 text-rose-400 border-rose-500/30",
                Description = "Toàn quyền cấu hình bảo mật, phân quyền tài khoản, quản lý dữ liệu và hệ thống.",
                Permissions = new List<PermissionModuleItem>
                {
                    new() { ModuleName = "Quản lý Khách hàng", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Toàn quyền" },
                    new() { ModuleName = "Lịch hẹn & Tiếp đón", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Toàn quyền" },
                    new() { ModuleName = "Danh mục Dịch vụ", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Toàn quyền" },
                    new() { ModuleName = "Báo cáo Doanh thu & KPIs", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Toàn quyền" },
                    new() { ModuleName = "Quản lý Đội ngũ & Phân quyền", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Nâng / hạ cấp vai trò" },
                    new() { ModuleName = "Cài đặt & Tích hợp Gateway", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Cấu hình Email/SMS" }
                }
            },
            new()
            {
                RoleId = 2,
                RoleName = "owner",
                DisplayName = "Chủ Spa (Owner)",
                ColorBadge = "bg-purple-500/20 text-purple-400 border-purple-500/30",
                Description = "Quản lý kinh doanh, doanh thu tài chính, chăm sóc khách hàng, marketing và kiểm soát nhân sự.",
                Permissions = new List<PermissionModuleItem>
                {
                    new() { ModuleName = "Quản lý Khách hàng", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Toàn quyền" },
                    new() { ModuleName = "Lịch hẹn & Tiếp đón", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Toàn quyền" },
                    new() { ModuleName = "Danh mục Dịch vụ", CanView = true, CanCreate = true, CanEdit = true, CanDelete = true, Note = "Thêm / sửa bảng giá" },
                    new() { ModuleName = "Báo cáo Doanh thu & KPIs", CanView = true, CanCreate = true, CanEdit = true, CanDelete = false, Note = "Xem & Xuất Excel" },
                    new() { ModuleName = "Quản lý Đội ngũ & Phân quyền", CanView = true, CanCreate = true, CanEdit = true, CanDelete = false, Note = "Thêm NV & Phân công" },
                    new() { ModuleName = "Cài đặt & Tích hợp Gateway", CanView = true, CanCreate = false, CanEdit = true, CanDelete = false, Note = "Xem kịch bản CSKH" }
                }
            },
            new()
            {
                RoleId = 3,
                RoleName = "staff",
                DisplayName = "Nhân viên Kỹ thuật (Staff)",
                ColorBadge = "bg-blue-500/20 text-blue-400 border-blue-500/30",
                Description = "Tiếp nhận lịch hẹn được phân công, thực hiện quy trình dịch vụ cho khách hàng, xem thông tin chăm sóc.",
                Permissions = new List<PermissionModuleItem>
                {
                    new() { ModuleName = "Quản lý Khách hàng", CanView = true, CanCreate = true, CanEdit = true, CanDelete = false, Note = "Xem hồ sơ & lịch sử" },
                    new() { ModuleName = "Lịch hẹn & Tiếp đón", CanView = true, CanCreate = true, CanEdit = true, CanDelete = false, Note = "Check-in & Hoàn thành" },
                    new() { ModuleName = "Danh mục Dịch vụ", CanView = true, CanCreate = false, CanEdit = false, CanDelete = false, Note = "Chỉ xem bảng giá" },
                    new() { ModuleName = "Báo cáo Doanh thu & KPIs", CanView = false, CanCreate = false, CanEdit = false, CanDelete = false, Note = "Không có quyền" },
                    new() { ModuleName = "Quản lý Đội ngũ & Phân quyền", CanView = false, CanCreate = false, CanEdit = false, CanDelete = false, Note = "Không có quyền" },
                    new() { ModuleName = "Cài đặt & Tích hợp Gateway", CanView = false, CanCreate = false, CanEdit = false, CanDelete = false, Note = "Không có quyền" }
                }
            }
        };
    }
}
