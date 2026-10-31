using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

public class CustomerCareController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly ILogger<CustomerCareController> _logger;

    public CustomerCareController(
        ApplicationDbContext context,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IWebHostEnvironment env,
        IEmailService emailService,
        ISmsService smsService,
        ILogger<CustomerCareController> logger)
    {
        _context = context;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _env = env;
        _emailService = emailService;
        _smsService = smsService;
        _logger = logger;
    }

    private (bool IsEmailConfigured, bool IsSmsConfigured, EmailSettings Email, SmsSettings Sms) GetGatewayState()
    {
        var email = new EmailSettings();
        _configuration.GetSection("EmailSettings").Bind(email);

        var sms = new SmsSettings();
        _configuration.GetSection("SmsSettings").Bind(sms);

        bool emailReady = !string.IsNullOrWhiteSpace(email.Username) &&
                          !string.IsNullOrWhiteSpace(email.Password) &&
                          !email.Username.Contains("your-email@example.com", StringComparison.OrdinalIgnoreCase) &&
                          !email.Password.Contains("your-smtp-app-password", StringComparison.OrdinalIgnoreCase);

        bool smsReady = !string.IsNullOrWhiteSpace(sms.AccountSid) &&
                        !string.IsNullOrWhiteSpace(sms.AuthToken) &&
                        !sms.AccountSid.Contains("TWILIO_ACCOUNT_SID", StringComparison.OrdinalIgnoreCase) &&
                        !sms.AuthToken.Contains("TWILIO_AUTH_TOKEN", StringComparison.OrdinalIgnoreCase);

        return (emailReady, smsReady, email, sms);
    }

    /// <summary>
    /// GET /CustomerCare/GetDashboardData
    /// Lấy danh sách kịch bản tự động (automation_rules), số lượng khách theo nhóm thực tế từ bảng customers,
    /// trạng thái kết nối cổng Gmail/SMS và lịch sử gửi tin gần nhất từ bảng customer_interactions.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDashboardData()
    {
        var rules = await _context.AutomationRules
            .OrderBy(r => r.RuleId)
            .Select(r => new
            {
                r.RuleId,
                r.RuleName,
                r.Description,
                r.Channel,
                r.TriggerType,
                r.TriggerOffsetMinutes,
                r.IsActive,
                r.MessageTemplate
            })
            .ToListAsync();

        var activeCustomersQuery = _context.Customers.Where(c => c.CustomerStatus == "Hoat_dong");
        var thirtyDaysAgo = DateTime.Now.Date.AddDays(-30);

        var allCount = await activeCustomersQuery.CountAsync();
        var vipCount = await activeCustomersQuery.CountAsync(c => c.CustomerType == "VIP");
        var loyalCount = await activeCustomersQuery.CountAsync(c => c.CustomerType == "Than_thiet");
        var newCount = await activeCustomersQuery.CountAsync(c => c.CustomerType == "Moi");
        var inactiveCount = await activeCustomersQuery.CountAsync(c => c.LastVisitAt == null || c.LastVisitAt < thirtyDaysAgo);

        var recentInteractions = await _context.CustomerInteractions
            .Include(i => i.Customer)
            .Include(i => i.Rule)
            .Include(i => i.Campaign)
            .OrderByDescending(i => i.SentAt)
            .Take(20)
            .Select(i => new
            {
                i.InteractionId,
                i.CustomerId,
                CustomerName = i.Customer != null ? i.Customer.FullName : "Khách hàng",
                i.Channel,
                i.Recipient,
                i.Content,
                i.Status,
                SentAt = i.SentAt.ToString("dd/MM/yyyy HH:mm"),
                SourceName = i.Rule != null
                    ? i.Rule.RuleName
                    : (i.Campaign != null ? i.Campaign.CampaignName : "Hệ thống"),
                i.ErrorMessage
            })
            .ToListAsync();

        var (isEmailConfigured, isSmsConfigured, emailCfg, smsCfg) = GetGatewayState();

        return Json(new
        {
            success = true,
            rules,
            groups = new
            {
                allCount,
                vipCount,
                loyalCount,
                newCount,
                inactiveCount
            },
            gateway = new
            {
                isEmailConfigured,
                isSmsConfigured,
                smtpServer = emailCfg.SmtpServer,
                smtpPort = emailCfg.SmtpPort,
                senderName = emailCfg.SenderName,
                senderEmail = isEmailConfigured ? emailCfg.SenderEmail : "",
                username = isEmailConfigured ? emailCfg.Username : "",
                hasEmailPassword = isEmailConfigured,
                accountSid = isSmsConfigured ? smsCfg.AccountSid : "",
                fromPhoneNumber = isSmsConfigured ? smsCfg.FromPhoneNumber : "",
                hasSmsAuthToken = isSmsConfigured
            },
            interactions = recentInteractions
        });
    }

    /// <summary>
    /// POST /CustomerCare/SaveGatewayConfig
    /// Lưu trực tiếp cấu hình Gmail SMTP (MailKit) và Twilio SMS vào appsettings.json và nạp lại cấu hình tức thì
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveGatewayConfig([FromBody] GatewayConfigDto dto)
    {
        if (dto == null)
            return BadRequest(new { success = false, message = "Dữ liệu cấu hình không hợp lệ." });

        try
        {
            string appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            string jsonText = await System.IO.File.ReadAllTextAsync(appSettingsPath);
            var rootNode = JsonNode.Parse(jsonText) ?? new JsonObject();

            var (_, _, currentEmail, currentSms) = GetGatewayState();

            var emailNode = rootNode["EmailSettings"] as JsonObject ?? new JsonObject();
            emailNode["SmtpServer"] = string.IsNullOrWhiteSpace(dto.SmtpServer) ? "smtp.gmail.com" : dto.SmtpServer.Trim();
            emailNode["SmtpPort"] = dto.SmtpPort > 0 ? dto.SmtpPort : 587;
            emailNode["SenderName"] = string.IsNullOrWhiteSpace(dto.SenderName) ? "SalonCare Spa & Beauty" : dto.SenderName.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                emailNode["Username"] = dto.Username.Trim();
                emailNode["SenderEmail"] = string.IsNullOrWhiteSpace(dto.SenderEmail)
                    ? dto.Username.Trim()
                    : dto.SenderEmail.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                // Xóa khoảng trắng nếu người dùng copy Mật khẩu ứng dụng 16 ký tự từ Google
                emailNode["Password"] = dto.Password.Replace(" ", "").Trim();
            }

            emailNode["EnableSsl"] = true;
            rootNode["EmailSettings"] = emailNode;

            var smsNode = rootNode["SmsSettings"] as JsonObject ?? new JsonObject();
            if (!string.IsNullOrWhiteSpace(dto.AccountSid))
                smsNode["AccountSid"] = dto.AccountSid.Trim();

            if (!string.IsNullOrWhiteSpace(dto.AuthToken))
                smsNode["AuthToken"] = dto.AuthToken.Trim();

            if (!string.IsNullOrWhiteSpace(dto.FromPhoneNumber))
                smsNode["FromPhoneNumber"] = dto.FromPhoneNumber.Trim();

            rootNode["SmsSettings"] = smsNode;

            var writeOptions = new JsonSerializerOptions { WriteIndented = true };
            await System.IO.File.WriteAllTextAsync(appSettingsPath, rootNode.ToJsonString(writeOptions));

            if (_configuration is IConfigurationRoot configRoot)
            {
                configRoot.Reload();
            }

            var (isEmailReady, isSmsReady, _, _) = GetGatewayState();

            return Json(new
            {
                success = true,
                isEmailConfigured = isEmailReady,
                isSmsConfigured = isSmsReady,
                message = "Đã lưu cấu hình Gmail SMTP & SMS Gateway thành công!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu cấu hình cổng gửi Email/SMS.");
            return StatusCode(500, new { success = false, message = "Không thể lưu cấu hình: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /CustomerCare/TestGateway
    /// Gửi thử nghiệm 1 Email hoặc 1 SMS thật để kiểm tra kết nối ngay trên giao diện
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> TestGateway([FromBody] TestGatewayDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Recipient))
            return BadRequest(new { success = false, message = "Vui lòng nhập Email hoặc SĐT nhận tin thử nghiệm." });

        string channel = (dto.Channel ?? "EMAIL").Trim().ToUpperInvariant();
        if (channel == "EMAIL")
        {
            var (ok, err) = await _emailService.SendEmailWithResultAsync(
                dto.Recipient.Trim(),
                "[SalonCare CRM] Kiểm tra kết nối gửi Gmail thành công ✨",
                "<p>Xin chào,</p><p>Đây là email kiểm tra kết nối SMTP từ hệ thống <b>SalonCare CRM</b>. Cổng gửi Email Marketing & Chăm sóc khách hàng của bạn đã hoạt động hoàn hảo!</p>",
                "Quản trị viên");

            return Json(new
            {
                success = ok,
                message = ok
                    ? $"Đã gửi Email thật thành công tới {dto.Recipient}! Vui lòng kiểm tra Hộp thư đến (hoặc mục Thư rác/Quảng cáo)."
                    : $"Gửi Email thất bại: {err}"
            });
        }
        else
        {
            var (ok, err) = await _smsService.SendSmsWithResultAsync(
                dto.Recipient.Trim(),
                "[SalonCare Spa] Tin nhan kiem tra ket noi SMS Gateway thanh cong!");

            return Json(new
            {
                success = ok,
                message = ok
                    ? $"Đã gửi SMS thật thành công tới số {dto.Recipient}!"
                    : $"Gửi SMS thất bại: {err}"
            });
        }
    }

    /// <summary>
    /// POST /CustomerCare/ToggleRule
    /// Bật/Tắt trạng thái is_active của kịch bản tự động trong bảng automation_rules
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ToggleRule([FromBody] ToggleRuleDto dto)
    {
        if (dto == null || dto.RuleId <= 0)
            return BadRequest(new { success = false, message = "Mã kịch bản không hợp lệ." });

        var rule = await _context.AutomationRules.FindAsync(dto.RuleId);
        if (rule == null)
            return NotFound(new { success = false, message = "Không tìm thấy kịch bản tự động." });

        rule.IsActive = dto.IsActive;
        rule.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            ruleId = rule.RuleId,
            isActive = rule.IsActive,
            message = rule.IsActive
                ? $"Đã kích hoạt kịch bản \"{rule.RuleName}\"."
                : $"Đã tạm dừng kịch bản \"{rule.RuleName}\"."
        });
    }

    /// <summary>
    /// POST /CustomerCare/SendCampaign
    /// Nhận thông tin chiến dịch gửi tin hàng loạt (channel, target_group, message_content),
    /// kiểm tra cấu hình cổng gửi, gửi thật qua SMTP Gmail / Twilio SMS và ghi nhật ký vào bảng customer_interactions.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SendCampaign([FromBody] SendCampaignDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.MessageContent))
        {
            return BadRequest(new { success = false, message = "Vui lòng nhập nội dung tin nhắn chiến dịch." });
        }

        string channel = (dto.Channel ?? "SMS").Trim().ToUpperInvariant();
        if (channel != "EMAIL" && channel != "SMS")
            channel = "SMS";

        // Kiểm tra ngay xem cổng Email hoặc SMS đã được cấu hình tài khoản thật chưa
        var (isEmailConfigured, isSmsConfigured, _, _) = GetGatewayState();
        if (channel == "EMAIL" && !isEmailConfigured)
        {
            return Json(new
            {
                success = false,
                needConfig = true,
                channel = "EMAIL",
                message = "Chưa cấu hình tài khoản Gmail & Mật khẩu ứng dụng (App Password 16 ký tự)! Vui lòng nhập cấu hình Gmail để gửi thư thật."
            });
        }

        if (channel == "SMS" && !isSmsConfigured)
        {
            return Json(new
            {
                success = false,
                needConfig = true,
                channel = "SMS",
                message = "Chưa cấu hình khóa API SMS (Twilio Account SID & Auth Token)! Vui lòng nhập cấu hình SMS để gửi tin nhắn thật."
            });
        }

        string targetGroup = (dto.TargetGroup ?? "ALL").Trim();
        var thirtyDaysAgo = DateTime.Now.Date.AddDays(-30);

        // 1. Truy vấn danh sách khách hàng từ bảng customers theo target_group
        var query = _context.Customers.Where(c => c.CustomerStatus == "Hoat_dong");

        switch (targetGroup.ToUpperInvariant())
        {
            case "VIP":
                query = query.Where(c => c.CustomerType == "VIP");
                break;
            case "THAN_THIET":
                query = query.Where(c => c.CustomerType == "Than_thiet");
                break;
            case "MOI":
                query = query.Where(c => c.CustomerType == "Moi");
                break;
            case "INACTIVE":
                query = query.Where(c => c.LastVisitAt == null || c.LastVisitAt < thirtyDaysAgo);
                break;
            case "ALL":
            default:
                break;
        }

        var recipients = await query
            .Select(c => new
            {
                c.CustomerId,
                c.FullName,
                c.Phone,
                c.Email
            })
            .ToListAsync();

        if (!recipients.Any())
        {
            return Json(new
            {
                success = false,
                message = "Không có khách hàng nào thuộc nhóm đã chọn trong CSDL."
            });
        }

        long? currentUserId = null;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(userIdClaim, out var parsedId))
            currentUserId = parsedId;

        // 2. Lưu thông tin chiến dịch vào bảng campaigns
        var campaign = new Campaign
        {
            CampaignName = $"Chiến dịch {channel} - Nhóm {targetGroup} ({DateTime.Now:dd/MM HH:mm})",
            Channel = channel,
            TargetGroup = targetGroup,
            MessageContent = dto.MessageContent.Trim(),
            TotalRecipients = recipients.Count,
            Status = "PROCESSING",
            CreatedBy = currentUserId,
            CreatedAt = DateTime.Now
        };

        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        long campaignId = campaign.CampaignId;
        string rawTemplate = dto.MessageContent.Trim();

        int successCount = 0;
        int failedCount = 0;
        string? firstError = null;

        // 3. Thực thi gửi Email/SMS và ghi log vào bảng customer_interactions
        var dispatchTask = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var smsService = scope.ServiceProvider.GetRequiredService<ISmsService>();

                foreach (var cust in recipients)
                {
                    string personalizedMsg = rawTemplate
                        .Replace("{full_name}", cust.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("{phone}", cust.Phone ?? "", StringComparison.OrdinalIgnoreCase)
                        .Replace("{voucher_code}", $"SPA{cust.CustomerId:D4}", StringComparison.OrdinalIgnoreCase);

                    string recipientAddress = channel == "EMAIL"
                        ? (cust.Email ?? "").Trim()
                        : (cust.Phone ?? "").Trim();

                    bool isSent = false;
                    string? errorMsg = null;

                    if (string.IsNullOrWhiteSpace(recipientAddress))
                    {
                        isSent = false;
                        errorMsg = channel == "EMAIL"
                            ? "Khách hàng chưa cập nhật địa chỉ Email trong hồ sơ."
                            : "Khách hàng chưa có số điện thoại.";
                    }
                    else if (channel == "EMAIL")
                    {
                        (isSent, errorMsg) = await emailService.SendEmailWithResultAsync(
                            recipientAddress,
                            "Thông báo ưu đãi & Chăm sóc từ SalonCare Spa ✨",
                            personalizedMsg,
                            cust.FullName);
                    }
                    else
                    {
                        var plainText = System.Text.RegularExpressions.Regex.Replace(personalizedMsg, "<.*?>", string.Empty);
                        (isSent, errorMsg) = await smsService.SendSmsWithResultAsync(recipientAddress, plainText);
                    }

                    if (isSent)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failedCount);
                        firstError ??= errorMsg;
                    }

                    db.CustomerInteractions.Add(new CustomerInteraction
                    {
                        CustomerId = cust.CustomerId,
                        CampaignId = campaignId,
                        Channel = channel,
                        Recipient = string.IsNullOrWhiteSpace(recipientAddress)
                            ? (channel == "EMAIL" ? "(Chưa có Email)" : "(Chưa có SĐT)")
                            : recipientAddress,
                        Content = personalizedMsg,
                        Status = isSent ? "SUCCESS" : "FAILED",
                        SentAt = DateTime.Now,
                        ErrorMessage = errorMsg
                    });
                }

                var campaignRecord = await db.Campaigns.FindAsync(campaignId);
                if (campaignRecord != null)
                {
                    campaignRecord.SuccessCount = successCount;
                    campaignRecord.FailedCount = failedCount;
                    campaignRecord.Status = failedCount == recipients.Count ? "FAILED" : "COMPLETED";
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong quá trình thực thi Background Campaign #{CampaignId}", campaignId);
                firstError ??= ex.Message;
            }
        });

        // Chờ tối đa 15 giây để trả về kết quả gửi thật chính xác cho người dùng
        await Task.WhenAny(dispatchTask, Task.Delay(15000));

        if (dispatchTask.IsCompleted && successCount == 0 && failedCount > 0)
        {
            return Json(new
            {
                success = false,
                campaignId,
                totalRecipients = recipients.Count,
                successCount,
                failedCount,
                message = $"Gửi thất bại ({failedCount}/{recipients.Count} khách). Nguyên nhân: {firstError}"
            });
        }

        return Json(new
        {
            success = true,
            campaignId,
            totalRecipients = recipients.Count,
            successCount,
            failedCount,
            channel,
            message = failedCount > 0
                ? $"Đã gửi thành công {successCount}/{recipients.Count} tin {channel} (Thất bại {failedCount} khách do thiếu thông tin hoặc lỗi cổng gửi)."
                : $"Đã gửi thành công {successCount}/{recipients.Count} tin nhắn {channel} tới khách hàng!"
        });
    }
}

public class ToggleRuleDto
{
    public int RuleId { get; set; }
    public bool IsActive { get; set; }
}

public class SendCampaignDto
{
    public string Channel { get; set; } = "SMS";
    public string TargetGroup { get; set; } = "ALL";
    public string MessageContent { get; set; } = string.Empty;
}

public class GatewayConfigDto
{
    public string SmtpServer { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public string SenderName { get; set; } = "SalonCare Spa & Beauty";
    public string SenderEmail { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }

    public string AccountSid { get; set; } = string.Empty;
    public string? AuthToken { get; set; }
    public string FromPhoneNumber { get; set; } = string.Empty;
}

public class TestGatewayDto
{
    public string Channel { get; set; } = "EMAIL";
    public string Recipient { get; set; } = string.Empty;
}
