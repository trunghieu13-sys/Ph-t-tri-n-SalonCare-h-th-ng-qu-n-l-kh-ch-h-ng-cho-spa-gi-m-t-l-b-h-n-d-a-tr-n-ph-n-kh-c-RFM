using CRM_V2.Models;

namespace CRM_V2.Services;

public class RfmThresholdConfigDto
{
    public int AnalysisWindowDays { get; set; } = 180;
    public decimal VipMinMonetary { get; set; } = 3000000m;
    public int VipMinFrequency { get; set; } = 6;
    public int VipMaxRecency { get; set; } = 30;
    public int LoyalMinFrequency { get; set; } = 3;
    public int LoyalMaxRecency { get; set; } = 45;
    public int AtRiskMinRecency { get; set; } = 60;
    public int NoShowRiskThreshold { get; set; } = 2;
}

public interface IRfmService
{
    Task<RfmThresholdConfigDto> GetThresholdsAsync();
    Task<List<SystemConfig>> GetAllSystemConfigsAsync();
    Task UpdateSystemConfigsAsync(Dictionary<string, string> updatedValues);
    Task<RfmScore?> RecalculateForCustomerAsync(long customerId);
    Task<int> RecalculateAllAsync();
}
