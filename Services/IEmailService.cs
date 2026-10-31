namespace CRM_V2.Services;

public interface IEmailService
{
    /// <summary>
    /// Gửi email đơn lẻ (hỗ trợ định dạng HTML)
    /// </summary>
    Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent, string? toName = null);

    /// <summary>
    /// Gửi email đơn lẻ kèm thông báo lỗi chi tiết nếu thất bại
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SendEmailWithResultAsync(string toEmail, string subject, string htmlContent, string? toName = null);

    /// <summary>
    /// Gửi email hàng loạt (Email Marketing / Campaign)
    /// </summary>
    Task<bool> SendBulkEmailAsync(IEnumerable<string> toEmails, string subject, string htmlContent);
}
