namespace CRM_V2.Services;

public interface INotificationService
{
    Task<NotificationListResult> GetNotificationsAsync(long? userId = null, int limit = 20);
    Task<(bool Success, string Message)> MarkAsReadAsync(long notificationId);
    Task<(bool Success, string Message, int UpdatedCount)> MarkAllAsReadAsync(long? userId = null);
    Task<int> ScanAndCreateRemindersAsync();
}

public class NotificationListResult
{
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public List<NotificationItemDto> Notifications { get; set; } = new();
}

public class NotificationItemDto
{
    public long NotificationId { get; set; }
    public long? UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TimeAgo { get; set; } = string.Empty;
    public string IconClass { get; set; } = "fa-bell";
    public string IconColorClass { get; set; } = "text-purple-400";
    public string BgColorClass { get; set; } = "bg-purple-500/20";
    public string BadgeText { get; set; } = string.Empty;
}
