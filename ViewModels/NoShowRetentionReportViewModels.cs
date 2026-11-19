namespace CRM_V2.ViewModels;

/// <summary>
/// DTO chứa các chỉ số Báo cáo Tỉ lệ bỏ hẹn (No-Show) & Khách quay lại (Retention)
/// của từng nhóm phân khúc khách hàng (VIP, Than_thiet, Moi, Nguy_co) trả về từ
/// Stored Procedure dbo.sp_Report_NoShowAndRetentionBySegment.
/// </summary>
public class SegmentNoShowRetentionDto
{
    public string SegmentCode { get; set; } = string.Empty;
    public string SegmentName { get; set; } = string.Empty;
    public int TotalCustomers { get; set; }
    public int ReturningCustomers { get; set; }
    public decimal RetentionRate { get; set; }
    public int TotalAppointments { get; set; }
    public int CompletedCount { get; set; }
    public int NoShowCount { get; set; }
    public int CancelledCount { get; set; }
    public decimal NoShowRate { get; set; }
    public decimal ActualRevenue { get; set; }
    public string FormattedRevenue { get; set; } = "0đ";

    /// <summary>
    /// Cảnh báo đỏ khi tỉ lệ bỏ hẹn của nhóm >= 15%
    /// </summary>
    public bool IsHighNoShowWarning => NoShowRate >= 15.0m;
}

/// <summary>
/// DTO chứa các chỉ số tổng quan toàn salon và danh sách chi tiết theo từng nhóm khách hàng.
/// </summary>
public class NoShowRetentionReportResponseDto
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public string FromDate { get; set; } = string.Empty;
    public string ToDate { get; set; } = string.Empty;
    public string FormattedDateRange { get; set; } = string.Empty;

    // 4 KPI Tổng quan toàn Salon
    public int TotalCustomers { get; set; }
    public int TotalReturningCustomers { get; set; }
    public decimal AverageRetentionRate { get; set; }

    public int TotalAppointments { get; set; }
    public int TotalCompletedCount { get; set; }
    public int TotalNoShowCount { get; set; }
    public int TotalCancelledCount { get; set; }
    public decimal AverageNoShowRate { get; set; }

    public decimal TotalActualRevenue { get; set; }
    public string FormattedTotalRevenue { get; set; } = "0đ";

    public string HighestNoShowSegmentCode { get; set; } = string.Empty;
    public string HighestNoShowSegmentName { get; set; } = "Chưa có";
    public decimal HighestNoShowSegmentRate { get; set; }
    public int HighestNoShowSegmentCount { get; set; }

    public List<SegmentNoShowRetentionDto> Segments { get; set; } = new();
}
