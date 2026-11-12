using Microsoft.AspNetCore.Http;

namespace CRM_V2.Services;

public class LegacyImportResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int ErrorRows { get; set; }
    public int NewCustomersCount { get; set; }
    public int ExistingCustomersCount { get; set; }
    public int CompletedAppointmentsCount { get; set; }
    public int NoShowAppointmentsCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

public interface ILegacyImportService
{
    byte[] GenerateExcelTemplate();
    byte[] GenerateCsvTemplate();
    Task<LegacyImportResultDto> ImportLegacyFileAsync(IFormFile file);
}
