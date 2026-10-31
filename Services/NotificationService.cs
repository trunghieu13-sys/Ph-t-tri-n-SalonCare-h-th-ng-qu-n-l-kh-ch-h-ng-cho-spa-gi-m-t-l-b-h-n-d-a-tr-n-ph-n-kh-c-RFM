using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ApplicationDbContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<NotificationListResult> GetNotificationsAsync(long? userId = null, int limit = 20)
    {
        // 1. Quét thông báo tự động (lịch hẹn & sinh nhật) để đảm bảo dữ liệu mới nhất
        try
        {
            await ScanAndCreateRemindersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi khi quét thông báo tự động trong GetNotificationsAsync");
        }

        // 2. Lấy thông báo theo người nhận hoặc toàn hệ thống (user_id IS NULL)
        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => !n.UserId.HasValue || (userId.HasValue && n.UserId == userId.Value));

        var unreadCount = await query.CountAsync(n => !n.IsRead);
        var totalCount = await query.CountAsync();

        var list = await query
            .OrderBy(n => n.IsRead) // Chưa đọc lên đầu
            .ThenByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync();

        var dtos = list.Select(n =>
        {
            var dto = new NotificationItemDto
            {
                NotificationId = n.NotificationId,
                UserId = n.UserId,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                ReferenceId = n.ReferenceId,
                ReferenceType = n.ReferenceType,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                TimeAgo = ComputeTimeAgo(n.CreatedAt)
            };

            switch (n.Type.ToUpper())
            {
                case "APPOINTMENT_REMINDER":
                    dto.IconClass = "fa-calendar-check";
                    dto.IconColorClass = "text-cyan-400";
                    dto.BgColorClass = "bg-cyan-500/20";
                    dto.BadgeText = "Lịch hẹn";
                    break;

                case "BIRTHDAY_REMINDER":
                    dto.IconClass = "fa-cake-candles";
                    dto.IconColorClass = "text-pink-400";
                    dto.BgColorClass = "bg-pink-500/20";
                    dto.BadgeText = "Sinh nhật";
                    break;

                case "CANCEL_ALERT":
                    dto.IconClass = "fa-calendar-xmark";
                    dto.IconColorClass = "text-rose-400";
                    dto.BgColorClass = "bg-rose-500/20";
                    dto.BadgeText = "Hủy lịch";
                    break;

                case "SYSTEM":
                default:
                    dto.IconClass = "fa-bell";
                    dto.IconColorClass = "text-purple-400";
                    dto.BgColorClass = "bg-purple-500/20";
                    dto.BadgeText = "Hệ thống";
                    break;
            }

            return dto;
        }).ToList();

        return new NotificationListResult
        {
            UnreadCount = unreadCount,
            TotalCount = totalCount,
            Notifications = dtos
        };
    }

    public async Task<(bool Success, string Message)> MarkAsReadAsync(long notificationId)
    {
        try
        {
            var notif = await _context.Notifications.FindAsync(notificationId);
            if (notif == null)
                return (false, "Không tìm thấy thông báo.");

            notif.IsRead = true;
            await _context.SaveChangesAsync();
            return (true, "Đã đánh dấu thông báo là đã đọc.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi đánh dấu thông báo đã đọc: {Id}", notificationId);
            return (false, "Lỗi máy chủ: " + ex.Message);
        }
    }

    public async Task<(bool Success, string Message, int UpdatedCount)> MarkAllAsReadAsync(long? userId = null)
    {
        try
        {
            var unreadList = await _context.Notifications
                .Where(n => !n.IsRead && (!n.UserId.HasValue || (userId.HasValue && n.UserId == userId.Value)))
                .ToListAsync();

            if (unreadList.Count == 0)
                return (true, "Không có thông báo nào chưa đọc.", 0);

            foreach (var n in unreadList)
            {
                n.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return (true, $"Đã đánh dấu {unreadList.Count} thông báo là đã đọc.", unreadList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi đánh dấu tất cả thông báo đã đọc");
            return (false, "Lỗi máy chủ: " + ex.Message, 0);
        }
    }

    public async Task<int> ScanAndCreateRemindersAsync()
    {
        int createdCount = 0;
        var now = DateTime.Now;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        // 1. Quét Lịch Hẹn Trong Ngày (Confirmed & Pending)
        // Tìm các lịch hẹn hôm nay chưa được tạo thông báo nhắc
        var todayAppts = await _context.Appointments
            .Include(a => a.Customer)
            .Include(a => a.AppointmentServices)
                .ThenInclude(aps => aps.Service)
            .Where(a => a.AppointmentDate == today 
                     && (a.Status == "Confirmed" || a.Status == "Pending")
                     && !a.IsHiddenFromBoard)
            .ToListAsync();

        var existingApptNotifIds = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.ReferenceType == "APPOINTMENT" && n.Type == "APPOINTMENT_REMINDER" && n.CreatedAt >= today)
            .Select(n => n.ReferenceId)
            .ToListAsync();

        foreach (var appt in todayAppts)
        {
            if (existingApptNotifIds.Contains(appt.AppointmentId)) continue;

            var custName = appt.Customer?.FullName ?? "Khách hàng";
            var custPhone = appt.Customer?.Phone ?? "";
            var services = appt.AppointmentServices.Select(s => s.Service?.ServiceName).Where(s => !string.IsNullOrEmpty(s));
            var serviceText = string.Join(", ", services);
            if (string.IsNullOrEmpty(serviceText)) serviceText = "dịch vụ Spa";
            var apptTimeStr = appt.StartTime.ToString(@"hh\:mm");

            _context.Notifications.Add(new Notification
            {
                Title = $"Lịch hẹn hôm nay: {custName}",
                Message = $"Khách hàng {custName} ({custPhone}) có lịch thực hiện {serviceText} lúc {apptTimeStr} hôm nay. Vui lòng chuẩn bị phòng phục vụ!",
                Type = "APPOINTMENT_REMINDER",
                ReferenceId = appt.AppointmentId,
                ReferenceType = "APPOINTMENT",
                IsRead = false,
                CreatedAt = DateTime.Now
            });

            createdCount++;
        }

        // 2. Quét Sinh Nhật Khách Hàng Ngày Mai
        var tomorrowCusts = await _context.Customers
            .AsNoTracking()
            .Where(c => c.DateOfBirth.HasValue 
                     && c.DateOfBirth.Value.Month == tomorrow.Month 
                     && c.DateOfBirth.Value.Day == tomorrow.Day)
            .ToListAsync();

        var existingBirthdayCustIds = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.ReferenceType == "CUSTOMER" && n.Type == "BIRTHDAY_REMINDER" && n.CreatedAt >= today)
            .Select(n => n.ReferenceId)
            .ToListAsync();

        foreach (var cust in tomorrowCusts)
        {
            if (existingBirthdayCustIds.Contains(cust.CustomerId)) continue;

            _context.Notifications.Add(new Notification
            {
                Title = $"Sinh nhật ngày mai: {cust.FullName}",
                Message = $"Khách hàng {cust.FullName} ({cust.Phone}) có ngày sinh vào ngày mai ({tomorrow:dd/MM}). Hãy gửi mã giảm giá tri ân hoặc gọi điện chúc mừng!",
                Type = "BIRTHDAY_REMINDER",
                ReferenceId = cust.CustomerId,
                ReferenceType = "CUSTOMER",
                IsRead = false,
                CreatedAt = DateTime.Now
            });

            createdCount++;
        }

        // 3. Quét Lịch Hẹn Đã Hủy Gần Đây (trong 24 giờ qua)
        var recentCancels = await _context.Appointments
            .Include(a => a.Customer)
            .Where(a => a.Status == "Cancelled" && a.CancelledAt.HasValue && a.CancelledAt.Value >= now.AddHours(-24))
            .ToListAsync();

        var existingCancelNotifIds = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.ReferenceType == "APPOINTMENT" && n.Type == "CANCEL_ALERT" && n.CreatedAt >= now.AddHours(-24))
            .Select(n => n.ReferenceId)
            .ToListAsync();

        foreach (var cAppt in recentCancels)
        {
            if (existingCancelNotifIds.Contains(cAppt.AppointmentId)) continue;

            var cName = cAppt.Customer?.FullName ?? "Khách hàng";
            var reason = string.IsNullOrWhiteSpace(cAppt.CancellationReason) ? "Khách báo bận đột xuất" : cAppt.CancellationReason;
            var cancelTimeStr = cAppt.StartTime.ToString(@"hh\:mm");
            var cancelDateStr = cAppt.AppointmentDate.ToString("dd/MM/yyyy");

            _context.Notifications.Add(new Notification
            {
                Title = $"Cảnh báo hủy lịch: {cName}",
                Message = $"Lịch hẹn #{cAppt.AppointmentId} của khách {cName} ({cancelDateStr} lúc {cancelTimeStr}) đã bị hủy. Lý do: {reason}.",
                Type = "CANCEL_ALERT",
                ReferenceId = cAppt.AppointmentId,
                ReferenceType = "APPOINTMENT",
                IsRead = false,
                CreatedAt = DateTime.Now
            });

            createdCount++;
        }

        if (createdCount > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Đã tự động tạo {Count} thông báo hệ thống mới.", createdCount);
        }

        return createdCount;
    }

    private static string ComputeTimeAgo(DateTime dt)
    {
        var diff = DateTime.Now - dt;

        if (diff.TotalMinutes < 1)
            return "Vừa xong";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes} phút trước";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours} giờ trước";
        if (diff.TotalDays < 2)
            return $"Hôm qua {dt:HH:mm}";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays} ngày trước";

        return dt.ToString("dd/MM HH:mm");
    }
}
