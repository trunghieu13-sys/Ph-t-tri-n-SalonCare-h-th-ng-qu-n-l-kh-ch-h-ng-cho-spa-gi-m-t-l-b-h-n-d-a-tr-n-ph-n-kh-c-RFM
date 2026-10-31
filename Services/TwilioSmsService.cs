using CRM_V2.Models;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace CRM_V2.Services;

public class TwilioSmsService : ISmsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(IConfiguration configuration, ILogger<TwilioSmsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private SmsSettings GetCurrentSettings()
    {
        var settings = new SmsSettings();
        _configuration.GetSection("SmsSettings").Bind(settings);
        return settings;
    }

    /// <summary>
    /// Chuẩn hóa SĐT Việt Nam (VD: 0886091716 -> +84886091716) sang chuẩn quốc tế E.164 cho Twilio
    /// </summary>
    public static string NormalizeVietnamPhoneToE164(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        string cleaned = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (cleaned.StartsWith("0") && cleaned.Length >= 9)
        {
            return "+84" + cleaned.Substring(1);
        }
        if (cleaned.StartsWith("84") && !cleaned.StartsWith("+"))
        {
            return "+" + cleaned;
        }
        if (!cleaned.StartsWith("+"))
        {
            return "+" + cleaned;
        }
        return cleaned;
    }

    public async Task<bool> SendSmsAsync(string toPhoneNumber, string message)
    {
        var (success, _) = await SendSmsWithResultAsync(toPhoneNumber, message);
        return success;
    }

    public async Task<(bool Success, string? ErrorMessage)> SendSmsWithResultAsync(string toPhoneNumber, string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toPhoneNumber))
                return (false, "Khách hàng chưa có số điện thoại.");

            var settings = GetCurrentSettings();

            if (string.IsNullOrWhiteSpace(settings.AccountSid) ||
                string.IsNullOrWhiteSpace(settings.AuthToken) ||
                settings.AccountSid.Contains("TWILIO_ACCOUNT_SID", StringComparison.OrdinalIgnoreCase) ||
                settings.AuthToken.Contains("TWILIO_AUTH_TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                const string msg = "Chưa cấu hình tài khoản SMS Twilio (Account SID, Auth Token & Số điện thoại gửi). Vui lòng bấm 'Cấu hình Gmail & SMS' để nhập khóa API.";
                _logger.LogWarning("[SmsService] {Message}", msg);
                return (false, msg);
            }

            TwilioClient.Init(settings.AccountSid.Trim(), settings.AuthToken.Trim());

            string normalizedTo = NormalizeVietnamPhoneToE164(toPhoneNumber);
            string normalizedFrom = NormalizeVietnamPhoneToE164(settings.FromPhoneNumber);

            var messageOptions = new CreateMessageOptions(new PhoneNumber(normalizedTo))
            {
                From = new PhoneNumber(normalizedFrom),
                Body = message
            };

            var response = await MessageResource.CreateAsync(messageOptions);

            _logger.LogInformation("SMS đã gửi tới {PhoneNumber} (E.164: {NormalizedTo}). Twilio SID: {Sid}, Status: {Status}",
                toPhoneNumber, normalizedTo, response.Sid, response.Status);

            if (response.Status == MessageResource.StatusEnum.Failed ||
                response.Status == MessageResource.StatusEnum.Undelivered)
            {
                return (false, $"Twilio trả về trạng thái {response.Status}: {response.ErrorMessage}");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi SMS Twilio thất bại cho SĐT {PhoneNumber}. Lý do: {Message}", toPhoneNumber, ex.Message);
            return (false, "Lỗi Twilio SMS: " + ex.Message);
        }
    }

    public async Task<bool> SendBulkSmsAsync(IEnumerable<string> phoneNumbers, string message)
    {
        var numbers = phoneNumbers.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        if (!numbers.Any()) return true;

        var allSuccessful = true;
        foreach (var phone in numbers)
        {
            var result = await SendSmsAsync(phone, message);
            if (!result)
            {
                allSuccessful = false;
            }
        }

        return allSuccessful;
    }
}
