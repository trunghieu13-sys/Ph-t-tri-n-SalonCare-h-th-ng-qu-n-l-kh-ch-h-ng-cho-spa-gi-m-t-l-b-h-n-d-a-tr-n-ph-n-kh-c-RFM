using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public interface IReminderOrchestrator
{
    Task<int> ScanAndSendAppointmentRemindersAsync(CancellationToken cancellationToken = default);
    Task<int> SendWinBackPromoToAtRiskCustomersAsync(CancellationToken cancellationToken = default);
    Task<bool> DispatchWithChannelStrategyAsync(
        Customer customer,
        Appointment? appointment,
        string messageType,
        string segmentAtSendTime,
        string content,
        string? overrideChannel = null);
}

public class ReminderOrchestrator : IReminderOrchestrator
{
    private readonly ApplicationDbContext _db;
    private readonly IRfmService _rfmService;
    private readonly ZaloOaNotificationSender _zaloSender;
    private readonly SmsNotificationSender _smsSender;
    private readonly EmailNotificationSender _emailSender;
    private readonly ILogger<ReminderOrchestrator> _logger;

    public ReminderOrchestrator(
        ApplicationDbContext db,
        IRfmService rfmService,
        ZaloOaNotificationSender zaloSender,
        SmsNotificationSender smsSender,
        EmailNotificationSender emailSender,
        ILogger<ReminderOrchestrator> logger)
    {
        _db = db;
        _rfmService = rfmService;
        _zaloSender = zaloSender;
        _smsSender = smsSender;
        _emailSender = emailSender;
        _logger = logger;
    }

    /// <summary>
    /// Phân luồng kênh gửi linh hoạt theo cấu hình SystemConfigs (Notification_PreferredChannel):
    /// - SmartFallback (Mặc định): Ưu tiên gửi ZaloOA/ZNS trước -> nếu lỗi tự động Fallback sang SMS -> nếu lỗi tiếp tục Fallback sang Email.
    /// - ZaloOA: Gửi qua ZaloOA (nếu lỗi vẫn tự động thử fallback sang SMS/Email).
    /// - SMS: Gửi trực tiếp qua SMS.
    /// - Email: Gửi trực tiếp qua Email.
    /// </summary>
    public async Task<bool> DispatchWithChannelStrategyAsync(
        Customer customer,
        Appointment? appointment,
        string messageType,
        string segmentAtSendTime,
        string content,
        string? overrideChannel = null)
    {
        string preferredChannel = overrideChannel ?? await _db.SystemConfigs
            .AsNoTracking()
            .Where(c => c.ConfigKey == "Notification_PreferredChannel")
            .Select(c => c.ConfigValue)
            .FirstOrDefaultAsync() ?? "SmartFallback";

        if (string.Equals(preferredChannel, "SMS", StringComparison.OrdinalIgnoreCase))
        {
            return await _smsSender.SendNotificationAsync(customer, appointment, messageType, segmentAtSendTime, content);
        }

        if (string.Equals(preferredChannel, "Email", StringComparison.OrdinalIgnoreCase))
        {
            return await _emailSender.SendNotificationAsync(customer, appointment, messageType, segmentAtSendTime, content);
        }

        // Mặc định SmartFallback hoặc ZaloOA: Ưu tiên gửi Zalo OA / ZNS trước
        bool zaloSent = await _zaloSender.SendNotificationAsync(customer, appointment, messageType, segmentAtSendTime, content);
        if (zaloSent)
        {
            return true;
        }

        // Cơ chế Dự phòng tự động (Fallback): Nếu gửi ZaloOA thất bại, tự động chuyển sang SMS rồi Email
        _logger.LogWarning(
            "[SmartFallback] Gửi ZaloOA ZNS cho khách hàng {CustomerId} ({Phone}) không thành công. Tự động Fallback sang kênh SMS/Email...",
            customer.CustomerId, customer.Phone);

        bool smsSent = await _smsSender.SendNotificationAsync(
            customer,
            appointment,
            messageType,
            segmentAtSendTime,
            $"[Fallback từ ZaloOA] {content}");

        if (smsSent)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            return await _emailSender.SendNotificationAsync(
                customer,
                appointment,
                messageType,
                segmentAtSendTime,
                $"[Fallback từ ZaloOA/SMS] {content}");
        }

        return false;
    }

    /// <summary>
    /// Quét các lịch hẹn sắp tới và gửi nhắc lịch cá nhân hóa theo phân khúc RFM (Nudge Theory):
    /// - Nhóm New hoặc IsNoShowRisk == true: Nhắc 2 mốc (24h & 2h) kèm link xác nhận 1-Click.
    /// - Nhóm VIP và Loyal: Nhắc 1 mốc (24h) lịch sự kèm tên KTV phụ trách và link xác nhận 1-Click.
    /// </summary>
    public async Task<int> ScanAndSendAppointmentRemindersAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var upcomingWindowDate = now.Date.AddDays(2);

        // Đảm bảo toàn bộ khách hàng đã có RfmScore trước khi quét
        bool anyMissingRfm = await _db.Customers.AnyAsync(c => c.RfmScore == null, cancellationToken);
        if (anyMissingRfm)
        {
            await _rfmService.RecalculateAllAsync();
        }

        var upcomingAppointments = await _db.Appointments
            .Include(a => a.Customer)
                .ThenInclude(c => c!.RfmScore)
            .Include(a => a.Employee)
            .Include(a => a.AppointmentServices)
                .ThenInclude(aps => aps.Service)
            .Where(a => a.AppointmentDate >= now.Date &&
                        a.AppointmentDate <= upcomingWindowDate &&
                        (a.Status == "Pending" || a.Status == "Confirmed"))
            .ToListAsync(cancellationToken);

        int sentCount = 0;

        foreach (var appt in upcomingAppointments)
        {
            if (appt.Customer == null) continue;

            if (appt.ConfirmationToken == Guid.Empty)
            {
                appt.ConfirmationToken = Guid.NewGuid();
            }

            var apptDateTime = appt.AppointmentDate.Date.Add(appt.StartTime);
            double hoursRemaining = (apptDateTime - now).TotalHours;

            if (hoursRemaining <= 0)
                continue;

            var rfm = appt.Customer.RfmScore;
            string segment = rfm?.SegmentGroup ?? "New";
            bool isNoShowRisk = rfm?.IsNoShowRisk ?? false;

            string serviceNames = appt.AppointmentServices.Any()
                ? string.Join(", ", appt.AppointmentServices.Where(s => s.Service != null).Select(s => s.Service!.ServiceName))
                : "Dịch vụ chăm sóc Spa";

            string staffName = appt.Employee?.FullName ?? "Chuyên viên SalonCare";
            string confirmUrl = $"/BookingPublic/Confirm?token={appt.ConfirmationToken}";
            string cancelUrl = $"/BookingPublic/Cancel?token={appt.ConfirmationToken}";

            // 1. Mốc nhắc trước 24 giờ (Từ > 2h đến <= 26h trước giờ hẹn)
            if (!appt.IsReminder24hSent && hoursRemaining <= 26.0 && hoursRemaining > 2.2)
            {
                string content;
                if (segment == "VIP" || segment == "Loyal")
                {
                    content = $"[ZNS Nhắc lịch 24h - {segment}] Kính chào quý khách {appt.Customer.FullName}, SalonCare trân trọng nhắc lịch hẹn dịch vụ \"{serviceNames}\" vào lúc {appt.StartTime:hh\\:mm} ngày {appt.AppointmentDate:dd/MM/yyyy} cùng KTV {staffName}. Xác nhận 1-Click: {confirmUrl}";
                }
                else
                {
                    content = $"[ZNS Nhắc lịch 24h - {segment}{(isNoShowRisk ? " • Cảnh báo bỏ hẹn" : "")}] Chào {appt.Customer.FullName}, bạn có lịch hẹn \"{serviceNames}\" lúc {appt.StartTime:hh\\:mm} ngày {appt.AppointmentDate:dd/MM/yyyy}. Vui lòng bấm xác nhận giữ chỗ 1-Click tại: {confirmUrl} hoặc báo đổi lịch tại: {cancelUrl}";
                }

                await DispatchWithChannelStrategyAsync(appt.Customer, appt, "Reminder24h", segment, content);
                appt.IsReminder24hSent = true;
                appt.UpdatedAt = DateTime.Now;
                sentCount++;
            }

            // 2. Mốc nhắc trước 2 giờ (Chỉ áp dụng cho nhóm New HOẶC có cờ IsNoShowRisk == true)
            bool needSecondNudge = segment == "New" || isNoShowRisk;
            if (needSecondNudge && !appt.IsReminder2hSent && hoursRemaining <= 2.2 && hoursRemaining > 0)
            {
                string content = $"[ZNS Nhắc khẩn trước 2h - {segment}{(isNoShowRisk ? " • Nguy cơ bỏ hẹn" : "")}] Chào {appt.Customer.FullName}, chỉ còn khoảng 2 giờ nữa là đến lịch hẹn \"{serviceNames}\" ({appt.StartTime:hh\\:mm} hôm nay) với KTV {staffName}. Xác nhận đến ngay (1-Click): {confirmUrl}";

                await DispatchWithChannelStrategyAsync(appt.Customer, appt, "Reminder2h", segment, content);
                appt.IsReminder2hSent = true;
                appt.UpdatedAt = DateTime.Now;
                sentCount++;
            }
        }

        if (sentCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return sentCount;
    }

    /// <summary>
    /// Gửi chiến dịch Win-Back tự động cho khách thuộc nhóm AtRisk (chưa nhận tin WinBackPromo trong 30 ngày qua)
    /// </summary>
    public async Task<int> SendWinBackPromoToAtRiskCustomersAsync(CancellationToken cancellationToken = default)
    {
        var thirtyDaysAgo = DateTime.Now.AddDays(-30);

        var recentlySentCustomerIds = await _db.NotificationLogs
            .Where(nl => nl.MessageType == "WinBackPromo" && nl.SentAt >= thirtyDaysAgo)
            .Select(nl => nl.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var atRiskCustomers = await _db.Customers
            .Include(c => c.RfmScore)
            .Where(c => c.RfmScore != null &&
                        c.RfmScore.SegmentGroup == "AtRisk" &&
                        !recentlySentCustomerIds.Contains(c.CustomerId))
            .ToListAsync(cancellationToken);

        int sentCount = 0;
        foreach (var cust in atRiskCustomers)
        {
            int rDays = cust.RfmScore?.RecencyDays ?? 60;
            string content = $"[ZNS Win-Back Ưu Đãi Nhóm AtRisk] Chào {cust.FullName}, đã {rDays} ngày SalonCare chưa được đón tiếp bạn! Tặng riêng bạn mã ưu đãi giảm 20% [WINBACK20] cho mọi liệu trình chăm sóc da & thư giãn tuần này.";

            await DispatchWithChannelStrategyAsync(cust, null, "WinBackPromo", "AtRisk", content);
            sentCount++;
        }

        return sentCount;
    }
}

/// <summary>
/// Background Worker chạy định kỳ mỗi 15 phút (và tự động tính lại RFM mỗi ngày mới)
/// </summary>
public class ReminderBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderBackgroundWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private DateTime _lastDailyRfmDate = DateTime.MinValue;

    public ReminderBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReminderBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== [ReminderBackgroundWorker] Khởi động dịch vụ Nhắc lịch theo phân khúc RFM & Zalo OA ZNS (Chu kỳ 15 phút/lần) ===");

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var rfmService = scope.ServiceProvider.GetRequiredService<IRfmService>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IReminderOrchestrator>();

                // Cập nhật lại toàn bộ điểm R (Recency) khi bắt đầu ngày mới (00:00) hoặc lần khởi chạy đầu tiên
                if (_lastDailyRfmDate.Date != DateTime.Today)
                {
                    int totalRfm = await rfmService.RecalculateAllAsync();
                    _lastDailyRfmDate = DateTime.Today;
                    _logger.LogInformation("[ReminderBackgroundWorker] Đã tự động cập nhật phân khúc RFM đầu ngày cho {Count} khách hàng.", totalRfm);
                }

                // Quét và gửi nhắc lịch 24h / 2h theo phân khúc
                int remindersSent = await orchestrator.ScanAndSendAppointmentRemindersAsync(stoppingToken);
                if (remindersSent > 0)
                {
                    _logger.LogInformation("[ReminderBackgroundWorker] Đã gửi {Count} tin nhắn nhắc lịch hẹn theo phân khúc RFM.", remindersSent);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReminderBackgroundWorker] Lỗi trong chu kỳ quét nhắc lịch RFM.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
