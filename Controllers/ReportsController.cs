using System.Data;
using System.Data.Common;
using System.Globalization;
using ClosedXML.Excel;
using CRM_V2.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

public class ReportsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReportsController> _logger;
    private static readonly CultureInfo VnCulture = new("vi-VN");

    public ReportsController(ApplicationDbContext context, ILogger<ReportsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// GET /Reports/GetOverviewData
    /// Trả về toàn bộ dữ liệu KPI, biểu đồ Chart.js (Line + Doughnut), bảng Top KTV, thống kê lịch hẹn
    /// theo khoảng thời gian được chọn (today, this_week, this_month, this_quarter, this_year, custom).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOverviewData(string period = "this_month", string? fromDate = null, string? toDate = null)
    {
        try
        {
            var (start, end, periodDisplay) = ResolveDateRange(period, fromDate, toDate);

            var reportData = await FetchFullReportDataAsync(start, end);

            return Json(new
            {
                success = true,
                period,
                periodDisplay,
                fromDate = start.ToString("yyyy-MM-dd"),
                toDate = end.ToString("yyyy-MM-dd"),
                formattedDateRange = $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}",
                kpis = reportData.Kpis,
                trend = reportData.Trend,
                services = reportData.Services,
                staff = reportData.Staff,
                bookingStats = reportData.BookingStats,
                customerTiers = reportData.CustomerTiers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy dữ liệu báo cáo tổng quan. Period: {Period}", period);
            return Json(new { success = false, message = "Đã xảy ra lỗi khi tổng hợp số liệu báo cáo: " + ex.Message });
        }
    }

    /// <summary>
    /// GET /Reports/ExportExcel
    /// Xuất báo cáo đa chiều ra file Microsoft Excel (.xlsx) với 4 Sheet hoàn chỉnh bằng ClosedXML
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportExcel(string period = "this_month", string? fromDate = null, string? toDate = null)
    {
        try
        {
            var (start, end, periodDisplay) = ResolveDateRange(period, fromDate, toDate);
            var reportData = await FetchFullReportDataAsync(start, end);

            using var workbook = new XLWorkbook();

            // 1. Sheet Tổng Quan Doanh Thu
            BuildOverviewSheet(workbook.Worksheets.Add("Tổng Quan Doanh Thu"), reportData, periodDisplay, start, end);

            // 2. Sheet Cơ Cấu Dịch Vụ
            BuildServicesSheet(workbook.Worksheets.Add("Cơ Cấu Dịch Vụ"), reportData);

            // 3. Sheet Hiệu Suất Nhân Viên
            BuildStaffSheet(workbook.Worksheets.Add("Hiệu Suất Nhân Viên"), reportData);

            // 4. Sheet Khách Hàng & Lịch Hẹn
            BuildBookingsSheet(workbook.Worksheets.Add("Khách Hàng & Đặt Lịch"), reportData);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();
            var fileName = $"BaoCao_SalonCare_{period}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xuất báo cáo Excel");
            return BadRequest("Lỗi khi xuất file Excel: " + ex.Message);
        }
    }

    #region Helper & Data Fetching

    private static (DateTime Start, DateTime End, string Display) ResolveDateRange(string period, string? fromDate, string? toDate)
    {
        var today = DateTime.Today;
        DateTime start;
        DateTime end;
        string display;

        switch (period?.ToLower())
        {
            case "today":
                start = today;
                end = today;
                display = "Hôm nay (" + today.ToString("dd/MM/yyyy") + ")";
                break;

            case "this_week":
                int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
                start = today.AddDays(-diff);
                end = start.AddDays(6);
                display = "Tuần này (" + start.ToString("dd/MM") + " - " + end.ToString("dd/MM/yyyy") + ")";
                break;

            case "this_quarter":
                int quarter = (today.Month - 1) / 3 + 1;
                start = new DateTime(today.Year, (quarter - 1) * 3 + 1, 1);
                end = start.AddMonths(3).AddDays(-1);
                display = $"Quý {quarter}/{today.Year}";
                break;

            case "this_year":
                start = new DateTime(today.Year, 1, 1);
                end = new DateTime(today.Year, 12, 31);
                display = $"Năm {today.Year}";
                break;

            case "custom":
                if (DateTime.TryParse(fromDate, out var parsedFrom) && DateTime.TryParse(toDate, out var parsedTo))
                {
                    start = parsedFrom <= parsedTo ? parsedFrom : parsedTo;
                    end = parsedFrom <= parsedTo ? parsedTo : parsedFrom;
                    display = $"Tùy chọn ({start:dd/MM/yyyy} - {end:dd/MM/yyyy})";
                }
                else
                {
                    start = new DateTime(today.Year, today.Month, 1);
                    end = start.AddMonths(1).AddDays(-1);
                    display = $"Tháng {today.Month}/{today.Year}";
                }
                break;

            case "this_month":
            default:
                start = new DateTime(today.Year, today.Month, 1);
                end = start.AddMonths(1).AddDays(-1);
                display = $"Tháng {today.Month}/{today.Year}";
                break;
        }

        return (start, end, display);
    }

    private async Task<ReportAggregateData> FetchFullReportDataAsync(DateTime start, DateTime end)
    {
        var data = new ReportAggregateData();
        var conn = _context.Database.GetDbConnection();

        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        // 1. Revenue & Trend Report
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "sp_GetRevenueReport";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParam(cmd, "@FromDate", start.Date);
            AddParam(cmd, "@ToDate", end.Date);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var totalRev = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0);
                var prevRev = reader.IsDBNull(1) ? 0m : reader.GetDecimal(1);
                var growth = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
                var visits = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                var aov = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4);
                var deposits = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);
                var directRev = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);
                var onlineRev = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7);

                data.Kpis.TotalRevenue = totalRev;
                data.Kpis.FormattedRevenue = totalRev.ToString("N0", VnCulture) + "đ";
                data.Kpis.PreviousRevenue = prevRev;
                data.Kpis.FormattedPreviousRevenue = prevRev.ToString("N0", VnCulture) + "đ";
                data.Kpis.GrowthRatePercentage = growth;
                data.Kpis.IsGrowthPositive = growth >= 0;
                data.Kpis.TotalCompletedVisits = visits;
                data.Kpis.AOV = aov;
                data.Kpis.FormattedAov = aov.ToString("N0", VnCulture) + "đ";
                data.Kpis.TotalDeposits = deposits;
                data.Kpis.FormattedDeposits = deposits.ToString("N0", VnCulture) + "đ";
                data.Kpis.DirectRevenue = directRev;
                data.Kpis.OnlineRevenue = onlineRev;
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    data.Trend.Labels.Add(reader.GetString(1)); // ShortDateLabel
                    data.Trend.FullLabels.Add(reader.GetString(0)); // DateLabel
                    data.Trend.Revenues.Add(reader.IsDBNull(2) ? 0m : reader.GetDecimal(2));
                    data.Trend.Visits.Add(reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
                }
            }
        }

        // 2. Top Services Report
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "sp_GetTopServicesReport";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParam(cmd, "@FromDate", start.Date);
            AddParam(cmd, "@ToDate", end.Date);
            AddParam(cmd, "@TopN", 6);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sId = reader.GetInt32(0);
                var sName = reader.GetString(1);
                var catName = reader.IsDBNull(2) ? "Dịch vụ chung" : reader.GetString(2);
                var count = reader.GetInt32(3);
                var rev = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4);
                var pct = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);

                data.Services.Add(new ServiceReportItem
                {
                    ServiceId = sId,
                    ServiceName = sName,
                    CategoryName = catName,
                    OrderCount = count,
                    TotalRevenue = rev,
                    FormattedRevenue = rev.ToString("N0", VnCulture) + "đ",
                    Percentage = decimal.Round(pct, 1)
                });
            }
        }

        // 3. Customer Growth & Booking Stats Report
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "sp_GetCustomerGrowthReport";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParam(cmd, "@FromDate", start.Date);
            AddParam(cmd, "@ToDate", end.Date);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                data.Kpis.NewCustomers = reader.GetInt32(0);
                data.Kpis.ReturningCustomers = reader.GetInt32(1);
                data.Kpis.TotalActiveCustomers = reader.GetInt32(2);
                data.Kpis.RetentionRatePercentage = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);

                data.BookingStats.TotalBooked = reader.GetInt32(4);
                data.BookingStats.CompletedCount = reader.GetInt32(5);
                data.BookingStats.CancelledCount = reader.GetInt32(6);
                data.BookingStats.NoShowCount = reader.GetInt32(7);
                data.BookingStats.CancellationRatePercentage = reader.IsDBNull(8) ? 0m : reader.GetDecimal(8);
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    data.CustomerTiers.Add(new CustomerTierItem
                    {
                        TierName = reader.GetString(0),
                        CustomerCount = reader.GetInt32(1),
                        TotalSpent = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2)
                    });
                }
            }
        }

        // 4. Staff Performance Report
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "sp_GetStaffPerformanceReport";
            cmd.CommandType = CommandType.StoredProcedure;
            AddParam(cmd, "@FromDate", start.Date);
            AddParam(cmd, "@ToDate", end.Date);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var uId = reader.GetInt64(0);
                var fName = reader.GetString(1);
                var avatar = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var rName = reader.IsDBNull(3) ? "KTV" : reader.GetString(3);
                var sCount = reader.GetInt32(4);
                var sRev = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);
                var rating = reader.IsDBNull(6) ? 5.0m : reader.GetDecimal(6);

                data.Staff.Add(new StaffPerformanceItem
                {
                    UserId = uId,
                    FullName = fName,
                    AvatarUrl = avatar,
                    RoleName = rName,
                    AppointmentsServed = sCount,
                    TotalRevenueGenerated = sRev,
                    FormattedRevenue = sRev.ToString("N0", VnCulture) + "đ",
                    Rating = rating
                });
            }
        }

        return data;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    #endregion

    #region Excel Sheets Styling & Builders

    private static void BuildOverviewSheet(IXLWorksheet ws, ReportAggregateData data, string periodDisplay, DateTime start, DateTime end)
    {
        // Title
        ws.Cell("A1").Value = "BÁO CÁO TỔNG QUAN DOANH THU & KINH DOANH - SALONCARE CRM";
        ws.Range("A1:G1").Merge().Style
            .Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#4F46E5"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(1).Height = 36;

        // Subtitle
        ws.Cell("A2").Value = $"Kỳ báo cáo: {periodDisplay}  |  Từ ngày: {start:dd/MM/yyyy} - Đến ngày: {end:dd/MM/yyyy}  |  Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Range("A2:G2").Merge().Style
            .Font.SetItalic().Font.SetFontSize(10).Font.SetFontColor(XLColor.FromHtml("#4B5563"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Row(2).Height = 22;

        // KPI Summary Box
        ws.Cell("A4").Value = "CHỈ SỐ TÀI CHÍNH CHÍNH (KPIs)";
        ws.Range("A4:D4").Merge().Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(XLColor.FromHtml("#1E293B"));

        var kpiHeaders = new[] { "Chỉ số", "Giá trị hiện tại", "Kỳ trước / So sánh", "Ghi chú" };
        for (int i = 0; i < kpiHeaders.Length; i++)
        {
            var cell = ws.Cell(5, i + 1);
            cell.Value = kpiHeaders[i];
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#6366F1"));
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        var kpiRows = new List<(string Name, object Value, string Comp, string Format)>
        {
            ("Tổng doanh thu thực tế", (double)data.Kpis.TotalRevenue, $"Tăng trưởng: {data.Kpis.GrowthRatePercentage:N1}%", "#,##0 \"đ\""),
            ("Số lượt phục vụ hoàn tất", data.Kpis.TotalCompletedVisits, "-", "#,##0 \"ca\""),
            ("Giá trị đơn trung bình (AOV)", (double)data.Kpis.AOV, "-", "#,##0 \"đ\""),
            ("Tổng tiền đặt cọc", (double)data.Kpis.TotalDeposits, "-", "#,##0 \"đ\""),
            ("Doanh thu tại quầy (Direct)", (double)data.Kpis.DirectRevenue, "-", "#,##0 \"đ\""),
            ("Doanh thu Online (Web/App/MXH)", (double)data.Kpis.OnlineRevenue, "-", "#,##0 \"đ\""),
            ("Tỷ lệ giữ chân khách hàng", (double)(data.Kpis.RetentionRatePercentage / 100m), $"Khách quay lại: {data.Kpis.ReturningCustomers}/{data.Kpis.TotalActiveCustomers}", "0.0%")
        };

        int r = 6;
        foreach (var item in kpiRows)
        {
            ws.Cell(r, 1).Value = item.Name;
            var valCell = ws.Cell(r, 2);
            if (item.Value is double d)
            {
                valCell.Value = d;
                valCell.Style.NumberFormat.Format = item.Format;
            }
            else if (item.Value is int n)
            {
                valCell.Value = n;
                valCell.Style.NumberFormat.Format = item.Format;
            }
            ws.Cell(r, 3).Value = item.Comp;
            ws.Cell(r, 4).Value = "Dữ liệu CRM_V2";

            ws.Range(r, 1, r, 4).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#CBD5E1"));
            r++;
        }

        // Daily Trend Table
        int trendStartRow = r + 2;
        ws.Cell(trendStartRow, 1).Value = "XU HƯỚNG DOANH THU THEO THỜI GIAN";
        ws.Range(trendStartRow, 1, trendStartRow, 3).Merge().Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(XLColor.FromHtml("#1E293B"));

        int headerRow = trendStartRow + 1;
        ws.Cell(headerRow, 1).Value = "Ngày";
        ws.Cell(headerRow, 2).Value = "Doanh thu (VNĐ)";
        ws.Cell(headerRow, 3).Value = "Số ca phục vụ";
        ws.Range(headerRow, 1, headerRow, 3).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#4F46E5"));

        int tr = headerRow + 1;
        for (int i = 0; i < data.Trend.FullLabels.Count; i++)
        {
            ws.Cell(tr, 1).Value = data.Trend.FullLabels[i];
            var revCell = ws.Cell(tr, 2);
            revCell.Value = (double)data.Trend.Revenues[i];
            revCell.Style.NumberFormat.Format = "#,##0 \"đ\"";

            var visitCell = ws.Cell(tr, 3);
            visitCell.Value = data.Trend.Visits[i];
            visitCell.Style.NumberFormat.Format = "#,##0";
            visitCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            if (tr % 2 == 0)
            {
                ws.Range(tr, 1, tr, 3).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F8FAFC"));
            }
            ws.Range(tr, 1, tr, 3).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#E2E8F0"));
            tr++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildServicesSheet(IXLWorksheet ws, ReportAggregateData data)
    {
        ws.Cell("A1").Value = "BÁO CÁO CƠ CẤU DOANH THU & DỊCH VỤ BÁN CHẠY";
        ws.Range("A1:F1").Merge().Style
            .Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#7C3AED"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Row(1).Height = 32;

        var headers = new[] { "Hạng", "Mã DV", "Tên dịch vụ", "Danh mục", "Số lượt hoàn thành", "Tổng doanh thu (VNĐ)", "Tỷ trọng (%)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(3, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#8B5CF6"));
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        int r = 4;
        int rank = 1;
        foreach (var s in data.Services)
        {
            ws.Cell(r, 1).Value = rank++;
            ws.Cell(r, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 2).Value = s.ServiceId;
            ws.Cell(r, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 3).Value = s.ServiceName;
            ws.Cell(r, 4).Value = s.CategoryName;

            var cntCell = ws.Cell(r, 5);
            cntCell.Value = s.OrderCount;
            cntCell.Style.NumberFormat.Format = "#,##0";
            cntCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            var revCell = ws.Cell(r, 6);
            revCell.Value = (double)s.TotalRevenue;
            revCell.Style.NumberFormat.Format = "#,##0 \"đ\"";

            var pctCell = ws.Cell(r, 7);
            pctCell.Value = (double)(s.Percentage / 100m);
            pctCell.Style.NumberFormat.Format = "0.0%";
            pctCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

            if (r % 2 == 0)
            {
                ws.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F5F3FF"));
            }
            ws.Range(r, 1, r, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#DDD6FE"));
            r++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildStaffSheet(IXLWorksheet ws, ReportAggregateData data)
    {
        ws.Cell("A1").Value = "BẢNG XẾP HẠNG & HIỆU SUẤT KỸ THUẬT VIÊN / NHÂN VIÊN";
        ws.Range("A1:F1").Merge().Style
            .Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#0D9488"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Row(1).Height = 32;

        var headers = new[] { "Xếp hạng", "Mã NV", "Họ và tên", "Chức vụ / Role", "Số ca phục vụ", "Doanh thu mang lại (VNĐ)", "Đánh giá ⭐" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(3, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#14B8A6"));
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        int r = 4;
        int rank = 1;
        foreach (var staff in data.Staff)
        {
            ws.Cell(r, 1).Value = rank++;
            ws.Cell(r, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 2).Value = staff.UserId;
            ws.Cell(r, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(r, 3).Value = staff.FullName;
            ws.Cell(r, 4).Value = staff.RoleName.ToUpper();
            ws.Cell(r, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            var sCell = ws.Cell(r, 5);
            sCell.Value = staff.AppointmentsServed;
            sCell.Style.NumberFormat.Format = "#,##0";
            sCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            var rCell = ws.Cell(r, 6);
            rCell.Value = (double)staff.TotalRevenueGenerated;
            rCell.Style.NumberFormat.Format = "#,##0 \"đ\"";

            var starCell = ws.Cell(r, 7);
            starCell.Value = (double)staff.Rating;
            starCell.Style.NumberFormat.Format = "0.0";
            starCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            if (r % 2 == 0)
            {
                ws.Range(r, 1, r, 7).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F0FDFA"));
            }
            ws.Range(r, 1, r, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#CCFBF1"));
            r++;
        }

        ws.Columns().AdjustToContents();
    }

    private static void BuildBookingsSheet(IXLWorksheet ws, ReportAggregateData data)
    {
        ws.Cell("A1").Value = "THỐNG KÊ LỊCH HẸN & PHÂN KHÚC KHÁCH HÀNG";
        ws.Range("A1:E1").Merge().Style
            .Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#E11D48"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Row(1).Height = 32;

        // Bảng 1: Trạng thái lịch hẹn
        ws.Cell("A3").Value = "1. TỶ LỆ CHUYỂN ĐỔI & TRẠNG THÁI LỊCH HẸN";
        ws.Range("A3:C3").Merge().Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(XLColor.FromHtml("#1E293B"));

        var bHeaders = new[] { "Trạng thái / Chỉ số", "Số lượng", "Tỷ lệ (%)" };
        for (int i = 0; i < bHeaders.Length; i++)
        {
            var cell = ws.Cell(4, i + 1);
            cell.Value = bHeaders[i];
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#F43F5E"));
        }

        int total = Math.Max(1, data.BookingStats.TotalBooked);
        var bookingRows = new List<(string Label, int Count, double Pct)>
        {
            ("Tổng lịch hẹn ghi nhận", data.BookingStats.TotalBooked, 100.0),
            ("Đã hoàn thành (Completed)", data.BookingStats.CompletedCount, (double)data.BookingStats.CompletedCount * 100.0 / total),
            ("Đã hủy (Cancelled)", data.BookingStats.CancelledCount, (double)data.BookingStats.CancelledCount * 100.0 / total),
            ("Vắng mặt (No-Show)", data.BookingStats.NoShowCount, (double)data.BookingStats.NoShowCount * 100.0 / total)
        };

        int br = 5;
        foreach (var b in bookingRows)
        {
            ws.Cell(br, 1).Value = b.Label;
            var cCell = ws.Cell(br, 2);
            cCell.Value = b.Count;
            cCell.Style.NumberFormat.Format = "#,##0";

            var pCell = ws.Cell(br, 3);
            pCell.Value = b.Pct / 100.0;
            pCell.Style.NumberFormat.Format = "0.0%";

            ws.Range(br, 1, br, 3).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#FECDD3"));
            br++;
        }

        // Bảng 2: Phân khúc khách hàng
        int cr = br + 2;
        ws.Cell(cr, 1).Value = "2. PHÂN HẠNG THÀNH VIÊN KHÁCH HÀNG (TIERS)";
        ws.Range(cr, 1, cr, 3).Merge().Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(XLColor.FromHtml("#1E293B"));

        int tierHeaderRow = cr + 1;
        ws.Cell(tierHeaderRow, 1).Value = "Hạng thành viên";
        ws.Cell(tierHeaderRow, 2).Value = "Số lượng khách";
        ws.Cell(tierHeaderRow, 3).Value = "Tổng tích lũy chi tiêu";
        ws.Range(tierHeaderRow, 1, tierHeaderRow, 3).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#E11D48"));

        int tr = tierHeaderRow + 1;
        foreach (var t in data.CustomerTiers)
        {
            ws.Cell(tr, 1).Value = t.TierName;
            var cntCell = ws.Cell(tr, 2);
            cntCell.Value = t.CustomerCount;
            cntCell.Style.NumberFormat.Format = "#,##0";

            var spentCell = ws.Cell(tr, 3);
            spentCell.Value = (double)t.TotalSpent;
            spentCell.Style.NumberFormat.Format = "#,##0 \"đ\"";

            ws.Range(tr, 1, tr, 3).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#FECDD3"));
            tr++;
        }

        ws.Columns().AdjustToContents();
    }

    #endregion
}

#region Aggregation DTO Models

public class ReportAggregateData
{
    public ReportKpis Kpis { get; set; } = new();
    public ReportTrend Trend { get; set; } = new();
    public List<ServiceReportItem> Services { get; set; } = new();
    public List<StaffPerformanceItem> Staff { get; set; } = new();
    public BookingStatsItem BookingStats { get; set; } = new();
    public List<CustomerTierItem> CustomerTiers { get; set; } = new();
}

public class ReportKpis
{
    public decimal TotalRevenue { get; set; }
    public string FormattedRevenue { get; set; } = "0đ";
    public decimal PreviousRevenue { get; set; }
    public string FormattedPreviousRevenue { get; set; } = "0đ";
    public decimal GrowthRatePercentage { get; set; }
    public bool IsGrowthPositive { get; set; } = true;
    public int TotalCompletedVisits { get; set; }
    public decimal AOV { get; set; }
    public string FormattedAov { get; set; } = "0đ";
    public decimal TotalDeposits { get; set; }
    public string FormattedDeposits { get; set; } = "0đ";
    public decimal DirectRevenue { get; set; }
    public decimal OnlineRevenue { get; set; }
    public int NewCustomers { get; set; }
    public int ReturningCustomers { get; set; }
    public int TotalActiveCustomers { get; set; }
    public decimal RetentionRatePercentage { get; set; }
}

public class ReportTrend
{
    public List<string> Labels { get; set; } = new();
    public List<string> FullLabels { get; set; } = new();
    public List<decimal> Revenues { get; set; } = new();
    public List<int> Visits { get; set; } = new();
}

public class ServiceReportItem
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public string FormattedRevenue { get; set; } = "0đ";
    public decimal Percentage { get; set; }
}

public class StaffPerformanceItem
{
    public long UserId { get; set; }
    public string FullName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string RoleName { get; set; } = "";
    public int AppointmentsServed { get; set; }
    public decimal TotalRevenueGenerated { get; set; }
    public string FormattedRevenue { get; set; } = "0đ";
    public decimal Rating { get; set; }
}

public class BookingStatsItem
{
    public int TotalBooked { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public int NoShowCount { get; set; }
    public decimal CancellationRatePercentage { get; set; }
}

public class CustomerTierItem
{
    public string TierName { get; set; } = "";
    public int CustomerCount { get; set; }
    public decimal TotalSpent { get; set; }
}

#endregion
