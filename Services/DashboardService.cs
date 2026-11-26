using System.Data;
using System.Data.Common;
using System.Globalization;
using CRM_V2.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<DashboardService> _logger;
    private static readonly CultureInfo VnCulture = new("vi-VN");

    private static readonly (string gradient, string textColor)[] ServiceStyles = new[]
    {
        ("from-purple-500 to-indigo-500", "text-purple-300"),
        ("from-pink-500 to-rose-500", "text-pink-300"),
        ("from-cyan-500 to-blue-500", "text-cyan-300"),
        ("from-emerald-500 to-teal-500", "text-emerald-300"),
        ("from-amber-500 to-orange-500", "text-amber-300")
    };

    public DashboardService(
        ApplicationDbContext context,
        ILogger<DashboardService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _context = context;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    private TenantResolution ResolveCurrentTenant()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        return TenantContext.Resolve(httpContext?.User, _context, httpContext);
    }

    public async Task<DashboardSummaryDto> GetSummaryDataAsync(DateTime? targetDate = null)
    {
        var result = new DashboardSummaryDto();
        var dateParam = (targetDate ?? DateTime.Today).Date;
        var tenant = ResolveCurrentTenant();

        // Nếu là Staff có SpaId == NULL (chưa tham gia đội ngũ Spa nào) -> trả về dữ liệu rỗng (0 khách, 0 lịch hẹn, 0 doanh thu)
        if (tenant.IsUnassignedTenantUser)
        {
            result.Kpis.MonthRevenueFormatted = "0đ";
            for (int i = 5; i >= 0; i--)
            {
                var m = dateParam.AddMonths(-i);
                result.Charts.Revenue6Months.Labels.Add($"T{m.Month}/{m.Year % 100}");
                result.Charts.Revenue6Months.RawRevenues.Add(0m);
                result.Charts.Revenue6Months.Data.Add(0);
            }
            return result;
        }

        // Nếu là Owner/Staff đã có SpaId hoặc Admin đang chọn lọc 1 Spa cụ thể -> Tính toán 100% theo WHERE spa_id = CurrentUserSpaId
        if (!tenant.IsAdminOrUnscoped && tenant.SpaId.HasValue)
        {
            return await BuildTenantScopedSummaryAsync(tenant.SpaId.Value, dateParam);
        }

        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.sp_GetDashboardSummary";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@TargetDate", SqlDbType.Date) { Value = dateParam.Date });

            await using var reader = await cmd.ExecuteReaderAsync();

            // 1. KPI Metrics
            if (await reader.ReadAsync())
            {
                result.Kpis.TotalCustomers = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader[0]);
                result.Kpis.CustomerGrowth = reader.IsDBNull(1) ? 0 : Math.Round(Convert.ToDouble(reader[1]), 1);
                result.Kpis.TodayAppointments = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader[2]);
                result.Kpis.AppointmentGrowth = reader.IsDBNull(3) ? 0 : Math.Round(Convert.ToDouble(reader[3]), 1);
                result.Kpis.MonthRevenue = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader[4]);
                result.Kpis.RevenueGrowth = reader.IsDBNull(5) ? 0 : Math.Round(Convert.ToDouble(reader[5]), 1);
                result.Kpis.ReturnRate = reader.IsDBNull(6) ? 0 : Math.Round(Convert.ToDouble(reader[6]), 1);
                result.Kpis.ReturnRateGrowth = reader.IsDBNull(7) ? 0 : Math.Round(Convert.ToDouble(reader[7]), 1);

                result.Kpis.MonthRevenueFormatted = string.Format(VnCulture, "{0:N0}đ", result.Kpis.MonthRevenue);
            }

            // 2. Calendar Appointments trong tháng
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        var apptDate = Convert.ToDateTime(reader[0]);
                        var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader[1]);

                        result.CalendarAppointments.Add(new CalendarAppointmentDayDto
                        {
                            Date = apptDate.ToString("yyyy-MM-dd"),
                            Day = apptDate.Day,
                            Count = count
                        });
                    }
                }
            }

            // 3. Chi tiết lịch hẹn hôm nay (hoặc ngày chọn)
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    result.TodayApptDetails.Add(new DashboardAppointmentItemDto
                    {
                        AppointmentId = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]),
                        Time = reader.IsDBNull(1) ? string.Empty : reader[1].ToString() ?? string.Empty,
                        CustomerId = reader.IsDBNull(2) ? 0 : Convert.ToInt64(reader[2]),
                        CustomerName = reader.IsDBNull(3) ? "Khách hàng" : reader[3].ToString() ?? "Khách hàng",
                        CustomerPhone = reader.IsDBNull(4) ? string.Empty : reader[4].ToString() ?? string.Empty,
                        ServiceName = reader.IsDBNull(5) ? "Dịch vụ Spa" : reader[5].ToString() ?? "Dịch vụ Spa",
                        Status = reader.IsDBNull(6) ? "Pending" : reader[6].ToString() ?? "Pending",
                        TotalPrice = reader.IsDBNull(7) ? 0m : Convert.ToDecimal(reader[7])
                    });
                }
            }

            // 4. Nhật ký hoạt động mới nhất (Audit Log)
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    var actType = reader.IsDBNull(0) ? "NOTIFICATION" : reader[0].ToString() ?? "NOTIFICATION";
                    var title = reader.IsDBNull(1) ? string.Empty : reader[1].ToString() ?? string.Empty;
                    var detail = reader.IsDBNull(2) ? string.Empty : reader[2].ToString() ?? string.Empty;
                    var createdAt = reader.IsDBNull(3) ? DateTime.Now : Convert.ToDateTime(reader[3]);
                    var icon = reader.IsDBNull(4) ? "fa-bell" : reader[4].ToString() ?? "fa-bell";
                    var color = reader.IsDBNull(5) ? "bg-purple-500/20 text-purple-400 border-purple-500/30" : reader[5].ToString() ?? "bg-purple-500/20 text-purple-400 border-purple-500/30";

                    result.RecentActivities.Add(new DashboardActivityDto
                    {
                        Type = actType,
                        Title = title,
                        Detail = detail,
                        CreatedAt = createdAt,
                        TimeAgo = FormatTimeAgo(createdAt),
                        IconClass = icon,
                        ColorClass = color
                    });
                }
            }

            // 5. Doanh thu 6 tháng gần nhất
            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    var label = reader.IsDBNull(1) ? string.Empty : reader[1].ToString() ?? string.Empty;
                    var rev = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader[4]);

                    result.Charts.Revenue6Months.Labels.Add(label);
                    result.Charts.Revenue6Months.RawRevenues.Add(rev);
                    // Chuyển sang triệu đồng để đồ thị đường hiển thị tinh gọn (VD: 4.25 tr)
                    result.Charts.Revenue6Months.Data.Add(Math.Round((double)rev / 1_000_000.0, 2));
                }
            }

            // 6. Top Dịch vụ được yêu thích tháng này
            if (await reader.NextResultAsync())
            {
                int styleIdx = 0;
                while (await reader.ReadAsync())
                {
                    var sName = reader.IsDBNull(0) ? "Dịch vụ" : reader[0].ToString() ?? "Dịch vụ";
                    var count = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader[1]);
                    var rev = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader[2]);
                    var pct = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader[3]);
                    var icon = reader.IsDBNull(4) ? "fa-spa" : reader[4].ToString() ?? "fa-spa";

                    var (gradient, textColor) = ServiceStyles[styleIdx % ServiceStyles.Length];
                    styleIdx++;

                    result.Charts.TopServices.ServiceNames.Add(sName);
                    result.Charts.TopServices.Percentages.Add(pct);
                    result.Charts.TopServices.Items.Add(new TopServiceItemDto
                    {
                        Name = sName,
                        BookingCount = count,
                        TotalRevenue = rev,
                        Percentage = pct,
                        Icon = icon,
                        GradientClass = gradient,
                        TextColor = textColor
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy dữ liệu tổng hợp Dashboard");
        }

        return result;
    }

    public async Task<List<DashboardAppointmentItemDto>> GetAppointmentsByDateAsync(DateTime date)
    {
        var list = new List<DashboardAppointmentItemDto>();
        var tenant = ResolveCurrentTenant();

        if (tenant.IsUnassignedTenantUser)
        {
            return list;
        }

        if (!tenant.IsAdminOrUnscoped && tenant.SpaId.HasValue)
        {
            var spaId = tenant.SpaId.Value;
            var appts = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Customer)
                .Include(a => a.Employee)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .Where(a => a.SpaId == spaId && a.AppointmentDate == date.Date)
                .OrderBy(a => a.StartTime)
                .ToListAsync();

            foreach (var a in appts)
            {
                var srvName = string.Join(", ", a.AppointmentServices.Select(s => s.Service?.ServiceName).Where(s => !string.IsNullOrWhiteSpace(s)));
                list.Add(new DashboardAppointmentItemDto
                {
                    AppointmentId = a.AppointmentId,
                    Time = a.StartTime.ToString(@"hh\:mm"),
                    EndTime = a.EndTime.ToString(@"hh\:mm"),
                    CustomerId = a.CustomerId,
                    CustomerName = a.Customer?.FullName ?? "Khách hàng",
                    CustomerPhone = a.Customer?.Phone ?? string.Empty,
                    ServiceName = string.IsNullOrWhiteSpace(srvName) ? "Dịch vụ Spa" : srvName,
                    Status = a.Status,
                    TotalPrice = a.TotalPrice,
                    StaffName = a.Employee?.FullName ?? "Chưa phân công"
                });
            }

            return list;
        }

        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.sp_GetAppointmentsByDate";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@SelectedDate", SqlDbType.Date) { Value = date.Date });

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new DashboardAppointmentItemDto
                {
                    AppointmentId = reader.IsDBNull(0) ? 0 : Convert.ToInt64(reader[0]),
                    Time = reader.IsDBNull(1) ? string.Empty : reader[1].ToString() ?? string.Empty,
                    EndTime = reader.IsDBNull(2) ? string.Empty : reader[2].ToString(),
                    CustomerId = reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader[3]),
                    CustomerName = reader.IsDBNull(4) ? "Khách hàng" : reader[4].ToString() ?? "Khách hàng",
                    CustomerPhone = reader.IsDBNull(5) ? string.Empty : reader[5].ToString() ?? string.Empty,
                    ServiceName = reader.IsDBNull(6) ? "Dịch vụ Spa" : reader[6].ToString() ?? "Dịch vụ Spa",
                    Status = reader.IsDBNull(7) ? "Pending" : reader[7].ToString() ?? "Pending",
                    TotalPrice = reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader[8]),
                    StaffName = reader.IsDBNull(9) ? "Chưa phân công" : reader[9].ToString() ?? "Chưa phân công"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách lịch hẹn theo ngày {Date}", date);
        }

        return list;
    }

    private async Task<DashboardSummaryDto> BuildTenantScopedSummaryAsync(long spaId, DateTime dateParam)
    {
        var result = new DashboardSummaryDto();
        try
        {
            var monthStart = new DateTime(dateParam.Year, dateParam.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var prevMonthStart = monthStart.AddMonths(-1);
            var prevMonthEnd = monthStart.AddDays(-1);

            var activeCustomers = await _context.Customers
                .AsNoTracking()
                .Where(c => c.SpaId == spaId && c.CustomerStatus == "Hoat_dong")
                .ToListAsync();

            int totalCust = activeCustomers.Count;
            int returningCust = activeCustomers.Count(c => c.TotalVisits > 1);

            var todayAppts = await _context.Appointments
                .AsNoTracking()
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(aps => aps.Service)
                .Where(a => a.SpaId == spaId && a.AppointmentDate == dateParam.Date)
                .OrderBy(a => a.StartTime)
                .ToListAsync();

            var monthAppts = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.SpaId == spaId && a.AppointmentDate >= monthStart && a.AppointmentDate <= monthEnd)
                .ToListAsync();

            decimal monthRevenue = monthAppts.Where(a => a.Status == "Completed").Sum(a => a.TotalPrice);
            decimal prevMonthRevenue = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.SpaId == spaId && a.Status == "Completed" && a.AppointmentDate >= prevMonthStart && a.AppointmentDate <= prevMonthEnd)
                .SumAsync(a => (decimal?)a.TotalPrice) ?? 0m;

            result.Kpis.TotalCustomers = totalCust;
            result.Kpis.CustomerGrowth = 0;
            result.Kpis.TodayAppointments = todayAppts.Count;
            result.Kpis.AppointmentGrowth = 0;
            result.Kpis.MonthRevenue = monthRevenue;
            result.Kpis.MonthRevenueFormatted = string.Format(VnCulture, "{0:N0}đ", monthRevenue);
            result.Kpis.RevenueGrowth = prevMonthRevenue > 0 ? Math.Round((double)((monthRevenue - prevMonthRevenue) * 100.0m / prevMonthRevenue), 1) : 0;
            result.Kpis.ReturnRate = totalCust > 0 ? Math.Round(returningCust * 100.0 / totalCust, 1) : 0;
            result.Kpis.ReturnRateGrowth = 0;

            foreach (var grp in monthAppts.GroupBy(a => a.AppointmentDate.Date).OrderBy(g => g.Key))
            {
                result.CalendarAppointments.Add(new CalendarAppointmentDayDto
                {
                    Date = grp.Key.ToString("yyyy-MM-dd"),
                    Day = grp.Key.Day,
                    Count = grp.Count()
                });
            }

            foreach (var a in todayAppts)
            {
                var srvName = string.Join(", ", a.AppointmentServices.Select(s => s.Service?.ServiceName).Where(s => !string.IsNullOrWhiteSpace(s)));
                result.TodayApptDetails.Add(new DashboardAppointmentItemDto
                {
                    AppointmentId = a.AppointmentId,
                    Time = a.StartTime.ToString(@"hh\:mm"),
                    CustomerId = a.CustomerId,
                    CustomerName = a.Customer?.FullName ?? "Khách hàng",
                    CustomerPhone = a.Customer?.Phone ?? string.Empty,
                    ServiceName = string.IsNullOrWhiteSpace(srvName) ? "Dịch vụ Spa" : srvName,
                    Status = a.Status,
                    TotalPrice = a.TotalPrice
                });
            }

            // Recent Activities của riêng Spa
            var recentLogs = await _context.AuditLogs
                .AsNoTracking()
                .Where(l => l.SpaId == spaId)
                .OrderByDescending(l => l.CreatedAt)
                .Take(6)
                .ToListAsync();

            foreach (var log in recentLogs)
            {
                result.RecentActivities.Add(new DashboardActivityDto
                {
                    Type = log.ModuleName,
                    Title = $"{log.ModuleName} • {log.ActionType}",
                    Detail = log.Description,
                    CreatedAt = log.CreatedAt,
                    TimeAgo = FormatTimeAgo(log.CreatedAt),
                    IconClass = "fa-bell",
                    ColorClass = "bg-purple-500/20 text-purple-400 border-purple-500/30"
                });
            }

            // Doanh thu 6 tháng gần nhất của Spa
            for (int i = 5; i >= 0; i--)
            {
                var mStart = new DateTime(dateParam.Year, dateParam.Month, 1).AddMonths(-i);
                var mEnd = mStart.AddMonths(1).AddDays(-1);
                var rev = await _context.Appointments
                    .AsNoTracking()
                    .Where(a => a.SpaId == spaId && a.Status == "Completed" && a.AppointmentDate >= mStart && a.AppointmentDate <= mEnd)
                    .SumAsync(a => (decimal?)a.TotalPrice) ?? 0m;

                result.Charts.Revenue6Months.Labels.Add($"T{mStart.Month}/{mStart.Year % 100}");
                result.Charts.Revenue6Months.RawRevenues.Add(rev);
                result.Charts.Revenue6Months.Data.Add(Math.Round((double)rev / 1_000_000.0, 2));
            }

            // Top Dịch vụ của Spa
            var topServicesRaw = await _context.AppointmentServices
                .AsNoTracking()
                .Include(aps => aps.Appointment)
                .Include(aps => aps.Service)
                .Where(aps => aps.Appointment != null && aps.Appointment.SpaId == spaId && aps.Service != null)
                .ToListAsync();

            int totalSrvBookings = Math.Max(1, topServicesRaw.Count);
            int styleIdx = 0;
            foreach (var grp in topServicesRaw.GroupBy(x => x.Service!.ServiceName).OrderByDescending(g => g.Count()).Take(5))
            {
                var (gradient, textColor) = ServiceStyles[styleIdx % ServiceStyles.Length];
                styleIdx++;
                int count = grp.Count();
                int pct = (int)Math.Round(count * 100.0 / totalSrvBookings);
                var icon = grp.First().Service?.IconClass ?? "fa-spa";

                result.Charts.TopServices.ServiceNames.Add(grp.Key);
                result.Charts.TopServices.Percentages.Add(pct);
                result.Charts.TopServices.Items.Add(new TopServiceItemDto
                {
                    Name = grp.Key,
                    BookingCount = count,
                    TotalRevenue = grp.Sum(x => x.Price),
                    Percentage = pct,
                    Icon = icon,
                    GradientClass = gradient,
                    TextColor = textColor
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tính toán Dashboard theo SpaId={SpaId}", spaId);
        }

        return result;
    }

    private static string FormatTimeAgo(DateTime dt)
    {
        var span = DateTime.Now - dt;
        if (span.TotalSeconds < 60) return "Vừa xong";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} phút trước";
        if (span.TotalHours < 24 && dt.Date == DateTime.Today) return $"{(int)span.TotalHours} giờ trước";
        if (dt.Date == DateTime.Today.AddDays(-1)) return $"Hôm qua {dt:HH:mm}";
        return dt.ToString("dd/MM/yyyy HH:mm");
    }
}
