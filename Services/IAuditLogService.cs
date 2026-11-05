using System.Text.Json.Serialization;

namespace CRM_V2.Services;

public interface IAuditLogService
{
    Task LogActivityAsync(
        long? userId,
        string moduleName,
        string actionType,
        string description,
        string? entityId = null,
        string? ipAddress = null);

    Task<AuditLogQueryResultDto> GetLogsAsync(
        long? employeeId = null,
        string? moduleName = null,
        string? actionType = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? search = null,
        int limit = 200);
}

public class AuditLogItemDto
{
    [JsonPropertyName("logId")]
    public long LogId { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("employeeName")]
    public string EmployeeName { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("roleName")]
    public string RoleName { get; set; } = "staff";

    [JsonPropertyName("roleDisplayName")]
    public string RoleDisplayName { get; set; } = "KTV";

    [JsonPropertyName("avatarUrl")]
    public string AvatarUrl { get; set; } = string.Empty;

    [JsonPropertyName("moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    [JsonPropertyName("moduleDisplayName")]
    public string ModuleDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("actionDisplayName")]
    public string ActionDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("entityId")]
    public string? EntityId { get; set; }

    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; set; } = "127.0.0.1";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("relativeTime")]
    public string RelativeTime { get; set; } = string.Empty;
}

public class AuditLogEmployeeFilterDto
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("roleDisplayName")]
    public string RoleDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("logsCount")]
    public int LogsCount { get; set; }
}

public class AuditLogQueryResultDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("createCount")]
    public int CreateCount { get; set; }

    [JsonPropertyName("updateCount")]
    public int UpdateCount { get; set; }

    [JsonPropertyName("deleteCount")]
    public int DeleteCount { get; set; }

    [JsonPropertyName("securityAndExportCount")]
    public int SecurityAndExportCount { get; set; }

    [JsonPropertyName("employees")]
    public List<AuditLogEmployeeFilterDto> Employees { get; set; } = new();

    [JsonPropertyName("logs")]
    public List<AuditLogItemDto> Logs { get; set; } = new();
}
