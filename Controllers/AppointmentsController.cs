using System.Security.Claims;
using System.Text;
using CRM_V2.Data;
using CRM_V2.Services;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

[Authorize]
public class AppointmentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(
        ApplicationDbContext context,
        IAppointmentService appointmentService,
        ILogger<AppointmentsController> logger)
    {
        _context = context;
        _appointmentService = appointmentService;
        _logger = logger;
    }

    /// <summary>
    /// API lấy danh mục Khách hàng, Nhân viên, Dịch vụ cho Bộ lọc và Modal Đặt lịch
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMetadata()
    {
        try
        {
            var customers = await _context.Customers
                .AsNoTracking()
                .OrderByDescending(c => c.CustomerId)
                .Select(c => new
                {
                    id = c.CustomerId,
                    name = c.FullName,
                    phone = c.Phone,
                    tier = c.CustomerType
                })
                .ToListAsync();

            var employees = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.Status == "active")
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    id = u.UserId,
                    name = u.FullName,
                    role = u.Role != null ? u.Role.RoleName : "staff"
                })
                .ToListAsync();

            var services = await _context.Services
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.ServiceId)
                .Select(s => new
                {
                    id = s.ServiceId,
                    name = s.ServiceName,
                    category = s.CategoryName,
                    duration = s.DurationMinutes,
                    price = s.Price,
                    formattedPrice = s.Price.ToString("N0") + "đ"
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                customers,
                employees,
                services
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu danh mục Lịch hẹn");
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Tính mốc 23:00:00 ngày Chủ Nhật cuối tuần của một ngày bất kỳ (Tuần từ Thứ Hai -> Chủ Nhật)
    /// </summary>
    public static DateTime GetSunday23hOfWeek(DateTime date)
    {
        int daysUntilSunday = date.DayOfWeek == DayOfWeek.Sunday
            ? 0
            : 7 - (int)date.DayOfWeek;

        return date.Date.AddDays(daysUntilSunday).AddHours(23);
    }

    /// <summary>
    /// Tự động ẩn khỏi bảng Kanban (nhưng KHÔNG xóa trong SQL) đối với các lịch hẹn ở ô
    /// Vắng mặt (NoShow), Hoàn thành (Completed) và Đã hủy (Cancelled) khi đã qua 23:00 ngày Chủ Nhật của tuần đó.
    /// </summary>
    private async Task AutoHideExpiredBoardAppointmentsAsync()
    {
        var now = DateTime.Now;
        var candidates = await _context.Appointments
            .Where(a => !a.IsHiddenFromBoard &&
                        (a.Status == "Completed" || a.Status == "NoShow" || a.Status == "Cancelled"))
            .ToListAsync();

        bool changed = false;
        foreach (var appt in candidates)
        {
            var refDate = appt.CompletedAt ?? appt.CancelledAt ?? appt.AppointmentDate;
            var expireAtSunday23h = GetSunday23hOfWeek(refDate);

            if (now >= expireAtSunday23h)
            {
                appt.IsHiddenFromBoard = true;
                appt.HiddenAt = now;
                changed = true;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// API Bộ lọc dữ liệu Lịch hẹn & Bảng Kanban (GET /Appointments/GetKanban)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetKanban(
        string? dateFilter = "all",
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? status = "ALL",
        long? employeeId = null,
        int? serviceId = null,
        string? search = null)
    {
        try
        {
            // Tự động ẩn các lịch Hoàn thành / Vắng mặt / Đã hủy đã qua 23h Chủ Nhật của tuần
            await AutoHideExpiredBoardAppointmentsAsync();

            var query = _context.Appointments
                .AsNoTracking()
                .Include(a => a.Customer)
                .Include(a => a.Employee)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .Where(a => !a.IsHiddenFromBoard)
                .AsQueryable();

            // 1. Lọc theo khoảng ngày
            var today = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(dateFilter))
            {
                switch (dateFilter.ToLower())
                {
                    case "today":
                        query = query.Where(a => a.AppointmentDate == today);
                        break;
                    case "tomorrow":
                        var tomorrow = today.AddDays(1);
                        query = query.Where(a => a.AppointmentDate == tomorrow);
                        break;
                    case "this_week":
                        int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                        var startOfWeek = today.AddDays(-1 * diff).Date;
                        var endOfWeek = startOfWeek.AddDays(6).Date;
                        query = query.Where(a => a.AppointmentDate >= startOfWeek && a.AppointmentDate <= endOfWeek);
                        break;
                    case "custom":
                        if (fromDate.HasValue)
                            query = query.Where(a => a.AppointmentDate >= fromDate.Value.Date);
                        if (toDate.HasValue)
                            query = query.Where(a => a.AppointmentDate <= toDate.Value.Date);
                        break;
                }
            }

            // 2. Lọc theo Trạng thái
            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            {
                query = query.Where(a => a.Status == status);
            }

            // 3. Lọc theo Nhân viên phụ trách
            if (employeeId.HasValue && employeeId.Value > 0)
            {
                query = query.Where(a => a.EmployeeId == employeeId.Value);
            }

            // 4. Lọc theo Dịch vụ
            if (serviceId.HasValue && serviceId.Value > 0)
            {
                query = query.Where(a => a.AppointmentServices.Any(aps => aps.ServiceId == serviceId.Value));
            }

            // 5. Tìm kiếm theo Tên hoặc SĐT khách hàng
            if (!string.IsNullOrWhiteSpace(search))
            {
                var kw = search.Trim().ToLower();
                query = query.Where(a =>
                    (a.Customer != null && a.Customer.FullName.ToLower().Contains(kw)) ||
                    (a.Customer != null && a.Customer.Phone.Contains(kw)));
            }

            var rawList = await query
                .OrderBy(a => a.AppointmentDate)
                .ThenBy(a => a.StartTime)
                .ToListAsync();

            var data = rawList.Select(a => new
            {
                id = a.AppointmentId,
                customerId = a.CustomerId,
                customerName = a.Customer?.FullName ?? "Khách vãng lai",
                customerPhone = a.Customer?.Phone ?? "---",
                customerTier = a.Customer?.CustomerType ?? "Moi",
                employeeId = a.EmployeeId,
                employeeName = a.Employee?.FullName ?? "Chưa phân công",
                appointmentDate = a.AppointmentDate.ToString("dd/MM/yyyy"),
                rawDate = a.AppointmentDate.ToString("yyyy-MM-dd"),
                startTime = a.StartTime.ToString(@"hh\:mm"),
                endTime = a.EndTime.ToString(@"hh\:mm"),
                status = a.Status,
                bookingSource = a.BookingSource,
                depositAmount = a.DepositAmount,
                formattedDeposit = a.DepositAmount.ToString("N0") + "đ",
                totalPrice = a.TotalPrice,
                formattedTotal = a.TotalPrice.ToString("N0") + "đ",
                notes = a.Notes ?? "",
                cancellationReason = a.CancellationReason ?? "",
                services = a.AppointmentServices
                    .Where(aps => !aps.IsHidden)
                    .Select(aps => new
                    {
                        id = aps.ServiceId,
                        name = aps.Service?.ServiceName ?? "Dịch vụ Spa",
                        price = aps.Price,
                        formattedPrice = aps.Price.ToString("N0") + "đ"
                    }).ToList()
            }).ToList();

            var summary = new
            {
                total = data.Count,
                pending = data.Count(x => x.status == "Pending"),
                confirmed = data.Count(x => x.status == "Confirmed"),
                inService = data.Count(x => x.status == "InService"),
                completed = data.Count(x => x.status == "Completed"),
                noShow = data.Count(x => x.status == "NoShow"),
                cancelled = data.Count(x => x.status == "Cancelled")
            };

            return Json(new
            {
                success = true,
                data,
                summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi truy vấn dữ liệu Bảng Kanban Lịch hẹn");
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// API xem chi tiết 1 lịch hẹn (GET /Appointments/GetDetail)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDetail(long id)
    {
        try
        {
            var a = await _context.Appointments
                .AsNoTracking()
                .Include(x => x.Customer)
                .Include(x => x.Employee)
                .Include(x => x.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .FirstOrDefaultAsync(x => x.AppointmentId == id);

            if (a == null)
            {
                return Json(new { success = false, message = "Không tìm thấy lịch hẹn." });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    id = a.AppointmentId,
                    customerId = a.CustomerId,
                    customerName = a.Customer?.FullName ?? "Khách vãng lai",
                    customerPhone = a.Customer?.Phone ?? "---",
                    customerTier = a.Customer?.CustomerType ?? "Moi",
                    employeeId = a.EmployeeId,
                    employeeName = a.Employee?.FullName ?? "Chưa phân công",
                    appointmentDate = a.AppointmentDate.ToString("dd/MM/yyyy"),
                    rawDate = a.AppointmentDate.ToString("yyyy-MM-dd"),
                    startTime = a.StartTime.ToString(@"hh\:mm"),
                    endTime = a.EndTime.ToString(@"hh\:mm"),
                    status = a.Status,
                    bookingSource = a.BookingSource,
                    depositAmount = a.DepositAmount,
                    formattedDeposit = a.DepositAmount.ToString("N0") + "đ",
                    totalPrice = a.TotalPrice,
                    formattedTotal = a.TotalPrice.ToString("N0") + "đ",
                    notes = a.Notes ?? "Không có ghi chú",
                    rawNotes = a.Notes ?? "",
                    confirmedAt = a.ConfirmedAt?.ToString("HH:mm dd/MM/yyyy"),
                    checkInAt = a.CheckInAt?.ToString("HH:mm dd/MM/yyyy"),
                    completedAt = a.CompletedAt?.ToString("HH:mm dd/MM/yyyy"),
                    cancelledAt = a.CancelledAt?.ToString("HH:mm dd/MM/yyyy"),
                    cancellationReason = a.CancellationReason ?? "",
                    serviceIds = a.AppointmentServices.Where(s => !s.IsHidden).Select(s => s.ServiceId).ToList(),
                    services = a.AppointmentServices
                        .Where(s => !s.IsHidden)
                        .Select(s => new
                        {
                            id = s.ServiceId,
                            name = s.Service?.ServiceName ?? "Dịch vụ",
                            price = s.Price,
                            formattedPrice = s.Price.ToString("N0") + "đ"
                        }).ToList()
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy chi tiết lịch hẹn ID={Id}", id);
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// API Tạo Lịch hẹn mới (POST /Appointments/Create)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AppointmentCreateViewModel model)
    {
        if (!ModelState.IsValid || model.ServiceIds == null || model.ServiceIds.Count == 0)
        {
            return Json(new
            {
                success = false,
                message = "Vui lòng chọn khách hàng, ngày giờ và ít nhất 1 dịch vụ."
            });
        }

        try
        {
            long? currentUserId = null;
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(userIdStr, out var parsedId))
                currentUserId = parsedId;

            var appointment = await _appointmentService.CreateAppointmentAsync(model, currentUserId);

            return Json(new
            {
                success = true,
                message = "Tạo lịch hẹn mới thành công!",
                appointmentId = appointment.AppointmentId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tạo lịch hẹn mới");
            return Json(new { success = false, message = "Lỗi khi lưu lịch hẹn: " + ex.Message });
        }
    }

    /// <summary>
    /// API Cập nhật trạng thái / Chuyển cột Kanban (POST /Appointments/UpdateStatus)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateStatus([FromBody] AppointmentUpdateStatusViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Dữ liệu yêu cầu không hợp lệ." });
        }

        try
        {
            var result = await _appointmentService.UpdateStatusAsync(
                model.AppointmentId,
                model.NewStatus,
                model.CancellationReason);

            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật trạng thái lịch hẹn ID={Id}", model.AppointmentId);
            return Json(new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// API Đổi lịch / Cập nhật thông tin lịch hẹn (POST /Appointments/Reschedule)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Reschedule([FromBody] AppointmentRescheduleViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Vui lòng kiểm tra lại ngày giờ hẹn." });
        }

        try
        {
            var result = await _appointmentService.RescheduleAsync(model);
            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi đổi lịch hẹn ID={Id}", model.AppointmentId);
            return Json(new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// API Hủy lịch hẹn bắt buộc kèm lý do hủy (POST /Appointments/Cancel)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Cancel([FromBody] AppointmentCancelViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return Json(new { success = false, message = "Vui lòng nhập lý do hủy lịch hẹn." });
        }

        try
        {
            var result = await _appointmentService.CancelAppointmentAsync(model.AppointmentId, model.CancellationReason);
            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi hủy lịch hẹn ID={Id}", model.AppointmentId);
            return Json(new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// Xuất danh sách lịch hẹn ra file CSV/Excel (GET /Appointments/ExportCsv)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportCsv(string? dateFilter = "all", string? status = "ALL")
    {
        var list = await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Customer)
            .Include(a => a.Employee)
            .Include(a => a.AppointmentServices)
                .ThenInclude(aps => aps.Service)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenBy(a => a.StartTime)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Mã Lịch Hẹn,Khách Hàng,Số Điện Thoại,Ngày Hẹn,Giờ Bắt Đầu,Giờ Kết Thúc,Dịch Vụ,KTV Phụ Trách,Tiền Đặt Cọc,Tổng Chi Phí,Trạng Thái,Nguồn Đặt,Ghi Chú");

        foreach (var a in list)
        {
            var servicesText = string.Join(" + ", a.AppointmentServices.Select(s => s.Service?.ServiceName ?? ""));
            var custName = (a.Customer?.FullName ?? "").Replace(",", " ");
            var empName = (a.Employee?.FullName ?? "Chưa phân công").Replace(",", " ");
            var notes = (a.Notes ?? "").Replace(",", " ").Replace("\r", " ").Replace("\n", " ");

            sb.AppendLine($"#{a.AppointmentId},{custName},{a.Customer?.Phone},{a.AppointmentDate:dd/MM/yyyy},{a.StartTime:hh\\:mm},{a.EndTime:hh\\:mm},\"{servicesText}\",{empName},{a.DepositAmount:0},{a.TotalPrice:0},{a.Status},{a.BookingSource},\"{notes}\"");
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fullBytes = preamble.Concat(bodyBytes).ToArray();

        return File(fullBytes, "text/csv; charset=utf-8", $"DanhSachLichHen_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    /// <summary>
    /// POST /Appointments/HideFromBoard
    /// Xóa hiển thị lịch hẹn/dịch vụ khỏi bảng Kanban (nhưng KHÔNG xóa dữ liệu trong SQL Server)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HideFromBoard([FromBody] HideAppointmentDto dto)
    {
        if (dto == null || dto.AppointmentId <= 0)
            return BadRequest(new { success = false, message = "Mã lịch hẹn không hợp lệ." });

        try
        {
            var appt = await _context.Appointments.FindAsync(dto.AppointmentId);
            if (appt == null)
                return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

            appt.IsHiddenFromBoard = true;
            appt.HiddenAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Đã xóa hiển thị lịch hẹn #{appt.AppointmentId} khỏi bảng Kanban (Dữ liệu vẫn được lưu trữ an toàn trong SQL)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi ẩn lịch hẹn ID={Id} khỏi bảng Kanban", dto.AppointmentId);
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Appointments/HideServiceDisplay
    /// Xóa hiển thị 1 dịch vụ cụ thể trong chi tiết lịch hẹn (nhưng KHÔNG xóa bản ghi trong SQL Server)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HideServiceDisplay([FromBody] HideServiceDisplayDto dto)
    {
        if (dto == null || dto.AppointmentId <= 0 || dto.ServiceId <= 0)
            return BadRequest(new { success = false, message = "Thông tin dịch vụ không hợp lệ." });

        try
        {
            var aps = await _context.AppointmentServices
                .Include(x => x.Service)
                .FirstOrDefaultAsync(x => x.AppointmentId == dto.AppointmentId && x.ServiceId == dto.ServiceId);

            if (aps == null)
                return NotFound(new { success = false, message = "Không tìm thấy dịch vụ trong lịch hẹn." });

            aps.IsHidden = true;
            await _context.SaveChangesAsync();

            // Kiểm tra nếu tất cả dịch vụ trong lịch hẹn này đều đã ẩn hiển thị thì ẩn luôn thẻ lịch hẹn khỏi bảng Kanban
            bool anyVisibleLeft = await _context.AppointmentServices
                .AnyAsync(x => x.AppointmentId == dto.AppointmentId && !x.IsHidden);

            if (!anyVisibleLeft)
            {
                var appt = await _context.Appointments.FindAsync(dto.AppointmentId);
                if (appt != null)
                {
                    appt.IsHiddenFromBoard = true;
                    appt.HiddenAt = DateTime.Now;
                    await _context.SaveChangesAsync();
                }
            }

            string srvName = aps.Service?.ServiceName ?? "Dịch vụ";
            return Json(new
            {
                success = true,
                allHidden = !anyVisibleLeft,
                message = $"Đã xóa hiển thị dịch vụ \"{srvName}\" (Dữ liệu gốc trong SQL vẫn được giữ nguyên)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa hiển thị dịch vụ ServiceId={ServiceId} của AppointmentId={ApptId}", dto.ServiceId, dto.AppointmentId);
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }
}

public class HideAppointmentDto
{
    public long AppointmentId { get; set; }
}

public class HideServiceDisplayDto
{
    public long AppointmentId { get; set; }
    public int ServiceId { get; set; }
}
