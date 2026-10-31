namespace CRM_V2.ViewModels;

public class DashboardViewModel
{
    public int TotalCustomers { get; set; } = 1248;
    public double CustomerGrowthPercentage { get; set; } = 12.0;

    public int TodayAppointments { get; set; } = 36;
    public double AppointmentGrowthPercentage { get; set; } = 8.0;

    public decimal MonthlyRevenue { get; set; } = 128500000m;
    public double RevenueGrowthPercentage { get; set; } = 15.0;

    public int RetentionRate { get; set; } = 68;
    public double RetentionGrowthPercentage { get; set; } = 6.0;

    public List<LoyalCustomerItem> LoyalCustomers { get; set; } = new();
    public List<AppointmentItem> UpcomingAppointments { get; set; } = new();
    public List<ServicePopularityItem> PopularServices { get; set; } = new();
    public List<ActivityItem> RecentActivities { get; set; } = new();
}

public class LoyalCustomerItem
{
    public string Initials { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string LastVisitDate { get; set; } = string.Empty;
    public string TotalSpent { get; set; } = string.Empty;
    public string Status { get; set; } = "Thân thiết";
    public string AvatarBgColor { get; set; } = "bg-purple-500/20 text-purple-300";
}

public class AppointmentItem
{
    public string Initials { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Status { get; set; } = "Đã xác nhận";
    public string StatusBadgeColor { get; set; } = "bg-emerald-500/20 text-emerald-400";
    public string AvatarBgColor { get; set; } = "bg-purple-500/20 text-purple-300";
}

public class ServicePopularityItem
{
    public string Name { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string Icon { get; set; } = "fa-spa";
    public string IconColor { get; set; } = "text-purple-400";
    public string GradientClass { get; set; } = "from-purple-500 to-indigo-500";
}

public class ActivityItem
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string TimeAgo { get; set; } = string.Empty;
    public string Icon { get; set; } = "fa-bell";
    public string ColorClass { get; set; } = "bg-purple-500/20 text-purple-400 border-purple-500/40";
}
