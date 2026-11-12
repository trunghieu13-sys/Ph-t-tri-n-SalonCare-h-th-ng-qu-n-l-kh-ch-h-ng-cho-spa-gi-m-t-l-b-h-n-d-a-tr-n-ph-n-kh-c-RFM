using System.Globalization;
using System.Text.Json;
using CRM_V2.Data;
using CRM_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

[Authorize]
public class RfmController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IRfmService _rfmService;
    private readonly IReminderOrchestrator _reminderOrchestrator;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<RfmController> _logger;
    private static readonly CultureInfo VnCulture = new("vi-VN");

    public RfmController(
        ApplicationDbContext context,
        IRfmService rfmService,
        IReminderOrchestrator reminderOrchestrator,
        IAuditLogService auditLogService,
        ILogger<RfmController> logger)
    {
        _context = context;
        _rfmService = rfmService;
        _reminderOrchestrator = reminderOrchestrator;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    [HttpGet("/Rfm")]
    public IActionResult Index()
    {
        return Redirect("/#rfmSegmentationView");
    }

    [HttpGet("/Notifications")]
    public IActionResult Notifications()
    {
        return Redirect("/#rfmNotificationsView");
    }

    /// <summary>
    /// GET /Rfm/GetDashboard?segment=ALL&onlyNoShowRisk=false&search=
    /// Trả về 5 thẻ KPI phân khúc, danh sách khách hàng kèm điểm R-F-M & ReasonText, và bảng cấu hình SystemConfigs (RFM + Zalo OA)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDashboard(string? segment = "ALL", bool onlyNoShowRisk = false, string? search = null)
    {
        try
        {
            bool anyScore = await _context.RfmScores.AnyAsync();
            if (!anyScore)
            {
                await _rfmService.RecalculateAllAsync();
            }

            var allScores = await _context.RfmScores
                .AsNoTracking()
                .Include(r => r.Customer)
                .Where(r => r.Customer != null)
                .ToListAsync();

            int totalCount = allScores.Count;
            int vipCount = allScores.Count(r => r.SegmentGroup == "VIP");
            int loyalCount = allScores.Count(r => r.SegmentGroup == "Loyal");
            int newCount = allScores.Count(r => r.SegmentGroup == "New");
            int atRiskCount = allScores.Count(r => r.SegmentGroup == "AtRisk");
            int noShowRiskCount = allScores.Count(r => r.IsNoShowRisk);

            var filtered = allScores.AsEnumerable();

            if (onlyNoShowRisk || string.Equals(segment, "NoShowRisk", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(r => r.IsNoShowRisk);
            }
            else if (!string.IsNullOrWhiteSpace(segment) && !string.Equals(segment, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(r => string.Equals(r.SegmentGroup, segment.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                string kw = search.Trim().ToLowerInvariant();
                filtered = filtered.Where(r =>
                    (r.Customer!.FullName != null && r.Customer.FullName.ToLowerInvariant().Contains(kw)) ||
                    (r.Customer!.Phone != null && r.Customer.Phone.Contains(kw)) ||
                    (r.ReasonText != null && r.ReasonText.ToLowerInvariant().Contains(kw)));
            }

            var list = filtered
                .OrderByDescending(r => r.MonetaryTotal)
                .ThenBy(r => r.RecencyDays)
                .Select(r => new
                {
                    customerId = r.CustomerId,
                    fullName = r.Customer!.FullName,
                    phone = r.Customer.Phone,
                    zaloUserId = r.Customer.ZaloUserId ?? "",
                    email = r.Customer.Email ?? "",
                    recencyDays = r.RecencyDays,
                    frequencyCount = r.FrequencyCount,
                    monetaryTotal = r.MonetaryTotal,
                    formattedMonetary = r.MonetaryTotal.ToString("N0", VnCulture) + "đ",
                    noShowCount = r.NoShowCount,
                    segmentGroup = r.SegmentGroup,
                    isNoShowRisk = r.IsNoShowRisk,
                    reasonText = r.ReasonText,
                    lastCalculatedAt = r.LastCalculatedAt.ToString("dd/MM/yyyy HH:mm")
                })
                .ToList();

            var configs = await _rfmService.GetAllSystemConfigsAsync();

            return Json(new
            {
                success = true,
                counts = new
                {
                    all = totalCount,
                    vip = vipCount,
                    loyal = loyalCount,
                    newCount = newCount,
                    atRisk = atRiskCount,
                    noShowRisk = noShowRiskCount
                },
                summary = new
                {
                    totalCount,
                    vipCount,
                    loyalCount,
                    newCount,
                    atRiskCount,
                    noShowRiskCount
                },
                data = list,
                configs = configs.Select(c => new
                {
                    configKey = c.ConfigKey,
                    configValue = c.ConfigValue,
                    description = c.Description ?? "",
                    updatedAt = c.UpdatedAt.ToString("dd/MM/yyyy HH:mm")
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải dữ liệu Dashboard RFM");
            return StatusCode(500, new { success = false, message = "Lỗi khi tải dữ liệu RFM: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Rfm/RecalculateAll
    /// Nút "Chạy tính lại RFM ngay" trên giao diện
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RecalculateAll()
    {
        try
        {
            int count = await _rfmService.RecalculateAllAsync();
            await _auditLogService.LogActionAsync(
                "CUSTOMERS",
                "UPDATE",
                $"Kích hoạt tính toán lại điểm và phân nhóm RFM cho toàn bộ {count} khách hàng dựa trên ngưỡng cấu hình động.");

            return Json(new
            {
                success = true,
                message = $"Đã tính toán lại điểm RFM và cập nhật giải thích phân khúc cho {count} khách hàng!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tính lại RFM");
            return StatusCode(500, new { success = false, message = "Lỗi khi tính lại RFM: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Rfm/UpdateConfigs
    /// Cập nhật các giá trị ngưỡng động RFM & cấu hình Zalo OA / ZNS trong bảng SystemConfigs (Chỉ dành cho Owner / Admin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Admin,owner,admin")]
    public async Task<IActionResult> UpdateConfigs([FromBody] JsonElement body)
    {
        try
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("configs", out var configsProp) && configsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in configsProp.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ToString();
                    }
                }
                else
                {
                    foreach (var prop in body.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.ToString();
                    }
                }
            }

            if (dict.Count == 0)
            {
                return BadRequest(new { success = false, message = "Dữ liệu cấu hình không hợp lệ." });
            }

            await _rfmService.UpdateSystemConfigsAsync(dict);
            int recalculated = await _rfmService.RecalculateAllAsync();

            return Json(new
            {
                success = true,
                message = $"Đã lưu {dict.Count} thông số cấu hình hệ thống (RFM & Zalo OA/ZNS) vào SQL Server và tính lại phân khúc cho {recalculated} khách hàng!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật cấu hình SystemConfigs");
            return StatusCode(500, new { success = false, message = "Lỗi khi lưu cấu hình: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Rfm/SendWinBackPromo & /Rfm/SendWinBackCampaign
    /// Nút "Gửi ưu đãi nhóm Nguy cơ (AtRisk)"
    /// </summary>
    [HttpPost("Rfm/SendWinBackPromo")]
    [HttpPost("Rfm/SendWinBackCampaign")]
    public async Task<IActionResult> SendWinBackPromo()
    {
        try
        {
            int sent = await _reminderOrchestrator.SendWinBackPromoToAtRiskCustomersAsync();
            await _auditLogService.LogActionAsync(
                "CUSTOMER_CARE",
                "CREATE",
                $"Phát động chiến dịch gửi tin ưu đãi Win-Back [WINBACK20] qua Zalo OA / ZNS tới {sent} khách hàng thuộc nhóm Nguy cơ rời bỏ (AtRisk).");

            return Json(new
            {
                success = true,
                sentCount = sent,
                message = sent > 0
                    ? $"Đã gửi thành công {sent} tin nhắn ưu đãi Win-Back (Zalo ZNS / Fallback) tới nhóm khách hàng Nguy cơ rời bỏ (AtRisk)!"
                    : "Tất cả khách hàng thuộc nhóm Nguy cơ rời bỏ (AtRisk) đều đã nhận được ưu đãi trong 30 ngày qua."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi ưu đãi nhóm AtRisk");
            return StatusCode(500, new { success = false, message = "Lỗi khi gửi ưu đãi: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Rfm/TriggerRemindersNow
    /// Quét và gửi nhắc lịch hẹn 24h / 2h ngay lập tức để phục vụ Demo
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> TriggerRemindersNow()
    {
        try
        {
            int sent = await _reminderOrchestrator.ScanAndSendAppointmentRemindersAsync();
            return Json(new
            {
                success = true,
                sentCount = sent,
                message = sent > 0
                    ? $"Đã quét và gửi {sent} tin nhắn nhắc lịch hẹn (Zalo OA ZNS / Fallback) kèm link xác nhận 1-Click!"
                    : "Đã quét toàn bộ lịch hẹn sắp tới (Không có lịch hẹn mới nào trong khung 24h/2h cần gửi nhắc thêm tại thời điểm này)."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi kích hoạt quét nhắc lịch ngay");
            return StatusCode(500, new { success = false, message = "Lỗi khi quét nhắc lịch: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Rfm/SendDemoZnsReminder
    /// Tạo nhanh 1 bản ghi gửi tin Zalo OA ZNS trên lịch hẹn gần nhất (hoặc khách hàng đầu tiên) để Demo luồng JSON Payload & Webhook 1-Click ngay lập tức
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SendDemoZnsReminder()
    {
        try
        {
            var targetAppt = await _context.Appointments
                .Include(a => a.Customer)
                    .ThenInclude(c => c!.RfmScore)
                .Include(a => a.Employee)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .Where(a => a.Customer != null && (a.Status == "Pending" || a.Status == "Confirmed"))
                .OrderByDescending(a => a.AppointmentDate)
                .ThenByDescending(a => a.AppointmentId)
                .FirstOrDefaultAsync();

            if (targetAppt == null)
            {
                targetAppt = await _context.Appointments
                    .Include(a => a.Customer)
                        .ThenInclude(c => c!.RfmScore)
                    .Include(a => a.Employee)
                    .Include(a => a.AppointmentServices)
                        .ThenInclude(aps => aps.Service)
                    .Where(a => a.Customer != null)
                    .OrderByDescending(a => a.AppointmentId)
                    .FirstOrDefaultAsync();
            }

            if (targetAppt?.Customer == null)
            {
                return BadRequest(new { success = false, message = "Chưa có lịch hẹn nào trong hệ thống để gửi thử Zalo ZNS." });
            }

            if (targetAppt.ConfirmationToken == Guid.Empty)
            {
                targetAppt.ConfirmationToken = Guid.NewGuid();
                await _context.SaveChangesAsync();
            }

            string segment = targetAppt.Customer.RfmScore?.SegmentGroup ?? "New";
            string serviceNames = targetAppt.AppointmentServices.Any()
                ? string.Join(", ", targetAppt.AppointmentServices.Where(s => s.Service != null).Select(s => s.Service!.ServiceName))
                : "Chăm sóc da chuyên sâu";
            string staffName = targetAppt.Employee?.FullName ?? "KTV Minh Anh";
            string confirmUrl = $"/BookingPublic/Confirm?token={targetAppt.ConfirmationToken}";

            string content = $"[Zalo ZNS Nhắc lịch 24h - {segment}] Chào {targetAppt.Customer.FullName}, bạn có lịch hẹn \"{serviceNames}\" lúc {targetAppt.StartTime:hh\\:mm} ngày {targetAppt.AppointmentDate:dd/MM/yyyy} cùng {staffName}. Xác nhận 1-Click: {confirmUrl}";

            await _reminderOrchestrator.DispatchWithChannelStrategyAsync(
                targetAppt.Customer,
                targetAppt,
                "Reminder24h",
                segment,
                content);

            return Json(new
            {
                success = true,
                message = $"Đã đóng gói JSON Payload chuẩn Zalo ZNS và gửi thông báo nhắc lịch #{targetAppt.AppointmentId} tới khách hàng {targetAppt.Customer.FullName} ({ZaloOaNotificationSender.NormalizePhoneForZaloZns(targetAppt.Customer.Phone)})!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi thử Zalo ZNS");
            return StatusCode(500, new { success = false, message = "Lỗi khi gửi thử Zalo ZNS: " + ex.Message });
        }
    }

    /// <summary>
    /// GET /Rfm/GetNotificationLogs
    /// Lấy danh sách Nhật ký nhắc lịch (NotificationLogs) hiển thị trên giao diện kèm JSON Payload & nút Giả lập Webhook Zalo 1-Click
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotificationLogs(
        string? messageType = "ALL",
        string? segment = "ALL",
        string? channel = "ALL")
    {
        try
        {
            var allLogs = await _context.NotificationLogs
                .AsNoTracking()
                .ToListAsync();

            var stats = new
            {
                totalSent = allLogs.Count,
                zaloCount = allLogs.Count(x => x.Channel == "ZaloOA"),
                smsCount = allLogs.Count(x => x.Channel == "SMS"),
                emailCount = allLogs.Count(x => x.Channel == "Email"),
                reminder24h = allLogs.Count(x => x.MessageType == "Reminder24h"),
                reminder2h = allLogs.Count(x => x.MessageType == "Reminder2h"),
                winBackPromo = allLogs.Count(x => x.MessageType == "WinBackPromo"),
                confirmedByCustomer = allLogs.Count(x => x.Status == "ConfirmedByCustomer")
            };

            var query = _context.NotificationLogs
                .AsNoTracking()
                .Include(nl => nl.Customer)
                .Include(nl => nl.Appointment)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(messageType) && messageType != "ALL")
            {
                query = query.Where(nl => nl.MessageType == messageType);
            }

            if (!string.IsNullOrWhiteSpace(segment) && segment != "ALL")
            {
                query = query.Where(nl => nl.SegmentAtSendTime == segment);
            }

            if (!string.IsNullOrWhiteSpace(channel) && channel != "ALL")
            {
                query = query.Where(nl => nl.Channel == channel);
            }

            var logs = await query
                .OrderByDescending(nl => nl.SentAt)
                .ThenByDescending(nl => nl.LogId)
                .Take(200)
                .Select(nl => new
                {
                    logId = nl.LogId,
                    customerId = nl.CustomerId,
                    customerName = nl.Customer != null ? nl.Customer.FullName : "Khách hàng",
                    customerPhone = nl.Customer != null ? nl.Customer.Phone : "---",
                    zaloPhone = nl.Customer != null ? ZaloOaNotificationSender.NormalizePhoneForZaloZns(nl.Customer.Phone) : "",
                    zaloUserId = nl.Customer != null ? (nl.Customer.ZaloUserId ?? "") : "",
                    appointmentId = nl.AppointmentId,
                    appointmentTime = nl.Appointment != null
                        ? $"{nl.Appointment.StartTime:hh\\:mm} - {nl.Appointment.AppointmentDate:dd/MM/yyyy}"
                        : "Chiến dịch CSKH",
                    appointmentStatus = nl.Appointment != null ? nl.Appointment.Status : "---",
                    confirmationToken = nl.Appointment != null ? nl.Appointment.ConfirmationToken.ToString() : "",
                    confirmUrl = nl.Appointment != null && nl.Appointment.ConfirmationToken != Guid.Empty
                        ? $"/BookingPublic/Confirm?token={nl.Appointment.ConfirmationToken}"
                        : "",
                    channel = nl.Channel,
                    messageType = nl.MessageType,
                    segmentAtSendTime = nl.SegmentAtSendTime,
                    content = nl.Content,
                    status = nl.Status,
                    externalMessageId = nl.ExternalMessageId ?? "",
                    payloadJson = nl.PayloadJson ?? "",
                    errorMessage = nl.ErrorMessage ?? "",
                    sentAt = nl.SentAt.ToString("dd/MM/yyyy HH:mm:ss")
                })
                .ToListAsync();

            return Json(new
            {
                success = true,
                stats,
                data = logs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải danh sách NotificationLogs");
            return StatusCode(500, new { success = false, message = "Lỗi khi tải nhật ký nhắc lịch: " + ex.Message });
        }
    }
}
