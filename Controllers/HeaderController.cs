using CRM_V2.Data;
using CRM_V2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

public class HeaderController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<HeaderController> _logger;

    public HeaderController(
        ApplicationDbContext context,
        INotificationService notificationService,
        ILogger<HeaderController> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// GET /Header/GlobalSearch?query={keyword}
    /// Tìm kiếm toàn cục xuyên suốt Khách hàng, Lịch hẹn, Dịch vụ và Đội ngũ nhân viên
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GlobalSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 1)
        {
            return Json(new
            {
                success = true,
                query = "",
                total = 0,
                customers = Array.Empty<object>(),
                appointments = Array.Empty<object>(),
                services = Array.Empty<object>(),
                staff = Array.Empty<object>()
            });
        }

        var keyword = query.Trim().ToLower();

        try
        {
            // 1. Tìm Khách hàng (Tên, SĐT, Email)
            var customers = await _context.Customers
                .AsNoTracking()
                .Where(c => c.FullName.ToLower().Contains(keyword) 
                         || (c.Phone != null && c.Phone.Contains(keyword)) 
                         || (c.Email != null && c.Email.ToLower().Contains(keyword)))
                .OrderBy(c => c.FullName)
                .Take(5)
                .Select(c => new
                {
                    id = c.CustomerId,
                    title = c.FullName,
                    subtitle = $"{(string.IsNullOrEmpty(c.Phone) ? "Chưa có SĐT" : c.Phone)} • {(string.IsNullOrEmpty(c.CustomerType) ? "Khách hàng" : c.CustomerType)}",
                    badge = c.CustomerType ?? "Mới",
                    avatar = c.AvatarUrl,
                    type = "customer",
                    typeDisplayName = "Khách hàng",
                    typeIcon = "fa-user"
                })
                .ToListAsync();

            // 2. Tìm Lịch hẹn (Mã lịch hẹn, Tên khách hàng, Trạng thái)
            var appointments = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .Where(a => a.AppointmentId.ToString().Contains(keyword)
                         || (a.Customer != null && a.Customer.FullName.ToLower().Contains(keyword))
                         || (a.Status != null && a.Status.ToLower().Contains(keyword))
                         || (a.Notes != null && a.Notes.ToLower().Contains(keyword)))
                .OrderByDescending(a => a.AppointmentDate)
                .Take(5)
                .Select(a => new
                {
                    id = a.AppointmentId,
                    title = $"Lịch #{a.AppointmentId} - {(a.Customer != null ? a.Customer.FullName : "Khách")}",
                    subtitle = $"{a.AppointmentDate:dd/MM/yyyy} lúc {a.StartTime:hh\\:mm} • {a.Status}",
                    badge = a.Status,
                    avatar = (string?)null,
                    type = "appointment",
                    typeDisplayName = "Lịch hẹn",
                    typeIcon = "fa-calendar-check"
                })
                .ToListAsync();

            // 3. Tìm Danh mục Dịch vụ (Tên dịch vụ, Danh mục nhóm)
            var services = await _context.Services
                .AsNoTracking()
                .Where(s => s.IsActive && (s.ServiceName.ToLower().Contains(keyword) || s.CategoryName.ToLower().Contains(keyword)))
                .OrderBy(s => s.ServiceName)
                .Take(5)
                .Select(s => new
                {
                    id = s.ServiceId,
                    title = s.ServiceName,
                    subtitle = $"{s.CategoryName} • {s.DurationMinutes} phút • {s.Price:N0}đ",
                    badge = s.CategoryName,
                    icon = string.IsNullOrEmpty(s.IconClass) ? "fa-spa" : s.IconClass,
                    type = "service",
                    typeDisplayName = "Dịch vụ",
                    typeIcon = "fa-spa"
                })
                .ToListAsync();

            // 4. Tìm Đội ngũ Nhân viên (Họ tên, Chuyên môn, SĐT, Username)
            var staff = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.Status != "resigned" && (
                    u.FullName.ToLower().Contains(keyword) ||
                    u.Username.ToLower().Contains(keyword) ||
                    (u.Phone != null && u.Phone.Contains(keyword)) ||
                    (u.Specialty != null && u.Specialty.ToLower().Contains(keyword))))
                .OrderBy(u => u.FullName)
                .Take(5)
                .Select(u => new
                {
                    id = u.UserId,
                    title = u.FullName,
                    subtitle = $"{(string.IsNullOrEmpty(u.Specialty) ? "Nhân viên" : u.Specialty)} • @{u.Username}",
                    badge = u.Role != null ? u.Role.RoleName.ToUpper() : "STAFF",
                    avatar = u.AvatarUrl,
                    type = "staff",
                    typeDisplayName = "Nhân sự",
                    typeIcon = "fa-user-tie"
                })
                .ToListAsync();

            var total = customers.Count + appointments.Count + services.Count + staff.Count;

            return Json(new
            {
                success = true,
                query,
                total,
                customers,
                appointments,
                services,
                staff
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tìm kiếm toàn cục: {Query}", query);
            return Json(new { success = false, message = "Lỗi máy chủ khi tìm kiếm: " + ex.Message });
        }
    }

    /// <summary>
    /// GET /Header/GetNotifications
    /// Lấy danh sách thông báo mới nhất kèm số lượng chưa đọc (badge count)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications(int limit = 15)
    {
        try
        {
            var result = await _notificationService.GetNotificationsAsync(userId: null, limit: limit);
            return Json(new
            {
                success = true,
                unreadCount = result.UnreadCount,
                totalCount = result.TotalCount,
                data = result.Notifications
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách thông báo");
            return Json(new { success = false, message = "Lỗi khi tải thông báo: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Header/MarkAsRead/{id}
    /// Đánh dấu 1 thông báo là đã đọc
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MarkAsRead(long id)
    {
        var (success, message) = await _notificationService.MarkAsReadAsync(id);
        return Json(new { success, message });
    }

    /// <summary>
    /// POST /Header/MarkAllAsRead
    /// Đánh dấu tất cả thông báo là đã đọc (ẩn chấm đỏ trên chuông)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var (success, message, count) = await _notificationService.MarkAllAsReadAsync(userId: null);
        return Json(new { success, message, updatedCount = count });
    }
}
