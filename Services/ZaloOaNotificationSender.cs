using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

/// <summary>
/// Dịch vụ gửi thông báo Zalo Official Account / ZNS (Zalo Notification Service)
/// Hỗ trợ 2 chế độ vận hành:
/// 1. UseSandboxMode = false: Gọi trực tiếp REST API Zalo ZNS (https://business.openapi.zalo.me/message/template)
///    kèm cơ chế tự động Refresh AccessToken (OAuth v4) khi gặp mã lỗi -216 (Token hết hạn sau 25 giờ).
/// 2. UseSandboxMode = true: Đóng gói đúng chuẩn JSON Payload của Zalo ZNS, sinh mã msg_id mô phỏng,
///    tạo đường dẫn xác nhận 1-Click (/BookingPublic/Confirm?token=...) và lưu vào NotificationLogs để kiểm thử & bảo vệ đồ án.
/// </summary>
public class ZaloOaNotificationSender : INotificationSender
{
    private const string ZaloZnsEndpoint = "https://business.openapi.zalo.me/message/template";
    private const string ZaloOAuthEndpoint = "https://oauth.zaloapp.com/v4/oa/access_token";

    private readonly ApplicationDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ZaloOaNotificationSender> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ChannelName => "ZaloOA";

    public ZaloOaNotificationSender(
        ApplicationDbContext context,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ZaloOaNotificationSender> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Chuẩn hóa SĐT Việt Nam (0901234567 hoặc +84901234567) thành định dạng bắt buộc của Zalo ZNS: 84901234567
    /// </summary>
    public static string NormalizePhoneForZaloZns(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        string digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0") && digits.Length >= 9)
        {
            return "84" + digits[1..];
        }
        if (digits.StartsWith("84"))
        {
            return digits;
        }
        return "84" + digits;
    }

    public async Task<bool> SendNotificationAsync(
        Customer customer,
        Appointment? appointment,
        string messageType,
        string segmentAtSendTime,
        string content)
    {
        var configs = await LoadZaloConfigsAsync();

        bool useSandbox = !configs.TryGetValue("Zalo_UseSandboxMode", out var sbxVal) ||
                          !string.Equals(sbxVal, "false", StringComparison.OrdinalIgnoreCase);

        string templateId = messageType == "WinBackPromo"
            ? configs.GetValueOrDefault("Zalo_PromoTemplateId", "318941")
            : configs.GetValueOrDefault("Zalo_ReminderTemplateId", "318940");

        if (string.IsNullOrWhiteSpace(templateId))
        {
            templateId = messageType == "WinBackPromo" ? "318941" : "318940";
        }

        string normalizedPhone = NormalizePhoneForZaloZns(customer.Phone);
        string baseUrl = GetCurrentBaseUrl();

        if (appointment != null && appointment.ConfirmationToken == Guid.Empty)
        {
            appointment.ConfirmationToken = Guid.NewGuid();
        }

        string confirmUrl = appointment != null
            ? $"{baseUrl}/BookingPublic/Confirm?token={appointment.ConfirmationToken}"
            : $"{baseUrl}/BookingPublic/Promo?customerId={customer.CustomerId}";

        string serviceName = appointment != null && appointment.AppointmentServices.Any()
            ? string.Join(", ", appointment.AppointmentServices.Where(s => s.Service != null).Select(s => s.Service!.ServiceName))
            : (messageType == "WinBackPromo" ? "Ưu đãi Chăm sóc Spa giảm 20% [WINBACK20]" : "Liệu trình chăm sóc da chuyên sâu");

        string technicianName = appointment?.Employee?.FullName ?? "KTV SalonCare";
        string apptDateStr = appointment != null ? appointment.AppointmentDate.ToString("dd/MM/yyyy") : DateTime.Now.AddDays(1).ToString("dd/MM/yyyy");
        string apptTimeStr = appointment != null ? appointment.StartTime.ToString(@"hh\:mm") : "09:30";
        string trackingId = $"{(appointment?.AppointmentId ?? customer.CustomerId)}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // Đóng gói chuẩn JSON Payload theo tài liệu Zalo ZNS API
        var znsPayloadObj = new
        {
            phone = normalizedPhone,
            template_id = templateId,
            template_data = new
            {
                customer_name = customer.FullName,
                appointment_date = apptDateStr,
                appointment_time = apptTimeStr,
                service_name = serviceName,
                technician_name = technicianName,
                confirm_url = confirmUrl
            },
            tracking_id = trackingId
        };

        string payloadJson = JsonSerializer.Serialize(znsPayloadObj, JsonOptions);

        bool isSuccess;
        string? externalMsgId;
        string? errorMsg;

        if (useSandbox)
        {
            // Chế độ Sandbox / Giả lập phục vụ pilot & demo bảo vệ đồ án
            isSuccess = !string.IsNullOrWhiteSpace(normalizedPhone) && normalizedPhone.Length >= 10;
            externalMsgId = isSuccess ? $"zns_sbx_{Guid.NewGuid():N}"[..20] : null;
            errorMsg = isSuccess ? null : "Số điện thoại khách hàng không hợp lệ cho chuẩn Zalo ZNS (84xxxxxxxxx).";

            _logger.LogInformation(
                "[ZaloOA ZNS Sandbox] Đã đóng gói & gửi ZNS [{MessageType}] tới {Phone} (MsgId: {MsgId}) | Payload: {Payload}",
                messageType, normalizedPhone, externalMsgId, payloadJson);
        }
        else
        {
            // Chế độ Production: Gọi trực tiếp REST API Zalo ZNS + Tự động Refresh Token nếu gặp lỗi -216
            var apiResult = await SendZnsTemplateAsync(payloadJson, configs);
            isSuccess = apiResult.Success;
            externalMsgId = apiResult.ExternalMessageId;
            errorMsg = apiResult.ErrorMessage;
        }

        var log = new NotificationLog
        {
            CustomerId = customer.CustomerId,
            AppointmentId = appointment?.AppointmentId,
            Channel = "ZaloOA",
            MessageType = messageType,
            SegmentAtSendTime = segmentAtSendTime,
            Content = content,
            Status = isSuccess ? "Sent" : "Failed",
            ExternalMessageId = externalMsgId,
            PayloadJson = payloadJson,
            ErrorMessage = errorMsg,
            SentAt = DateTime.Now
        };

        _context.NotificationLogs.Add(log);
        await _context.SaveChangesAsync();

        return isSuccess;
    }

    /// <summary>
    /// Gọi POST https://business.openapi.zalo.me/message/template
    /// Tự động Refresh AccessToken nếu nhận mã lỗi -216 (Access Token hết hạn sau 25 giờ)
    /// </summary>
    public async Task<(bool Success, string? ExternalMessageId, string? ErrorMessage)> SendZnsTemplateAsync(
        string payloadJson,
        Dictionary<string, string>? preloadedConfigs = null,
        bool isRetryAfterRefresh = false)
    {
        var configs = preloadedConfigs ?? await LoadZaloConfigsAsync();
        string accessToken = configs.GetValueOrDefault("Zalo_AccessToken", string.Empty);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return (false, null, "Chưa cấu hình Zalo_AccessToken trong bảng SystemConfigs.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("ZaloZnsClient");
            using var request = new HttpRequestMessage(HttpMethod.Post, ZaloZnsEndpoint);
            request.Headers.Add("access_token", accessToken);
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            var jsonNode = JsonNode.Parse(responseBody);
            int errorCode = jsonNode?["error"]?.GetValue<int>() ?? -1;
            string message = jsonNode?["message"]?.GetValue<string>() ?? "Unknown Zalo ZNS response";

            // Mã lỗi -216: Access Token của Zalo OA đã hết hạn (sau 25 giờ) -> Tự động gọi RefreshAccessTokenAsync và gửi lại
            if (errorCode == -216 && !isRetryAfterRefresh)
            {
                _logger.LogWarning("[ZaloOA ZNS] AccessToken hết hạn (error = -216). Đang tự động gọi RefreshAccessTokenAsync...");
                bool refreshed = await RefreshAccessTokenAsync(configs);
                if (refreshed)
                {
                    var updatedConfigs = await LoadZaloConfigsAsync();
                    return await SendZnsTemplateAsync(payloadJson, updatedConfigs, isRetryAfterRefresh: true);
                }

                return (false, null, "Zalo AccessToken hết hạn (-216) và không thể tự động làm mới bằng RefreshToken.");
            }

            if (errorCode == 0)
            {
                string? msgId = jsonNode?["data"]?["msg_id"]?.GetValue<string>();
                _logger.LogInformation("[ZaloOA ZNS Production] Gửi tin ZNS thành công! msg_id = {MsgId}", msgId);
                return (true, msgId, null);
            }

            string errDetail = $"Zalo ZNS Error ({errorCode}): {message}";
            _logger.LogWarning("[ZaloOA ZNS Production] Gửi thất bại: {Error}", errDetail);
            return (false, null, errDetail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZaloOA ZNS Production] Lỗi kết nối tới máy chủ Zalo OpenAPI.");
            return (false, null, $"Lỗi kết nối Zalo OpenAPI: {ex.Message}");
        }
    }

    /// <summary>
    /// Tự động làm mới AccessToken qua POST https://oauth.zaloapp.com/v4/oa/access_token
    /// và lưu Zalo_AccessToken + Zalo_RefreshToken mới vào bảng SystemConfigs
    /// </summary>
    public async Task<bool> RefreshAccessTokenAsync(Dictionary<string, string>? preloadedConfigs = null)
    {
        var configs = preloadedConfigs ?? await LoadZaloConfigsAsync();
        string appId = configs.GetValueOrDefault("Zalo_AppId", string.Empty);
        string appSecret = configs.GetValueOrDefault("Zalo_AppSecret", string.Empty);
        string refreshToken = configs.GetValueOrDefault("Zalo_RefreshToken", string.Empty);

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("[ZaloOA OAuth] Thiếu Zalo_AppId hoặc Zalo_RefreshToken trong SystemConfigs.");
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("ZaloZnsClient");
            using var request = new HttpRequestMessage(HttpMethod.Post, ZaloOAuthEndpoint);
            if (!string.IsNullOrWhiteSpace(appSecret))
            {
                request.Headers.Add("secret_key", appSecret);
            }

            var formValues = new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["app_id"] = appId,
                ["grant_type"] = "refresh_token"
            };
            request.Content = new FormUrlEncodedContent(formValues);

            using var response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(body);

            string? newAccessToken = node?["access_token"]?.GetValue<string>();
            string? newRefreshToken = node?["refresh_token"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(newAccessToken))
            {
                _logger.LogWarning("[ZaloOA OAuth] Không lấy được access_token mới. Phản hồi: {Body}", body);
                return false;
            }

            await UpsertSystemConfigAsync("Zalo_AccessToken", newAccessToken);
            if (!string.IsNullOrWhiteSpace(newRefreshToken))
            {
                await UpsertSystemConfigAsync("Zalo_RefreshToken", newRefreshToken);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("[ZaloOA OAuth] Đã tự động làm mới Zalo_AccessToken và Zalo_RefreshToken trong SystemConfigs.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZaloOA OAuth] Lỗi khi làm mới AccessToken từ Zalo OAuth v4.");
            return false;
        }
    }

    private async Task<Dictionary<string, string>> LoadZaloConfigsAsync()
    {
        var list = await _context.SystemConfigs
            .AsNoTracking()
            .Where(c => c.ConfigKey.StartsWith("Zalo_") || c.ConfigKey == "Notification_PreferredChannel")
            .ToListAsync();

        return list.ToDictionary(c => c.ConfigKey, c => c.ConfigValue, StringComparer.OrdinalIgnoreCase);
    }

    private async Task UpsertSystemConfigAsync(string key, string value)
    {
        var existing = await _context.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (existing != null)
        {
            existing.ConfigValue = value;
            existing.UpdatedAt = DateTime.Now;
        }
        else
        {
            _context.SystemConfigs.Add(new SystemConfig
            {
                ConfigKey = key,
                ConfigValue = value,
                Description = $"Cấu hình động Zalo OA ({key})",
                UpdatedAt = DateTime.Now
            });
        }
    }

    private string GetCurrentBaseUrl()
    {
        var req = _httpContextAccessor.HttpContext?.Request;
        if (req != null && req.Host.HasValue)
        {
            return $"{req.Scheme}://{req.Host.Value}";
        }
        return "http://localhost:5164";
    }
}
