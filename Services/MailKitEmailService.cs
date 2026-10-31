using CRM_V2.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CRM_V2.Services;

public class MailKitEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MailKitEmailService> _logger;

    public MailKitEmailService(IConfiguration configuration, ILogger<MailKitEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private EmailSettings GetCurrentSettings()
    {
        var settings = new EmailSettings();
        _configuration.GetSection("EmailSettings").Bind(settings);
        return settings;
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent, string? toName = null)
    {
        var (success, _) = await SendEmailWithResultAsync(toEmail, subject, htmlContent, toName);
        return success;
    }

    public async Task<(bool Success, string? ErrorMessage)> SendEmailWithResultAsync(string toEmail, string subject, string htmlContent, string? toName = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toEmail))
                return (false, "Khách hàng chưa có địa chỉ Email.");

            var settings = GetCurrentSettings();

            // Kiểm tra nếu chưa cấu hình tài khoản Gmail / SMTP thật
            if (string.IsNullOrWhiteSpace(settings.Username) ||
                string.IsNullOrWhiteSpace(settings.Password) ||
                settings.Username.Contains("your-email@example.com", StringComparison.OrdinalIgnoreCase) ||
                settings.Password.Contains("your-smtp-app-password", StringComparison.OrdinalIgnoreCase))
            {
                const string msg = "Chưa cấu hình tài khoản Gmail & Mật khẩu ứng dụng (App Password 16 ký tự). Vui lòng bấm 'Cấu hình Gmail & SMS' để nhập tài khoản gửi.";
                _logger.LogWarning("[EmailService] {Message}", msg);
                return (false, msg);
            }

            string senderEmail = string.IsNullOrWhiteSpace(settings.SenderEmail) ||
                                 settings.SenderEmail.Contains("your-email@example.com", StringComparison.OrdinalIgnoreCase)
                ? settings.Username.Trim()
                : settings.SenderEmail.Trim();

            string senderName = string.IsNullOrWhiteSpace(settings.SenderName)
                ? "SalonCare Spa & Beauty"
                : settings.SenderName.Trim();

            // Tự động loại bỏ khoảng trắng trong Mật khẩu ứng dụng 16 ký tự của Google (vd: "abcd efgh ijkl mnop" -> "abcdefghijklmnop")
            string cleanPassword = settings.Password.Replace(" ", "").Trim();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress(toName ?? toEmail, toEmail.Trim()));
            message.Subject = subject;

            // Bọc nội dung trong mẫu HTML chuyên nghiệp nếu người dùng chỉ nhập văn bản thuần
            string finalHtml = htmlContent.Contains("<html", StringComparison.OrdinalIgnoreCase) || htmlContent.Contains("<div", StringComparison.OrdinalIgnoreCase)
                ? htmlContent
                : $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border-radius: 12px; background: #0f172a; color: #f8fafc; border: 1px solid #334155;'>
                    <h2 style='color: #c084fc; margin-top: 0;'>✨ {senderName}</h2>
                    <div style='font-size: 15px; line-height: 1.6; color: #e2e8f0; white-space: pre-line;'>{htmlContent}</div>
                    <hr style='border: none; border-top: 1px solid #334155; margin: 20px 0;' />
                    <p style='font-size: 12px; color: #94a3b8; margin-bottom: 0;'>Tin nhắn được gửi từ hệ thống Chăm sóc khách hàng SalonCare CRM.</p>
                </div>";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = finalHtml,
                TextBody = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<.*?>", string.Empty)
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 15000;

            var secureSocketOptions = settings.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : (settings.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

            await client.ConnectAsync(settings.SmtpServer.Trim(), settings.SmtpPort, secureSocketOptions);
            await client.AuthenticateAsync(settings.Username.Trim(), cleanPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Đã gửi Email THẬT thành công tới {ToEmail} với tiêu đề: {Subject}", toEmail, subject);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi Email tới {ToEmail} thất bại. Lỗi: {Message}", toEmail, ex.Message);
            string friendlyError = ex.Message.Contains("5.7.8", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("Username and Password not accepted", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                ? "Sai tài khoản Gmail hoặc Mật khẩu ứng dụng (Lưu ý: Gmail bắt buộc dùng Mật khẩu ứng dụng 16 ký tự trong Bảo mật Google, không dùng mật khẩu đăng nhập thường). Chi tiết: " + ex.Message
                : ex.Message;

            return (false, friendlyError);
        }
    }

    public async Task<bool> SendBulkEmailAsync(IEnumerable<string> toEmails, string subject, string htmlContent)
    {
        var emailList = toEmails.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
        if (!emailList.Any()) return true;

        var allSuccessful = true;
        foreach (var email in emailList)
        {
            var result = await SendEmailAsync(email, subject, htmlContent);
            if (!result)
            {
                allSuccessful = false;
            }
        }

        return allSuccessful;
    }
}
