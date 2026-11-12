using CRM_V2.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

[AllowAnonymous]
[Route("Booking")]
[Route("BookingPublic")]
public class BookingPublicController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BookingPublicController> _logger;

    public BookingPublicController(ApplicationDbContext context, ILogger<BookingPublicController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// GET /Booking/Confirm?token={ConfirmationToken}&json=true|false
    /// Cho phép khách hàng bấm link xác nhận 1-Click từ tin nhắn mà không cần đăng nhập
    /// </summary>
    [HttpGet("Confirm")]
    public async Task<IActionResult> Confirm(Guid token, bool json = false)
    {
        if (token == Guid.Empty)
        {
            if (json) return BadRequest(new { success = false, message = "Token xác nhận không hợp lệ." });
            return Content("Link xác nhận không hợp lệ.", "text/html; charset=utf-8");
        }

        var appointment = await _context.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.ConfirmationToken == token);

        if (appointment == null)
        {
            if (json) return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn tương ứng với mã xác nhận này." });
            return Content("Không tìm thấy lịch hẹn tương ứng.", "text/html; charset=utf-8");
        }

        appointment.Status = "Confirmed";
        appointment.ConfirmedAt = DateTime.Now;
        appointment.UpdatedAt = DateTime.Now;

        var logs = await _context.NotificationLogs
            .Where(nl => nl.AppointmentId == appointment.AppointmentId)
            .ToListAsync();

        foreach (var log in logs)
        {
            log.Status = "ConfirmedByCustomer";
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Khách hàng {Name} đã xác nhận 1-Click cho lịch hẹn #{Id}",
            appointment.Customer?.FullName, appointment.AppointmentId);

        if (json)
        {
            return Json(new
            {
                success = true,
                message = $"Khách hàng {appointment.Customer?.FullName} đã bấm Xác nhận 1-Click! Lịch hẹn #{appointment.AppointmentId} đã chuyển sang trạng thái Confirmed."
            });
        }

        string html = $@"
<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>Xác nhận lịch hẹn thành công - SalonCare</title>
    <script src=""https://cdn.tailwindcss.com""></script>
</head>
<body class=""bg-slate-950 text-white min-h-screen flex items-center justify-center p-4"">
    <div class=""max-w-md w-full bg-slate-900 border border-emerald-500/40 rounded-3xl p-8 text-center shadow-2xl"">
        <div class=""w-16 h-16 rounded-2xl bg-emerald-500/20 border border-emerald-500/40 text-emerald-400 flex items-center justify-center mx-auto mb-4 text-3xl"">✓</div>
        <h1 class=""text-xl font-bold text-white mb-2"">Đã Xác Nhận Lịch Hẹn 1-Click!</h1>
        <p class=""text-sm text-slate-300 mb-6"">Cảm ơn quý khách <strong>{appointment.Customer?.FullName}</strong> đã xác nhận giữ chỗ tại <strong>SalonCare Spa</strong>.</p>
        <div class=""bg-white/5 rounded-2xl p-4 text-left text-xs space-y-2 border border-white/10 mb-6"">
            <p><span class=""text-slate-400"">Mã lịch hẹn:</span> <strong class=""text-purple-300"">#{appointment.AppointmentId}</strong></p>
            <p><span class=""text-slate-400"">Ngày giờ hẹn:</span> <strong>{appointment.StartTime:hh\:mm} - Ngày {appointment.AppointmentDate:dd/MM/yyyy}</strong></p>
            <p><span class=""text-slate-400"">Kỹ thuật viên:</span> <strong>{appointment.Employee?.FullName ?? "Chuyên viên SalonCare"}</strong></p>
            <p><span class=""text-slate-400"">Trạng thái:</span> <span class=""px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 font-bold"">Đã xác nhận (Confirmed)</span></p>
        </div>
        <a href=""/"" class=""inline-block px-6 py-2.5 rounded-xl bg-purple-600 hover:bg-purple-500 text-white text-xs font-bold transition"">Quay về trang chủ SalonCare</a>
    </div>
</body>
</html>";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// GET /Booking/Cancel?token={ConfirmationToken}&json=true|false
    /// Cho phép khách hàng báo hủy lịch hẹn 1-Click từ tin nhắn
    /// </summary>
    [HttpGet("Cancel")]
    public async Task<IActionResult> Cancel(Guid token, bool json = false)
    {
        if (token == Guid.Empty)
        {
            if (json) return BadRequest(new { success = false, message = "Token không hợp lệ." });
            return Content("Link không hợp lệ.", "text/html; charset=utf-8");
        }

        var appointment = await _context.Appointments
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.ConfirmationToken == token);

        if (appointment == null)
        {
            if (json) return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });
            return Content("Không tìm thấy lịch hẹn.", "text/html; charset=utf-8");
        }

        appointment.Status = "Cancelled";
        appointment.CancelledAt = DateTime.Now;
        appointment.CancellationReason = "Khách hàng báo hủy lịch chủ động qua link 1-Click";
        appointment.UpdatedAt = DateTime.Now;

        var logs = await _context.NotificationLogs
            .Where(nl => nl.AppointmentId == appointment.AppointmentId)
            .ToListAsync();

        foreach (var log in logs)
        {
            log.Status = "CanceledByCustomer";
        }

        await _context.SaveChangesAsync();

        if (json)
        {
            return Json(new
            {
                success = true,
                message = $"Khách hàng {appointment.Customer?.FullName} đã báo hủy lịch hẹn #{appointment.AppointmentId} qua link 1-Click."
            });
        }

        string html = $@"
<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>Đã hủy lịch hẹn - SalonCare</title>
    <script src=""https://cdn.tailwindcss.com""></script>
</head>
<body class=""bg-slate-950 text-white min-h-screen flex items-center justify-center p-4"">
    <div class=""max-w-md w-full bg-slate-900 border border-rose-500/40 rounded-3xl p-8 text-center shadow-2xl"">
        <div class=""w-16 h-16 rounded-2xl bg-rose-500/20 border border-rose-500/40 text-rose-400 flex items-center justify-center mx-auto mb-4 text-3xl"">✕</div>
        <h1 class=""text-xl font-bold text-white mb-2"">Đã Ghi Nhận Hủy Lịch Hẹn</h1>
        <p class=""text-sm text-slate-300 mb-6"">Lịch hẹn <strong>#{appointment.AppointmentId}</strong> của quý khách <strong>{appointment.Customer?.FullName}</strong> đã được hủy để nhường khung giờ cho khách khác. Hẹn gặp lại quý khách lần sau!</p>
        <a href=""/"" class=""inline-block px-6 py-2.5 rounded-xl bg-white/10 hover:bg-white/20 text-white text-xs font-bold transition"">Quay về trang chủ SalonCare</a>
    </div>
</body>
</html>";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// POST /Booking/SimulateOneClick?logId={LogId}
    /// Phục vụ demo vấn đáp: Giả lập khách hàng bấm nút Xác nhận 1-Click ngay trên bảng Nhật ký nhắc lịch
    /// </summary>
    [HttpPost("SimulateOneClick")]
    public async Task<IActionResult> SimulateOneClick(long logId)
    {
        var log = await _context.NotificationLogs
            .Include(nl => nl.Customer)
            .Include(nl => nl.Appointment)
            .FirstOrDefaultAsync(nl => nl.LogId == logId);

        if (log == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy bản ghi nhật ký nhắc lịch." });
        }

        log.Status = "ConfirmedByCustomer";

        if (log.Appointment != null)
        {
            log.Appointment.Status = "Confirmed";
            log.Appointment.ConfirmedAt = DateTime.Now;
            log.Appointment.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        return Json(new
        {
            success = true,
            message = $"Đã giả lập khách hàng \"{log.Customer?.FullName}\" bấm link Xác nhận 1-Click thành công! Trạng thái tin nhắn -> ConfirmedByCustomer" +
                      (log.Appointment != null ? $", Lịch hẹn #{log.Appointment.AppointmentId} -> Confirmed." : ".")
        });
    }
}
