using CRM_V2.Models;
using CRM_V2.ViewModels;

namespace CRM_V2.Services;

public interface IAppointmentService
{
    Task<Appointment> CreateAppointmentAsync(AppointmentCreateViewModel model, long? currentUserId);
    Task<(bool Success, string Message, Appointment? Appointment)> UpdateStatusAsync(long appointmentId, string newStatus, string? cancellationReason = null);
    Task<(bool Success, string Message)> RescheduleAsync(AppointmentRescheduleViewModel model);
    Task<(bool Success, string Message)> CancelAppointmentAsync(long appointmentId, string cancellationReason);
}
