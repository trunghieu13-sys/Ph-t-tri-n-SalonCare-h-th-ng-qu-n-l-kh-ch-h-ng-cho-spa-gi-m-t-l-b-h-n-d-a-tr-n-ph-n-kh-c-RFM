using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CRM_V2.Controllers;
using CRM_V2.Data;
using CRM_V2.Models;
using CRM_V2.Services;
using CRM_V2.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace SalonCare.Tests;

public class SalonCareCoreTests
{
    private static ApplicationDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// TC-01 (Auto): Đăng nhập sai mật khẩu / tài khoản bị khóa (status = 'locked') -> Phải từ chối đăng nhập.
    /// </summary>
    [Fact]
    public async Task TC01_Login_WrongPasswordOrLockedAccount_ShouldRejectLogin()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC01_Login_WrongPasswordOrLockedAccount_ShouldRejectLogin));

        var staffRole = new Role { RoleId = 3, RoleName = "staff", Description = "Nhân viên" };
        db.Roles.Add(staffRole);

        var activeUser = new User
        {
            UserId = 1,
            Username = "ktv_active",
            Email = "active@saloncare.vn",
            FullName = "KTV Đang Làm",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPass@123"),
            RoleId = 3,
            Role = staffRole,
            Status = "active"
        };

        var lockedUser = new User
        {
            UserId = 2,
            Username = "ktv_locked",
            Email = "locked@saloncare.vn",
            FullName = "KTV Bị Khóa",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("CorrectPass@123"),
            RoleId = 3,
            Role = staffRole,
            Status = "locked"
        };

        db.Users.AddRange(activeUser, lockedUser);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        var controller = new AccountController(db, emailMock.Object, NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        // Case 1: Sai mật khẩu
        var wrongPassResult = await controller.Login(new LoginViewModel
        {
            UsernameOrEmail = "ktv_active",
            Password = "WrongPassword999"
        });

        Assert.IsType<ViewResult>(wrongPassResult);
        Assert.False(controller.ModelState.IsValid);
        var wrongPassErrors = string.Join(" | ", controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        Assert.Contains("không chính xác", wrongPassErrors);

        // Case 2: Tài khoản bị khóa (status = 'locked')
        controller.ModelState.Clear();
        var lockedResult = await controller.Login(new LoginViewModel
        {
            UsernameOrEmail = "ktv_locked",
            Password = "CorrectPass@123"
        });

        Assert.IsType<ViewResult>(lockedResult);
        Assert.False(controller.ModelState.IsValid);
        var lockedErrors = string.Join(" | ", controller.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        Assert.Contains("tạm khóa", lockedErrors);
    }

    /// <summary>
    /// TC-02 (Auto): Thêm mới khách hàng bị trùng số điện thoại (phone) đã có trong DB -> Phải chặn lưu và báo lỗi trùng lặp.
    /// </summary>
    [Fact]
    public async Task TC02_CreateCustomer_DuplicatePhone_ShouldBlockAndReturnDuplicateError()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC02_CreateCustomer_DuplicatePhone_ShouldBlockAndReturnDuplicateError));

        db.Customers.Add(new Customer
        {
            CustomerId = 1,
            FullName = "Nguyễn Thị Mai",
            Phone = "0912345678",
            CustomerType = "Moi",
            CustomerStatus = "Hoat_dong"
        });
        await db.SaveChangesAsync();

        var rfmMock = new Mock<IRfmService>();
        var auditMock = new Mock<IAuditLogService>();

        var controller = new CustomersController(
            db,
            rfmMock.Object,
            auditMock.Object,
            NullLogger<CustomersController>.Instance);

        var duplicateDto = new CustomerCreateViewModel
        {
            FullName = "Trần Khách Trùng SĐT",
            Phone = "0912345678",
            CustomerType = "Moi"
        };

        var actionResult = await controller.Create(duplicateDto);
        var jsonResult = Assert.IsType<JsonResult>(actionResult);

        var json = JsonSerializer.Serialize(jsonResult.Value);
        using var doc = JsonDocument.Parse(json);
        bool success = doc.RootElement.GetProperty("success").GetBoolean();
        string message = doc.RootElement.GetProperty("message").GetString() ?? "";

        Assert.False(success);
        Assert.Contains("đã tồn tại", message);
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    /// <summary>
    /// TC-03 (Auto): Đặt lịch hẹn trùng khung giờ của cùng 1 Kỹ thuật viên (KTV) đang ở trạng thái Pending, Confirmed hoặc InService -> Phải chặn đặt trùng.
    /// </summary>
    [Fact]
    public async Task TC03_CreateAppointment_OverlappingTimeSlotForSameEmployee_ShouldBlockDoubleBooking()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC03_CreateAppointment_OverlappingTimeSlotForSameEmployee_ShouldBlockDoubleBooking));

        var role = new Role { RoleId = 3, RoleName = "staff", Description = "KTV" };
        var ktv = new User
        {
            UserId = 5,
            Username = "ktv_minhanh",
            FullName = "KTV Minh Anh",
            Email = "minhanh@saloncare.vn",
            PasswordHash = "hash",
            RoleId = 3,
            Role = role,
            Status = "active"
        };
        var customer = new Customer { CustomerId = 10, FullName = "Lê Hoàng Yến", Phone = "0903112233", CustomerStatus = "Hoat_dong" };
        var service = new Service { ServiceId = 1, ServiceName = "Chăm sóc da chuyên sâu", CategoryName = "Chăm sóc da", DurationMinutes = 60, Price = 450000m, IsActive = true };

        var targetDate = new DateTime(2026, 11, 20);
        var existingAppointment = new Appointment
        {
            AppointmentId = 100,
            CustomerId = 10,
            EmployeeId = 5,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(14, 0, 0),
            EndTime = new TimeSpan(15, 0, 0),
            Status = "Confirmed",
            TotalPrice = 450000m
        };

        db.Roles.Add(role);
        db.Users.Add(ktv);
        db.Customers.Add(customer);
        db.Services.Add(service);
        db.Appointments.Add(existingAppointment);
        await db.SaveChangesAsync();

        var rfmMock = new Mock<IRfmService>();
        var appointmentService = new AppointmentManagerService(db, rfmMock.Object, NullLogger<AppointmentManagerService>.Instance);

        var overlappingRequest = new AppointmentCreateViewModel
        {
            CustomerId = 10,
            EmployeeId = 5,
            AppointmentDate = targetDate,
            StartTime = "14:30",
            ServiceIds = new List<int> { 1 }
        };

        var result = await appointmentService.CreateAppointmentSafeAsync(overlappingRequest, currentUserId: 1);

        Assert.False(result.Success);
        Assert.Contains(result.Code, new[] { "EMPLOYEE_BUSY", "ALL_BUSY" });
    }

    /// <summary>
    /// TC-04 (Auto): Đặt 1 lịch hẹn gồm nhiều dịch vụ -> Tự động cộng đúng tổng thời lượng (end_time) và tổng tiền (total_price).
    /// </summary>
    [Fact]
    public async Task TC04_CreateAppointment_MultipleServices_ShouldCalculateTotalDurationAndTotalPrice()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC04_CreateAppointment_MultipleServices_ShouldCalculateTotalDurationAndTotalPrice));

        var role = new Role { RoleId = 3, RoleName = "staff", Description = "KTV" };
        var ktv = new User
        {
            UserId = 6,
            Username = "ktv_lan",
            FullName = "KTV Ngọc Lan",
            Email = "lan@saloncare.vn",
            PasswordHash = "hash",
            RoleId = 3,
            Role = role,
            Status = "active"
        };
        var customer = new Customer { CustomerId = 11, FullName = "Phạm Minh Tuấn", Phone = "0978998877", CustomerStatus = "Hoat_dong" };
        var srv1 = new Service { ServiceId = 1, ServiceName = "Chăm sóc da mặt", CategoryName = "Da", DurationMinutes = 60, Price = 450000m, IsActive = true };
        var srv2 = new Service { ServiceId = 2, ServiceName = "Massage cổ vai gáy", CategoryName = "Body", DurationMinutes = 45, Price = 350000m, IsActive = true };

        db.Roles.Add(role);
        db.Users.Add(ktv);
        db.Customers.Add(customer);
        db.Services.AddRange(srv1, srv2);
        await db.SaveChangesAsync();

        var rfmMock = new Mock<IRfmService>();
        var appointmentService = new AppointmentManagerService(db, rfmMock.Object, NullLogger<AppointmentManagerService>.Instance);

        var multiServiceRequest = new AppointmentCreateViewModel
        {
            CustomerId = 11,
            EmployeeId = 6,
            AppointmentDate = new DateTime(2026, 11, 21),
            StartTime = "09:00",
            ServiceIds = new List<int> { 1, 2 }
        };

        var result = await appointmentService.CreateAppointmentSafeAsync(multiServiceRequest, currentUserId: 1);

        Assert.True(result.Success);
        Assert.NotNull(result.Appointment);
        Assert.Equal(105, result.TotalDurationMinutes);
        Assert.Equal(new TimeSpan(10, 45, 0), result.Appointment!.EndTime);
        Assert.Equal(800000m, result.Appointment.TotalPrice);
    }

    /// <summary>
    /// TC-05 (Auto): Tài khoản quyền Staff gọi vào API/Controller Nhật ký thao tác (AuditLog) của Chủ Spa -> Phải chặn quyền (403 Forbidden / Unauthorized).
    /// </summary>
    [Fact]
    public void TC05_AuditLogController_StaffRoleAccess_ShouldBeForbidden()
    {
        var authorizeAttr = typeof(AuditLogController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.False(string.IsNullOrWhiteSpace(authorizeAttr!.Roles));

        var allowedRoles = authorizeAttr.Roles!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Name, "ktv_staff"),
            new Claim(ClaimTypes.Role, "Staff"),
            new Claim(ClaimTypes.Role, "staff")
        }, "TestCookieAuth"));

        bool isStaffAllowed = allowedRoles.Any(role => staffPrincipal.IsInRole(role));
        Assert.False(isStaffAllowed);
    }

    /// <summary>
    /// TC-06 (Auto): Tính toán Báo cáo tỉ lệ bỏ hẹn (NoShowRate) và tỉ lệ khách quay lại (RetentionRate) theo từng nhóm khách hàng (VIP, Thân thiết, Mới).
    /// </summary>
    [Fact]
    public async Task TC06_GetNoShowAndRetentionReport_ByCustomerSegment_ShouldCalculateRatesCorrectly()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC06_GetNoShowAndRetentionReport_ByCustomerSegment_ShouldCalculateRatesCorrectly));

        db.Customers.AddRange(
            new Customer { CustomerId = 1, FullName = "Khách VIP 1", Phone = "0900000001", CustomerType = "VIP", CustomerStatus = "Hoat_dong", TotalVisits = 5 },
            new Customer { CustomerId = 2, FullName = "Khách Mới 1", Phone = "0900000002", CustomerType = "Moi", CustomerStatus = "Hoat_dong", TotalVisits = 1 },
            new Customer { CustomerId = 3, FullName = "Khách Mới 2", Phone = "0900000003", CustomerType = "Moi", CustomerStatus = "Hoat_dong", TotalVisits = 0 }
        );

        db.Appointments.AddRange(
            new Appointment { AppointmentId = 1, CustomerId = 1, AppointmentDate = new DateTime(2026, 11, 5), Status = "Completed", TotalPrice = 1000000m },
            new Appointment { AppointmentId = 2, CustomerId = 2, AppointmentDate = new DateTime(2026, 11, 6), Status = "Completed", TotalPrice = 400000m },
            new Appointment { AppointmentId = 3, CustomerId = 3, AppointmentDate = new DateTime(2026, 11, 7), Status = "NoShow", TotalPrice = 300000m }
        );
        await db.SaveChangesAsync();

        var controller = new ReportsController(db, NullLogger<ReportsController>.Instance);
        var actionResult = await controller.GetNoShowAndRetentionReport("2026-11-01", "2026-11-30");

        var jsonResult = Assert.IsType<JsonResult>(actionResult);
        var dto = Assert.IsType<NoShowRetentionReportResponseDto>(jsonResult.Value);

        Assert.True(dto.Success, $"Expected Success=true but got false. Message: {dto.Message}");
        var moiSeg = dto.Segments.FirstOrDefault(s => s.SegmentCode == "Moi");
        Assert.NotNull(moiSeg);
        Assert.Equal(50.00m, moiSeg!.NoShowRate);
    }

    /// <summary>
    /// TC-07 (Auto): Xuất báo cáo ra file Excel (.xlsx) qua ClosedXML -> File trả về không rỗng (byte[].Length > 0) và đúng Content-Type Excel.
    /// </summary>
    [Fact]
    public async Task TC07_ExportNoShowRetentionExcel_ClosedXml_ShouldReturnNonEmptyExcelFile()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC07_ExportNoShowRetentionExcel_ClosedXml_ShouldReturnNonEmptyExcelFile));

        db.Customers.Add(new Customer
        {
            CustomerId = 1,
            FullName = "Nguyễn Thị Mai",
            Phone = "0912345678",
            CustomerType = "Moi",
            CustomerStatus = "Hoat_dong",
            TotalVisits = 1
        });
        db.Appointments.Add(new Appointment
        {
            AppointmentId = 1,
            CustomerId = 1,
            AppointmentDate = new DateTime(2026, 11, 5),
            Status = "Completed",
            TotalPrice = 450000m
        });
        await db.SaveChangesAsync();

        var controller = new ReportsController(db, NullLogger<ReportsController>.Instance);
        var result = await controller.ExportNoShowRetentionExcel("2026-11-01", "2026-11-30");

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult.ContentType);
        Assert.True(fileResult.FileContents.Length > 0);
    }

    /// <summary>
    /// Security Test 1 (RBAC & Least Privilege):
    /// Tài khoản quyền Staff gọi các API của Owner/Admin (AuditLogController, ReportsController, StaffController.UpdateRole/Create/ToggleStatus)
    /// -> Phải bị chặn và trả về 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task TC08_Security_RBAC_StaffCallingOwnerApi_ShouldReturn403Forbidden()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC08_Security_RBAC_StaffCallingOwnerApi_ShouldReturn403Forbidden));

        var staffPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "15"),
            new Claim(ClaimTypes.Name, "ktv_nhanvien"),
            new Claim(ClaimTypes.Role, "Staff")
        }, "SalonCareCookieAuth"));

        var httpContext = new DefaultHttpContext { User = staffPrincipal };

        // 1. Staff gọi AuditLogController.GetLogs -> 403 Forbidden
        var auditMock = new Mock<IAuditLogService>();
        var auditController = new AuditLogController(auditMock.Object, NullLogger<AuditLogController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        var auditRes = await auditController.GetLogs();
        var auditObj = Assert.IsType<ObjectResult>(auditRes);
        Assert.Equal(StatusCodes.Status403Forbidden, auditObj.StatusCode);

        // 2. Staff gọi ReportsController.GetNoShowAndRetentionReport -> 403 Forbidden
        var reportsController = new ReportsController(db, NullLogger<ReportsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        var reportRes = await reportsController.GetNoShowAndRetentionReport();
        var reportObj = Assert.IsType<ObjectResult>(reportRes);
        Assert.Equal(StatusCodes.Status403Forbidden, reportObj.StatusCode);

        // 3. Staff gọi StaffController.UpdateRole, Create, ToggleStatus -> 403 Forbidden
        var staffMock = new Mock<IStaffService>();
        var staffController = new StaffController(staffMock.Object, NullLogger<StaffController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var updateRoleRes = await staffController.UpdateRole(new UpdateRoleRequest { UserId = 15, NewRoleId = 2 });
        var updateRoleObj = Assert.IsType<ObjectResult>(updateRoleRes);
        Assert.Equal(StatusCodes.Status403Forbidden, updateRoleObj.StatusCode);

        var createStaffRes = await staffController.Create(new CreateStaffDto { FullName = "NV Mới", Username = "nvmoi" });
        var createStaffObj = Assert.IsType<ObjectResult>(createStaffRes);
        Assert.Equal(StatusCodes.Status403Forbidden, createStaffObj.StatusCode);

        var toggleRes = await staffController.ToggleStatus(1);
        var toggleObj = Assert.IsType<ObjectResult>(toggleRes);
        Assert.Equal(StatusCodes.Status403Forbidden, toggleObj.StatusCode);

        // 4. Staff gọi CustomersController (Create, Update, Delete), ServicesController (Create, Update, Delete),
        //    AppointmentsController (Create, UpdateStatus, Reschedule, Cancel) và AdminController -> 403 Forbidden
        var customersController = new CustomersController(
            db,
            new Mock<IRfmService>().Object,
            auditMock.Object,
            NullLogger<CustomersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await customersController.Create(new CustomerCreateViewModel { FullName = "Test", Phone = "0909111222" })).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await customersController.Delete(new CustomerDeleteDto { CustomerId = 1 })).StatusCode);

        var servicesController = new ServicesController(db, NullLogger<ServicesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await servicesController.Create(new ServiceUpsertDto { ServiceName = "Test", DurationMinutes = 60, Price = 100000 })).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await servicesController.Delete(1)).StatusCode);

        var appointmentsController = new AppointmentsController(
            db,
            new Mock<IAppointmentService>().Object,
            NullLogger<AppointmentsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await appointmentsController.Create(new AppointmentCreateViewModel())).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await appointmentsController.UpdateStatus(new AppointmentUpdateStatusViewModel { AppointmentId = 1, NewStatus = "Confirmed" })).StatusCode);

        var adminController = new AdminController(db, NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(await adminController.GetSystemOverview()).StatusCode);
    }

    /// <summary>
    /// Security Test 2 (MaliciousInputFilter - Chống XSS & SQL Injection):
    /// Gửi chuỗi <script>alert(1)</script> hoặc ' OR '1'='1 vào GlobalSearch / CreateCustomer
    /// -> Phải bị chặn và trả về 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task TC09_Security_MaliciousInput_XssAndSqlInjection_ShouldReturn400BadRequest()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC09_Security_MaliciousInput_XssAndSqlInjection_ShouldReturn400BadRequest));

        var notifMock = new Mock<INotificationService>();
        var headerController = new HeaderController(db, notifMock.Object, NullLogger<HeaderController>.Instance);

        var rfmMock = new Mock<IRfmService>();
        var auditMock = new Mock<IAuditLogService>();
        var customersController = new CustomersController(db, rfmMock.Object, auditMock.Object, NullLogger<CustomersController>.Instance);

        // 1. Gửi payload XSS <script>alert(1)</script> vào GlobalSearch -> 400 Bad Request
        var xssSearchRes = await headerController.GlobalSearch("<script>alert(1)</script>");
        var xssSearchBadReq = Assert.IsType<BadRequestObjectResult>(xssSearchRes);
        Assert.Equal(StatusCodes.Status400BadRequest, xssSearchBadReq.StatusCode);

        // 2. Gửi payload SQL Injection ' OR '1'='1 vào GlobalSearch -> 400 Bad Request
        var sqliSearchRes = await headerController.GlobalSearch("' OR '1'='1");
        var sqliSearchBadReq = Assert.IsType<BadRequestObjectResult>(sqliSearchRes);
        Assert.Equal(StatusCodes.Status400BadRequest, sqliSearchBadReq.StatusCode);

        // 3. Gửi payload XSS <script>alert(1)</script> vào CreateCustomer -> 400 Bad Request
        var xssCustRes = await customersController.Create(new CustomerCreateViewModel
        {
            FullName = "<script>alert(1)</script>",
            Phone = "0911223344",
            CustomerType = "Moi"
        });
        var xssCustBadReq = Assert.IsType<BadRequestObjectResult>(xssCustRes);
        Assert.Equal(StatusCodes.Status400BadRequest, xssCustBadReq.StatusCode);

        // 4. Gửi payload SQL Injection ' OR '1'='1 vào CreateCustomer -> 400 Bad Request
        var sqliCustRes = await customersController.Create(new CustomerCreateViewModel
        {
            FullName = "Nguyễn Văn A' OR '1'='1",
            Phone = "0911223344",
            CustomerType = "Moi"
        });
        var sqliCustBadReq = Assert.IsType<BadRequestObjectResult>(sqliCustRes);
        Assert.Equal(StatusCodes.Status400BadRequest, sqliCustBadReq.StatusCode);

        // 5. Kiểm tra trực tiếp qua MaliciousInputFilter.OnActionExecuting
        var filter = new MaliciousInputFilter(NullLogger<MaliciousInputFilter>.Instance);
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());

        var executingContext = new Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext(
            actionContext,
            new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>(),
            new Dictionary<string, object?> { ["query"] = "DROP TABLE customers;--" },
            headerController);

        filter.OnActionExecuting(executingContext);
        var filterBadReq = Assert.IsType<BadRequestObjectResult>(executingContext.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, filterBadReq.StatusCode);
    }

    /// <summary>
    /// TC-10 (Auto): Mời nhân viên vào đội ngũ -> Pending + thông báo; Staff đồng ý -> users.spa_id được gán;
    /// Chủ Spa xóa khỏi đội ngũ -> spa_id = NULL.
    /// </summary>
    [Fact]
    public async Task TC10_SpaInvitation_SendAcceptAndRemove_ShouldUpdateSpaIdCorrectly()
    {
        await using var db = CreateInMemoryDbContext(nameof(TC10_SpaInvitation_SendAcceptAndRemove_ShouldUpdateSpaIdCorrectly));
        db.Roles.AddRange(
            new Role { RoleId = 2, RoleName = "owner", Description = "Chủ quán" },
            new Role { RoleId = 3, RoleName = "staff", Description = "Nhân viên" });
        db.Users.AddRange(
            new User { UserId = 100, FullName = "Chủ Spa A", Username = "owner_a", Email = "a@x.com", PasswordHash = "x", RoleId = 2, Status = "active" },
            new User { UserId = 200, FullName = "Nhân Viên Mới", Username = "staff_new", Email = "new@x.com", PasswordHash = "x", RoleId = 3, Status = "active" });
        db.Spas.Add(new Spa { SpaId = 1, SpaName = "Spa của Chủ Spa A", OwnerUserId = 100, Status = "active" });
        await db.SaveChangesAsync();
        (await db.Users.FindAsync(100L))!.SpaId = 1;
        await db.SaveChangesAsync();

        ServiceHttp Make(long userId, string role, string? spaId)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Role, role),
                new("SpaId", spaId ?? string.Empty)
            };
            var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
            var accessorMock = new Mock<IHttpContextAccessor>();
            accessorMock.Setup(a => a.HttpContext).Returns(ctx);
            var svc = new StaffService(db, NullLogger<StaffService>.Instance, accessorMock.Object);
            return new ServiceHttp(svc);
        }

        var owner = Make(100, "Owner", "1").Service;
        var staff = Make(200, "Staff", null).Service;

        // Tìm -> trạng thái NoSpa
        var found = await owner.SearchStaffForInvitationAsync("staff_new");
        Assert.Single(found);
        Assert.Equal("NoSpa", found[0].InviteStatus);

        // Gửi lời mời -> Pending + notification
        var sent = await owner.SendInvitationAsync(200);
        Assert.True(sent.Success, sent.Message);
        Assert.Single(db.SpaInvitations.Where(i => i.Status == "Pending"));
        Assert.Single(db.Notifications.Where(n => n.UserId == 200 && n.Type == "SPA_INVITATION"));
        Assert.Equal("Pending", (await owner.SearchStaffForInvitationAsync("staff_new"))[0].InviteStatus);

        // Gửi trùng -> bị chặn
        Assert.False((await owner.SendInvitationAsync(200)).Success);

        // Staff đồng ý
        var invitation = (await staff.GetMyPendingInvitationsAsync(200)).Single();
        var accepted = await staff.AcceptInvitationAsync(invitation.InvitationId, 200);
        Assert.True(accepted.Success, accepted.Message);
        Assert.Equal(1L, accepted.SpaId);
        Assert.Equal(1L, (await db.Users.FindAsync(200L))!.SpaId);
        var inv = await db.SpaInvitations.SingleAsync();
        Assert.Equal("Accepted", inv.Status);
        Assert.NotNull(inv.RespondedAt);

        // Danh sách đội ngũ của quán có nhân viên mới
        var team = await owner.GetStaffListAsync();
        Assert.Contains(team, t => t.UserId == 200);

        // Chủ Spa xóa khỏi đội ngũ -> spa_id = NULL
        var removed = await owner.RemoveFromTeamAsync(200);
        Assert.True(removed.Success, removed.Message);
        Assert.Null((await db.Users.FindAsync(200L))!.SpaId);
    }

    private sealed record ServiceHttp(StaffService Service);
}
