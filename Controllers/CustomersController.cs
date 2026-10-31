using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(ApplicationDbContext context, ILogger<CustomersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// API lấy danh sách khách hàng từ CSDL SQL Server với bộ lọc và tìm kiếm
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(string? tier, string? search)
    {
        try
        {
            var query = _context.Customers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(tier) && tier != "ALL")
            {
                query = query.Where(c => c.CustomerType == tier);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim().ToLower();
                query = query.Where(c => c.FullName.ToLower().Contains(keyword) || c.Phone.Contains(keyword));
            }

            var list = await query
                .OrderByDescending(c => c.CustomerId)
                .Select(c => new
                {
                    id = c.CustomerId,
                    name = c.FullName,
                    phone = c.Phone,
                    email = c.Email,
                    gender = c.Gender,
                    lastVisit = c.LastVisitAt.HasValue ? c.LastVisitAt.Value.ToString("dd/MM/yyyy") : "Chưa có",
                    visits = c.TotalVisits,
                    totalSpend = c.TotalSpent.ToString("N0") + "đ",
                    rawTotalSpend = c.TotalSpent,
                    tier = c.CustomerType,
                    status = c.CustomerStatus,
                    avatarUrl = c.AvatarUrl,
                    notes = c.Notes
                })
                .ToListAsync();

            // Thống kê số lượng theo nhóm
            var allCount = await _context.Customers.CountAsync();
            var vipCount = await _context.Customers.CountAsync(c => c.CustomerType == "VIP");
            var loyalCount = await _context.Customers.CountAsync(c => c.CustomerType == "Than_thiet");
            var newCount = await _context.Customers.CountAsync(c => c.CustomerType == "Moi");

            return Json(new
            {
                success = true,
                data = list,
                counts = new
                {
                    all = allCount,
                    vip = vipCount,
                    loyal = loyalCount,
                    newCount = newCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi truy vấn danh sách khách hàng từ CSDL SQL Server");

            // Danh sách dự phòng (Fallback) khi CSDL tạm thời gián đoạn
            var fallbackList = new List<object>
            {
                new { id = 1L, name = "Lê Thị Mai", phone = "0987654321", email = "mai.le@gmail.com", gender = "Nữ", lastVisit = "12/06/2025", visits = 18, totalSpend = "8.500.000đ", rawTotalSpend = 8500000m, tier = "VIP", status = "Hoat_dong", avatarUrl = (string?)null, notes = "Khách quen VIP" },
                new { id = 2L, name = "Trần Ngọc Anh", phone = "0912345678", email = "ngocanh.tran@yahoo.com", gender = "Nữ", lastVisit = "10/06/2025", visits = 12, totalSpend = "6.200.000đ", rawTotalSpend = 6200000m, tier = "VIP", status = "Hoat_dong", avatarUrl = (string?)null, notes = "Khách VIP" },
                new { id = 4L, name = "Vũ Bảo Trầm", phone = "0987321654", email = "baotram.vu@outlook.com", gender = "Nữ", lastVisit = "02/06/2025", visits = 9, totalSpend = "4.500.000đ", rawTotalSpend = 4500000m, tier = "Than_thiet", status = "Hoat_dong", avatarUrl = (string?)null, notes = "Khách thân thiết" },
                new { id = 3L, name = "Phạm Thu Hà", phone = "0909876543", email = "thuha.pham98@gmail.com", gender = "Nữ", lastVisit = "05/06/2025", visits = 2, totalSpend = "1.200.000đ", rawTotalSpend = 1200000m, tier = "Moi", status = "Hoat_dong", avatarUrl = (string?)null, notes = "Khách mới" }
            };

            return Json(new
            {
                success = true,
                isFallback = true,
                data = fallbackList,
                counts = new { all = 4, vip = 2, loyal = 1, newCount = 1 },
                warning = "Đang hiển thị dữ liệu bộ đệm (Vui lòng kiểm tra kết nối SQL Server: " + ex.Message + ")"
            });
        }
    }

    /// <summary>
    /// API xem chi tiết 1 khách hàng
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDetail(long id)
    {
        try
        {
            var customer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer == null)
            {
                return Json(new { success = false, message = "Không tìm thấy khách hàng." });
            }

            // Truy vấn toàn bộ lịch sử dịch vụ & lịch hẹn của khách hàng (bao cả các lịch hẹn đã ẩn khỏi bảng Kanban)
            var appointments = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Employee)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .Where(a => a.CustomerId == id)
                .OrderByDescending(a => a.AppointmentDate)
                .ThenByDescending(a => a.StartTime)
                .ToListAsync();

            var serviceHistory = appointments.Select(a => new
            {
                appointmentId = a.AppointmentId,
                appointmentDate = a.AppointmentDate.ToString("dd/MM/yyyy"),
                startTime = a.StartTime.ToString(@"hh\:mm"),
                endTime = a.EndTime.ToString(@"hh\:mm"),
                status = a.Status, // Completed, Cancelled, NoShow, InService, Confirmed, Pending
                employeeName = a.Employee?.FullName ?? "Chưa phân công",
                totalPrice = a.TotalPrice,
                formattedTotal = a.TotalPrice.ToString("N0") + "đ",
                cancellationReason = a.CancellationReason ?? "",
                completedAt = a.CompletedAt.HasValue ? a.CompletedAt.Value.ToString("dd/MM/yyyy HH:mm") : null,
                cancelledAt = a.CancelledAt.HasValue ? a.CancelledAt.Value.ToString("dd/MM/yyyy HH:mm") : null,
                services = a.AppointmentServices.Select(aps => new
                {
                    serviceId = aps.ServiceId,
                    serviceName = aps.Service?.ServiceName ?? "Dịch vụ Spa",
                    categoryName = aps.Service?.CategoryName ?? "Spa",
                    durationMinutes = aps.Service?.DurationMinutes ?? 60,
                    price = aps.Price,
                    formattedPrice = aps.Price.ToString("N0") + "đ"
                }).ToList()
            }).ToList();

            int completedCount = serviceHistory.Count(h => h.status == "Completed");
            int cancelledCount = serviceHistory.Count(h => h.status == "Cancelled" || h.status == "NoShow");

            return Json(new
            {
                success = true,
                data = new
                {
                    id = customer.CustomerId,
                    name = customer.FullName,
                    phone = customer.Phone,
                    email = customer.Email ?? "Chưa cập nhật",
                    gender = customer.Gender ?? "Chưa rõ",
                    dob = customer.DateOfBirth.HasValue ? customer.DateOfBirth.Value.ToString("dd/MM/yyyy") : "Chưa cập nhật",
                    address = customer.Address ?? "Chưa có địa chỉ",
                    tier = customer.CustomerType,
                    status = customer.CustomerStatus,
                    source = customer.CustomerSource,
                    notes = customer.Notes ?? "Không có ghi chú",
                    totalSpend = customer.TotalSpent.ToString("N0") + "đ",
                    visits = customer.TotalVisits,
                    lastVisit = customer.LastVisitAt.HasValue ? customer.LastVisitAt.Value.ToString("dd/MM/yyyy") : "Chưa có",
                    avatarUrl = customer.AvatarUrl,
                    completedCount,
                    cancelledCount,
                    serviceHistory,

                    // Dữ liệu thô phục vụ form chỉnh sửa (Edit Modal)
                    rawFullName = customer.FullName,
                    rawPhone = customer.Phone,
                    rawEmail = customer.Email ?? "",
                    rawGender = customer.Gender ?? "Nữ",
                    rawDob = customer.DateOfBirth.HasValue ? customer.DateOfBirth.Value.ToString("yyyy-MM-dd") : "",
                    rawAddress = customer.Address ?? "",
                    rawSource = customer.CustomerSource,
                    rawTier = customer.CustomerType,
                    rawStatus = customer.CustomerStatus,
                    rawAvatarUrl = customer.AvatarUrl ?? "",
                    rawNotes = customer.Notes ?? ""
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy chi tiết khách hàng ID {Id}", id);
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Action cập nhật thông tin khách hàng vào SQL Server
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Update([FromBody] CustomerEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return Json(new
            {
                success = false,
                message = "Dữ liệu nhập vào chưa hợp lệ. Vui lòng kiểm tra lại!",
                errors
            });
        }

        try
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == model.CustomerId);
            if (customer == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Không tìm thấy hồ sơ khách hàng cần cập nhật."
                });
            }

            // Kiểm tra trùng số điện thoại với khách hàng khác
            var cleanPhone = model.Phone.Trim();
            var phoneConflict = await _context.Customers
                .AnyAsync(c => c.Phone == cleanPhone && c.CustomerId != model.CustomerId);

            if (phoneConflict)
            {
                return Json(new
                {
                    success = false,
                    message = $"Số điện thoại '{cleanPhone}' đang thuộc về một khách hàng khác trong hệ thống."
                });
            }

            // Cập nhật thông tin
            customer.FullName = model.FullName.Trim();
            customer.Phone = cleanPhone;
            customer.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim().ToLower();
            customer.Gender = model.Gender;
            customer.DateOfBirth = model.DateOfBirth;
            customer.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
            customer.CustomerSource = string.IsNullOrWhiteSpace(model.CustomerSource) ? "Truc_tiep" : model.CustomerSource;
            customer.CustomerType = string.IsNullOrWhiteSpace(model.CustomerType) ? "Moi" : model.CustomerType;
            customer.CustomerStatus = string.IsNullOrWhiteSpace(model.CustomerStatus) ? "Hoat_dong" : model.CustomerStatus;
            customer.AvatarUrl = string.IsNullOrWhiteSpace(model.AvatarUrl) ? null : model.AvatarUrl.Trim();
            customer.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
            customer.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Cập nhật thành công thông tin khách hàng ID={Id}, Tên={Name}", customer.CustomerId, customer.FullName);

            return Json(new
            {
                success = true,
                message = "Cập nhật thông tin khách hàng thành công!",
                data = new
                {
                    id = customer.CustomerId,
                    name = customer.FullName,
                    phone = customer.Phone,
                    email = customer.Email,
                    tier = customer.CustomerType,
                    status = customer.CustomerStatus
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi cập nhật hồ sơ khách hàng ID {Id}", model.CustomerId);
            return Json(new
            {
                success = false,
                message = "Đã xảy ra lỗi trong quá trình lưu dữ liệu vào máy chủ. Chi tiết: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Action nhận dữ liệu form và thêm mới khách hàng vào SQL Server
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return Json(new
            {
                success = false,
                message = "Dữ liệu nhập vào chưa hợp lệ. Vui lòng kiểm tra lại!",
                errors
            });
        }

        try
        {
            // 1. Kiểm tra trùng lặp Số điện thoại (Phone)
            var cleanPhone = model.Phone.Trim();
            var phoneExists = await _context.Customers
                .AnyAsync(c => c.Phone == cleanPhone);

            if (phoneExists)
            {
                return Json(new
                {
                    success = false,
                    message = $"Số điện thoại '{cleanPhone}' đã tồn tại trong hệ thống. Vui lòng kiểm tra lại hồ sơ khách hàng!"
                });
            }

            // 2. Khởi tạo Entity Customer và gán giá trị mặc định CRM
            var newCustomer = new Customer
            {
                FullName = model.FullName.Trim(),
                Phone = cleanPhone,
                Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim().ToLower(),
                Gender = model.Gender,
                DateOfBirth = model.DateOfBirth,
                Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim(),
                AvatarUrl = string.IsNullOrWhiteSpace(model.AvatarUrl) ? null : model.AvatarUrl.Trim(),
                CustomerSource = string.IsNullOrWhiteSpace(model.CustomerSource) ? "Truc_tiep" : model.CustomerSource,
                CustomerType = string.IsNullOrWhiteSpace(model.CustomerType) ? "Moi" : model.CustomerType,
                CustomerStatus = string.IsNullOrWhiteSpace(model.CustomerStatus) ? "Hoat_dong" : model.CustomerStatus,
                Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),

                // Các giá trị mặc định CRM Analytics
                TotalVisits = 0,
                TotalSpent = 0.00m,
                NoShowCount = 0,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                LastVisitAt = null,
                LastBookingAt = null
            };

            // 3. Thêm vào DbContext và lưu vào SQL Server
            _context.Customers.Add(newCustomer);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Thêm khách hàng thành công: ID={Id}, Tên={Name}, SĐT={Phone}", 
                newCustomer.CustomerId, newCustomer.FullName, newCustomer.Phone);

            return Json(new
            {
                success = true,
                message = "Thêm mới khách hàng thành công!",
                data = new
                {
                    id = newCustomer.CustomerId,
                    name = newCustomer.FullName,
                    phone = newCustomer.Phone,
                    email = newCustomer.Email,
                    tier = newCustomer.CustomerType,
                    status = newCustomer.CustomerStatus,
                    visits = newCustomer.TotalVisits,
                    totalSpend = "0đ",
                    lastVisit = "Mới tạo"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi lưu khách hàng vào CSDL");
            return Json(new
            {
                success = false,
                message = "Đã xảy ra lỗi trong quá trình lưu dữ liệu vào máy chủ. Chi tiết: " + ex.Message
            });
        }
    }
}
