using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

public class ServicesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ServicesController> _logger;

    public ServicesController(ApplicationDbContext context, ILogger<ServicesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// GET /Services/GetAllActive
    /// Lấy danh sách tất cả dịch vụ có is_active = 1 dạng JSON kèm danh mục nhóm dịch vụ
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllActive()
    {
        try
        {
            var services = await _context.Services
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.ServiceId)
                .Select(s => new
                {
                    id = s.ServiceId,
                    serviceId = s.ServiceId,
                    name = s.ServiceName,
                    serviceName = s.ServiceName,
                    category = s.CategoryName,
                    categoryName = s.CategoryName,
                    duration = s.DurationMinutes,
                    durationMinutes = s.DurationMinutes,
                    price = s.Price,
                    formattedPrice = s.Price.ToString("N0", new System.Globalization.CultureInfo("vi-VN")) + "đ",
                    description = s.Description ?? "",
                    iconClass = string.IsNullOrWhiteSpace(s.IconClass) ? "fa-spa" : s.IconClass,
                    bgColor = string.IsNullOrWhiteSpace(s.BgColor) ? "bg-purple-500" : s.BgColor,
                    isActive = s.IsActive
                })
                .ToListAsync();

            var categories = await _context.ServiceCategories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.CategoryName)
                .Select(c => new
                {
                    c.CategoryId,
                    c.CategoryName,
                    c.DefaultIcon,
                    c.DefaultBgColor
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                data = services,
                categories
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách dịch vụ hoạt động.");
            return Json(new { success = false, message = "Không thể tải danh sách dịch vụ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Services/Create
    /// Thêm mới dịch vụ từ Modal UI vào bảng services (và đồng bộ danh mục mới vào service_categories)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ServiceUpsertDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.ServiceName))
            return BadRequest(new { success = false, message = "Vui lòng nhập tên dịch vụ." });

        if (dto.DurationMinutes <= 0)
            return BadRequest(new { success = false, message = "Thời lượng dịch vụ phải lớn hơn 0 phút." });

        if (dto.Price <= 0)
            return BadRequest(new { success = false, message = "Giá dịch vụ phải lớn hơn 0 VNĐ." });

        try
        {
            string categoryName = string.IsNullOrWhiteSpace(dto.CategoryName)
                ? "Chăm sóc da"
                : dto.CategoryName.Trim();

            string iconClass = string.IsNullOrWhiteSpace(dto.IconClass) ? "fa-spa" : dto.IconClass.Trim();
            string bgColor = string.IsNullOrWhiteSpace(dto.BgColor) ? "bg-purple-500" : dto.BgColor.Trim();

            // Tự động thêm vào bảng service_categories nếu là danh mục mới
            bool catExists = await _context.ServiceCategories
                .AnyAsync(c => c.CategoryName == categoryName);

            if (!catExists)
            {
                _context.ServiceCategories.Add(new ServiceCategory
                {
                    CategoryName = categoryName,
                    DefaultIcon = iconClass,
                    DefaultBgColor = bgColor,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            var service = new Service
            {
                ServiceName = dto.ServiceName.Trim(),
                CategoryName = categoryName,
                DurationMinutes = dto.DurationMinutes,
                Price = dto.Price,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                IconClass = iconClass,
                BgColor = bgColor,
                IsActive = true,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.Services.Add(service);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Đã tạo mới dịch vụ ID={Id}: {Name} ({Price:N0}đ)", service.ServiceId, service.ServiceName, service.Price);

            return Json(new
            {
                success = true,
                message = $"Đã thêm mới dịch vụ \"{service.ServiceName}\" thành công!",
                serviceId = service.ServiceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi thêm mới dịch vụ.");
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi thêm dịch vụ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Services/Update
    /// Chỉnh sửa thông tin tên, giá, thời lượng, danh mục, icon, màu sắc của dịch vụ
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Update([FromBody] ServiceUpsertDto dto)
    {
        if (dto == null || dto.ServiceId <= 0)
            return BadRequest(new { success = false, message = "Mã dịch vụ không hợp lệ." });

        if (string.IsNullOrWhiteSpace(dto.ServiceName))
            return BadRequest(new { success = false, message = "Vui lòng nhập tên dịch vụ." });

        if (dto.DurationMinutes <= 0)
            return BadRequest(new { success = false, message = "Thời lượng dịch vụ phải lớn hơn 0 phút." });

        if (dto.Price <= 0)
            return BadRequest(new { success = false, message = "Giá dịch vụ phải lớn hơn 0 VNĐ." });

        try
        {
            var service = await _context.Services.FindAsync(dto.ServiceId);
            if (service == null)
                return NotFound(new { success = false, message = "Không tìm thấy dịch vụ cần chỉnh sửa." });

            string categoryName = string.IsNullOrWhiteSpace(dto.CategoryName)
                ? "Chăm sóc da"
                : dto.CategoryName.Trim();

            string iconClass = string.IsNullOrWhiteSpace(dto.IconClass) ? service.IconClass : dto.IconClass.Trim();
            string bgColor = string.IsNullOrWhiteSpace(dto.BgColor) ? service.BgColor : dto.BgColor.Trim();

            bool catExists = await _context.ServiceCategories
                .AnyAsync(c => c.CategoryName == categoryName);

            if (!catExists)
            {
                _context.ServiceCategories.Add(new ServiceCategory
                {
                    CategoryName = categoryName,
                    DefaultIcon = iconClass,
                    DefaultBgColor = bgColor,
                    DisplayOrder = 10,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            service.ServiceName = dto.ServiceName.Trim();
            service.CategoryName = categoryName;
            service.DurationMinutes = dto.DurationMinutes;
            service.Price = dto.Price;
            service.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            service.IconClass = iconClass;
            service.BgColor = bgColor;
            service.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Đã cập nhật dịch vụ ID={Id}: {Name}", service.ServiceId, service.ServiceName);

            return Json(new
            {
                success = true,
                message = $"Đã cập nhật dịch vụ \"{service.ServiceName}\" thành công!",
                serviceId = service.ServiceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật dịch vụ ID={Id}", dto.ServiceId);
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi cập nhật dịch vụ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Services/Delete/{id}
    /// Soft Delete: Cập nhật is_active = 0 thay vì xóa vật lý khỏi DB để bảo toàn lịch sử hóa đơn/lịch hẹn cũ
    /// </summary>
    [HttpPost("/Services/Delete/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null)
                return NotFound(new { success = false, message = "Không tìm thấy dịch vụ cần xóa." });

            service.IsActive = false;
            service.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Đã ẩn/ngừng kinh doanh (Soft Delete) dịch vụ ID={Id}: {Name}", service.ServiceId, service.ServiceName);

            return Json(new
            {
                success = true,
                message = $"Đã ngừng kinh doanh & ẩn dịch vụ \"{service.ServiceName}\" khỏi danh mục!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa mềm dịch vụ ID={Id}", id);
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi xóa dịch vụ: " + ex.Message });
        }
    }
}

public class ServiceUpsertDto
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = "Chăm sóc da";
    public int DurationMinutes { get; set; } = 60;
    public decimal Price { get; set; } = 0;
    public string? Description { get; set; }
    public string IconClass { get; set; } = "fa-spa";
    public string BgColor { get; set; } = "bg-purple-500";
}
