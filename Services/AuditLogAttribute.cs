using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CRM_V2.Services;

/// <summary>
/// ActionFilterAttribute tự động ghi nhận thao tác của người dùng vào bảng dbo.audit_logs
/// Có thể gắn trực tiếp lên Controller hoặc Action: [AuditLog("REPORTS", "EXPORT", "Xuất file báo cáo lịch hẹn")]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuditLogAttribute : ActionFilterAttribute
{
    public string ModuleName { get; }
    public string ActionType { get; }
    public string DescriptionTemplate { get; }

    public AuditLogAttribute(string moduleName, string actionType, string descriptionTemplate)
    {
        ModuleName = moduleName;
        ActionType = actionType;
        DescriptionTemplate = descriptionTemplate;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();

        if (executedContext.Exception == null)
        {
            var auditService = context.HttpContext.RequestServices.GetService<IAuditLogService>();
            if (auditService != null)
            {
                var user = context.HttpContext.User;
                long? userId = null;
                var idClaim = user?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (long.TryParse(idClaim, out var parsedId))
                {
                    userId = parsedId;
                }

                var actorName = user?.FindFirstValue("FullName")
                                ?? user?.Identity?.Name
                                ?? "Nhân viên";

                var ip = context.HttpContext.Connection?.RemoteIpAddress?.ToString();
                var desc = DescriptionTemplate.Replace("{Actor}", actorName);

                await auditService.LogActivityAsync(userId, ModuleName, ActionType, desc, null, ip);
            }
        }
    }
}
