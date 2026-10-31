using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. CẤU HÌNH DATABASE (EF Core & SQL Server)
// ==========================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ==========================================
// 2. CẤU HÌNH AUTHENTICATION & COOKIE SESSION
// ==========================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "SalonCareCRM.Auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// ==========================================
// 3. ĐĂNG KÝ OPTIONS (Configuration Binding)
// ==========================================
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.Configure<SmsSettings>(builder.Configuration.GetSection("SmsSettings"));

// ==========================================
// 4. ĐĂNG KÝ THIRD-PARTY SERVICES (DI CONTAINER)
// ==========================================
// Email Service (MailKit)
builder.Services.AddScoped<IEmailService, MailKitEmailService>();

// SMS Service (Twilio)
builder.Services.AddScoped<ISmsService, TwilioSmsService>();

// Appointment Manager Service
builder.Services.AddScoped<IAppointmentService, AppointmentManagerService>();

// Staff & Role Management Service
builder.Services.AddScoped<IStaffService, StaffService>();

// System Notifications & Alert Service
builder.Services.AddScoped<INotificationService, NotificationService>();

// Dashboard Analytics & Summary Service
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Background Worker Service quét kịch bản tự động (5 phút/lần)
builder.Services.AddHostedService<AutomationBackgroundService>();

// ==========================================
// 5. CẤU HÌNH MVC CONTROLLERS & VIEWS
// ==========================================
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ==========================================
// 6. CẤU HÌNH HTTP REQUEST PIPELINE (MIDDLEWARE)
// ==========================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Đặt Authentication trước Authorization
app.UseAuthentication();
app.UseAuthorization();

// Cấu hình Default Route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
