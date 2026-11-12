using CRM_V2.Models;

namespace CRM_V2.Services;

public interface INotificationSender
{
    string ChannelName { get; }
    Task<bool> SendNotificationAsync(
        Customer customer,
        Appointment? appointment,
        string messageType,
        string segmentAtSendTime,
        string content);
}
