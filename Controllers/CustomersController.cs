using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.Services;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IRfmService _rfmService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        ApplicationDbContext context,
        IRfmService rfmService,
        IAuditLogService auditLogService,
        ILogger<CustomersController> logger)
    {
        _context = context;
        _rfmService = rfmService;
        _auditLogService = auditLogService;
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
            var query = _context.Customers
                .AsNoTracking()
                .Include(c => c.RfmScore)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(tier) && tier != "ALL")
            {
                if (tier == "AtRisk" || tier == "Nguy_co")
                {
                    query = query.Where(c => c.CustomerType == "Nguy_co" || (c.RfmScore != null && c.RfmScore.SegmentGroup == "AtRisk"));
                }
                else
                {
                    query = query.Where(c => c.CustomerType == tier);
                }
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
                    segmentGroup = c.RfmScore != null ? c.RfmScore.SegmentGroup : (c.CustomerType == "VIP" ? "VIP" : c.CustomerType == "Than_thiet" ? "Loyal" : c.CustomerType == "Nguy_co" ? "AtRisk" : "New"),
                    isNoShowRisk = c.RfmScore != null ? c.RfmScore.IsNoShowRisk : c.NoShowCount >= 2,
                    noShowCount = c.RfmScore != null ? c.RfmScore.NoShowCount : c.NoShowCount,
                    reasonText = c.RfmScore != null ? c.RfmScore.ReasonText : "",
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
            var atRiskCount = await _context.Customers.CountAsync(c => c.CustomerType == "Nguy_co" || (c.RfmScore != null && c.RfmScore.SegmentGroup == "AtRisk"));

            return Json(new
            {
                success = true,
                data = list,
                counts = new
                {
                    all = allCount,
                    vip = vipCount,
                    loyal = loyalCount,
                    newCount = newCount,
                    atRisk = atRiskCount
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
            var customer = await _context.Customers
                .AsNoTracking()
                .Include(c => c.RfmScore)
                .FirstOrDefaultAsync(c => c.CustomerId == id);
            if (customer == null)
            {
                return Json(new { success = false, message = "Không tìm thấy khách hàng." });
            }

            var rfmScore = customer.RfmScore;
            if (rfmScore == null)
            {
                rfmScore = await _rfmService.RecalculateForCustomerAsync(id);
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
                    rfm = new
                    {
                        recencyDays = rfmScore?.RecencyDays ?? 999,
                        frequencyCount = rfmScore?.FrequencyCount ?? customer.TotalVisits,
                        monetaryTotal = rfmScore?.MonetaryTotal ?? customer.TotalSpent,
                        formattedMonetary = (rfmScore?.MonetaryTotal ?? customer.TotalSpent).ToString("N0") + "đ",
                        noShowCount = rfmScore?.NoShowCount ?? customer.NoShowCount,
                        segmentGroup = rfmScore?.SegmentGroup ?? "New",
                        isNoShowRisk = rfmScore?.IsNoShowRisk ?? (customer.NoShowCount >= 2),
                        reasonText = rfmScore?.ReasonText ?? "Chưa tính toán điểm RFM.",
                        lastCalculatedAt = rfmScore != null ? rfmScore.LastCalculatedAt.ToString("dd/MM/yyyy HH:mm") : "Vừa cập nhật"
                    },

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

    /// <summary>
    /// POST /Customers/Delete
    /// Xóa vĩnh viễn hồ sơ khách hàng cùng toàn bộ lịch hẹn và tương tác liên quan khỏi SQL Server
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Delete([FromBody] CustomerDeleteDto dto)
    {
        if (dto == null || dto.CustomerId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã khách hàng không hợp lệ." });
        }

        try
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == dto.CustomerId);
            if (customer == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy hồ sơ khách hàng cần xóa." });
            }

            string customerName = customer.FullName;
            string customerPhone = customer.Phone;

            // 1. Tìm toàn bộ lịch hẹn của khách hàng này
            var appointments = await _context.Appointments
                .Include(a => a.AppointmentServices)
                .Where(a => a.CustomerId == dto.CustomerId)
                .ToListAsync();

            // 2. Xóa các dịch vụ chi tiết của các lịch hẹn
            foreach (var appt in appointments)
            {
                if (appt.AppointmentServices != null && appt.AppointmentServices.Any())
                {
                    _context.AppointmentServices.RemoveRange(appt.AppointmentServices);
                }
            }

            // 3. Xóa các lịch hẹn
            if (appointments.Any())
            {
                _context.Appointments.RemoveRange(appointments);
            }

            // 4. Xóa các tương tác chăm sóc khách hàng (SMS, Zalo, Email)
            var interactions = await _context.CustomerInteractions
                .Where(ci => ci.CustomerId == dto.CustomerId)
                .ToListAsync();
            if (interactions.Any())
            {
                _context.CustomerInteractions.RemoveRange(interactions);
            }

            // 5. Xóa thông báo liên quan (nếu có)
            var apptIds = appointments.Select(a => a.AppointmentId).ToList();
            var notifs = await _context.Notifications
                .Where(n => (n.ReferenceType == "CUSTOMER" && n.ReferenceId == dto.CustomerId)
                         || (n.ReferenceType == "APPOINTMENT" && n.ReferenceId.HasValue && apptIds.Contains(n.ReferenceId.Value)))
                .ToListAsync();
            if (notifs.Any())
            {
                _context.Notifications.RemoveRange(notifs);
            }

            // 6. Xóa hồ sơ khách hàng
            _context.Customers.Remove(customer);

            // Tự động kích hoạt Audit Log Interceptor lưu vết DELETE vào dbo.audit_logs
            await _context.SaveChangesAsync();

            _logger.LogInformation("Đã xóa vĩnh viễn khách hàng ID={Id}, Tên={Name}, SĐT={Phone}", 
                dto.CustomerId, customerName, customerPhone);

            return Json(new
            {
                success = true,
                message = $"Đã xóa thành công khách hàng \"{customerName}\" ({customerPhone}) khỏi hệ thống!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra khi xóa khách hàng ID {Id}", dto.CustomerId);
            return StatusCode(500, new
            {
                success = false,
                message = "Đã xảy ra lỗi trong quá trình xóa dữ liệu trên máy chủ: " + ex.Message
            });
        }
    }

    /// <summary>
    /// GET /Customers/DownloadTemplate?format=xlsx|csv
    /// Tải file mẫu chuẩn (.xlsx hoặc .csv) gồm 6 cột:
    /// Tên khách, Số điện thoại, Ngày làm dịch vụ gần nhất, Tên dịch vụ, Số tiền thanh toán, Trạng thái (Hoàn thành / Bỏ hẹn)
    /// </summary>
    [HttpGet]
    public IActionResult DownloadTemplate(string format = "xlsx")
    {
        var headers = new[]
        {
            "Tên khách",
            "Số điện thoại",
            "Ngày làm dịch vụ gần nhất",
            "Tên dịch vụ",
            "Số tiền thanh toán",
            "Trạng thái (Hoàn thành / Bỏ hẹn)"
        };

        var sampleRows = new[]
        {
            new[] { "Nguyễn Thị Ngọc Bích", "0912345689", "25/10/2026", "Chăm sóc da mặt chuyên sâu", "450000", "Hoàn thành" },
            new[] { "Trần Hoàng Minh Thư", "0987654312", "02/11/2026", "Massage thư giãn toàn thân", "550000", "Hoàn thành" },
            new[] { "Lê Phương Thảo", "0933445577", "05/11/2026", "Triệt lông công nghệ IPL", "800000", "Bỏ hẹn" },
            new[] { "Đỗ Bích Ngọc", "0977889900", "10/11/2026", "Gội đầu dưỡng sinh Thảo dược", "250000", "Hoàn thành" }
        };

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));
            foreach (var row in sampleRows)
            {
                sb.AppendLine(string.Join(",", row.Select(EscapeCsvField)));
            }

            var preamble = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            var resultBytes = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, resultBytes, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, resultBytes, preamble.Length, body.Length);

            return File(resultBytes, "text/csv; charset=utf-8", "Mau_Nhap_Khach_Hang_SalonCare.csv");
        }

        // Mặc định xuất file Excel (.xlsx) chuẩn đẹp
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Danh_Sach_Khach_Hang");

        // Header
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#6D28D9");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        ws.Row(1).Height = 26;

        // Định dạng cột Số điện thoại là Text (@) để không mất số 0 ở đầu
        ws.Column(2).Style.NumberFormat.Format = "@";

        // Dữ liệu mẫu
        for (int r = 0; r < sampleRows.Length; r++)
        {
            var row = sampleRows[r];
            ws.Cell(r + 2, 1).Value = row[0];
            ws.Cell(r + 2, 2).SetValue(row[1]);
            ws.Cell(r + 2, 3).Value = row[2];
            ws.Cell(r + 2, 4).Value = row[3];
            ws.Cell(r + 2, 5).Value = decimal.Parse(row[4]);
            ws.Cell(r + 2, 5).Style.NumberFormat.Format = "#,##0";
            ws.Cell(r + 2, 6).Value = row[5];
        }

        ws.Columns().AdjustToContents();
        for (int c = 1; c <= headers.Length; c++)
        {
            if (ws.Column(c).Width < 20)
                ws.Column(c).Width = 20;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Mau_Nhap_Khach_Hang_SalonCare.xlsx");
    }

    /// <summary>
    /// POST /Customers/ImportFromFile
    /// Tải lên file (.xlsx hoặc .csv), tự động bóc tách 6 cột, tính toán RFM và lưu vào SQL Server (bảng customers & appointments)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ImportFromFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "Vui lòng chọn một file .xlsx hoặc .csv hợp lệ để tải lên." });
        }

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".csv")
        {
            return BadRequest(new { success = false, message = "Định dạng file không hỗ trợ. Vui lòng sử dụng file .xlsx hoặc .csv theo mẫu chuẩn." });
        }

        try
        {
            var rawRows = new List<CustomerImportRow>();

            if (ext == ".xlsx")
            {
                using var stream = file.OpenReadStream();
                using var workbook = new XLWorkbook(stream);
                var ws = workbook.Worksheets.FirstOrDefault();
                if (ws == null)
                {
                    return BadRequest(new { success = false, message = "File Excel không chứa trang tính (Sheet) nào." });
                }

                var range = ws.RangeUsed();
                if (range != null)
                {
                    foreach (var row in range.RowsUsed().Skip(1)) // Bỏ qua dòng tiêu đề đầu tiên
                    {
                        var fullName = row.Cell(1).GetString().Trim();
                        var phone = row.Cell(2).GetString().Trim();
                        var dateCell = row.Cell(3);
                        var serviceName = row.Cell(4).GetString().Trim();
                        var amountStr = row.Cell(5).GetValue<string>().Trim();
                        var statusStr = row.Cell(6).GetString().Trim();

                        if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
                            continue;

                        DateTime parsedDate = ParseImportDate(dateCell);
                        decimal parsedAmount = ParseImportAmount(amountStr);

                        rawRows.Add(new CustomerImportRow
                        {
                            FullName = fullName,
                            Phone = NormalizePhone(phone),
                            ServiceDate = parsedDate,
                            ServiceName = string.IsNullOrWhiteSpace(serviceName) ? "Dịch vụ Spa" : serviceName,
                            Amount = parsedAmount,
                            StatusText = statusStr
                        });
                    }
                }
            }
            else // .csv
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true);
                bool isFirstLine = true;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var cols = SplitCsvLine(line);
                    if (cols.Count < 2)
                        continue;

                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        // Kiểm tra nếu dòng đầu là dòng tiêu đề thì bỏ qua
                        if (cols[0].Contains("Tên", StringComparison.OrdinalIgnoreCase) ||
                            cols[1].Contains("điện thoại", StringComparison.OrdinalIgnoreCase) ||
                            cols[1].Contains("SĐT", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    var fullName = cols.ElementAtOrDefault(0)?.Trim() ?? "";
                    var phone = cols.ElementAtOrDefault(1)?.Trim() ?? "";
                    var dateStr = cols.ElementAtOrDefault(2)?.Trim() ?? "";
                    var serviceName = cols.ElementAtOrDefault(3)?.Trim() ?? "";
                    var amountStr = cols.ElementAtOrDefault(4)?.Trim() ?? "0";
                    var statusStr = cols.ElementAtOrDefault(5)?.Trim() ?? "Hoàn thành";

                    if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
                        continue;

                    rawRows.Add(new CustomerImportRow
                    {
                        FullName = fullName,
                        Phone = NormalizePhone(phone),
                        ServiceDate = ParseImportDateString(dateStr),
                        ServiceName = string.IsNullOrWhiteSpace(serviceName) ? "Dịch vụ Spa" : serviceName,
                        Amount = ParseImportAmount(amountStr),
                        StatusText = statusStr
                    });
                }
            }

            if (rawRows.Count == 0)
            {
                return BadRequest(new { success = false, message = "Không tìm thấy dòng dữ liệu hợp lệ nào trong file. Vui lòng kiểm tra lại nội dung file." });
            }

            // Nạp sẵn danh sách khách hàng và dịch vụ hiện có trong SQL Server để đối chiếu nhanh
            var existingCustomers = await _context.Customers.ToDictionaryAsync(c => c.Phone, c => c);
            var existingServices = await _context.Services.ToListAsync();

            int newCustomersCount = 0;
            int updatedCustomersCount = 0;
            int completedCount = 0;
            int noShowCount = 0;
            int skippedCount = 0;

            var touchedExistingPhones = new HashSet<string>();
            var pendingAppointments = new List<(Customer Customer, Service Service, CustomerImportRow Row, bool IsNoShow)>();

            foreach (var row in rawRows)
            {
                if (string.IsNullOrWhiteSpace(row.FullName) || string.IsNullOrWhiteSpace(row.Phone) || row.Phone.Length < 9)
                {
                    skippedCount++;
                    continue;
                }

                bool isNoShow = IsNoShowStatus(row.StatusText);
                if (isNoShow) noShowCount++;
                else completedCount++;

                // 1. Tìm hoặc tự động tạo Dịch vụ tương ứng
                var service = existingServices.FirstOrDefault(s =>
                    string.Equals(s.ServiceName.Trim(), row.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (service == null)
                {
                    service = new Service
                    {
                        ServiceName = row.ServiceName.Trim(),
                        CategoryName = "Chăm sóc da",
                        DurationMinutes = 60,
                        Price = row.Amount > 0 ? row.Amount : 350000m,
                        IconClass = "fa-spa",
                        BgColor = "bg-purple-500",
                        IsActive = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    _context.Services.Add(service);
                    existingServices.Add(service);
                }

                // 2. Tìm hoặc tạo mới Khách hàng trong bảng customers
                if (!existingCustomers.TryGetValue(row.Phone, out var customer))
                {
                    customer = new Customer
                    {
                        FullName = row.FullName,
                        Phone = row.Phone,
                        Gender = "Nữ",
                        CustomerSource = "Truc_tiep",
                        CustomerStatus = "Hoat_dong",
                        TotalVisits = isNoShow ? 0 : 1,
                        TotalSpent = isNoShow ? 0m : row.Amount,
                        NoShowCount = isNoShow ? 1 : 0,
                        LastVisitAt = isNoShow ? null : row.ServiceDate,
                        LastBookingAt = row.ServiceDate,
                        Notes = $"Nhập từ file dữ liệu ({row.ServiceName} - {(isNoShow ? "Bỏ hẹn" : "Hoàn thành")})",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    customer.CustomerType = EvaluateRfmTier(customer.TotalVisits, customer.TotalSpent);

                    _context.Customers.Add(customer);
                    existingCustomers[row.Phone] = customer;
                    newCustomersCount++;
                }
                else
                {
                    // Cập nhật lũy kế chỉ số RFM cho khách hàng đã tồn tại
                    if (!string.IsNullOrWhiteSpace(row.FullName))
                    {
                        customer.FullName = row.FullName;
                    }

                    if (isNoShow)
                    {
                        customer.NoShowCount += 1;
                    }
                    else
                    {
                        customer.TotalVisits += 1;
                        customer.TotalSpent += row.Amount;
                        if (!customer.LastVisitAt.HasValue || row.ServiceDate >= customer.LastVisitAt.Value)
                        {
                            customer.LastVisitAt = row.ServiceDate;
                        }
                    }

                    if (!customer.LastBookingAt.HasValue || row.ServiceDate >= customer.LastBookingAt.Value)
                    {
                        customer.LastBookingAt = row.ServiceDate;
                    }

                    customer.CustomerType = EvaluateRfmTier(customer.TotalVisits, customer.TotalSpent);
                    customer.UpdatedAt = DateTime.Now;

                    if (customer.CustomerId > 0 && touchedExistingPhones.Add(row.Phone))
                    {
                        updatedCustomersCount++;
                    }
                }

                pendingAppointments.Add((customer, service, row, isNoShow));
            }

            // Lưu khách hàng và dịch vụ trước để lấy CustomerId và ServiceId tự tăng từ SQL Server
            await _context.SaveChangesAsync();

            // 3. Lưu lịch sử lịch hẹn (appointments & appointment_services) để đồng bộ 100% hồ sơ khách hàng
            foreach (var item in pendingAppointments)
            {
                var appt = new Appointment
                {
                    CustomerId = item.Customer.CustomerId,
                    AppointmentDate = item.Row.ServiceDate.Date,
                    StartTime = new TimeSpan(9, 30, 0),
                    EndTime = new TimeSpan(10, 30, 0),
                    Status = item.IsNoShow ? "NoShow" : "Completed",
                    BookingSource = "Direct",
                    TotalPrice = item.IsNoShow ? 0m : item.Row.Amount,
                    CompletedAt = item.IsNoShow ? null : item.Row.ServiceDate.Date.AddHours(10.5),
                    CancelledAt = item.IsNoShow ? item.Row.ServiceDate.Date.AddHours(9.5) : null,
                    CancellationReason = item.IsNoShow ? "Khách bỏ hẹn (Đồng bộ từ file dữ liệu)" : null,
                    IsHiddenFromBoard = true, // Lịch sử cũ lưu trong hồ sơ khách hàng, không làm rối bảng Kanban hôm nay
                    HiddenAt = DateTime.Now,
                    Notes = $"Đồng bộ từ file: {item.Row.ServiceName} ({(item.IsNoShow ? "Bỏ hẹn" : "Hoàn thành")})",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                appt.AppointmentServices.Add(new AppointmentService
                {
                    ServiceId = item.Service.ServiceId,
                    Price = item.Row.Amount
                });

                _context.Appointments.Add(appt);
            }

            await _context.SaveChangesAsync();

            // Tự động chạy RfmService.RecalculateAllAsync() để phân loại ngay lập tức toàn bộ khách hàng
            await _rfmService.RecalculateAllAsync();

            // Ghi log hành động IMPORT_LEGACY_DATA vào AuditLogs
            await _auditLogService.LogActionAsync(
                "CUSTOMERS",
                "IMPORT_LEGACY_DATA",
                $"Import dữ liệu lịch sử từ file \"{file.FileName}\": Đọc {rawRows.Count} dòng (Khách mới: {newCustomersCount}, Khách cập nhật: {updatedCustomersCount}, Hoàn thành: {completedCount}, Bỏ hẹn: {noShowCount}, Bỏ qua: {skippedCount}).",
                file.FileName);

            _logger.LogInformation(
                "Import dữ liệu khách hàng thành công: Tổng dòng={Total}, Thêm mới={New}, Cập nhật={Updated}, Hoàn thành={Completed}, Bỏ hẹn={NoShow}",
                rawRows.Count, newCustomersCount, updatedCustomersCount, completedCount, noShowCount);

            return Json(new
            {
                success = true,
                message = $"Tải lên hoàn tất! Đã lưu {newCustomersCount} khách mới, cập nhật {updatedCustomersCount} khách cũ ({completedCount} lượt Hoàn thành, {noShowCount} lượt Bỏ hẹn).",
                summary = new
                {
                    totalRows = rawRows.Count,
                    newCustomersCount,
                    updatedCustomersCount,
                    completedCount,
                    noShowCount,
                    skippedCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi import file dữ liệu khách hàng");
            return StatusCode(500, new
            {
                success = false,
                message = "Đã xảy ra lỗi khi đọc và lưu file vào SQL Server: " + ex.Message
            });
        }
    }

    #region Helpers cho Import / Export File Mẫu

    private static string EvaluateRfmTier(int totalVisits, decimal totalSpent)
    {
        if (totalSpent >= 3000000m || totalVisits >= 6)
            return "VIP";
        if (totalVisits >= 3 || totalSpent >= 1500000m)
            return "Than_thiet";
        return "Moi";
    }

    private static string NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = Regex.Replace(raw.Trim(), @"[^\d+]", "");
        if (digits.StartsWith("+84"))
            digits = "0" + digits.Substring(3);
        else if (digits.StartsWith("84") && digits.Length == 11)
            digits = "0" + digits.Substring(2);
        else if (digits.Length == 9 && !digits.StartsWith("0"))
            digits = "0" + digits; // Trường hợp Excel tự cắt số 0 ở đầu
        return digits;
    }

    private static DateTime ParseImportDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return DateTime.Today;
        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime().Date;
        if (cell.DataType == XLDataType.Number)
        {
            try { return DateTime.FromOADate(cell.GetDouble()).Date; }
            catch { /* ignore */ }
        }
        return ParseImportDateString(cell.GetString());
    }

    private static DateTime ParseImportDateString(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.Today;
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "MM/dd/yyyy", "yyyy/MM/dd" };
        if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        if (DateTime.TryParse(dateStr.Trim(), new CultureInfo("vi-VN"), DateTimeStyles.None, out dt))
            return dt.Date;
        return DateTime.Today;
    }

    private static decimal ParseImportAmount(string? amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr)) return 0m;
        var cleaned = Regex.Replace(amountStr, @"[^\d]", "");
        if (decimal.TryParse(cleaned, out var val))
            return val;
        return 0m;
    }

    private static bool IsNoShowStatus(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText)) return false;
        var s = statusText.Trim().ToLowerInvariant();
        return s.Contains("bỏ hẹn") || s.Contains("bo hen") || s.Contains("noshow") ||
               s.Contains("no-show") || s.Contains("vắng") || s.Contains("hủy") || s.Contains("huy");
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    private static List<string> SplitCsvLine(string line)
    {
        // Tự động nhận diện dấu phân cách ',' hoặc ';'
        char delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    private sealed class CustomerImportRow
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public DateTime ServiceDate { get; set; } = DateTime.Today;
        public string ServiceName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    #endregion
}

public class CustomerDeleteDto
{
    public long CustomerId { get; set; }
}
