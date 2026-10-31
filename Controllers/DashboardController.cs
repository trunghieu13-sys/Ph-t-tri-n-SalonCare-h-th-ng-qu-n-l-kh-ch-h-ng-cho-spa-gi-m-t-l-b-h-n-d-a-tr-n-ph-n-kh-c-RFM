using System.Globalization;
using CRM_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM_V2.Controllers;

public class DashboardController : Controller
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IDashboardService dashboardService, ILogger<DashboardController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    /// <summary>
    /// GET /Dashboard/GetSummaryData
    /// Trả về toàn bộ dữ liệu KPI, Lịch làm việc tháng/ngày, Nhật ký hoạt động thời gian thực và Biểu đồ
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSummaryData(string? date = null)
    {
        try
        {
            DateTime targetDate = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsedDate))
            {
                targetDate = parsedDate;
            }

            var summary = await _dashboardService.GetSummaryDataAsync(targetDate);

            return Json(new
            {
                success = true,
                targetDate = targetDate.ToString("yyyy-MM-dd"),
                targetDateFormatted = targetDate.ToString("dd/MM/yyyy"),
                kpis = new
                {
                    totalCustomers = summary.Kpis.TotalCustomers,
                    customerGrowth = summary.Kpis.CustomerGrowth,
                    todayAppointments = summary.Kpis.TodayAppointments,
                    appointmentGrowth = summary.Kpis.AppointmentGrowth,
                    monthRevenue = summary.Kpis.MonthRevenue,
                    monthRevenueFormatted = summary.Kpis.MonthRevenueFormatted,
                    revenueGrowth = summary.Kpis.RevenueGrowth,
                    returnRate = summary.Kpis.ReturnRate,
                    returnRateGrowth = summary.Kpis.ReturnRateGrowth
                },
                calendarAppointments = summary.CalendarAppointments,
                todayApptDetails = summary.TodayApptDetails,
                recentActivities = summary.RecentActivities.Select(a => new
                {
                    type = a.Type,
                    title = a.Title,
                    detail = a.Detail,
                    timeAgo = a.TimeAgo,
                    iconClass = a.IconClass,
                    colorClass = a.ColorClass
                }),
                charts = new
                {
                    revenue6Months = new
                    {
                        labels = summary.Charts.Revenue6Months.Labels,
                        data = summary.Charts.Revenue6Months.Data,
                        rawRevenues = summary.Charts.Revenue6Months.RawRevenues
                    },
                    topServices = new
                    {
                        serviceNames = summary.Charts.TopServices.ServiceNames,
                        percentages = summary.Charts.TopServices.Percentages,
                        items = summary.Charts.TopServices.Items
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy dữ liệu tổng quan Dashboard");
            return StatusCode(500, new { success = false, message = "Lỗi khi tải dữ liệu tổng quan: " + ex.Message });
        }
    }

    /// <summary>
    /// GET /Dashboard/GetDayAppointments?date=2026-10-22
    /// Lấy chi tiết danh sách lịch hẹn khi nhấp vào ngày bất kỳ trên Calendar Widget
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDayAppointments(string date)
    {
        try
        {
            if (!DateTime.TryParse(date, out var targetDate))
            {
                return BadRequest(new { success = false, message = "Ngày không đúng định dạng (yyyy-MM-dd)" });
            }

            var appts = await _dashboardService.GetAppointmentsByDateAsync(targetDate);

            return Json(new
            {
                success = true,
                date = targetDate.ToString("yyyy-MM-dd"),
                dateFormatted = targetDate.ToString("dd/MM/yyyy"),
                total = appts.Count,
                data = appts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy lịch hẹn theo ngày {Date}", date);
            return StatusCode(500, new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }
}
