using System.Globalization;
using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class RfmService : IRfmService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RfmService> _logger;
    private static readonly CultureInfo VnCulture = new("vi-VN");

    public RfmService(ApplicationDbContext context, ILogger<RfmService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<SystemConfig>> GetAllSystemConfigsAsync()
    {
        return await _context.SystemConfigs
            .AsNoTracking()
            .OrderBy(c => c.ConfigKey)
            .ToListAsync();
    }

    public async Task<RfmThresholdConfigDto> GetThresholdsAsync()
    {
        var dict = await _context.SystemConfigs
            .AsNoTracking()
            .ToDictionaryAsync(c => c.ConfigKey, c => c.ConfigValue, StringComparer.OrdinalIgnoreCase);

        return new RfmThresholdConfigDto
        {
            AnalysisWindowDays = ParseInt(dict, "RFM_AnalysisWindowDays", 180),
            VipMinMonetary = ParseDecimal(dict, "RFM_VipMinMonetary", 3000000m),
            VipMinFrequency = ParseInt(dict, "RFM_VipMinFrequency", 6),
            VipMaxRecency = ParseInt(dict, "RFM_VipMaxRecency", 30),
            LoyalMinFrequency = ParseInt(dict, "RFM_LoyalMinFrequency", 3),
            LoyalMaxRecency = ParseInt(dict, "RFM_LoyalMaxRecency", 45),
            AtRiskMinRecency = ParseInt(dict, "RFM_AtRiskMinRecency", 60),
            NoShowRiskThreshold = ParseInt(dict, "RFM_NoShowRiskThreshold", 2)
        };
    }

    public async Task UpdateSystemConfigsAsync(Dictionary<string, string> updatedValues)
    {
        var existing = await _context.SystemConfigs.ToDictionaryAsync(c => c.ConfigKey, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var kv in updatedValues)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                continue;

            if (existing.TryGetValue(kv.Key, out var cfg))
            {
                if (cfg.ConfigValue != kv.Value.Trim())
                {
                    cfg.ConfigValue = kv.Value.Trim();
                    cfg.UpdatedAt = DateTime.Now;
                }
            }
            else
            {
                _context.SystemConfigs.Add(new SystemConfig
                {
                    ConfigKey = kv.Key.Trim(),
                    ConfigValue = kv.Value.Trim(),
                    Description = "Cấu hình ngưỡng hệ thống RFM",
                    UpdatedAt = DateTime.Now
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task<RfmScore?> RecalculateForCustomerAsync(long customerId)
    {
        var customer = await _context.Customers
            .Include(c => c.RfmScore)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        if (customer == null)
            return null;

        var thresholds = await GetThresholdsAsync();
        var windowStartDate = DateTime.Today.AddDays(-thresholds.AnalysisWindowDays);

        var appointments = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId && a.AppointmentDate >= windowStartDate)
            .ToListAsync();

        var score = ComputeCustomerRfm(customer, appointments, thresholds);
        await _context.SaveChangesAsync();

        return score;
    }

    public async Task<int> RecalculateAllAsync()
    {
        var thresholds = await GetThresholdsAsync();
        var windowStartDate = DateTime.Today.AddDays(-thresholds.AnalysisWindowDays);

        var customers = await _context.Customers
            .Include(c => c.RfmScore)
            .ToListAsync();

        var allAppointments = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentDate >= windowStartDate)
            .ToListAsync();

        var apptsByCust = allAppointments
            .GroupBy(a => a.CustomerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var customer in customers)
        {
            apptsByCust.TryGetValue(customer.CustomerId, out var custAppts);
            ComputeCustomerRfm(customer, custAppts ?? new List<Appointment>(), thresholds);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Đã tính toán lại điểm RFM và giải thích phân khúc cho {Count} khách hàng.", customers.Count);

        return customers.Count;
    }

    private RfmScore ComputeCustomerRfm(Customer customer, List<Appointment> windowAppointments, RfmThresholdConfigDto t)
    {
        var completedAppts = windowAppointments
            .Where(a => string.Equals(a.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var noShowAppts = windowAppointments
            .Where(a => string.Equals(a.Status, "NoShow", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Tính R, F, M, NoShow dựa trên lịch hẹn Completed trong cửa sổ phân tích (kết hợp đồng bộ với số liệu tổng hợp trên hồ sơ)
        int fCount = completedAppts.Count > 0 ? completedAppts.Count : customer.TotalVisits;
        decimal mTotal = completedAppts.Count > 0 ? completedAppts.Sum(a => a.TotalPrice) : customer.TotalSpent;
        int noShowCount = Math.Max(noShowAppts.Count, customer.NoShowCount);

        DateTime? lastVisitDate = completedAppts.Count > 0
            ? completedAppts.Max(a => a.AppointmentDate)
            : customer.LastVisitAt;

        int rDays = 999;
        if (lastVisitDate.HasValue)
        {
            rDays = Math.Max(0, (DateTime.Today - lastVisitDate.Value.Date).Days);
        }

        // Thứ tự ưu tiên xét nhóm (Evaluation Priority - tránh chồng chéo điều kiện):
        // 1. VIP: M >= VipMinMonetary HOẶC (F >= VipMinFrequency VÀ R <= VipMaxRecency)
        // 2. AtRisk: Không phải VIP, F >= 2 VÀ R > AtRiskMinRecency
        // 3. Loyal: F >= LoyalMinFrequency VÀ R <= LoyalMaxRecency
        // 4. New: Các trường hợp còn lại
        string segment;
        string reason;

        string mFormatted = mTotal.ToString("N0", VnCulture) + "đ";
        string vipMFormatted = t.VipMinMonetary.ToString("N0", VnCulture) + "đ";
        string rDisplay = rDays == 999 ? "chưa có lượt hoàn thành" : $"{rDays} ngày trước";

        if (mTotal >= t.VipMinMonetary)
        {
            segment = "VIP";
            reason = $"Thuộc nhóm VIP vì tổng chi tiêu đạt {mFormatted} (đạt ngưỡng VIP >= {vipMFormatted}), tần suất {fCount} lần đến, lần gần nhất: {rDisplay}.";
        }
        else if (fCount >= t.VipMinFrequency && rDays <= t.VipMaxRecency)
        {
            segment = "VIP";
            reason = $"Thuộc nhóm VIP vì có tần suất đến {fCount} lần (đạt ngưỡng >= {t.VipMinFrequency} lần) và lần gần nhất cách đây {rDays} ngày (trong ngưỡng <= {t.VipMaxRecency} ngày), tổng chi tiêu {mFormatted}.";
        }
        else if (fCount >= 2 && rDays > t.AtRiskMinRecency)
        {
            segment = "AtRisk";
            reason = $"Thuộc nhóm Nguy cơ rời bỏ (AtRisk) vì đã {rDays} ngày chưa quay lại (vượt ngưỡng cảnh báo > {t.AtRiskMinRecency} ngày) dù từng sử dụng dịch vụ {fCount} lần (chi tiêu {mFormatted}).";
        }
        else if (fCount >= t.LoyalMinFrequency && rDays <= t.LoyalMaxRecency)
        {
            segment = "Loyal";
            reason = $"Thuộc nhóm Thân thiết (Loyal) vì có {fCount} lần sử dụng dịch vụ (đạt ngưỡng >= {t.LoyalMinFrequency} lần) và mới ghé cách đây {rDays} ngày (<= ngưỡng {t.LoyalMaxRecency} ngày), chi tiêu {mFormatted}.";
        }
        else
        {
            segment = "New";
            reason = $"Thuộc nhóm Khách mới / Vãng lai (New) vì tần suất hiện tại là {fCount} lần (chưa đạt ngưỡng Thân thiết >= {t.LoyalMinFrequency} lần) và chi tiêu {mFormatted} (chưa đạt ngưỡng VIP >= {vipMFormatted}).";
        }

        bool isNoShowRisk = noShowCount >= t.NoShowRiskThreshold;
        if (isNoShowRisk)
        {
            reason += $" [Cờ cảnh báo bỏ hẹn: Đã bỏ hẹn {noShowCount} lần >= ngưỡng {t.NoShowRiskThreshold} lần -> Tự động bật nhắc lịch 2 bước 24h & 2h].";
        }

        // Cập nhật đồng bộ sang bảng Customer
        customer.TotalVisits = fCount;
        customer.TotalSpent = mTotal;
        customer.NoShowCount = noShowCount;
        if (lastVisitDate.HasValue)
            customer.LastVisitAt = lastVisitDate.Value;

        customer.CustomerType = segment switch
        {
            "VIP" => "VIP",
            "Loyal" => "Than_thiet",
            "AtRisk" => "Nguy_co",
            _ => "Moi"
        };

        var rfm = customer.RfmScore;
        if (rfm == null)
        {
            rfm = new RfmScore
            {
                CustomerId = customer.CustomerId
            };
            _context.RfmScores.Add(rfm);
            customer.RfmScore = rfm;
        }

        rfm.RecencyDays = rDays;
        rfm.FrequencyCount = fCount;
        rfm.MonetaryTotal = mTotal;
        rfm.NoShowCount = noShowCount;
        rfm.SegmentGroup = segment;
        rfm.IsNoShowRisk = isNoShowRisk;
        rfm.ReasonText = reason;
        rfm.LastCalculatedAt = DateTime.Now;

        return rfm;
    }

    private static int ParseInt(Dictionary<string, string> dict, string key, int defaultValue)
    {
        if (dict.TryGetValue(key, out var raw) && int.TryParse(raw, out var val))
            return val;
        return defaultValue;
    }

    private static decimal ParseDecimal(Dictionary<string, string> dict, string key, decimal defaultValue)
    {
        if (dict.TryGetValue(key, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return val;
        return defaultValue;
    }
}
