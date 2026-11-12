using System.Text.Encodings.Web;
using System.Text.Json;
using CRM_V2.Data;
using CRM_V2.Models;

namespace CRM_V2.Services;

/// <summary>
/// Kênh gửi qua Email thật (kết nối IEmailService SMTP hiện có) + Lưu vào bảng notification_logs
/// </summary>
public class EmailNotificationSender : INotificationSender
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailNotificationSender> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ChannelName => "Email";

    public EmailNotificationSender(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<EmailNotificationSender> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<bool> SendNotificationAsync(
        Customer customer,
        Appointment? appointment,
        string messageType,
        string segmentAtSendTime,
        string content)
    {
        bool success = false;
        string? errorMsg = null;

        string subject = messageType switch
        {
            "Reminder24h" => "[SalonCare] Nhắc lịch hẹn trước 24 giờ & Xác nhận 1-Click",
            "Reminder2h" => "[SalonCare] Nhắc lịch hẹn sắp diễn ra trong 2 giờ tới",
            "WinBackPromo" => "[SalonCare] Ưu đãi đặc biệt dành riêng cho quý khách",
            _ => "[SalonCare] Thông báo lịch hẹn"
        };

        if (!string.IsNullOrWhiteSpace(customer.Email))
        {
            try
            {
                success = await _emailService.SendEmailAsync(customer.Email, subject, content);
                if (!success)
                {
                    errorMsg = "SMTP Email chưa cấu hình hoặc gửi không thành công.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi gửi Email nhắc lịch cho khách hàng {CustomerId}", customer.CustomerId);
                success = false;
                errorMsg = ex.Message;
            }
        }
        else
        {
            errorMsg = "Khách hàng chưa có địa chỉ Email trong hồ sơ.";
        }

        string payloadJson = JsonSerializer.Serialize(new
        {
            channel = "Email",
            to_email = customer.Email ?? "(missing)",
            subject,
            customer_name = customer.FullName,
            appointment_id = appointment?.AppointmentId,
            body = content
        }, JsonOptions);

        var log = new NotificationLog
        {
            CustomerId = customer.CustomerId,
            AppointmentId = appointment?.AppointmentId,
            Channel = "Email",
            MessageType = messageType,
            SegmentAtSendTime = segmentAtSendTime,
            Content = content,
            Status = success ? "Sent" : "Failed",
            ExternalMessageId = success ? $"smtp_{Guid.NewGuid():N}"[..16] : null,
            PayloadJson = payloadJson,
            ErrorMessage = errorMsg,
            SentAt = DateTime.Now
        };

        _context.NotificationLogs.Add(log);
        await _context.SaveChangesAsync();
        return success;
    }
}

/// <summary>
/// Kênh gửi qua SMS (kết nối ISmsService / Twilio hiện có) + Lưu vào bảng notification_logs
/// </summary>
public class SmsNotificationSender : INotificationSender
{
    private readonly ApplicationDbContext _context;
    private readonly ISmsService _smsService;
    private readonly ILogger<SmsNotificationSender> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ChannelName => "SMS";

    public SmsNotificationSender(
        ApplicationDbContext context,
        ISmsService smsService,
        ILogger<SmsNotificationSender> logger)
    {
        _context = context;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task<bool> SendNotificationAsync(
        Customer customer,
        Appointment? appointment,
        string messageType,
        string segmentAtSendTime,
        string content)
    {
        var (success, err) = await _smsService.SendSmsWithResultAsync(customer.Phone, content);

        string payloadJson = JsonSerializer.Serialize(new
        {
            channel = "SMS",
            to_phone_e164 = TwilioSmsService.NormalizeVietnamPhoneToE164(customer.Phone),
            customer_name = customer.FullName,
            appointment_id = appointment?.AppointmentId,
            message_body = content
        }, JsonOptions);

        var log = new NotificationLog
        {
            CustomerId = customer.CustomerId,
            AppointmentId = appointment?.AppointmentId,
            Channel = "SMS",
            MessageType = messageType,
            SegmentAtSendTime = segmentAtSendTime,
            Content = content,
            Status = success ? "Sent" : "Failed",
            ExternalMessageId = success ? $"sms_{Guid.NewGuid():N}"[..16] : null,
            PayloadJson = payloadJson,
            ErrorMessage = err,
            SentAt = DateTime.Now
        };

        _context.NotificationLogs.Add(log);
        await _context.SaveChangesAsync();
        return success;
    }
}
