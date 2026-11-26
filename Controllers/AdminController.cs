using System.Security.Claims;
using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

/// <summary>
/// Phân hệ Quản trị Toàn quyền dành riêng cho Admin hệ thống (AdminController)
/// Quản lý toàn bộ người dùng (users) và danh sách các quán Spa (spas).
/// </summary>
[Authorize(Roles = "Admin,admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AdminController> _logger;

    public AdminController(ApplicationDbContext context, ILogger<AdminController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private bool IsForbiddenForCurrentUser()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return false;

        return !User.IsInRole("Admin") && !User.IsInRole("admin");
    }

    private ObjectResult ForbiddenResult() =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            success = false,
            code = "FORBIDDEN",
            message = "403 Forbidden: Chỉ Quản trị viên Hệ thống (Admin) mới có quyền truy cập phân hệ Quản trị hệ thống!"
        });

    /// <summary>
    /// GET /Admin/Index
    /// Trang độc lập Quản trị hệ thống (nếu truy cập trực tiếp qua URL /Admin)
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        return RedirectToAction("Index", "Home", new { view = "adminSystemView" });
    }

    /// <summary>
    /// GET /Admin/GetSystemOverview
    /// Lấy toàn bộ danh sách người dùng (users) và danh sách các quán Spa (spas) trên hệ thống
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSystemOverview(string? userSearch = null, string? spaSearch = null)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        try
        {
            var usersQuery = _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Include(u => u.Spa)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(userSearch))
            {
                var kw = userSearch.Trim().ToLower();
                usersQuery = usersQuery.Where(u =>
                    u.FullName.ToLower().Contains(kw) ||
                    u.Username.ToLower().Contains(kw) ||
                    u.Email.ToLower().Contains(kw) ||
                    (u.Phone != null && u.Phone.Contains(kw)));
            }

            var users = await usersQuery
                .OrderBy(u => u.RoleId)
                .ThenByDescending(u => u.UserId)
                .Select(u => new
                {
                    userId = u.UserId,
                    fullName = u.FullName,
                    username = u.Username,
                    email = u.Email,
                    phone = u.Phone ?? "",
                    specialty = u.Specialty ?? "",
                    roleId = u.RoleId,
                    roleName = u.Role != null ? u.Role.RoleName : "staff",
                    spaId = u.SpaId,
                    spaName = u.Spa != null ? u.Spa.SpaName : "Chưa thuộc Spa nào",
                    status = u.Status,
                    avatarUrl = string.IsNullOrWhiteSpace(u.AvatarUrl)
                        ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(u.FullName)}&background=7C3AED&color=fff&size=120"
                        : u.AvatarUrl,
                    createdAt = u.CreatedAt.ToString("dd/MM/yyyy HH:mm")
                })
                .ToListAsync();

            var spasQuery = _context.Spas
                .AsNoTracking()
                .Include(s => s.OwnerUser)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(spaSearch))
            {
                var skw = spaSearch.Trim().ToLower();
                spasQuery = spasQuery.Where(s =>
                    s.SpaName.ToLower().Contains(skw) ||
                    (s.Phone != null && s.Phone.Contains(skw)) ||
                    (s.Address != null && s.Address.ToLower().Contains(skw)) ||
                    (s.OwnerUser != null && s.OwnerUser.FullName.ToLower().Contains(skw)));
            }

            var rawSpas = await spasQuery
                .OrderByDescending(s => s.SpaId)
                .ToListAsync();

            var memberCounts = await _context.Users
                .AsNoTracking()
                .Where(u => u.SpaId.HasValue && u.Status != "resigned")
                .GroupBy(u => u.SpaId!.Value)
                .Select(g => new { SpaId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SpaId, x => x.Count);

            var customerCounts = await _context.Customers
                .AsNoTracking()
                .Where(c => c.SpaId.HasValue)
                .GroupBy(c => c.SpaId!.Value)
                .Select(g => new { SpaId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SpaId, x => x.Count);

            var apptCounts = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.SpaId.HasValue)
                .GroupBy(a => a.SpaId!.Value)
                .Select(g => new { SpaId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SpaId, x => x.Count);

            var spas = rawSpas.Select(s => new
            {
                spaId = s.SpaId,
                spaName = s.SpaName,
                ownerUserId = s.OwnerUserId,
                ownerName = s.OwnerUser?.FullName ?? $"User #{s.OwnerUserId}",
                ownerUsername = s.OwnerUser?.Username ?? "",
                address = s.Address ?? "",
                phone = s.Phone ?? "",
                status = s.Status,
                membersCount = memberCounts.TryGetValue(s.SpaId, out var mc) ? mc : 0,
                customersCount = customerCounts.TryGetValue(s.SpaId, out var cc) ? cc : 0,
                appointmentsCount = apptCounts.TryGetValue(s.SpaId, out var ac) ? ac : 0,
                createdAt = s.CreatedAt.ToString("dd/MM/yyyy HH:mm")
            }).ToList();

            return Json(new
            {
                success = true,
                stats = new
                {
                    totalUsers = users.Count,
                    activeUsers = users.Count(u => u.status == "active"),
                    lockedUsers = users.Count(u => u.status == "locked"),
                    totalSpas = spas.Count,
                    activeSpas = spas.Count(s => s.status == "active")
                },
                users,
                spas
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu Quản trị hệ thống Admin");
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Admin/UpdateUser
    /// Admin chỉnh sửa thông tin người dùng, đổi vai trò (Admin=1 / Owner=2 / Staff=3), gán quán Spa hoặc đổi trạng thái
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateUser([FromBody] AdminUpdateUserDto dto)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        if (dto == null || dto.UserId <= 0)
            return BadRequest(new { success = false, message = "Mã người dùng không hợp lệ." });

        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == dto.UserId);
            if (user == null)
                return NotFound(new { success = false, message = "Không tìm thấy tài khoản người dùng." });

            if (!string.IsNullOrWhiteSpace(dto.FullName))
                user.FullName = dto.FullName.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var cleanEmail = dto.Email.Trim().ToLower();
                var emailTaken = await _context.Users.AnyAsync(u => u.Email == cleanEmail && u.UserId != user.UserId);
                if (emailTaken)
                    return Json(new { success = false, message = $"Email '{cleanEmail}' đã thuộc về tài khoản khác." });
                user.Email = cleanEmail;
            }

            user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            user.Specialty = string.IsNullOrWhiteSpace(dto.Specialty) ? user.Specialty : dto.Specialty.Trim();

            if (dto.RoleId is >= 1 and <= 3)
            {
                user.RoleId = dto.RoleId;
            }

            user.SpaId = dto.SpaId.HasValue && dto.SpaId.Value > 0 ? dto.SpaId.Value : null;

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                user.Status = dto.Status.Trim().ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(dto.NewPassword) && dto.NewPassword.Trim().Length >= 6)
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword.Trim());
            }

            user.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Đã cập nhật tài khoản '{user.FullName}' (@{user.Username}) thành công!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi Admin cập nhật người dùng ID={Id}", dto.UserId);
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Admin/ToggleUserStatus
    /// Admin khóa / mở khóa tài khoản người dùng bất kỳ
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ToggleUserStatus([FromBody] AdminEntityIdDto dto)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        if (dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Mã người dùng không hợp lệ." });

        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == dto.Id);
        if (user == null)
            return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

        user.Status = user.Status == "active" ? "locked" : "active";
        user.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            newStatus = user.Status,
            message = user.Status == "active"
                ? $"Đã mở khóa tài khoản '{user.FullName}' (@{user.Username})."
                : $"Đã khóa tài khoản '{user.FullName}' (@{user.Username})."
        });
    }

    /// <summary>
    /// POST /Admin/DeleteUser
    /// Admin xóa hoặc vô hiệu hóa tài khoản người dùng
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteUser([FromBody] AdminEntityIdDto dto)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        if (dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Mã người dùng không hợp lệ." });

        var currentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(currentIdStr, out var currentUserId) && currentUserId == dto.Id)
        {
            return Json(new { success = false, message = "Bạn không thể tự xóa chính tài khoản Admin đang đăng nhập." });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == dto.Id);
        if (user == null)
            return NotFound(new { success = false, message = "Không tìm thấy người dùng." });

        try
        {
            // Kiểm tra xem user có đang là Owner của Spa nào không
            var ownedSpas = await _context.Spas.Where(s => s.OwnerUserId == user.UserId).ToListAsync();
            if (ownedSpas.Count > 0)
            {
                // Chuyển sang trạng thái resigned và gỡ spa_id để bảo toàn khóa ngoại lịch sử
                user.Status = "resigned";
                user.SpaId = null;
                user.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
                return Json(new
                {
                    success = true,
                    message = $"Tài khoản '{user.FullName}' đang đứng tên Chủ Spa nên đã được chuyển sang trạng thái Đã nghỉ / Ngừng hoạt động."
                });
            }

            // Xóa lời mời liên quan
            var invs = await _context.SpaInvitations
                .Where(i => i.InviterUserId == user.UserId || i.InviteeUserId == user.UserId)
                .ToListAsync();
            if (invs.Count > 0) _context.SpaInvitations.RemoveRange(invs);

            // Gỡ khỏi các lịch hẹn
            var appts = await _context.Appointments.Where(a => a.EmployeeId == user.UserId || a.CreatedBy == user.UserId).ToListAsync();
            foreach (var a in appts)
            {
                if (a.EmployeeId == user.UserId) a.EmployeeId = null;
                if (a.CreatedBy == user.UserId) a.CreatedBy = null;
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Đã xóa tài khoản '{user.FullName}' (@{user.Username}) khỏi hệ thống!"
            });
        }
        catch
        {
            user.Status = "resigned";
            user.SpaId = null;
            user.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Json(new
            {
                success = true,
                message = $"Đã vô hiệu hóa tài khoản '{user.FullName}' (@{user.Username})."
            });
        }
    }

    /// <summary>
    /// POST /Admin/UpdateSpa
    /// Admin chỉnh sửa thông tin quán Spa (Tên quán, Địa chỉ, SĐT, Chủ Spa, Trạng thái)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateSpa([FromBody] AdminUpdateSpaDto dto)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        if (dto == null || dto.SpaId <= 0 || string.IsNullOrWhiteSpace(dto.SpaName))
            return BadRequest(new { success = false, message = "Vui lòng nhập tên quán Spa hợp lệ." });

        var spa = await _context.Spas.FirstOrDefaultAsync(s => s.SpaId == dto.SpaId);
        if (spa == null)
            return NotFound(new { success = false, message = "Không tìm thấy quán Spa." });

        spa.SpaName = dto.SpaName.Trim();
        spa.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        spa.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            spa.Status = dto.Status.Trim().ToLowerInvariant();
        }

        if (dto.OwnerUserId.HasValue && dto.OwnerUserId.Value > 0)
        {
            var newOwner = await _context.Users.FirstOrDefaultAsync(u => u.UserId == dto.OwnerUserId.Value);
            if (newOwner != null)
            {
                spa.OwnerUserId = newOwner.UserId;
                newOwner.SpaId = spa.SpaId;
            }
        }

        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            message = $"Đã cập nhật thông tin quán '{spa.SpaName}' thành công!"
        });
    }

    /// <summary>
    /// POST /Admin/ToggleSpaStatus
    /// Admin khóa / mở khóa hoạt động của 1 quán Spa
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ToggleSpaStatus([FromBody] AdminEntityIdDto dto)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        if (dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Mã quán Spa không hợp lệ." });

        var spa = await _context.Spas.FirstOrDefaultAsync(s => s.SpaId == dto.Id);
        if (spa == null)
            return NotFound(new { success = false, message = "Không tìm thấy quán Spa." });

        spa.Status = spa.Status == "active" ? "locked" : "active";
        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            newStatus = spa.Status,
            message = spa.Status == "active"
                ? $"Đã kích hoạt lại quán '{spa.SpaName}'."
                : $"Đã tạm khóa quán '{spa.SpaName}'."
        });
    }

    /// <summary>
    /// POST /Admin/DeleteSpa
    /// Admin xóa một quán Spa khỏi hệ thống (gỡ liên kết spa_id của các bản ghi phụ thuộc trước khi xóa)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteSpa([FromBody] AdminEntityIdDto dto)
    {
        if (IsForbiddenForCurrentUser())
            return ForbiddenResult();

        if (dto == null || dto.Id <= 0)
            return BadRequest(new { success = false, message = "Mã quán Spa không hợp lệ." });

        var spa = await _context.Spas.FirstOrDefaultAsync(s => s.SpaId == dto.Id);
        if (spa == null)
            return NotFound(new { success = false, message = "Không tìm thấy quán Spa." });

        try
        {
            var spaId = spa.SpaId;
            var spaName = spa.SpaName;

            var usersInSpa = await _context.Users.Where(u => u.SpaId == spaId).ToListAsync();
            foreach (var u in usersInSpa) u.SpaId = null;

            var custsInSpa = await _context.Customers.Where(c => c.SpaId == spaId).ToListAsync();
            foreach (var c in custsInSpa) c.SpaId = null;

            var srvsInSpa = await _context.Services.Where(s => s.SpaId == spaId).ToListAsync();
            foreach (var s in srvsInSpa) s.SpaId = null;

            var apptsInSpa = await _context.Appointments.Where(a => a.SpaId == spaId).ToListAsync();
            foreach (var a in apptsInSpa) a.SpaId = null;

            var logsInSpa = await _context.AuditLogs.Where(l => l.SpaId == spaId).ToListAsync();
            foreach (var l in logsInSpa) l.SpaId = null;

            var notifsInSpa = await _context.Notifications.Where(n => n.SpaId == spaId).ToListAsync();
            foreach (var n in notifsInSpa) n.SpaId = null;

            var rulesInSpa = await _context.AutomationRules.Where(r => r.SpaId == spaId).ToListAsync();
            foreach (var r in rulesInSpa) r.SpaId = null;

            var invitations = await _context.SpaInvitations.Where(i => i.SpaId == spaId).ToListAsync();
            if (invitations.Count > 0) _context.SpaInvitations.RemoveRange(invitations);

            _context.Spas.Remove(spa);
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Đã xóa quán Spa '{spaName}' khỏi hệ thống!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa quán Spa ID={Id}", dto.Id);
            return StatusCode(500, new { success = false, message = "Lỗi khi xóa quán Spa: " + ex.Message });
        }
    }
}

public class AdminUpdateUserDto
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Specialty { get; set; }
    public int RoleId { get; set; }
    public long? SpaId { get; set; }
    public string Status { get; set; } = "active";
    public string? NewPassword { get; set; }
}

public class AdminUpdateSpaDto
{
    public long SpaId { get; set; }
    public string SpaName { get; set; } = string.Empty;
    public long? OwnerUserId { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string Status { get; set; } = "active";
}

public class AdminEntityIdDto
{
    public long Id { get; set; }
}
