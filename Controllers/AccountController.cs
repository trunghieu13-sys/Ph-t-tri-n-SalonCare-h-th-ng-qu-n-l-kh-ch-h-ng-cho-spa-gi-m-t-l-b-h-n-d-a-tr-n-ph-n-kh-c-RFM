using System.Security.Claims;
using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.Services;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<AccountController> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    // =========================================================================
    // 1. ĐĂNG NHẬP (LOGIN)
    // =========================================================================
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        try
        {
            ViewData["ReturnUrl"] = model.ReturnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var identifier = model.UsernameOrEmail.Trim();

            // 1. Tìm user theo username HOẶC email
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == identifier || u.Email == identifier.ToLower());

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không chính xác.");
                return View(model);
            }

            // 2. Kiểm tra mật khẩu mã hóa BCrypt
            bool isPasswordValid = false;
            try
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kiểm tra mã hóa mật khẩu cho user {Username}", user.Username);
            }

            if (!isPasswordValid)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không chính xác.");
                return View(model);
            }

            // 3. Kiểm tra trạng thái tài khoản
            if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Tài khoản của bạn đang bị tạm khóa hoặc chưa kích hoạt. Vui lòng liên hệ Quản trị viên.");
                return View(model);
            }

            // 4. Cập nhật thời điểm đăng nhập gần nhất
            user.LastLoginAt = DateTime.Now;
            await _context.SaveChangesAsync();

            // 5. Thiết lập Claims & Phiên đăng nhập Cookie Authentication
            var roleName = (user.Role?.RoleName ?? "staff").Trim().ToLowerInvariant();
            var normalizedRole = roleName switch
            {
                "admin" => "Admin",
                "owner" => "Owner",
                _ => "Staff"
            };
            var defaultAvatar = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150";

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, normalizedRole),
                new(ClaimTypes.Role, roleName),
                new("FullName", user.FullName),
                new("AvatarUrl", string.IsNullOrWhiteSpace(user.AvatarUrl) ? defaultAvatar : user.AvatarUrl),
                new("Phone", user.Phone ?? string.Empty)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe 
                    ? DateTimeOffset.UtcNow.AddDays(7) 
                    : DateTimeOffset.UtcNow.AddHours(12),
                IssuedUtc = DateTimeOffset.UtcNow
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            // Ghi nhận thao tác LOGIN vào bảng audit_logs
            try
            {
                var ipAddr = HttpContext.Connection?.RemoteIpAddress?.ToString();
                if (string.IsNullOrWhiteSpace(ipAddr) || ipAddr == "::1") ipAddr = "127.0.0.1";

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = user.UserId,
                    ModuleName = "AUTH",
                    ActionType = "LOGIN",
                    Description = $"Nhân viên {user.FullName} (@{user.Username} - {normalizedRole}) đăng nhập thành công vào hệ thống SalonCare CRM.",
                    EntityId = user.UserId.ToString(),
                    IpAddress = ipAddr,
                    CreatedAt = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "Không thể ghi AuditLog LOGIN cho user {Username}", user.Username);
            }

            _logger.LogInformation("Người dùng {Username} ({FullName}) đã đăng nhập thành công vào hệ thống.", user.Username, user.FullName);

            TempData["SuccessMessage"] = $"Chào mừng bạn trở lại, {user.FullName}!";
            return RedirectToLocal(model.ReturnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LỖI ĐĂNG NHẬP: {Message}", ex.Message);
            return StatusCode(500, ex.ToString());
        }
    }

    // =========================================================================
    // 2. ĐĂNG KÝ (REGISTER)
    // =========================================================================
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var cleanUsername = model.Username.Trim();
        var cleanEmail = model.Email.Trim().ToLower();

        // 1. Kiểm tra trùng Username
        var usernameExists = await _context.Users.AnyAsync(u => u.Username == cleanUsername);
        if (usernameExists)
        {
            ModelState.AddModelError("Username", "Tên đăng nhập này đã được sử dụng. Vui lòng chọn tên khác.");
            return View(model);
        }

        // 2. Kiểm tra trùng Email
        var emailExists = await _context.Users.AnyAsync(u => u.Email == cleanEmail);
        if (emailExists)
        {
            ModelState.AddModelError("Email", "Địa chỉ email này đã được đăng ký trong hệ thống.");
            return View(model);
        }

        // 3. Lấy thông tin Role (mặc định 'owner' hoặc 'staff')
        var targetRoleName = string.IsNullOrWhiteSpace(model.RoleName) ? "staff" : model.RoleName.Trim().ToLower();
        var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == targetRoleName)
                   ?? await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "staff");

        if (role == null)
        {
            // Tạo tạm vai trò staff nếu chưa có
            role = new Role { RoleName = "staff", Description = "Nhân viên spa" };
            _context.Roles.Add(role);
            await _context.SaveChangesAsync();
        }

        // 4. Mã hóa mật khẩu bằng BCrypt
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

        // 5. Tạo mới User
        var newUser = new User
        {
            FullName = model.FullName.Trim(),
            Username = cleanUsername,
            Email = cleanEmail,
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            PasswordHash = passwordHash,
            RoleId = role.RoleId,
            AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150",
            Status = "active",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Đăng ký thành công tài khoản mới: {Username} ({Email})", newUser.Username, newUser.Email);

        // 6. Tự động đăng nhập luôn sau khi đăng ký
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, newUser.UserId.ToString()),
            new(ClaimTypes.Name, newUser.Username),
            new(ClaimTypes.Email, newUser.Email),
            new(ClaimTypes.Role, role.RoleName),
            new("FullName", newUser.FullName),
            new("AvatarUrl", newUser.AvatarUrl),
            new("Phone", newUser.Phone ?? string.Empty)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity));

        TempData["SuccessMessage"] = $"Đăng ký thành công! Chào mừng bạn, {newUser.FullName}.";
        return RedirectToAction("Index", "Home");
    }

    // =========================================================================
    // 3. ĐĂNG XUẤT (LOGOUT)
    // =========================================================================
    [HttpPost]
    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("Người dùng đã đăng xuất khỏi phiên làm việc.");
        TempData["SuccessMessage"] = "Bạn đã đăng xuất an toàn khỏi SalonCare CRM.";
        return RedirectToAction("Login", "Account");
    }

    // =========================================================================
    // 4. QUÊN MẬT KHẨU (FORGOT PASSWORD)
    // =========================================================================
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var cleanEmail = model.Email.Trim().ToLower();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == cleanEmail);

        if (user == null)
        {
            // Bảo mật: không báo lộ việc email có tồn tại hay không, vẫn báo đã gửi hướng dẫn
            ViewBag.MessageSent = true;
            ViewBag.InfoMessage = "Nếu email tồn tại trong hệ thống SalonCare CRM, chúng tôi đã gửi đường link đặt lại mật khẩu.";
            return View(model);
        }

        // Tạo Token ngẫu nhiên bảo mật 64 ký tự và thời hạn hiệu lực 2 giờ
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        user.PasswordResetToken = token;
        user.PasswordResetExpiresAt = DateTime.Now.AddHours(2);
        await _context.SaveChangesAsync();

        // Tạo đường link khôi phục
        var resetLink = Url.Action("ResetPassword", "Account", new { email = user.Email, token = token }, Request.Scheme);

        // Gửi qua Email Service (MailKit)
        var emailBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #0B0F19; color: #F3F4F6; border-radius: 12px;'>
                <h2 style='color: #A855F7;'>SalonCare CRM - Yêu cầu Đặt lại Mật khẩu</h2>
                <p>Xin chào <strong>{user.FullName}</strong>,</p>
                <p>Hệ thống nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn (Username: <strong>{user.Username}</strong>).</p>
                <p style='margin: 25px 0;'>
                    <a href='{resetLink}' style='background: linear-gradient(135deg, #7C3AED, #EC4899); color: white; padding: 12px 24px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Đặt lại Mật khẩu ngay
                    </a>
                </p>
                <p style='color: #94A3B8; font-size: 12px;'>Đường link này chỉ có hiệu lực trong vòng <strong>2 giờ</strong>. Nếu bạn không yêu cầu hành động này, vui lòng bỏ qua email.</p>
            </div>";

        try
        {
            await _emailService.SendEmailAsync(user.Email, "SalonCare CRM - Hướng dẫn Đặt lại Mật khẩu", emailBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi email khôi phục mật khẩu tới {Email}", user.Email);
        }

        // Cung cấp trực tiếp liên kết hỗ trợ tiện test môi trường local/dev
        ViewBag.MessageSent = true;
        ViewBag.ResetLink = resetLink;
        ViewBag.InfoMessage = "Đường link đặt lại mật khẩu đã được tạo thành công!";

        return View(model);
    }

    // =========================================================================
    // 5. ĐẶT LẠI MẬT KHẨU (RESET PASSWORD)
    // =========================================================================
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            TempData["ErrorMessage"] = "Đường dẫn đặt lại mật khẩu không hợp lệ.";
            return RedirectToAction("Login");
        }

        var cleanEmail = email.Trim().ToLower();
        var user = await _context.Users.FirstOrDefaultAsync(u => 
            u.Email == cleanEmail && 
            u.PasswordResetToken == token && 
            u.PasswordResetExpiresAt > DateTime.Now);

        if (user == null)
        {
            TempData["ErrorMessage"] = "Mã xác thực token không hợp lệ hoặc đã hết hạn (2 giờ). Vui lòng thử lại.";
            return RedirectToAction("ForgotPassword");
        }

        return View(new ResetPasswordViewModel
        {
            Email = cleanEmail,
            Token = token
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var cleanEmail = model.Email.Trim().ToLower();
        var user = await _context.Users.FirstOrDefaultAsync(u => 
            u.Email == cleanEmail && 
            u.PasswordResetToken == model.Token && 
            u.PasswordResetExpiresAt > DateTime.Now);

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Token xác thực không hợp lệ hoặc đã hết hạn. Vui lòng gửi lại yêu cầu quên mật khẩu.");
            return View(model);
        }

        // Cập nhật mật khẩu mới mã hóa BCrypt và vô hiệu hóa Token
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;
        user.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Người dùng {Username} ({Email}) đã đổi mật khẩu thành công bằng Token.", user.Username, user.Email);

        TempData["SuccessMessage"] = "Mật khẩu của bạn đã được đặt lại thành công! Vui lòng đăng nhập với mật khẩu mới.";
        return RedirectToAction("Login");
    }

    // =========================================================================
    // 6. ĐỔI MẬT KHẨU TRONG TÀI KHOẢN (CHANGE PASSWORD)
    // =========================================================================
    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdClaim, out var userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return RedirectToAction("Login");
        }

        // Kiểm tra mật khẩu hiện tại
        if (!BCrypt.Net.BCrypt.Verify(model.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError("CurrentPassword", "Mật khẩu hiện tại không chính xác.");
            return View(model);
        }

        // Mã hóa mật khẩu mới
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
        user.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
        return RedirectToAction("Index", "Home");
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================
    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}
