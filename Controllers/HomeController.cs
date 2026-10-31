using CRM_V2.Services;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM_V2.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IDashboardService dashboardService, ILogger<HomeController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var summary = await _dashboardService.GetSummaryDataAsync();

        var viewModel = new DashboardViewModel
        {
            TotalCustomers = summary.Kpis.TotalCustomers,
            CustomerGrowthPercentage = summary.Kpis.CustomerGrowth,
            TodayAppointments = summary.Kpis.TodayAppointments,
            AppointmentGrowthPercentage = summary.Kpis.AppointmentGrowth,
            MonthlyRevenue = summary.Kpis.MonthRevenue,
            RevenueGrowthPercentage = summary.Kpis.RevenueGrowth,
            RetentionRate = (int)Math.Round(summary.Kpis.ReturnRate),
            RetentionGrowthPercentage = summary.Kpis.ReturnRateGrowth,
            UpcomingAppointments = summary.TodayApptDetails.Select(a => new AppointmentItem
            {
                CustomerName = a.CustomerName,
                ServiceName = a.ServiceName,
                Time = a.Time,
                Status = a.Status,
                StatusBadgeColor = a.Status == "Completed" ? "bg-emerald-500/20 text-emerald-400" :
                                   a.Status == "Cancelled" ? "bg-rose-500/20 text-rose-400" :
                                   a.Status == "InService" ? "bg-cyan-500/20 text-cyan-400" : "bg-amber-500/20 text-amber-400"
            }).ToList(),
            PopularServices = summary.Charts.TopServices.Items.Select(s => new ServicePopularityItem
            {
                Name = s.Name,
                Percentage = s.Percentage,
                Icon = s.Icon,
                GradientClass = s.GradientClass,
                IconColor = s.TextColor
            }).ToList(),
            RecentActivities = summary.RecentActivities.Select(a => new ActivityItem
            {
                Title = a.Title,
                Detail = a.Detail,
                TimeAgo = a.TimeAgo,
                Icon = a.IconClass,
                ColorClass = a.ColorClass
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpPost]
    public IActionResult CreateQuickBooking(string customerName, string phoneNumber, string service, string bookingDate, string bookingTime)
    {
        // Xử lý tạo lịch hẹn nhanh từ modal
        _logger.LogInformation("Tạo lịch hẹn nhanh: {Customer} - {Phone} - {Service}", customerName, phoneNumber, service);
        return Json(new { success = true, message = "Tạo lịch hẹn thành công!" });
    }

    public IActionResult Privacy()
    {
        return View();
    }
}
