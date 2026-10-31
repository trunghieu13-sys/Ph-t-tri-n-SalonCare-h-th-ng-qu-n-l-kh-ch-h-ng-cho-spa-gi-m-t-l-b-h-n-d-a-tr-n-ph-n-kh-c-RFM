using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

/// <summary>
/// Background Job định kỳ (5 phút/lần) quét kịch bản tự động trong bảng automation_rules:
/// 1. BEFORE_APPOINTMENT: Nhắc lịch hẹn sắp diễn ra trong 2 giờ qua SMS.
/// 2. BIRTHDAY: Gửi Email chúc mừng sinh nhật + Mã Voucher giảm 20%.
/// 3. AFTER_LAST_VISIT: Gửi tin nhắn khảo sát / tái ghé cho khách có last_visit_at cách đây >= 30 ngày.
/// </summary>
public class AutomationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomationBackgroundService> _logger;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);

    public AutomationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AutomationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== [SalonCare Automation Worker] Khởi chạy Background Service (Chu kỳ quét: 5 phút/lần) ===");

        // Chờ 10 giây sau khi ứng dụng khởi động hoàn tất rồi thực hiện chu kỳ quét đầu tiên
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessActiveAutomationRulesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SalonCare Automation Worker] Lỗi khi thực thi chu kỳ quét kịch bản tự động.");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }
    }

    public async Task<int> ProcessActiveAutomationRulesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();

        var now = DateTime.Now;
        var today = now.Date;

        // Tự động ẩn các lịch hẹn ở ô Vắng mặt (NoShow), Hoàn thành (Completed) và Đã hủy (Cancelled)
        // khỏi bảng Kanban khi đã qua 23:00 ngày Chủ Nhật của tuần đó (không xóa trong SQL)
        var boardCandidates = await db.Appointments
            .Where(a => !a.IsHiddenFromBoard &&
                        (a.Status == "Completed" || a.Status == "NoShow" || a.Status == "Cancelled"))
            .ToListAsync(cancellationToken);

        int hiddenCount = 0;
        foreach (var appt in boardCandidates)
        {
            var refDate = appt.CompletedAt ?? appt.CancelledAt ?? appt.AppointmentDate;
            int daysUntilSunday = refDate.DayOfWeek == DayOfWeek.Sunday ? 0 : 7 - (int)refDate.DayOfWeek;
            var sunday23h = refDate.Date.AddDays(daysUntilSunday).AddHours(23);

            if (now >= sunday23h)
            {
                appt.IsHiddenFromBoard = true;
                appt.HiddenAt = now;
                hiddenCount++;
            }
        }
        if (hiddenCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("[SalonCare Automation Worker] Đã tự động ẩn {Count} lịch hẹn (Hoàn thành/Vắng/Hủy) sau 23h Chủ Nhật khỏi bảng Kanban.", hiddenCount);
        }

        // Tự động quét và phát sinh thông báo hệ thống (Lịch hẹn sắp tới, Sinh nhật ngày mai, Hủy lịch)
        try
        {
            var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifService.ScanAndCreateRemindersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SalonCare Automation Worker] Lỗi khi quét thông báo hệ thống tự động.");
        }

        var activeRules = await db.AutomationRules
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);

        if (!activeRules.Any())
            return 0;

        int totalDispatched = 0;

        foreach (var rule in activeRules)
        {
            switch (rule.TriggerType.ToUpperInvariant())
            {
                case "BEFORE_APPOINTMENT":
                    totalDispatched += await HandleBeforeAppointmentRuleAsync(db, smsService, emailService, rule, now, today, cancellationToken);
                    break;

                case "BIRTHDAY":
                    totalDispatched += await HandleBirthdayRuleAsync(db, smsService, emailService, rule, now, today, cancellationToken);
                    break;

                case "AFTER_LAST_VISIT":
                    totalDispatched += await HandleAfterLastVisitRuleAsync(db, smsService, emailService, rule, now, today, cancellationToken);
                    break;
            }
        }

        return totalDispatched;
    }

    /// <summary>
    /// 1. Quét lịch hẹn trong bảng appointments sắp diễn ra trong vòng 2 giờ tới để gửi SMS nhắc lịch
    /// </summary>
    private async Task<int> HandleBeforeAppointmentRuleAsync(
        ApplicationDbContext db,
        ISmsService smsService,
        IEmailService emailService,
        AutomationRule rule,
        DateTime now,
        DateTime today,
        CancellationToken ct)
    {
        var currentTime = now.TimeOfDay;
        var windowEnd = currentTime.Add(TimeSpan.FromMinutes(Math.Abs(rule.TriggerOffsetMinutes > 0 ? rule.TriggerOffsetMinutes : 120)));

        var upcomingAppointments = await db.Appointments
            .Include(a => a.Customer)
            .Where(a => a.AppointmentDate == today
                        && (a.Status == "Pending" || a.Status == "Confirmed")
                        && a.StartTime >= currentTime
                        && a.StartTime <= windowEnd)
            .ToListAsync(ct);

        int count = 0;
        foreach (var appt in upcomingAppointments)
        {
            if (appt.Customer == null) continue;

            // Kiểm tra đã gửi nhắc lịch cho khách này trong hôm nay chưa
            bool alreadySent = await db.CustomerInteractions.AnyAsync(
                i => i.CustomerId == appt.CustomerId
                     && i.RuleId == rule.RuleId
                     && i.SentAt >= today, ct);

            if (alreadySent) continue;

            string content = FormatTemplate(rule.MessageTemplate, appt.Customer, appt.StartTime.ToString(@"hh\:mm"), "REMIND2H");
            bool sentSuccess = await DispatchMessageAsync(rule.Channel, appt.Customer, "Nhắc lịch hẹn tại SalonCare Spa", content, smsService, emailService);

            db.CustomerInteractions.Add(new CustomerInteraction
            {
                CustomerId = appt.CustomerId,
                RuleId = rule.RuleId,
                Channel = rule.Channel,
                Recipient = rule.Channel.Equals("EMAIL", StringComparison.OrdinalIgnoreCase)
                    ? (appt.Customer.Email ?? appt.Customer.Phone)
                    : appt.Customer.Phone,
                Content = content,
                Status = sentSuccess ? "SUCCESS" : "FAILED",
                SentAt = DateTime.Now,
                ErrorMessage = sentSuccess ? null : "Không thể kết nối cổng gửi hoặc thiếu thông tin liên hệ."
            });
            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(ct);

        return count;
    }

    /// <summary>
    /// 2. Quét khách hàng trong bảng customers có ngày sinh (Tháng/Ngày) trùng với hôm nay để gửi Email Chúc mừng + Voucher
    /// </summary>
    private async Task<int> HandleBirthdayRuleAsync(
        ApplicationDbContext db,
        ISmsService smsService,
        IEmailService emailService,
        AutomationRule rule,
        DateTime now,
        DateTime today,
        CancellationToken ct)
    {
        var birthdayCustomers = await db.Customers
            .Where(c => c.CustomerStatus == "Hoat_dong"
                        && c.DateOfBirth != null
                        && c.DateOfBirth.Value.Month == today.Month
                        && c.DateOfBirth.Value.Day == today.Day)
            .ToListAsync(ct);

        int count = 0;
        foreach (var cust in birthdayCustomers)
        {
            bool alreadySent = await db.CustomerInteractions.AnyAsync(
                i => i.CustomerId == cust.CustomerId
                     && i.RuleId == rule.RuleId
                     && i.SentAt >= today, ct);

            if (alreadySent) continue;

            string voucherCode = $"HPBD20-{cust.CustomerId:D4}";
            string content = FormatTemplate(rule.MessageTemplate, cust, "08:00", voucherCode);
            bool sentSuccess = await DispatchMessageAsync(rule.Channel, cust, "Chúc mừng sinh nhật & Quà tặng Voucher 20% từ SalonCare Spa", content, smsService, emailService);

            db.CustomerInteractions.Add(new CustomerInteraction
            {
                CustomerId = cust.CustomerId,
                RuleId = rule.RuleId,
                Channel = rule.Channel,
                Recipient = rule.Channel.Equals("EMAIL", StringComparison.OrdinalIgnoreCase)
                    ? (cust.Email ?? cust.Phone)
                    : cust.Phone,
                Content = content,
                Status = sentSuccess ? "SUCCESS" : "FAILED",
                SentAt = DateTime.Now,
                ErrorMessage = sentSuccess ? null : "Khách hàng chưa cập nhật địa chỉ Email hợp lệ."
            });
            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(ct);

        return count;
    }

    /// <summary>
    /// 3. Quét khách hàng có last_visit_at cách đây >= 30 ngày để gửi thông báo Khảo sát / Tái ghé
    /// </summary>
    private async Task<int> HandleAfterLastVisitRuleAsync(
        ApplicationDbContext db,
        ISmsService smsService,
        IEmailService emailService,
        AutomationRule rule,
        DateTime now,
        DateTime today,
        CancellationToken ct)
    {
        var thresholdDate = today.AddDays(-30);
        var recentCutoff = today.AddDays(-7); // Tránh gửi lặp lại trong vòng 7 ngày

        var inactiveCustomers = await db.Customers
            .Where(c => c.CustomerStatus == "Hoat_dong"
                        && c.LastVisitAt != null
                        && c.LastVisitAt.Value.Date <= thresholdDate)
            .ToListAsync(ct);

        int count = 0;
        foreach (var cust in inactiveCustomers)
        {
            bool alreadySentRecently = await db.CustomerInteractions.AnyAsync(
                i => i.CustomerId == cust.CustomerId
                     && i.RuleId == rule.RuleId
                     && i.SentAt >= recentCutoff, ct);

            if (alreadySentRecently) continue;

            string voucherCode = $"COMEBACK15-{cust.CustomerId:D4}";
            string content = FormatTemplate(rule.MessageTemplate, cust, "", voucherCode);
            bool sentSuccess = await DispatchMessageAsync(rule.Channel, cust, "Lời nhắc chăm sóc da định kỳ từ SalonCare Spa", content, smsService, emailService);

            db.CustomerInteractions.Add(new CustomerInteraction
            {
                CustomerId = cust.CustomerId,
                RuleId = rule.RuleId,
                Channel = rule.Channel,
                Recipient = rule.Channel.Equals("EMAIL", StringComparison.OrdinalIgnoreCase)
                    ? (cust.Email ?? cust.Phone)
                    : cust.Phone,
                Content = content,
                Status = sentSuccess ? "SUCCESS" : "FAILED",
                SentAt = DateTime.Now,
                ErrorMessage = sentSuccess ? null : "Không thể gửi tin nhắn tái ghé."
            });
            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(ct);

        return count;
    }

    private static string FormatTemplate(string template, Customer customer, string appointmentTime, string voucherCode)
    {
        return template
            .Replace("{full_name}", customer.FullName, StringComparison.OrdinalIgnoreCase)
            .Replace("{phone}", customer.Phone, StringComparison.OrdinalIgnoreCase)
            .Replace("{appointment_time}", appointmentTime, StringComparison.OrdinalIgnoreCase)
            .Replace("{voucher_code}", voucherCode, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> DispatchMessageAsync(
        string channel,
        Customer customer,
        string subject,
        string content,
        ISmsService smsService,
        IEmailService emailService)
    {
        if (channel.Equals("EMAIL", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(customer.Email))
                return false;

            return await emailService.SendEmailAsync(customer.Email, subject, content, customer.FullName);
        }

        if (string.IsNullOrWhiteSpace(customer.Phone))
            return false;

        var plainText = System.Text.RegularExpressions.Regex.Replace(content, "<.*?>", string.Empty);
        return await smsService.SendSmsAsync(customer.Phone, plainText);
    }
}
