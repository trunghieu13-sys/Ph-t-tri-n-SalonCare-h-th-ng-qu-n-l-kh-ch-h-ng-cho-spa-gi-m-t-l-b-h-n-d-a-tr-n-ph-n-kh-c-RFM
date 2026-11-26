using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace CRM_V2.Services;

/// <summary>
/// Bộ lọc bảo mật toàn cục (Global Security Action Filter) theo chuẩn AWS Academy Cloud Security Foundations & OWASP Top 10:
/// 1. Kiểm soát truy cập theo Đặc quyền Tối thiểu (Least Privilege & RBAC): Chặn và trả về 403 Forbidden nếu tài khoản không đủ quyền (VD: Staff gọi API của Admin/Owner).
/// 2. Phòng chống SQL Injection & XSS (Input Validation Filter): Quét toàn bộ tham số đầu vào (Query, Form, ActionArguments, DTO properties)
///    và trả về 400 Bad Request ngay lập tức nếu phát hiện payload độc hại (<script>, onerror=, ' OR '1'='1, DROP TABLE, UNION SELECT...).
/// </summary>
public class MaliciousInputFilter : IActionFilter
{
    private readonly ILogger<MaliciousInputFilter> _logger;

    private static readonly Regex[] MaliciousPatterns = new[]
    {
        // XSS Patterns
        new Regex(@"<\s*script\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"<\s*/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bon(error|load|click|mouseover|focus|blur|submit|change|keyup|keydown)\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"<\s*(iframe|object|embed|svg|math)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\balert\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"document\s*\.\s*cookie", RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // SQL Injection Patterns
        new Regex(@"'\s*OR\s+'?1'?\s*=\s*'?1", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"""\s*OR\s+""?1""?\s*=\s*""?1", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"'\s*OR\s+'?[a-z0-9_]+'?\s*=\s*'?[a-z0-9_]+'?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bOR\s+1\s*=\s*1\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bUNION\s+(ALL\s+)?SELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bDROP\s+(TABLE|DATABASE|PROCEDURE|VIEW)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bTRUNCATE\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bDELETE\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bINSERT\s+INTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bEXEC(UTE)?\s+(sp_|xp_)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@";\s*--", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    public MaliciousInputFilter(ILogger<MaliciousInputFilter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Kiểm tra một chuỗi bất kỳ có chứa payload XSS hoặc SQL Injection hay không.
    /// </summary>
    public static bool ContainsMaliciousPayload(string? input, out string matchedRule)
    {
        matchedRule = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        foreach (var regex in MaliciousPatterns)
        {
            if (regex.IsMatch(input))
            {
                matchedRule = regex.ToString();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Quét đệ quy một đối tượng (string, DTO, ViewModel, Dictionary) để tìm chuỗi độc hại.
    /// </summary>
    public static bool TryFindMaliciousInputInObject(object? obj, out string offendingField, out string matchedRule)
    {
        offendingField = string.Empty;
        matchedRule = string.Empty;

        if (obj == null)
            return false;

        if (obj is string str)
        {
            if (ContainsMaliciousPayload(str, out matchedRule))
            {
                offendingField = "Value";
                return true;
            }
            return false;
        }

        var type = obj.GetType();
        if (type.IsPrimitive || type.IsEnum || obj is DateTime || obj is DateTimeOffset || obj is decimal || obj is Guid || obj is TimeSpan)
            return false;

        if (obj is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (TryFindMaliciousInputInObject(item, out offendingField, out matchedRule))
                    return true;
            }
            return false;
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var prop in props)
        {
            object? val;
            try
            {
                val = prop.GetValue(obj);
            }
            catch
            {
                continue;
            }

            if (val is string s)
            {
                if (ContainsMaliciousPayload(s, out matchedRule))
                {
                    offendingField = prop.Name;
                    return true;
                }
            }
        }

        return false;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        // 1. Kiểm tra phân quyền RBAC (Least Privilege) trên Action/Controller nếu User đã xác thực nhưng không đủ quyền
        if (context.ActionDescriptor is ControllerActionDescriptor cad)
        {
            var authAttrs = cad.MethodInfo.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(cad.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>(true))
                .ToList();

            var roleAttrs = authAttrs.Where(a => !string.IsNullOrWhiteSpace(a.Roles)).ToList();
            if (roleAttrs.Count > 0)
            {
                var user = context.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated == true)
                {
                    foreach (var attr in roleAttrs)
                    {
                        var allowedRoles = attr.Roles!
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        bool hasRole = allowedRoles.Any(r =>
                            user.IsInRole(r) ||
                            user.Claims.Any(c =>
                                (c.Type == ClaimTypes.Role || c.Type == "role") &&
                                string.Equals(c.Value, r, StringComparison.OrdinalIgnoreCase)));

                        if (!hasRole)
                        {
                            _logger.LogWarning("RBAC 403 Forbidden: Tài khoản '{User}' không có quyền '{Roles}' khi gọi {Action}",
                                user.Identity?.Name ?? "Unknown", attr.Roles, cad.DisplayName);

                            context.Result = new ObjectResult(new
                            {
                                success = false,
                                code = "FORBIDDEN",
                                message = "403 Forbidden: Tài khoản của bạn không có quyền thực hiện thao tác quản trị này!"
                            })
                            {
                                StatusCode = StatusCodes.Status403Forbidden
                            };
                            return;
                        }
                    }
                }
            }
        }

        // 2. Quét toàn bộ ActionArguments (tham số truyền vào Controller Action & các thuộc tính bên trong DTO/ViewModel)
        foreach (var kvp in context.ActionArguments)
        {
            if (TryFindMaliciousInputInObject(kvp.Value, out var field, out var rule))
            {
                var paramName = string.IsNullOrEmpty(field) || field == "Value" ? kvp.Key : $"{kvp.Key}.{field}";
                _logger.LogWarning("Cảnh báo bảo mật (400 Bad Request): Phát hiện payload độc hại tại tham số '{Param}' (Rule: {Rule})", paramName, rule);

                context.Result = new BadRequestObjectResult(new
                {
                    success = false,
                    code = "MALICIOUS_INPUT_DETECTED",
                    parameter = paramName,
                    message = $"Cảnh báo bảo mật: Dữ liệu đầu vào tại '{paramName}' chứa mã độc XSS hoặc SQL Injection đã bị hệ thống chặn!"
                });
                return;
            }
        }

        // 3. Quét thêm QueryString nếu có
        var request = context.HttpContext?.Request;
        if (request?.Query != null && request.Query.Count > 0)
        {
            foreach (var q in request.Query)
            {
                foreach (var val in q.Value)
                {
                    if (ContainsMaliciousPayload(val, out var rule))
                    {
                        _logger.LogWarning("Cảnh báo bảo mật (400 Bad Request): Phát hiện payload độc hại trên QueryString '{Key}' (Rule: {Rule})", q.Key, rule);
                        context.Result = new BadRequestObjectResult(new
                        {
                            success = false,
                            code = "MALICIOUS_INPUT_DETECTED",
                            parameter = q.Key,
                            message = $"Cảnh báo bảo mật: Tham số truy vấn '{q.Key}' chứa mã độc XSS hoặc SQL Injection đã bị hệ thống chặn!"
                        });
                        return;
                    }
                }
            }
        }

        // 4. Quét thêm Form data nếu có
        if (request != null && request.HasFormContentType && request.Form != null)
        {
            foreach (var f in request.Form)
            {
                foreach (var val in f.Value)
                {
                    if (ContainsMaliciousPayload(val, out var rule))
                    {
                        _logger.LogWarning("Cảnh báo bảo mật (400 Bad Request): Phát hiện payload độc hại trên Form '{Key}' (Rule: {Rule})", f.Key, rule);
                        context.Result = new BadRequestObjectResult(new
                        {
                            success = false,
                            code = "MALICIOUS_INPUT_DETECTED",
                            parameter = f.Key,
                            message = $"Cảnh báo bảo mật: Trường dữ liệu '{f.Key}' chứa mã độc XSS hoặc SQL Injection đã bị hệ thống chặn!"
                        });
                        return;
                    }
                }
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
