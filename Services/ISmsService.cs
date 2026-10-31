namespace CRM_V2.Services;

public interface ISmsService
{
    /// <summary>
    /// Gửi SMS đơn lẻ qua Twilio hoặc eSMS
    /// </summary>
    Task<bool> SendSmsAsync(string toPhoneNumber, string message);

    /// <summary>
    /// Gửi SMS đơn lẻ kèm thông báo lỗi chi tiết nếu thất bại
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SendSmsWithResultAsync(string toPhoneNumber, string message);

    /// <summary>
    /// Gửi SMS tự động hàng loạt (SMS Automation / Broadcast)
    /// </summary>
    Task<bool> SendBulkSmsAsync(IEnumerable<string> phoneNumbers, string message);
}
