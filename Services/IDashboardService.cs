using System.Globalization;

namespace CRM_V2.Services;

public class DashboardSummaryDto
{
    public DashboardKpiDto Kpis { get; set; } = new();
    public List<CalendarAppointmentDayDto> CalendarAppointments { get; set; } = new();
    public List<DashboardAppointmentItemDto> TodayApptDetails { get; set; } = new();
    public List<DashboardActivityDto> RecentActivities { get; set; } = new();
    public DashboardChartsDto Charts { get; set; } = new();
}

public class DashboardKpiDto
{
    public int TotalCustomers { get; set; }
    public double CustomerGrowth { get; set; }
    public int TodayAppointments { get; set; }
    public double AppointmentGrowth { get; set; }
    public decimal MonthRevenue { get; set; }
    public string MonthRevenueFormatted { get; set; } = "0đ";
    public double RevenueGrowth { get; set; }
    public double ReturnRate { get; set; }
    public double ReturnRateGrowth { get; set; }
}

public class CalendarAppointmentDayDto
{
    public string Date { get; set; } = string.Empty;
    public int Day { get; set; }
    public int Count { get; set; }
    public bool HasAppointments => Count > 0;
}

public class DashboardAppointmentItemDto
{
    public long AppointmentId { get; set; }
    public string Time { get; set; } = string.Empty;
    public string? EndTime { get; set; }
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    public string? StaffName { get; set; }
}

public class DashboardActivityDto
{
    public string Type { get; set; } = string.Empty; // CUSTOMER, APPOINTMENT, NOTIFICATION
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
    public string IconClass { get; set; } = "fa-bell";
    public string ColorClass { get; set; } = "bg-purple-500/20 text-purple-400 border-purple-500/30";
    public DateTime CreatedAt { get; set; }
}

public class DashboardChartsDto
{
    public RevenueChartDto Revenue6Months { get; set; } = new();
    public TopServicesChartDto TopServices { get; set; } = new();
}

public class RevenueChartDto
{
    public List<string> Labels { get; set; } = new();
    public List<double> Data { get; set; } = new(); // In millions VND for clean chart rendering
    public List<decimal> RawRevenues { get; set; } = new();
}

public class TopServicesChartDto
{
    public List<string> ServiceNames { get; set; } = new();
    public List<int> Percentages { get; set; } = new();
    public List<TopServiceItemDto> Items { get; set; } = new();
}

public class TopServiceItemDto
{
    public string Name { get; set; } = string.Empty;
    public int BookingCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public int Percentage { get; set; }
    public string Icon { get; set; } = "fa-spa";
    public string GradientClass { get; set; } = "from-purple-500 to-indigo-500";
    public string TextColor { get; set; } = "text-purple-300";
}

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryDataAsync(DateTime? targetDate = null);
    Task<List<DashboardAppointmentItemDto>> GetAppointmentsByDateAsync(DateTime date);
}
