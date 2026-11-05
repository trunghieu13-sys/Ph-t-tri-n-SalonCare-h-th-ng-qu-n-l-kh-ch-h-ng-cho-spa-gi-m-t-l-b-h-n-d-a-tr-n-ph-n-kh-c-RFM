using System.Data;
using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class AppointmentManagerService : IAppointmentService
{
    private static readonly string[] ActiveBlockingStatuses = { "Pending", "Confirmed", "InService" };

    private readonly ApplicationDbContext _context;
    private readonly ILogger<AppointmentManagerService> _logger;

    public AppointmentManagerService(ApplicationDbContext context, ILogger<AppointmentManagerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Công thức toán học kiểm tra 2 khoảng thời gian [startA, endA] và [startB, endB] bị trùng nhau:
    /// Start_A < End_B AND End_A > Start_B
    /// </summary>
    public static bool IsTimeOverlapping(TimeSpan startA, TimeSpan endA, TimeSpan startB, TimeSpan endB)
    {
        return startA < endB && endA > startB;
    }

    /// <summary>
    /// Gọi trực tiếp hàm T-SQL dbo.fn_CheckEmployeeAvailability trong SQL Server
    /// Trả về true nếu KTV rảnh (1), false nếu bị trùng lịch (0)
    /// </summary>
    public async Task<bool> CheckEmployeeAvailabilitySqlAsync(
        long employeeId,
        DateTime appointmentDate,
        TimeSpan startTime,
        TimeSpan endTime,
        long? excludeAppointmentId = null)
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            bool shouldClose = false;
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
                shouldClose = true;
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT dbo.fn_CheckEmployeeAvailability(@EmployeeId, @AppointmentDate, @StartTime, @EndTime, @ExcludeAppointmentId)";

                var pEmp = new SqlParameter("@EmployeeId", SqlDbType.BigInt) { Value = employeeId };
                var pDate = new SqlParameter("@AppointmentDate", SqlDbType.Date) { Value = appointmentDate.Date };
                var pStart = new SqlParameter("@StartTime", SqlDbType.Time) { Value = startTime };
                var pEnd = new SqlParameter("@EndTime", SqlDbType.Time) { Value = endTime };
                var pExcl = new SqlParameter("@ExcludeAppointmentId", SqlDbType.BigInt)
                {
                    Value = excludeAppointmentId.HasValue ? excludeAppointmentId.Value : DBNull.Value
                };

                cmd.Parameters.Add(pEmp);
                cmd.Parameters.Add(pDate);
                cmd.Parameters.Add(pStart);
                cmd.Parameters.Add(pEnd);
                cmd.Parameters.Add(pExcl);

                var scalar = await cmd.ExecuteScalarAsync();
                if (scalar != null && scalar != DBNull.Value)
                {
                    return Convert.ToBoolean(scalar);
                }
            }
            finally
            {
                if (shouldClose && conn.State == ConnectionState.Open)
                {
                    await conn.CloseAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback sang EF Core LINQ khi gọi fn_CheckEmployeeAvailability cho EmployeeId={EmpId}", employeeId);
        }

        // Fallback C# LINQ nếu hàm SQL chưa khởi tạo
        bool hasConflict = await _context.Appointments
            .AsNoTracking()
            .AnyAsync(a =>
                a.EmployeeId == employeeId &&
                a.AppointmentDate == appointmentDate.Date &&
                ActiveBlockingStatuses.Contains(a.Status) &&
                (!excludeAppointmentId.HasValue || a.AppointmentId != excludeAppointmentId.Value) &&
                a.StartTime < endTime &&
                a.EndTime > startTime);

        return !hasConflict;
    }

    /// <summary>
    /// Lấy danh sách tất cả KTV/Nhân viên đang hoạt động kèm trạng thái Rảnh / Bận
    /// đối chiếu trong khoảng [startTime, startTime + durationMinutes] và gợi ý các khung giờ trống gần nhất
    /// </summary>
    public async Task<AvailableEmployeesResultDto> GetAvailableEmployeesAsync(
        DateTime date,
        TimeSpan startTime,
        int durationMinutes,
        long? excludeAppointmentId = null,
        TimeSpan? explicitEndTime = null)
    {
        var targetDate = date.Date;
        if (durationMinutes <= 0)
            durationMinutes = 60;

        TimeSpan endTime;
        if (explicitEndTime.HasValue && explicitEndTime.Value > startTime)
        {
            endTime = explicitEndTime.Value;
            durationMinutes = (int)Math.Round((endTime - startTime).TotalMinutes);
        }
        else
        {
            endTime = startTime.Add(TimeSpan.FromMinutes(durationMinutes));
            if (endTime > new TimeSpan(23, 59, 0))
            {
                endTime = new TimeSpan(23, 59, 0);
            }
        }

        // 1. Lấy tất cả KTV / Nhân viên đang hoạt động (Ưu tiên hiển thị KTV role_id = 3 lên đầu)
        var employees = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.Status == "active")
            .OrderByDescending(u => u.RoleId)
            .ThenBy(u => u.FullName)
            .ToListAsync();

        // 2. Lấy toàn bộ lịch hẹn đang giữ chỗ ('Pending', 'Confirmed', 'InService') trong ngày đó
        var dayAppointments = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Customer)
            .Where(a =>
                a.AppointmentDate == targetDate &&
                ActiveBlockingStatuses.Contains(a.Status) &&
                (!excludeAppointmentId.HasValue || a.AppointmentId != excludeAppointmentId.Value))
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        var empDtos = new List<EmployeeAvailabilityDto>();

        foreach (var emp in employees)
        {
            // Kiểm tra xung đột: Start_A < End_B AND End_A > Start_B
            var conflict = dayAppointments.FirstOrDefault(a =>
                a.EmployeeId == emp.UserId &&
                IsTimeOverlapping(startTime, endTime, a.StartTime, a.EndTime));

            empDtos.Add(new EmployeeAvailabilityDto
            {
                Id = emp.UserId,
                Name = emp.FullName,
                Specialty = string.IsNullOrWhiteSpace(emp.Specialty) ? "Chăm sóc tổng hợp" : emp.Specialty,
                Role = emp.Role?.RoleName ?? "staff",
                IsAvailable = conflict == null,
                ConflictAppointmentId = conflict?.AppointmentId,
                ConflictStartTime = conflict?.StartTime.ToString(@"hh\:mm"),
                ConflictEndTime = conflict?.EndTime.ToString(@"hh\:mm"),
                ConflictStatus = conflict?.Status,
                ConflictCustomerName = conflict?.Customer?.FullName
            });
        }

        int availableCount = empDtos.Count(e => e.IsAvailable);
        int busyCount = empDtos.Count - availableCount;
        bool allBusy = empDtos.Count > 0 && availableCount == 0;

        // 3. Tính toán danh sách các khung giờ còn trống gần nhất trong ngày (08:00 -> 20:30)
        var suggestedSlots = CalculateNearestAvailableSlots(
            employees.Select(e => e.UserId).ToList(),
            dayAppointments,
            startTime,
            durationMinutes);

        string? statusMsg = allBusy
            ? "Tất cả KTV đều đang có lịch tại thời gian này, vui lòng chọn thời gian khác"
            : $"Có {availableCount}/{empDtos.Count} KTV đang sẵn sàng trong khung giờ {startTime:hh\\:mm} - {endTime:hh\\:mm}.";

        return new AvailableEmployeesResultDto
        {
            Success = true,
            Date = targetDate.ToString("yyyy-MM-dd"),
            StartTime = startTime.ToString(@"hh\:mm"),
            EndTime = endTime.ToString(@"hh\:mm"),
            DurationMinutes = durationMinutes,
            TotalEmployees = empDtos.Count,
            AvailableCount = availableCount,
            BusyCount = busyCount,
            AllBusy = allBusy,
            Message = statusMsg,
            Employees = empDtos,
            SuggestedSlots = suggestedSlots
        };
    }

    /// <summary>
    /// Tìm tối đa 5 khung giờ trống gần nhất trong ngày (từ 08:00 đến 20:30, bước nhảy 30 phút)
    /// mà có ít nhất 1 KTV đang rảnh (hoặc KTV được chỉ định đang rảnh)
    /// </summary>
    private static List<string> CalculateNearestAvailableSlots(
        List<long> activeEmployeeIds,
        List<Appointment> dayAppointments,
        TimeSpan requestedStart,
        int durationMinutes,
        long? specificEmployeeId = null)
    {
        var candidateStarts = new List<TimeSpan>();
        for (int minutes = 8 * 60; minutes <= 20 * 60; minutes += 30)
        {
            var slotStart = TimeSpan.FromMinutes(minutes);
            var slotEnd = slotStart.Add(TimeSpan.FromMinutes(durationMinutes));
            if (slotEnd <= new TimeSpan(21, 30, 0) && slotStart != requestedStart)
            {
                candidateStarts.Add(slotStart);
            }
        }

        // Sắp xếp ưu tiên các khung giờ gần với giờ người dùng đang chọn nhất
        var sortedCandidates = candidateStarts
            .OrderBy(slot => Math.Abs((slot - requestedStart).TotalMinutes))
            .ThenBy(slot => slot)
            .ToList();

        var availableSlots = new List<string>();
        var targetEmpIds = specificEmployeeId.HasValue && specificEmployeeId.Value > 0
            ? new List<long> { specificEmployeeId.Value }
            : activeEmployeeIds;

        foreach (var slotStart in sortedCandidates)
        {
            var slotEnd = slotStart.Add(TimeSpan.FromMinutes(durationMinutes));

            // Kiểm tra xem có ít nhất 1 KTV trong danh sách rảnh suốt khoảng [slotStart, slotEnd] không
            bool anyFree = targetEmpIds.Any(empId =>
                !dayAppointments.Any(a =>
                    a.EmployeeId == empId &&
                    IsTimeOverlapping(slotStart, slotEnd, a.StartTime, a.EndTime)));

            if (anyFree)
            {
                availableSlots.Add($"{slotStart:hh\\:mm} - {slotEnd:hh\\:mm}");
                if (availableSlots.Count >= 5)
                    break;
            }
        }

        return availableSlots;
    }

    /// <summary>
    /// Tạo lịch hẹn mới an toàn với Serializable Transaction chống Race Condition
    /// và tự động tính end_time = start_time + SUM(service.duration_minutes)
    /// </summary>
    public async Task<AppointmentOperationResult> CreateAppointmentSafeAsync(AppointmentCreateViewModel model, long? currentUserId)
    {
        if (!TimeSpan.TryParse(model.StartTime, out var startTime))
            startTime = new TimeSpan(9, 0, 0);

        var distinctServiceIds = (model.ServiceIds ?? new List<int>()).Distinct().ToList();
        if (distinctServiceIds.Count == 0)
        {
            return new AppointmentOperationResult
            {
                Success = false,
                Code = "INVALID_INPUT",
                Message = "Vui lòng chọn ít nhất 1 dịch vụ cho lịch hẹn."
            };
        }

        // Mở Transaction với mức cô lập Serializable để khóa phạm vi đọc/ghi, chống Race Condition tuyệt đối
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            // 1. Lấy danh sách dịch vụ từ CSDL để tính tổng tiền VÀ tổng thời lượng (duration_minutes)
            var selectedServices = await _context.Services
                .Where(s => distinctServiceIds.Contains(s.ServiceId))
                .ToListAsync();

            if (selectedServices.Count == 0)
            {
                await transaction.RollbackAsync();
                return new AppointmentOperationResult
                {
                    Success = false,
                    Code = "INVALID_INPUT",
                    Message = "Dịch vụ được chọn không tồn tại hoặc đã ngừng kinh doanh."
                };
            }

            int totalDurationMinutes = selectedServices.Sum(s => s.DurationMinutes);
            if (totalDurationMinutes <= 0)
                totalDurationMinutes = 60;

            // Tự động tính toán end_time dựa trên tổng thời lượng các dịch vụ được chọn
            TimeSpan endTime = startTime.Add(TimeSpan.FromMinutes(totalDurationMinutes));
            if (endTime > new TimeSpan(23, 59, 0))
            {
                endTime = new TimeSpan(23, 59, 0);
            }

            decimal totalPrice = selectedServices.Sum(s => s.Price);
            var targetDate = model.AppointmentDate.Date;

            // 2. Truy vấn toàn bộ KTV đang active và các lịch hẹn đang giữ chỗ trong ngày (trong Transaction Serializable)
            var activeEmployees = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Status == "active")
                .OrderByDescending(u => u.RoleId)
                .ThenBy(u => u.FullName)
                .ToListAsync();

            var dayAppointments = await _context.Appointments
                .Include(a => a.Customer)
                .Where(a =>
                    a.AppointmentDate == targetDate &&
                    ActiveBlockingStatuses.Contains(a.Status))
                .OrderBy(a => a.StartTime)
                .ToListAsync();

            // Đánh giá trạng thái từng KTV tại khung giờ [startTime, endTime]
            var empAvailabilityList = activeEmployees.Select(emp =>
            {
                var conflict = dayAppointments.FirstOrDefault(a =>
                    a.EmployeeId == emp.UserId &&
                    IsTimeOverlapping(startTime, endTime, a.StartTime, a.EndTime));

                return new EmployeeAvailabilityDto
                {
                    Id = emp.UserId,
                    Name = emp.FullName,
                    Specialty = string.IsNullOrWhiteSpace(emp.Specialty) ? "Chăm sóc tổng hợp" : emp.Specialty,
                    Role = emp.Role?.RoleName ?? "staff",
                    IsAvailable = conflict == null,
                    ConflictAppointmentId = conflict?.AppointmentId,
                    ConflictStartTime = conflict?.StartTime.ToString(@"hh\:mm"),
                    ConflictEndTime = conflict?.EndTime.ToString(@"hh\:mm"),
                    ConflictStatus = conflict?.Status,
                    ConflictCustomerName = conflict?.Customer?.FullName
                };
            }).ToList();

            var freeEmployees = empAvailabilityList.Where(e => e.IsAvailable).ToList();

            // 3. KIỂM TRA TRƯỜNG HỢP TẤT CẢ KTV ĐỀU BẬN
            if (activeEmployees.Count > 0 && freeEmployees.Count == 0)
            {
                var suggestedAll = CalculateNearestAvailableSlots(
                    activeEmployees.Select(e => e.UserId).ToList(),
                    dayAppointments,
                    startTime,
                    totalDurationMinutes);

                await transaction.RollbackAsync();
                return new AppointmentOperationResult
                {
                    Success = false,
                    Code = "ALL_BUSY",
                    Message = "Tất cả KTV đều đang có lịch tại thời gian này, vui lòng chọn thời gian khác",
                    CalculatedStartTime = startTime.ToString(@"hh\:mm"),
                    CalculatedEndTime = endTime.ToString(@"hh\:mm"),
                    TotalDurationMinutes = totalDurationMinutes,
                    SuggestedSlots = suggestedAll,
                    AvailableEmployees = empAvailabilityList
                };
            }

            long? assignedEmployeeId = model.EmployeeId;

            // 4. KIỂM TRA NẾU NGƯỜI DÙNG CHỌN CỤ THỂ 1 KTV
            if (assignedEmployeeId.HasValue && assignedEmployeeId.Value > 0)
            {
                var selectedEmpStatus = empAvailabilityList.FirstOrDefault(e => e.Id == assignedEmployeeId.Value);
                if (selectedEmpStatus == null)
                {
                    await transaction.RollbackAsync();
                    return new AppointmentOperationResult
                    {
                        Success = false,
                        Code = "INVALID_INPUT",
                        Message = "KTV được chọn không tồn tại hoặc đang tạm ngưng hoạt động."
                    };
                }

                if (!selectedEmpStatus.IsAvailable)
                {
                    var suggestedForEmp = CalculateNearestAvailableSlots(
                        activeEmployees.Select(e => e.UserId).ToList(),
                        dayAppointments,
                        startTime,
                        totalDurationMinutes,
                        assignedEmployeeId.Value);

                    await transaction.RollbackAsync();
                    return new AppointmentOperationResult
                    {
                        Success = false,
                        Code = "EMPLOYEE_BUSY",
                        Message = $"KTV {selectedEmpStatus.Name} đã có lịch hẹn từ {selectedEmpStatus.ConflictStartTime} đến {selectedEmpStatus.ConflictEndTime}. Vui lòng chọn KTV khác hoặc đổi khung giờ!",
                        CalculatedStartTime = startTime.ToString(@"hh\:mm"),
                        CalculatedEndTime = endTime.ToString(@"hh\:mm"),
                        TotalDurationMinutes = totalDurationMinutes,
                        SuggestedSlots = suggestedForEmp,
                        AvailableEmployees = empAvailabilityList
                    };
                }
            }
            else
            {
                // 5. NẾU CHỌN TỰ ĐỘNG ĐIỀU PHỐI -> Phân công tự động cho KTV đang rảnh có ít ca nhất trong ngày
                var bestCandidate = freeEmployees
                    .OrderBy(e => e.Role == "staff" ? 0 : 1)
                    .ThenBy(e => dayAppointments.Count(a => a.EmployeeId == e.Id))
                    .FirstOrDefault();

                if (bestCandidate != null)
                {
                    assignedEmployeeId = bestCandidate.Id;
                }
            }

            // 6. Khởi tạo và lưu đối tượng Appointment
            var appointment = new Appointment
            {
                CustomerId = model.CustomerId,
                EmployeeId = assignedEmployeeId,
                AppointmentDate = targetDate,
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

            // 7. Lưu chi tiết dịch vụ vào bảng appointment_services
            foreach (var srv in selectedServices)
            {
                _context.AppointmentServices.Add(new AppointmentService
                {
                    AppointmentId = appointment.AppointmentId,
                    ServiceId = srv.ServiceId,
                    Price = srv.Price,
                    EmployeeId = assignedEmployeeId
                });
            }

            // 8. Cập nhật last_booking_at cho khách hàng
            var customer = await _context.Customers.FindAsync(model.CustomerId);
            if (customer != null)
            {
                customer.LastBookingAt = DateTime.Now;
                customer.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var assignedName = empAvailabilityList.FirstOrDefault(e => e.Id == assignedEmployeeId)?.Name ?? "Tự động";
            _logger.LogInformation(
                "Đã tạo lịch hẹn ID={Id}, Khách={CustId}, KTV={EmpName} ({EmpId}), Giờ={Start}-{End} ({Mins}p), Tổng={Total:N0}",
                appointment.AppointmentId, appointment.CustomerId, assignedName, assignedEmployeeId,
                startTime.ToString(@"hh\:mm"), endTime.ToString(@"hh\:mm"), totalDurationMinutes, appointment.TotalPrice);

            return new AppointmentOperationResult
            {
                Success = true,
                Code = "OK",
                Message = $"Tạo lịch hẹn thành công! KTV phụ trách: {assignedName} ({startTime:hh\\:mm} - {endTime:hh\\:mm}).",
                Appointment = appointment,
                CalculatedStartTime = startTime.ToString(@"hh\:mm"),
                CalculatedEndTime = endTime.ToString(@"hh\:mm"),
                TotalDurationMinutes = totalDurationMinutes,
                AvailableEmployees = empAvailabilityList
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lỗi Transaction khi tạo lịch hẹn mới");
            throw;
        }
    }

    public async Task<Appointment> CreateAppointmentAsync(AppointmentCreateViewModel model, long? currentUserId)
    {
        var result = await CreateAppointmentSafeAsync(model, currentUserId);
        if (!result.Success || result.Appointment == null)
        {
            throw new InvalidOperationException(result.Message);
        }
        return result.Appointment;
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
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);

        if (appointment == null)
        {
            return (false, "Không tìm thấy lịch hẹn cần cập nhật.", null);
        }

        // Nếu chuyển sang trạng thái chiếm lịch (Pending, Confirmed, InService) và đã gán KTV, kiểm tra không cho trùng lịch
        if (ActiveBlockingStatuses.Contains(newStatus) && appointment.EmployeeId.HasValue)
        {
            var conflict = await _context.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.AppointmentId != appointment.AppointmentId &&
                    a.EmployeeId == appointment.EmployeeId.Value &&
                    a.AppointmentDate == appointment.AppointmentDate &&
                    ActiveBlockingStatuses.Contains(a.Status) &&
                    a.StartTime < appointment.EndTime &&
                    a.EndTime > appointment.StartTime);

            if (conflict != null)
            {
                string empName = appointment.Employee?.FullName ?? $"#{appointment.EmployeeId.Value}";
                return (false, $"KTV {empName} đã có lịch hẹn #{conflict.AppointmentId} từ {conflict.StartTime:hh\\:mm} đến {conflict.EndTime:hh\\:mm}. Vui lòng đổi KTV hoặc khung giờ trước khi chuyển trạng thái!", null);
            }
        }

        var previousStatus = appointment.Status;
        var now = DateTime.Now;

        appointment.Status = newStatus;
        appointment.UpdatedAt = now;

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

    /// <summary>
    /// Đổi lịch / Điều phối lại KTV có kiểm tra xung đột thời gian trong Serializable Transaction
    /// </summary>
    public async Task<AppointmentOperationResult> RescheduleSafeAsync(AppointmentRescheduleViewModel model)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var appointment = await _context.Appointments
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .FirstOrDefaultAsync(a => a.AppointmentId == model.AppointmentId);

            if (appointment == null)
            {
                await transaction.RollbackAsync();
                return new AppointmentOperationResult
                {
                    Success = false,
                    Code = "INVALID_INPUT",
                    Message = "Không tìm thấy lịch hẹn cần cập nhật."
                };
            }

            if (!TimeSpan.TryParse(model.StartTime, out var startTime))
                startTime = appointment.StartTime;

            if (!TimeSpan.TryParse(model.EndTime, out var endTime) || endTime <= startTime)
            {
                int currentDuration = appointment.AppointmentServices
                    .Where(s => !s.IsHidden && s.Service != null)
                    .Sum(s => s.Service!.DurationMinutes);
                if (currentDuration <= 0) currentDuration = 60;
                endTime = startTime.Add(TimeSpan.FromMinutes(currentDuration));
            }

            var targetDate = model.AppointmentDate.Date;
            int durationMinutes = (int)Math.Max(15, (endTime - startTime).TotalMinutes);

            // Nếu lịch hẹn đang ở trạng thái hoạt động (Pending, Confirmed, InService), bắt buộc kiểm tra xung đột KTV
            if (ActiveBlockingStatuses.Contains(appointment.Status))
            {
                var activeEmployees = await _context.Users
                    .Include(u => u.Role)
                    .Where(u => u.Status == "active")
                    .OrderByDescending(u => u.RoleId)
                    .ThenBy(u => u.FullName)
                    .ToListAsync();

                var dayAppointments = await _context.Appointments
                    .Include(a => a.Customer)
                    .Where(a =>
                        a.AppointmentDate == targetDate &&
                        a.AppointmentId != appointment.AppointmentId &&
                        ActiveBlockingStatuses.Contains(a.Status))
                    .OrderBy(a => a.StartTime)
                    .ToListAsync();

                var empAvailabilityList = activeEmployees.Select(emp =>
                {
                    var conflict = dayAppointments.FirstOrDefault(a =>
                        a.EmployeeId == emp.UserId &&
                        IsTimeOverlapping(startTime, endTime, a.StartTime, a.EndTime));

                    return new EmployeeAvailabilityDto
                    {
                        Id = emp.UserId,
                        Name = emp.FullName,
                        Specialty = string.IsNullOrWhiteSpace(emp.Specialty) ? "Chăm sóc tổng hợp" : emp.Specialty,
                        Role = emp.Role?.RoleName ?? "staff",
                        IsAvailable = conflict == null,
                        ConflictAppointmentId = conflict?.AppointmentId,
                        ConflictStartTime = conflict?.StartTime.ToString(@"hh\:mm"),
                        ConflictEndTime = conflict?.EndTime.ToString(@"hh\:mm"),
                        ConflictStatus = conflict?.Status,
                        ConflictCustomerName = conflict?.Customer?.FullName
                    };
                }).ToList();

                var freeEmployees = empAvailabilityList.Where(e => e.IsAvailable).ToList();

                if (activeEmployees.Count > 0 && freeEmployees.Count == 0)
                {
                    var suggestedAll = CalculateNearestAvailableSlots(
                        activeEmployees.Select(e => e.UserId).ToList(),
                        dayAppointments,
                        startTime,
                        durationMinutes);

                    await transaction.RollbackAsync();
                    return new AppointmentOperationResult
                    {
                        Success = false,
                        Code = "ALL_BUSY",
                        Message = "Tất cả KTV đều đang có lịch tại thời gian này, vui lòng chọn thời gian khác",
                        CalculatedStartTime = startTime.ToString(@"hh\:mm"),
                        CalculatedEndTime = endTime.ToString(@"hh\:mm"),
                        TotalDurationMinutes = durationMinutes,
                        SuggestedSlots = suggestedAll,
                        AvailableEmployees = empAvailabilityList
                    };
                }

                if (model.EmployeeId.HasValue && model.EmployeeId.Value > 0)
                {
                    var selectedEmpStatus = empAvailabilityList.FirstOrDefault(e => e.Id == model.EmployeeId.Value);
                    if (selectedEmpStatus != null && !selectedEmpStatus.IsAvailable)
                    {
                        var suggestedForEmp = CalculateNearestAvailableSlots(
                            activeEmployees.Select(e => e.UserId).ToList(),
                            dayAppointments,
                            startTime,
                            durationMinutes,
                            model.EmployeeId.Value);

                        await transaction.RollbackAsync();
                        return new AppointmentOperationResult
                        {
                            Success = false,
                            Code = "EMPLOYEE_BUSY",
                            Message = $"KTV {selectedEmpStatus.Name} đã có lịch hẹn từ {selectedEmpStatus.ConflictStartTime} đến {selectedEmpStatus.ConflictEndTime}. Vui lòng chọn KTV khác hoặc đổi khung giờ!",
                            CalculatedStartTime = startTime.ToString(@"hh\:mm"),
                            CalculatedEndTime = endTime.ToString(@"hh\:mm"),
                            TotalDurationMinutes = durationMinutes,
                            SuggestedSlots = suggestedForEmp,
                            AvailableEmployees = empAvailabilityList
                        };
                    }
                }
            }

            appointment.AppointmentDate = targetDate;
            appointment.StartTime = startTime;
            appointment.EndTime = endTime;
            appointment.EmployeeId = model.EmployeeId;
            appointment.DepositAmount = model.DepositAmount < 0 ? 0 : model.DepositAmount;
            appointment.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
            appointment.UpdatedAt = DateTime.Now;

            if (model.ServiceIds != null && model.ServiceIds.Count > 0)
            {
                _context.AppointmentServices.RemoveRange(appointment.AppointmentServices);

                var distinctIds = model.ServiceIds.Distinct().ToList();
                var services = await _context.Services
                    .Where(s => distinctIds.Contains(s.ServiceId))
                    .ToListAsync();

                appointment.TotalPrice = services.Sum(s => s.Price);

                int newDuration = services.Sum(s => s.DurationMinutes);
                if (newDuration > 0)
                {
                    appointment.EndTime = startTime.Add(TimeSpan.FromMinutes(newDuration));
                }

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
            else
            {
                foreach (var aps in appointment.AppointmentServices)
                {
                    aps.EmployeeId = model.EmployeeId;
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new AppointmentOperationResult
            {
                Success = true,
                Code = "OK",
                Message = "Đổi lịch và cập nhật thông tin lịch hẹn thành công!",
                Appointment = appointment
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Lỗi Transaction khi đổi lịch hẹn ID={Id}", model.AppointmentId);
            throw;
        }
    }

    public async Task<(bool Success, string Message)> RescheduleAsync(AppointmentRescheduleViewModel model)
    {
        var res = await RescheduleSafeAsync(model);
        return (res.Success, res.Message);
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
