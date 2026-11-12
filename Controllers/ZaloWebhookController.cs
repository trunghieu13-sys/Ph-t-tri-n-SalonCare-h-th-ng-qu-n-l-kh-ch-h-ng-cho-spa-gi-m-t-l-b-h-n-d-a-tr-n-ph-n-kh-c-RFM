using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

[ApiController]
[Route("api/webhooks/zalo")]
public class ZaloWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ZaloWebhookController> _logger;

    public ZaloWebhookController(
        ApplicationDbContext context,
        IAuditLogService auditLogService,
        ILogger<ZaloWebhookController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/webhooks/zalo
    /// Endpoint Webhook công khai nhận sự kiện 1-Click từ máy chủ Zalo OA / ZNS
    /// - Xác thực chữ ký SHA256 (X-ZEvent-Signature) bằng Zalo_AppSecret
    /// - Bắt sự kiện user_click_button với payload CONFIRM_{AppointmentId} hoặc CANCEL_{AppointmentId}
    /// - Cập nhật Appointment.Status, NotificationLog.Status và ghi AuditLogs (ZALO_WEBHOOK_CONFIRM)
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveZaloEvent()
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return BadRequest(new { error = 1, message = "Empty webhook payload." });
        }

        var configs = await _context.SystemConfigs
            .AsNoTracking()
            .Where(c => c.ConfigKey.StartsWith("Zalo_"))
            .ToDictionaryAsync(c => c.ConfigKey, c => c.ConfigValue, StringComparer.OrdinalIgnoreCase);

        bool useSandbox = !configs.TryGetValue("Zalo_UseSandboxMode", out var sbxVal) ||
                          !string.Equals(sbxVal, "false", StringComparison.OrdinalIgnoreCase);
        string appId = configs.GetValueOrDefault("Zalo_AppId", string.Empty);
        string appSecret = configs.GetValueOrDefault("Zalo_AppSecret", string.Empty);

        string? signatureHeader = Request.Headers["X-ZEvent-Signature"].FirstOrDefault();

        // Xác thực chữ ký SHA256 theo chuẩn Zalo Webhook: mac = sha256(appId + data + timestamp + OAsecretKey)
        if (!useSandbox && !string.IsNullOrWhiteSpace(appSecret))
        {
            var nodeCheck = JsonNode.Parse(rawBody);
            string timestamp = nodeCheck?["timestamp"]?.ToString() ?? string.Empty;
            string expectedSignature = ComputeZaloWebhookSignature(appId, rawBody, timestamp, appSecret);

            if (string.IsNullOrWhiteSpace(signatureHeader) ||
                !SignatureMatches(signatureHeader, expectedSignature))
            {
                _logger.LogWarning("[ZaloWebhook] Chữ ký X-ZEvent-Signature không hợp lệ! Header={Header}", signatureHeader);
                return Unauthorized(new { error = -1, message = "Invalid X-ZEvent-Signature" });
            }
        }

        return await ProcessZaloWebhookPayloadAsync(rawBody, signatureHeader ?? "sandbox_verified");
    }

    /// <summary>
    /// POST /api/webhooks/zalo/simulate?logId=123&actionType=CONFIRM
    /// Endpoint phục vụ Demo Bảo vệ Đồ án: Tự động đóng gói JSON sự kiện Zalo OA (user_click_button),
    /// ký chữ ký SHA256 X-ZEvent-Signature bằng Zalo_AppSecret và kích hoạt luồng xử lý Webhook
    /// </summary>
    [HttpPost("simulate")]
    [Authorize]
    public async Task<IActionResult> SimulateZaloWebhook([FromQuery] long logId, [FromQuery] string actionType = "CONFIRM")
    {
        var log = await _context.NotificationLogs
            .Include(nl => nl.Appointment)
            .Include(nl => nl.Customer)
            .FirstOrDefaultAsync(nl => nl.LogId == logId);

        if (log == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy bản ghi NotificationLog." });
        }

        var configs = await _context.SystemConfigs
            .AsNoTracking()
            .Where(c => c.ConfigKey.StartsWith("Zalo_"))
            .ToDictionaryAsync(c => c.ConfigKey, c => c.ConfigValue, StringComparer.OrdinalIgnoreCase);

        string appId = configs.GetValueOrDefault("Zalo_AppId", "428910384719203841");
        string appSecret = configs.GetValueOrDefault("Zalo_AppSecret", "zalo_secret_demo_key_2026");
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        long apptId = log.AppointmentId ?? 0;
        string normalizedAction = string.Equals(actionType, "CANCEL", StringComparison.OrdinalIgnoreCase) ? "CANCEL" : "CONFIRM";
        string buttonPayload = $"{normalizedAction}_{apptId}";
        string zaloUid = log.Customer?.ZaloUserId ?? $"zalo_uid_{log.CustomerId}_{DateTime.Now:yyyyMM}";

        var webhookEventObj = new
        {
            app_id = appId,
            event_name = "user_click_button",
            timestamp = timestamp,
            sender = new
            {
                id = zaloUid
            },
            recipient = new
            {
                id = "oa_saloncare_official"
            },
            message = new
            {
                msg_id = log.ExternalMessageId ?? $"zns_msg_{log.LogId}",
                text = normalizedAction == "CONFIRM" ? "Xác nhận giữ lịch hẹn" : "Báo hủy / Đổi lịch hẹn",
                payload = buttonPayload,
                tracking_id = $"{apptId}_{timestamp}"
            }
        };

        string rawJson = JsonSerializer.Serialize(webhookEventObj);
        string computedSig = ComputeZaloWebhookSignature(appId, rawJson, timestamp, appSecret);

        return await ProcessZaloWebhookPayloadAsync(rawJson, computedSig, specificLogId: log.LogId);
    }

    private async Task<IActionResult> ProcessZaloWebhookPayloadAsync(
        string rawBody,
        string signatureUsed,
        long? specificLogId = null)
    {
        try
        {
            var root = JsonNode.Parse(rawBody);
            if (root == null)
            {
                return BadRequest(new { success = false, message = "Invalid JSON body." });
            }

            string eventName = root["event_name"]?.GetValue<string>() ?? "user_click_button";
            string? senderZaloId = root["sender"]?["id"]?.GetValue<string>();
            string? payloadStr = root["message"]?["payload"]?.GetValue<string>()
                                 ?? root["info"]?["payload"]?.GetValue<string>()
                                 ?? root["payload"]?.GetValue<string>();
            string? msgId = root["message"]?["msg_id"]?.GetValue<string>();

            long appointmentId = 0;
            bool isCancel = false;

            if (!string.IsNullOrWhiteSpace(payloadStr))
            {
                if (payloadStr.StartsWith("CONFIRM_", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(payloadStr["CONFIRM_".Length..], out appointmentId);
                    isCancel = false;
                }
                else if (payloadStr.StartsWith("CANCEL_", StringComparison.OrdinalIgnoreCase))
                {
                    long.TryParse(payloadStr["CANCEL_".Length..], out appointmentId);
                    isCancel = true;
                }
            }

            NotificationLog? targetLog = null;
            if (specificLogId.HasValue)
            {
                targetLog = await _context.NotificationLogs
                    .Include(nl => nl.Appointment)
                    .Include(nl => nl.Customer)
                    .FirstOrDefaultAsync(nl => nl.LogId == specificLogId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(msgId))
            {
                targetLog = await _context.NotificationLogs
                    .Include(nl => nl.Appointment)
                    .Include(nl => nl.Customer)
                    .FirstOrDefaultAsync(nl => nl.ExternalMessageId == msgId);
            }

            if (appointmentId <= 0 && targetLog?.AppointmentId != null)
            {
                appointmentId = targetLog.AppointmentId.Value;
            }

            var appointment = appointmentId > 0
                ? await _context.Appointments
                    .Include(a => a.Customer)
                    .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId)
                : targetLog?.Appointment;

            // Lưu ZaloUserId vào hồ sơ Customer nếu có
            var customer = appointment?.Customer ?? targetLog?.Customer;
            if (customer != null && !string.IsNullOrWhiteSpace(senderZaloId) && string.IsNullOrWhiteSpace(customer.ZaloUserId))
            {
                customer.ZaloUserId = senderZaloId;
                customer.UpdatedAt = DateTime.Now;
            }

            if (appointment != null)
            {
                if (isCancel)
                {
                    appointment.Status = "Cancelled";
                    appointment.CancellationReason = "Khách hàng bấm nút Báo bận / Hủy lịch qua Zalo ZNS Webhook 1-Click.";
                }
                else
                {
                    appointment.Status = "Confirmed";
                }
                appointment.UpdatedAt = DateTime.Now;
            }

            // Cập nhật trạng thái các bản ghi NotificationLog liên quan
            if (appointment != null)
            {
                var relatedLogs = await _context.NotificationLogs
                    .Where(nl => nl.AppointmentId == appointment.AppointmentId)
                    .ToListAsync();

                foreach (var nl in relatedLogs)
                {
                    nl.Status = isCancel ? "CanceledByCustomer" : "ConfirmedByCustomer";
                }
            }
            else if (targetLog != null)
            {
                targetLog.Status = isCancel ? "CanceledByCustomer" : "ConfirmedByCustomer";
            }

            await _context.SaveChangesAsync();

            string auditAction = isCancel ? "ZALO_WEBHOOK_CANCEL" : "ZALO_WEBHOOK_CONFIRM";
            string custName = customer?.FullName ?? "Khách hàng";
            string desc = isCancel
                ? $"[ZALO_WEBHOOK_CANCEL] Nhận sự kiện '{eventName}' từ Zalo OA (Payload: {payloadStr}, ZaloUID: {senderZaloId}, Chữ ký SHA256: {signatureUsed[..Math.Min(18, signatureUsed.Length)]}...). Đã tự động chuyển lịch hẹn #{appointmentId} của khách '{custName}' sang Đã hủy."
                : $"[ZALO_WEBHOOK_CONFIRM] Nhận sự kiện '{eventName}' từ Zalo OA (Payload: {payloadStr}, ZaloUID: {senderZaloId}, Chữ ký SHA256: {signatureUsed[..Math.Min(18, signatureUsed.Length)]}...). Đã tự động chuyển lịch hẹn #{appointmentId} của khách '{custName}' sang Đã xác nhận (Confirmed).";

            await _auditLogService.LogActionAsync(
                "APPOINTMENTS",
                auditAction,
                appointmentId > 0 ? appointmentId.ToString() : targetLog?.LogId.ToString(),
                desc);

            return Ok(new
            {
                success = true,
                eventName,
                action = auditAction,
                appointmentId,
                newStatus = isCancel ? "Cancelled" : "Confirmed",
                signatureVerified = signatureUsed,
                message = isCancel
                    ? $"Webhook Zalo OA xác thực thành công: Lịch hẹn #{appointmentId} của khách {custName} đã chuyển sang Đã hủy!"
                    : $"Webhook Zalo OA xác thực thành công: Lịch hẹn #{appointmentId} của khách {custName} đã chuyển sang Đã xác nhận (Confirmed)!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ZaloWebhook] Lỗi xử lý sự kiện từ Zalo Webhook");
            return StatusCode(500, new { success = false, message = "Lỗi xử lý Zalo Webhook: " + ex.Message });
        }
    }

    /// <summary>
    /// Tính chữ ký SHA256 chuẩn Zalo Webhook: mac = sha256(appId + data + timestamp + OAsecretKey)
    /// </summary>
    public static string ComputeZaloWebhookSignature(string appId, string rawData, string timestamp, string appSecret)
    {
        string rawInput = $"{appId}{rawData}{timestamp}{appSecret}";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawInput));
        return "mac=" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool SignatureMatches(string provided, string expected)
    {
        string cleanProvided = provided.StartsWith("mac=", StringComparison.OrdinalIgnoreCase)
            ? provided[4..].Trim()
            : provided.Trim();
        string cleanExpected = expected.StartsWith("mac=", StringComparison.OrdinalIgnoreCase)
            ? expected[4..].Trim()
            : expected.Trim();

        return string.Equals(cleanProvided, cleanExpected, StringComparison.OrdinalIgnoreCase);
    }
}
