using CRM_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM_V2.Controllers;

[Authorize]
[Route("Import")]
public class ImportController : Controller
{
    private readonly ILegacyImportService _legacyImportService;

    public ImportController(ILegacyImportService legacyImportService)
    {
        _legacyImportService = legacyImportService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return Redirect("/#customersView");
    }

    [HttpGet("DownloadExcelTemplate")]
    public IActionResult DownloadExcelTemplate()
    {
        var bytes = _legacyImportService.GenerateExcelTemplate();
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "SalonCare_Legacy_Import_Template.xlsx");
    }

    [HttpGet("DownloadCsvTemplate")]
    public IActionResult DownloadCsvTemplate()
    {
        var bytes = _legacyImportService.GenerateCsvTemplate();
        return File(bytes, "text/csv; charset=utf-8", "SalonCare_Legacy_Import_Template.csv");
    }

    [HttpPost("UploadLegacyFile")]
    public async Task<IActionResult> UploadLegacyFile(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, message = "Vui lòng chọn file .xlsx hoặc .csv hợp lệ." });
        }

        var res = await _legacyImportService.ImportLegacyFileAsync(file);
        if (!res.Success)
        {
            return BadRequest(new { success = false, message = res.Message, errors = res.Errors });
        }

        return Json(new
        {
            success = true,
            message = res.Message,
            summary = new
            {
                totalRows = res.TotalRows,
                successRows = res.SuccessRows,
                errorRows = res.ErrorRows,
                newCustomersCount = res.NewCustomersCount,
                updatedCustomersCount = res.ExistingCustomersCount,
                completedCount = res.CompletedAppointmentsCount,
                noShowCount = res.NoShowAppointmentsCount,
                errors = res.Errors
            }
        });
    }
}
