using System.Security.Claims;
using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        ApplicationDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogService> logger)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogActivityAsync(
        long? userId,
        string moduleName,
        string actionType,
        string description,
        string? entityId = null,
        string? ipAddress = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            long resolvedUserId = userId ?? 0;

            if (resolvedUserId <= 0 && httpContext != null)
            {
                var idClaim = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (long.TryParse(idClaim, out var parsed))
                {
                    resolvedUserId = parsed;
                }
            }

            if (resolvedUserId <= 0)
            {
                resolvedUserId = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Status == "active")
                    .OrderBy(u => u.RoleId)
                    .Select(u => u.UserId)
                    .FirstOrDefaultAsync();
            }

            if (resolvedUserId <= 0)
                return;

            var resolvedIp = ipAddress;
            if (string.IsNullOrWhiteSpace(resolvedIp))
            {
                resolvedIp = httpContext?.Connection?.RemoteIpAddress?.ToString();
                if (string.IsNullOrWhiteSpace(resolvedIp) || resolvedIp == "::1")
                {
                    resolvedIp = "127.0.0.1";
                }
            }

            var entry = new AuditLog
            {
                UserId = resolvedUserId,
                ModuleName = moduleName.ToUpperInvariant(),
                ActionType = actionType.ToUpperInvariant(),
                Description = description,
                EntityId = entityId,
                IpAddress = resolvedIp,
                CreatedAt = DateTime.Now
            };

            _context.AuditLogs.Add(entry);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể ghi bản ghi AuditLog cho Module={Module}, Action={Action}", moduleName, actionType);
        }
    }

    public async Task<AuditLogQueryResultDto> GetLogsAsync(
        long? employeeId = null,
        string? moduleName = null,
        string? actionType = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? search = null,
        int limit = 200)
    {
        // 1. Lấy danh sách toàn bộ nhân viên để hiển thị trong Dropdown lọc nhân viên
        var allUsers = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderBy(u => u.RoleId)
            .ThenBy(u => u.FullName)
            .ToListAsync();

        var logCountsByUser = await _context.AuditLogs
            .AsNoTracking()
            .GroupBy(l => l.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var employeeFilters = allUsers.Select(u =>
        {
            var rName = u.Role?.RoleName?.ToLowerInvariant() ?? "staff";
            string rDisplay = rName switch
            {
                "admin" => "Admin",
                "owner" => "Chủ Spa",
                _ => "KTV"
            };

            return new AuditLogEmployeeFilterDto
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Username = u.Username,
                RoleDisplayName = rDisplay,
                LogsCount = logCountsByUser.TryGetValue(u.UserId, out var c) ? c : 0
            };
        }).ToList();

        // 2. Xây dựng truy vấn lọc Nhật ký thao tác
        var query = _context.AuditLogs
            .AsNoTracking()
            .Include(l => l.User)
                .ThenInclude(u => u!.Role)
            .AsQueryable();

        if (employeeId.HasValue && employeeId.Value > 0)
        {
            query = query.Where(l => l.UserId == employeeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(moduleName) && !string.Equals(moduleName, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var mod = moduleName.Trim().ToUpperInvariant();
            query = query.Where(l => l.ModuleName.ToUpper() == mod);
        }

        if (!string.IsNullOrWhiteSpace(actionType) && !string.Equals(actionType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var act = actionType.Trim().ToUpperInvariant();
            query = query.Where(l => l.ActionType.ToUpper() == act);
        }

        if (fromDate.HasValue)
        {
            var start = fromDate.Value.Date;
            query = query.Where(l => l.CreatedAt >= start);
        }

        if (toDate.HasValue)
        {
            var end = toDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(l => l.CreatedAt <= end);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim().ToLower();
            query = query.Where(l =>
                l.Description.ToLower().Contains(kw) ||
                l.ModuleName.ToLower().Contains(kw) ||
                l.ActionType.ToLower().Contains(kw) ||
                (l.EntityId != null && l.EntityId.ToLower().Contains(kw)) ||
                (l.User != null && l.User.FullName.ToLower().Contains(kw)));
        }

        var rawLogs = await query
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.LogId)
            .Take(limit > 0 ? limit : 200)
            .ToListAsync();

        var now = DateTime.Now;
        var logDtos = rawLogs.Select(l =>
        {
            var fullName = l.User?.FullName ?? "Hệ thống";
            var username = l.User?.Username ?? "system";
            var roleName = l.User?.Role?.RoleName?.ToLowerInvariant() ?? "staff";
            string roleDisplay = roleName switch
            {
                "admin" => "Admin",
                "owner" => "Chủ Spa",
                _ => "KTV"
            };

            var avatarUrl = !string.IsNullOrWhiteSpace(l.User?.AvatarUrl)
                ? l.User!.AvatarUrl!
                : $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(fullName)}&background=7C3AED&color=fff&size=120";

            return new AuditLogItemDto
            {
                LogId = l.LogId,
                UserId = l.UserId,
                EmployeeName = fullName,
                Username = username,
                RoleName = roleName,
                RoleDisplayName = roleDisplay,
                AvatarUrl = avatarUrl,
                ModuleName = l.ModuleName.ToUpperInvariant(),
                ModuleDisplayName = FormatModuleDisplayName(l.ModuleName),
                ActionType = l.ActionType.ToUpperInvariant(),
                ActionDisplayName = FormatActionDisplayName(l.ActionType),
                Description = l.Description,
                EntityId = l.EntityId,
                IpAddress = string.IsNullOrWhiteSpace(l.IpAddress) ? "127.0.0.1" : l.IpAddress,
                CreatedAt = l.CreatedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                RelativeTime = FormatRelativeTime(l.CreatedAt, now)
            };
        }).ToList();

        return new AuditLogQueryResultDto
        {
            Success = true,
            TotalCount = logDtos.Count,
            CreateCount = logDtos.Count(x => x.ActionType == "CREATE"),
            UpdateCount = logDtos.Count(x => x.ActionType == "UPDATE"),
            DeleteCount = logDtos.Count(x => x.ActionType == "DELETE"),
            SecurityAndExportCount = logDtos.Count(x => x.ActionType == "LOGIN" || x.ActionType == "EXPORT"),
            Employees = employeeFilters,
            Logs = logDtos
        };
    }

    private static string FormatModuleDisplayName(string moduleName)
    {
        return moduleName.ToUpperInvariant() switch
        {
            "CUSTOMERS" => "Khách hàng",
            "APPOINTMENTS" => "Lịch hẹn",
            "SERVICES" => "Dịch vụ",
            "STAFF" => "Nhân sự & Phân quyền",
            "CUSTOMER_CARE" => "Chăm sóc KH",
            "REPORTS" => "Báo cáo & Thống kê",
            "SETTINGS" => "Cài đặt hệ thống",
            "AUTH" => "Xác thực & Bảo mật",
            _ => moduleName
        };
    }

    private static string FormatActionDisplayName(string actionType)
    {
        return actionType.ToUpperInvariant() switch
        {
            "CREATE" => "Thêm mới (CREATE)",
            "UPDATE" => "Cập nhật (UPDATE)",
            "DELETE" => "Xóa / Ẩn (DELETE)",
            "LOGIN" => "Đăng nhập (LOGIN)",
            "EXPORT" => "Xuất dữ liệu (EXPORT)",
            _ => actionType
        };
    }

    private static string FormatRelativeTime(DateTime createdAt, DateTime now)
    {
        var diff = now - createdAt;
        if (diff.TotalSeconds < 60) return "Vừa xong";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} phút trước";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} giờ trước";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} ngày trước";
        return createdAt.ToString("dd/MM/yyyy");
    }
}
