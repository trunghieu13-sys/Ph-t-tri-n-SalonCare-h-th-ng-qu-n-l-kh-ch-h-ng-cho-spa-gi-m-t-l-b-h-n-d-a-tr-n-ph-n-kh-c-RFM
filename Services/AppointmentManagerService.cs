using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class AppointmentManagerService : IAppointmentService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AppointmentManagerService> _logger;

    public AppointmentManagerService(ApplicationDbContext context, ILogger<AppointmentManagerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Appointment> CreateAppointmentAsync(AppointmentCreateViewModel model, long? currentUserId)
    {
        // 1. Parse thời gian bắt đầu và kết thúc
        if (!TimeSpan.TryParse(model.StartTime, out var startTime))
            startTime = new TimeSpan(9, 0, 0);

        if (!TimeSpan.TryParse(model.EndTime, out var endTime))
            endTime = startTime.Add(TimeSpan.FromHours(1));

        // 2. Truy vấn danh sách dịch vụ được chọn để tính tổng tiền chính xác từ CSDL
        var distinctServiceIds = model.ServiceIds.Distinct().ToList();
        var selectedServices = await _context.Services
            .Where(s => distinctServiceIds.Contains(s.ServiceId))
            .ToListAsync();

        decimal totalPrice = selectedServices.Sum(s => s.Price);

        // 3. Khởi tạo đối tượng Appointment
        var appointment = new Appointment
        {
            CustomerId = model.CustomerId,
            EmployeeId = model.EmployeeId,
            AppointmentDate = model.AppointmentDate.Date,
            StartTime = startTime,
            EndTime = endTime,
            Status = "Pending",
            BookingSource = string.IsNullOrWhiteSpace(model.BookingSource) ? "Direct" : model.BookingSource,
            DepositAmount = model.DepositAmount < 0 ? 0 : model.DepositAmount,
            TotalPrice = totalPrice,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            CreatedBy = currentUserId,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        // 4. Lưu vào bảng trung gian appointment_services
        foreach (var srv in selectedServices)
        {
            _context.AppointmentServices.Add(new AppointmentService
            {
                AppointmentId = appointment.AppointmentId,
                ServiceId = srv.ServiceId,
                Price = srv.Price,
                EmployeeId = model.EmployeeId
            });
        }

        // 5. Cập nhật last_booking_at cho khách hàng
        var customer = await _context.Customers.FindAsync(model.CustomerId);
        if (customer != null)
        {
            customer.LastBookingAt = DateTime.Now;
            customer.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Đã tạo mới lịch hẹn ID={Id} cho khách hàng ID={CustId}, Tổng tiền={Total:N0}", 
            appointment.AppointmentId, appointment.CustomerId, appointment.TotalPrice);

        return appointment;
    }

    public async Task<(bool Success, string Message, Appointment? Appointment)> UpdateStatusAsync(
        long appointmentId, string newStatus, string? cancellationReason = null)
    {
        var validStatuses = new[] { "Pending", "Confirmed", "InService", "Completed", "NoShow", "Cancelled" };
        if (!validStatuses.Contains(newStatus))
        {
            return (false, $"Trạng thái '{newStatus}' không hợp lệ.", null);
        }

        var appointment = await _context.Appointments
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

        if (appointment == null)
        {
            return (false, "Không tìm thấy lịch hẹn cần cập nhật.", null);
        }

        var previousStatus = appointment.Status;
        var now = DateTime.Now;

        appointment.Status = newStatus;
        appointment.UpdatedAt = now;

        // Tự động cập nhật các mốc thời gian theo trạng thái mới
        switch (newStatus)
        {
            case "Confirmed":
                appointment.ConfirmedAt ??= now;
                break;

            case "InService":
                appointment.ConfirmedAt ??= now;
                appointment.CheckInAt ??= now;
                break;

            case "Completed":
                appointment.ConfirmedAt ??= now;
                appointment.CheckInAt ??= now;
                appointment.CompletedAt = now;

                // Nếu chuyển sang Hoàn thành lần đầu, tự động cộng điểm chi tiêu & lượt ghé cho khách
                if (previousStatus != "Completed" && appointment.Customer != null)
                {
                    appointment.Customer.TotalVisits += 1;
                    appointment.Customer.TotalSpent += appointment.TotalPrice;
                    appointment.Customer.LastVisitAt = now;
                    appointment.Customer.UpdatedAt = now;
                }
                break;

            case "Cancelled":
                appointment.CancelledAt = now;
                if (!string.IsNullOrWhiteSpace(cancellationReason))
                {
                    appointment.CancellationReason = cancellationReason.Trim();
                }
                break;

            case "NoShow":
                if (previousStatus != "NoShow" && appointment.Customer != null)
                {
                    appointment.Customer.NoShowCount += 1;
                    appointment.Customer.UpdatedAt = now;
                }
                break;
        }

        await _context.SaveChangesAsync();
        return (true, "Đã cập nhật trạng thái lịch hẹn thành công!", appointment);
    }

    public async Task<(bool Success, string Message)> RescheduleAsync(AppointmentRescheduleViewModel model)
    {
        var appointment = await _context.Appointments
            .Include(a => a.AppointmentServices)
            .FirstOrDefaultAsync(a => a.AppointmentId == model.AppointmentId);

        if (appointment == null)
        {
            return (false, "Không tìm thấy lịch hẹn.");
        }

        if (!TimeSpan.TryParse(model.StartTime, out var startTime))
            startTime = appointment.StartTime;

        if (!TimeSpan.TryParse(model.EndTime, out var endTime))
            endTime = appointment.EndTime;

        appointment.AppointmentDate = model.AppointmentDate.Date;
        appointment.StartTime = startTime;
        appointment.EndTime = endTime;
        appointment.EmployeeId = model.EmployeeId;
        appointment.DepositAmount = model.DepositAmount < 0 ? 0 : model.DepositAmount;
        appointment.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
        appointment.UpdatedAt = DateTime.Now;

        // Nếu có thay đổi danh sách dịch vụ
        if (model.ServiceIds != null && model.ServiceIds.Count > 0)
        {
            _context.AppointmentServices.RemoveRange(appointment.AppointmentServices);

            var distinctIds = model.ServiceIds.Distinct().ToList();
            var services = await _context.Services
                .Where(s => distinctIds.Contains(s.ServiceId))
                .ToListAsync();

            appointment.TotalPrice = services.Sum(s => s.Price);

            foreach (var s in services)
            {
                _context.AppointmentServices.Add(new AppointmentService
                {
                    AppointmentId = appointment.AppointmentId,
                    ServiceId = s.ServiceId,
                    Price = s.Price,
                    EmployeeId = model.EmployeeId
                });
            }
        }

        await _context.SaveChangesAsync();
        return (true, "Đổi lịch và cập nhật thông tin lịch hẹn thành công!");
    }

    public async Task<(bool Success, string Message)> CancelAppointmentAsync(long appointmentId, string cancellationReason)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
        {
            return (false, "Vui lòng nhập lý do hủy lịch hẹn.");
        }

        var result = await UpdateStatusAsync(appointmentId, "Cancelled", cancellationReason);
        return (result.Success, result.Success ? "Đã hủy lịch hẹn thành công." : result.Message);
    }
}
