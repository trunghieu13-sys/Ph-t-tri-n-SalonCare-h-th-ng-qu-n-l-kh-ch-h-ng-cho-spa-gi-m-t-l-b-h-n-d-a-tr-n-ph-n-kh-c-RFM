using System.Data;
using System.Data.Common;
using System.Globalization;
using CRM_V2.Data;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;
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

    public DashboardService(ApplicationDbContext context, ILogger<DashboardService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DashboardSummaryDto> GetSummaryDataAsync(DateTime? targetDate = null)
    {
        var result = new DashboardSummaryDto();
        var dateParam = targetDate ?? DateTime.Today;

        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.sp_GetDashboardSummary";
            cmd.CommandType = CommandType.StoredProcedure;

            var pDate = cmd.CreateParameter();
            pDate.ParameterName = "@TargetDate";
            pDate.DbType = DbType.Date;
            pDate.Value = dateParam.Date;
            cmd.Parameters.Add(pDate);

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

        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.sp_GetAppointmentsByDate";
            cmd.CommandType = CommandType.StoredProcedure;

            var pDate = cmd.CreateParameter();
            pDate.ParameterName = "@SelectedDate";
            pDate.DbType = DbType.Date;
            pDate.Value = date.Date;
            cmd.Parameters.Add(pDate);

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
