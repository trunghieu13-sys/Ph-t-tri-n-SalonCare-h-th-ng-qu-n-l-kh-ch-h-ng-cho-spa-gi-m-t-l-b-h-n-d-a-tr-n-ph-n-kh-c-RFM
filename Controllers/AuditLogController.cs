using System.Text;
using CRM_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM_V2.Controllers;

/// <summary>
/// Controller Quản lý & Truy xuất Nhật ký hệ thống (Audit Logs)
/// Phân quyền nghiêm ngặt: Chỉ tài khoản Quản trị viên (Admin) và Chủ Spa (Owner) được phép truy cập.
/// Nếu Nhân viên (Staff) gọi API này, hệ thống lập tức chặn với mã 403 Forbidden.
/// </summary>
[Authorize(Roles = "Admin,Owner,admin,owner")]
public class AuditLogController : Controller
{
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AuditLogController> _logger;

    public AuditLogController(IAuditLogService auditLogService, ILogger<AuditLogController> logger)
    {
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// GET /AuditLog/GetLogs?employeeId={ID}&moduleName={Module}&actionType={Action}&fromDate={Date}&toDate={Date}&search={Keyword}
    /// Nếu employeeId bỏ trống -> trả về toàn bộ nhật ký của tất cả nhân viên (mới nhất lên đầu).
    /// Nếu truyền employeeId -> chỉ lọc riêng nhật ký của nhân viên đó.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        long? employeeId = null,
        string? moduleName = null,
        string? actionType = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? search = null)
    {
        try
        {
            var result = await _auditLogService.GetLogsAsync(
                employeeId,
                moduleName,
                actionType,
                fromDate,
                toDate,
                search);

            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi truy xuất dữ liệu Nhật ký hệ thống (Audit Logs)");
            return StatusCode(500, new
            {
                success = false,
                message = "Lỗi máy chủ khi tải nhật ký hệ thống: " + ex.Message
            });
        }
    }

    /// <summary>
    /// GET /AuditLog/ExportCsv
    /// Xuất danh sách nhật ký thao tác ra file Excel/CSV (Chỉ dành cho Admin/Owner)
    /// </summary>
    [HttpGet]
    [AuditLog("REPORTS", "EXPORT", "Nhân viên {Actor} xuất báo cáo Nhật ký thao tác hệ thống (Audit Logs) ra file CSV/Excel.")]
    public async Task<IActionResult> ExportCsv(
        long? employeeId = null,
        string? moduleName = null,
        string? actionType = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? search = null)
    {
        var result = await _auditLogService.GetLogsAsync(
            employeeId,
            moduleName,
            actionType,
            fromDate,
            toDate,
            search,
            limit: 1000);

        var sb = new StringBuilder();
        sb.AppendLine("Mã Log,Thời Gian,Nhân Viên Thao Tác,Tên Đăng Nhập,Vai Trò,Phân Hệ,Loại Hành Động,Mã Bản Ghi,Địa Chỉ IP,Chi Tiết Thao Tác");

        foreach (var l in result.Logs)
        {
            var cleanDesc = (l.Description ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
            sb.AppendLine($"#{l.LogId},\"{l.CreatedAt}\",\"{l.EmployeeName}\",\"{l.Username}\",\"{l.RoleDisplayName}\",\"{l.ModuleDisplayName} ({l.ModuleName})\",\"{l.ActionType}\",\"{l.EntityId ?? "-"}\",\"{l.IpAddress}\",\"{cleanDesc}\"");
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fullBytes = preamble.Concat(bodyBytes).ToArray();

        return File(fullBytes, "text/csv; charset=utf-8", $"NhatKyHeThong_AuditLogs_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }
}
