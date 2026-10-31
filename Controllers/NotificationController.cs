using CRM_V2.Services;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace CRM_V2.Controllers;

public class NotificationController : Controller
{
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(
        IEmailService emailService,
        ISmsService smsService,
        ILogger<NotificationController> logger)
    {
        _emailService = emailService;
        _smsService = smsService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var model = new NotificationViewModel
        {
            Subject = "[CRM Notification] Cập nhật thông tin tài khoản",
            Content = "<p>Xin chào quý khách, hệ thống CRM đã cập nhật thành công trạng thái đơn hàng của bạn.</p>"
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(NotificationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var emailSuccess = true;
        var smsSuccess = true;
        var statusMessages = new List<string>();

        // 1. Xử lý gửi Email
        if (model.Channel is NotificationChannel.Email or NotificationChannel.Both)
        {
            if (string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Vui lòng nhập Email người nhận.");
                return View(model);
            }

            emailSuccess = await _emailService.SendEmailAsync(
                toEmail: model.Email,
                subject: model.Subject ?? "Thông báo từ CRM",
                htmlContent: model.Content
            );

            statusMessages.Add(emailSuccess 
                ? $"Đã gửi Email thành công tới {model.Email}." 
                : $"Gửi Email tới {model.Email} thất bại. Vui lòng kiểm tra cấu hình SMTP.");
        }

        // 2. Xử lý gửi SMS
        if (model.Channel is NotificationChannel.Sms or NotificationChannel.Both)
        {
            if (string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                ModelState.AddModelError(nameof(model.PhoneNumber), "Vui lòng nhập Số điện thoại người nhận.");
                return View(model);
            }

            // Loại bỏ tag HTML khỏi SMS nếu người dùng nhập HTML
            var plainTextContent = System.Text.RegularExpressions.Regex.Replace(model.Content, "<.*?>", string.Empty);

            smsSuccess = await _smsService.SendSmsAsync(
                toPhoneNumber: model.PhoneNumber,
                message: plainTextContent
            );

            statusMessages.Add(smsSuccess 
                ? $"Đã gửi SMS thành công tới {model.PhoneNumber}." 
                : $"Gửi SMS tới {model.PhoneNumber} thất bại. Vui lòng kiểm tra tài khoản Twilio.");
        }

        if (emailSuccess && smsSuccess)
        {
            TempData["SuccessMessage"] = string.Join(" | ", statusMessages);
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ErrorMessage = string.Join(" | ", statusMessages);
        return View(model);
    }
}
