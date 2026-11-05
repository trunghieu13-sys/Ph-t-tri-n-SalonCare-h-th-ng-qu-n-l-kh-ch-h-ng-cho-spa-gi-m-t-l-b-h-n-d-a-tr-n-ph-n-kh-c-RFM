using CRM_V2.Models;
using CRM_V2.ViewModels;

namespace CRM_V2.Services;

public interface IAppointmentService
{
    Task<AvailableEmployeesResultDto> GetAvailableEmployeesAsync(
        DateTime date,
        TimeSpan startTime,
        int durationMinutes,
        long? excludeAppointmentId = null,
        TimeSpan? explicitEndTime = null);

    Task<bool> CheckEmployeeAvailabilitySqlAsync(
        long employeeId,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        long? excludeAppointmentId = null);

    Task<AppointmentOperationResult> CreateAppointmentSafeAsync(AppointmentCreateViewModel model, long? currentUserId);
    Task<Appointment> CreateAppointmentAsync(AppointmentCreateViewModel model, long? currentUserId);
    Task<(bool Success, string Message, Appointment? Appointment)> UpdateStatusAsync(long appointmentId, string newStatus, string? cancellationReason = null);
    Task<AppointmentOperationResult> RescheduleSafeAsync(AppointmentRescheduleViewModel model);
    Task<(bool Success, string Message)> RescheduleAsync(AppointmentRescheduleViewModel model);
    Task<(bool Success, string Message)> CancelAppointmentAsync(long appointmentId, string cancellationReason);
}
