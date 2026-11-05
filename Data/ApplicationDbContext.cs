using System.Security.Claims;
using CRM_V2.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CRM_V2.Data;

public class ApplicationDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private bool _isSavingAuditLogs = false;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentService> AppointmentServices => Set<AppointmentService>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CustomerInteraction> CustomerInteractions => Set<CustomerInteraction>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================
        // 1. CẤU HÌNH BẢNG ROLES
        // ==========================================
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(r => r.RoleId);

            entity.HasIndex(r => r.RoleName)
                  .IsUnique()
                  .HasDatabaseName("UQ_roles_role_name");

            entity.Property(r => r.RoleName).IsRequired().HasMaxLength(50);
            entity.Property(r => r.Description).HasMaxLength(255);
        });

        // ==========================================
        // 2. CẤU HÌNH BẢNG USERS
        // ==========================================
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", tb => tb.UseSqlOutputClause(false));
            entity.HasKey(u => u.UserId);

            entity.HasIndex(u => u.Username)
                  .IsUnique()
                  .HasDatabaseName("UQ_users_username");

            entity.HasIndex(u => u.Email)
                  .IsUnique()
                  .HasDatabaseName("UQ_users_email");

            entity.HasIndex(u => u.Phone)
                  .HasDatabaseName("IX_users_phone");

            entity.HasIndex(u => u.RoleId)
                  .HasDatabaseName("IX_users_role_id");

            entity.HasIndex(u => u.Status)
                  .HasDatabaseName("IX_users_status");

            entity.Property(u => u.FullName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.Username).IsRequired().HasMaxLength(50);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(100);
            entity.Property(u => u.Phone).HasMaxLength(15);
            entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(255);
            entity.Property(u => u.Specialty).HasMaxLength(100);
            entity.Property(u => u.AvatarUrl).HasMaxLength(255);
            entity.Property(u => u.Status).HasDefaultValue("active").HasMaxLength(20);
            entity.Property(u => u.CreatedAt).HasDefaultValueSql("GETDATE()");
            entity.Property(u => u.UpdatedAt).HasDefaultValueSql("GETDATE()");

            entity.HasOne(u => u.Role)
                  .WithMany(r => r.Users)
                  .HasForeignKey(u => u.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // 3. CẤU HÌNH BẢNG CUSTOMERS
        // ==========================================
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers", tb => tb.UseSqlOutputClause(false));
            entity.HasKey(e => e.CustomerId);

            entity.HasIndex(e => e.Phone)
                  .IsUnique()
                  .HasDatabaseName("UQ_customers_phone");

            entity.HasIndex(e => e.FullName)
                  .HasDatabaseName("IX_customers_full_name");

            entity.HasIndex(e => new { e.CustomerType, e.CustomerStatus })
                  .HasDatabaseName("IX_customers_customer_type_status");

            entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Phone).IsRequired().HasMaxLength(15);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.CustomerSource).HasDefaultValue("Truc_tiep");
            entity.Property(e => e.CustomerType).HasDefaultValue("Moi");
            entity.Property(e => e.CustomerStatus).HasDefaultValue("Hoat_dong");
            entity.Property(e => e.TotalSpent).HasPrecision(18, 2).HasDefaultValue(0.00m);
            entity.Property(e => e.TotalVisits).HasDefaultValue(0);
            entity.Property(e => e.NoShowCount).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETDATE()");
        });

        // ==========================================
        // 4. CẤU HÌNH BẢNG SERVICE_CATEGORIES & SERVICES
        // ==========================================
        modelBuilder.Entity<ServiceCategory>(entity =>
        {
            entity.ToTable("service_categories");
            entity.HasKey(sc => sc.CategoryId);
            entity.HasIndex(sc => sc.CategoryName)
                  .IsUnique()
                  .HasDatabaseName("UQ_service_categories_name");
            entity.Property(sc => sc.CategoryName).IsRequired().HasMaxLength(50);
            entity.Property(sc => sc.DefaultIcon).HasDefaultValue("fa-spa").HasMaxLength(50);
            entity.Property(sc => sc.DefaultBgColor).HasDefaultValue("bg-purple-500").HasMaxLength(30);
            entity.Property(sc => sc.DisplayOrder).HasDefaultValue(0);
            entity.Property(sc => sc.IsActive).HasDefaultValue(true);
            entity.Property(sc => sc.CreatedAt).HasDefaultValueSql("GETDATE()");
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.ToTable("services");
            entity.HasKey(s => s.ServiceId);
            entity.Property(s => s.ServiceName).IsRequired().HasMaxLength(150);
            entity.Property(s => s.CategoryName).IsRequired().HasMaxLength(50).HasDefaultValue("Chăm sóc da");
            entity.Property(s => s.DurationMinutes).HasDefaultValue(60);
            entity.Property(s => s.Price).HasPrecision(12, 2).HasDefaultValue(0.00m);
            entity.Property(s => s.IconClass).HasDefaultValue("fa-spa").HasMaxLength(50);
            entity.Property(s => s.BgColor).HasDefaultValue("bg-purple-500").HasMaxLength(30);
            entity.Property(s => s.IsActive).HasDefaultValue(true);
            entity.Property(s => s.CreatedAt).HasDefaultValueSql("GETDATE()");
            entity.Property(s => s.UpdatedAt).HasDefaultValueSql("GETDATE()");
        });

        // ==========================================
        // 5. CẤU HÌNH BẢNG APPOINTMENTS
        // ==========================================
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("appointments", tb => tb.UseSqlOutputClause(false));
            entity.HasKey(a => a.AppointmentId);

            entity.HasIndex(a => new { a.AppointmentDate, a.Status })
                  .HasDatabaseName("IX_appointments_date_status");

            entity.HasIndex(a => a.CustomerId)
                  .HasDatabaseName("IX_appointments_customer_id");

            entity.HasIndex(a => a.EmployeeId)
                  .HasDatabaseName("IX_appointments_employee_id");

            entity.Property(a => a.Status).HasDefaultValue("Pending").HasMaxLength(30);
            entity.Property(a => a.BookingSource).HasDefaultValue("Direct").HasMaxLength(30);
            entity.Property(a => a.DepositAmount).HasPrecision(12, 2).HasDefaultValue(0.00m);
            entity.Property(a => a.TotalPrice).HasPrecision(12, 2).HasDefaultValue(0.00m);
            entity.Property(a => a.CreatedAt).HasDefaultValueSql("GETDATE()");
            entity.Property(a => a.UpdatedAt).HasDefaultValueSql("GETDATE()");
            entity.Property(a => a.IsHiddenFromBoard).HasDefaultValue(false);

            entity.HasOne(a => a.Customer)
                  .WithMany()
                  .HasForeignKey(a => a.CustomerId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Employee)
                  .WithMany()
                  .HasForeignKey(a => a.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(a => a.Creator)
                  .WithMany()
                  .HasForeignKey(a => a.CreatedBy)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        // ==========================================
        // 6. CẤU HÌNH BẢNG TRUNG GIAN APPOINTMENT_SERVICES
        // ==========================================
        modelBuilder.Entity<AppointmentService>(entity =>
        {
            entity.ToTable("appointment_services");
            entity.HasKey(aps => new { aps.AppointmentId, aps.ServiceId });

            entity.Property(aps => aps.Price).HasPrecision(12, 2).HasDefaultValue(0.00m);
            entity.Property(aps => aps.IsHidden).HasDefaultValue(false);

            entity.HasOne(aps => aps.Appointment)
                  .WithMany(a => a.AppointmentServices)
                  .HasForeignKey(aps => aps.AppointmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(aps => aps.Service)
                  .WithMany(s => s.AppointmentServices)
                  .HasForeignKey(aps => aps.ServiceId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(aps => aps.Employee)
                  .WithMany()
                  .HasForeignKey(aps => aps.EmployeeId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        // ==========================================
        // 7. CẤU HÌNH BẢNG AUTOMATION_RULES
        // ==========================================
        modelBuilder.Entity<AutomationRule>(entity =>
        {
            entity.ToTable("automation_rules");
            entity.HasKey(r => r.RuleId);
            entity.Property(r => r.RuleName).IsRequired().HasMaxLength(100);
            entity.Property(r => r.Channel).HasDefaultValue("SMS").HasMaxLength(20);
            entity.Property(r => r.TriggerType).IsRequired().HasMaxLength(50);
            entity.Property(r => r.IsActive).HasDefaultValue(true);
            entity.Property(r => r.CreatedAt).HasDefaultValueSql("GETDATE()");
            entity.Property(r => r.UpdatedAt).HasDefaultValueSql("GETDATE()");
        });

        // ==========================================
        // 8. CẤU HÌNH BẢNG CAMPAIGNS
        // ==========================================
        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.ToTable("campaigns");
            entity.HasKey(c => c.CampaignId);
            entity.Property(c => c.CampaignName).IsRequired().HasMaxLength(150);
            entity.Property(c => c.Channel).IsRequired().HasMaxLength(20);
            entity.Property(c => c.TargetGroup).IsRequired().HasMaxLength(50);
            entity.Property(c => c.Status).HasDefaultValue("PENDING").HasMaxLength(20);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("GETDATE()");
        });

        // ==========================================
        // 9. CẤU HÌNH BẢNG CUSTOMER_INTERACTIONS
        // ==========================================
        modelBuilder.Entity<CustomerInteraction>(entity =>
        {
            entity.ToTable("customer_interactions");
            entity.HasKey(i => i.InteractionId);
            entity.Property(i => i.Channel).IsRequired().HasMaxLength(20);
            entity.Property(i => i.Recipient).IsRequired().HasMaxLength(100);
            entity.Property(i => i.Status).HasDefaultValue("PENDING").HasMaxLength(20);
            entity.Property(i => i.SentAt).HasDefaultValueSql("GETDATE()");

            entity.HasOne(i => i.Customer)
                  .WithMany()
                  .HasForeignKey(i => i.CustomerId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Rule)
                  .WithMany()
                  .HasForeignKey(i => i.RuleId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(i => i.Campaign)
                  .WithMany()
                  .HasForeignKey(i => i.CampaignId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ==========================================
        // 10. CẤU HÌNH BẢNG NOTIFICATIONS
        // ==========================================
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(n => n.NotificationId);
            entity.Property(n => n.Title).IsRequired().HasMaxLength(150);
            entity.Property(n => n.Message).IsRequired();
            entity.Property(n => n.Type).IsRequired().HasMaxLength(30);
            entity.Property(n => n.ReferenceType).HasMaxLength(30);
            entity.Property(n => n.IsRead).HasDefaultValue(false);
            entity.Property(n => n.CreatedAt).HasDefaultValueSql("GETDATE()");

            entity.HasOne(n => n.User)
                  .WithMany()
                  .HasForeignKey(n => n.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ==========================================
        // 11. CẤU HÌNH BẢNG AUDIT_LOGS (NHẬT KÝ HỆ THỐNG)
        // ==========================================
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(a => a.LogId);

            entity.HasIndex(a => new { a.UserId, a.CreatedAt })
                  .HasDatabaseName("IX_audit_logs_user_id_created_at");

            entity.HasIndex(a => new { a.ModuleName, a.ActionType, a.CreatedAt })
                  .HasDatabaseName("IX_audit_logs_module_action_created_at");

            entity.Property(a => a.ModuleName).IsRequired().HasMaxLength(50);
            entity.Property(a => a.ActionType).IsRequired().HasMaxLength(20);
            entity.Property(a => a.Description).IsRequired();
            entity.Property(a => a.EntityId).HasMaxLength(50);
            entity.Property(a => a.IpAddress).HasMaxLength(50).HasDefaultValue("127.0.0.1");
            entity.Property(a => a.CreatedAt).HasDefaultValueSql("GETDATE()");

            entity.HasOne(a => a.User)
                  .WithMany()
                  .HasForeignKey(a => a.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // =========================================================================
    // EF CORE AUTOMATIC AUDIT INTERCEPTOR (GHI ĐÈ SAVECHANGES & SAVECHANGESASYNC)
    // =========================================================================
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (_isSavingAuditLogs)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        var pendingAudits = CaptureAuditEntriesBeforeSave();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);

        if (pendingAudits.Count > 0)
        {
            PersistCapturedAuditEntriesSync(pendingAudits);
        }

        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (_isSavingAuditLogs)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var pendingAudits = CaptureAuditEntriesBeforeSave();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (pendingAudits.Count > 0)
        {
            await PersistCapturedAuditEntriesAsync(pendingAudits, cancellationToken);
        }

        return result;
    }

    private sealed class PendingAuditEntry
    {
        public object Entity { get; init; } = null!;
        public string ModuleName { get; init; } = string.Empty;
        public string ActionType { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string? EntityId { get; set; }
    }

    private (long? UserId, string ActorName, string IpAddress) GetCurrentActorContext()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null)
        {
            return (null, "Hệ thống tự động", "127.0.0.1");
        }

        long? userId = null;
        var userIdStr = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(userIdStr, out var parsedId) && parsedId > 0)
        {
            userId = parsedId;
        }

        var fullName = httpContext.User?.FindFirstValue("FullName")
                       ?? httpContext.User?.Identity?.Name
                       ?? "Nhân viên";

        var ip = httpContext.Connection?.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(ip) || ip == "::1")
        {
            ip = "127.0.0.1";
        }

        return (userId, fullName, ip);
    }

    private List<PendingAuditEntry> CaptureAuditEntriesBeforeSave()
    {
        // Chỉ tự động ghi log khi có phiên HTTP thực tế của người dùng
        if (_httpContextAccessor?.HttpContext == null)
        {
            return new List<PendingAuditEntry>();
        }

        ChangeTracker.DetectChanges();
        var actor = GetCurrentActorContext();
        var list = new List<PendingAuditEntry>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.Entity is Notification || entry.Entity is AppointmentService)
                continue;

            if (entry.State != EntityState.Added && entry.State != EntityState.Modified && entry.State != EntityState.Deleted)
                continue;

            switch (entry.Entity)
            {
                case Customer cust:
                    {
                        string action = entry.State == EntityState.Added ? "CREATE"
                                      : entry.State == EntityState.Deleted ? "DELETE"
                                      : "UPDATE";

                        string desc;
                        if (entry.State == EntityState.Added)
                        {
                            desc = $"Nhân viên {actor.ActorName} thêm mới khách hàng {cust.FullName} (SĐT: {cust.Phone} - Phân hạng: {cust.CustomerType}).";
                        }
                        else if (entry.State == EntityState.Deleted ||
                                 (entry.Property(nameof(Customer.CustomerStatus)).IsModified && cust.CustomerStatus == "Ngung_hoat_dong"))
                        {
                            action = "DELETE";
                            desc = $"Nhân viên {actor.ActorName} vô hiệu hóa / xóa hồ sơ khách hàng {cust.FullName} (SĐT: {cust.Phone}).";
                        }
                        else
                        {
                            // Bỏ qua nếu chỉ tự động cập nhật LastBookingAt khi đặt lịch
                            var modifiedProps = entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList();
                            if (modifiedProps.All(p => p == nameof(Customer.LastBookingAt) || p == nameof(Customer.UpdatedAt) || p == nameof(Customer.TotalVisits) || p == nameof(Customer.TotalSpent) || p == nameof(Customer.LastVisitAt) || p == nameof(Customer.NoShowCount)))
                                continue;

                            desc = $"Nhân viên {actor.ActorName} cập nhật thông tin khách hàng {cust.FullName} (SĐT: {cust.Phone}).";
                        }

                        list.Add(new PendingAuditEntry
                        {
                            Entity = cust,
                            ModuleName = "CUSTOMERS",
                            ActionType = action,
                            Description = desc,
                            EntityId = cust.CustomerId > 0 ? cust.CustomerId.ToString() : null
                        });
                        break;
                    }

                case Appointment appt:
                    {
                        string action = entry.State == EntityState.Added ? "CREATE"
                                      : entry.State == EntityState.Deleted ? "DELETE"
                                      : "UPDATE";

                        string desc;
                        if (entry.State == EntityState.Added)
                        {
                            desc = $"Nhân viên {actor.ActorName} tạo lịch hẹn mới ngày {appt.AppointmentDate:dd/MM/yyyy} ({appt.StartTime:hh\\:mm} - {appt.EndTime:hh\\:mm}, Tổng: {appt.TotalPrice:N0}đ).";
                        }
                        else if (entry.State == EntityState.Modified)
                        {
                            bool isHiddenChanged = entry.Property(nameof(Appointment.IsHiddenFromBoard)).IsModified && appt.IsHiddenFromBoard;
                            bool isStatusChanged = entry.Property(nameof(Appointment.Status)).IsModified;

                            if (isHiddenChanged)
                            {
                                // Nếu tự động ẩn khi tải bảng Kanban (GET) thì không ghi log thao tác tay
                                var method = _httpContextAccessor?.HttpContext?.Request?.Method;
                                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                action = "DELETE";
                                desc = $"Nhân viên {actor.ActorName} ẩn hiển thị lịch hẹn #{appt.AppointmentId} khỏi bảng điều phối Kanban.";
                            }
                            else if (isStatusChanged && appt.Status == "Cancelled")
                            {
                                action = "DELETE";
                                desc = $"Nhân viên {actor.ActorName} hủy lịch hẹn #{appt.AppointmentId} (Lý do: {appt.CancellationReason ?? "Không ghi rõ"}).";
                            }
                            else if (isStatusChanged)
                            {
                                string statusVi = appt.Status switch
                                {
                                    "Pending" => "Chờ xác nhận (Pending)",
                                    "Confirmed" => "Đã xác nhận (Confirmed)",
                                    "InService" => "Đang phục vụ (InService)",
                                    "Completed" => "Hoàn thành (Completed)",
                                    "NoShow" => "Khách vắng mặt (NoShow)",
                                    _ => appt.Status
                                };
                                desc = $"Nhân viên {actor.ActorName} cập nhật trạng thái lịch hẹn #{appt.AppointmentId} thành {statusVi}.";
                            }
                            else
                            {
                                desc = $"Nhân viên {actor.ActorName} đổi lịch / cập nhật điều phối lịch hẹn #{appt.AppointmentId} sang ngày {appt.AppointmentDate:dd/MM/yyyy} ({appt.StartTime:hh\\:mm} - {appt.EndTime:hh\\:mm}).";
                            }
                        }
                        else
                        {
                            desc = $"Nhân viên {actor.ActorName} xóa lịch hẹn #{appt.AppointmentId}.";
                        }

                        list.Add(new PendingAuditEntry
                        {
                            Entity = appt,
                            ModuleName = "APPOINTMENTS",
                            ActionType = action,
                            Description = desc,
                            EntityId = appt.AppointmentId > 0 ? appt.AppointmentId.ToString() : null
                        });
                        break;
                    }

                case Service srv:
                    {
                        string action = entry.State == EntityState.Added ? "CREATE"
                                      : entry.State == EntityState.Deleted ? "DELETE"
                                      : "UPDATE";

                        string desc;
                        if (entry.State == EntityState.Added)
                        {
                            desc = $"Nhân viên {actor.ActorName} thêm mới dịch vụ \"{srv.ServiceName}\" ({srv.CategoryName} • {srv.DurationMinutes} phút • {srv.Price:N0}đ).";
                        }
                        else if (entry.State == EntityState.Deleted ||
                                 (entry.Property(nameof(Service.IsActive)).IsModified && !srv.IsActive))
                        {
                            action = "DELETE";
                            desc = $"Nhân viên {actor.ActorName} ngừng kinh doanh / xóa dịch vụ \"{srv.ServiceName}\".";
                        }
                        else
                        {
                            desc = $"Nhân viên {actor.ActorName} cập nhật dịch vụ \"{srv.ServiceName}\" (Giá: {srv.Price:N0}đ • {srv.DurationMinutes} phút).";
                        }

                        list.Add(new PendingAuditEntry
                        {
                            Entity = srv,
                            ModuleName = "SERVICES",
                            ActionType = action,
                            Description = desc,
                            EntityId = srv.ServiceId > 0 ? srv.ServiceId.ToString() : null
                        });
                        break;
                    }

                case User usr:
                    {
                        // Nếu chỉ cập nhật LastLoginAt khi đăng nhập thì bỏ qua ở đây (đã ghi log LOGIN riêng)
                        if (entry.State == EntityState.Modified)
                        {
                            var modProps = entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToList();
                            if (modProps.Count == 1 && modProps[0] == nameof(User.LastLoginAt))
                                continue;
                        }

                        string action = entry.State == EntityState.Added ? "CREATE"
                                      : entry.State == EntityState.Deleted ? "DELETE"
                                      : "UPDATE";

                        string desc;
                        if (entry.State == EntityState.Added)
                        {
                            desc = $"Nhân viên {actor.ActorName} thêm nhân sự mới: {usr.FullName} (@{usr.Username} - Chuyên môn: {usr.Specialty}).";
                        }
                        else if (entry.State == EntityState.Modified)
                        {
                            if (entry.Property(nameof(User.Status)).IsModified && usr.Status == "resigned")
                            {
                                action = "DELETE";
                                desc = $"Nhân viên {actor.ActorName} chuyển nhân viên {usr.FullName} (@{usr.Username}) sang trạng thái Đã nghỉ việc.";
                            }
                            else if (entry.Property(nameof(User.Status)).IsModified)
                            {
                                string stText = usr.Status == "active" ? "Đang hoạt động (Mở khóa)" : "Tạm khóa tài khoản";
                                desc = $"Nhân viên {actor.ActorName} cập nhật trạng thái tài khoản nhân viên {usr.FullName} thành {stText}.";
                            }
                            else if (entry.Property(nameof(User.RoleId)).IsModified)
                            {
                                string roleText = usr.RoleId == 1 ? "Quản trị viên (Admin)" : usr.RoleId == 2 ? "Chủ Spa (Owner)" : "Kỹ thuật viên (Staff)";
                                desc = $"Nhân viên {actor.ActorName} phân quyền vai trò của {usr.FullName} thành {roleText}.";
                            }
                            else if (entry.Property(nameof(User.Specialty)).IsModified)
                            {
                                desc = $"Nhân viên {actor.ActorName} cập nhật chuyên môn của nhân viên {usr.FullName} thành \"{usr.Specialty}\".";
                            }
                            else
                            {
                                desc = $"Nhân viên {actor.ActorName} cập nhật hồ sơ tài khoản nhân viên {usr.FullName}.";
                            }
                        }
                        else
                        {
                            desc = $"Nhân viên {actor.ActorName} xóa tài khoản nhân viên {usr.FullName}.";
                        }

                        list.Add(new PendingAuditEntry
                        {
                            Entity = usr,
                            ModuleName = "STAFF",
                            ActionType = action,
                            Description = desc,
                            EntityId = usr.UserId > 0 ? usr.UserId.ToString() : null
                        });
                        break;
                    }

                case AutomationRule rule:
                    {
                        string stateText = rule.IsActive ? "Bật kích hoạt" : "Tạm dừng";
                        list.Add(new PendingAuditEntry
                        {
                            Entity = rule,
                            ModuleName = "CUSTOMER_CARE",
                            ActionType = "UPDATE",
                            Description = $"Nhân viên {actor.ActorName} {stateText.ToLower()} kịch bản chăm sóc tự động \"{rule.RuleName}\".",
                            EntityId = rule.RuleId.ToString()
                        });
                        break;
                    }

                case Campaign camp when entry.State == EntityState.Added:
                    {
                        list.Add(new PendingAuditEntry
                        {
                            Entity = camp,
                            ModuleName = "CUSTOMER_CARE",
                            ActionType = "CREATE",
                            Description = $"Nhân viên {actor.ActorName} phát động chiến dịch gửi tin \"{camp.CampaignName}\" qua kênh {camp.Channel} (Nhóm: {camp.TargetGroup}).",
                            EntityId = camp.CampaignId > 0 ? camp.CampaignId.ToString() : null
                        });
                        break;
                    }
            }
        }

        return list;
    }

    private static string? ResolveEntityPrimaryKey(object entity, string? existingId)
    {
        if (!string.IsNullOrWhiteSpace(existingId))
            return existingId;

        return entity switch
        {
            Customer c when c.CustomerId > 0 => c.CustomerId.ToString(),
            Appointment a when a.AppointmentId > 0 => a.AppointmentId.ToString(),
            Service s when s.ServiceId > 0 => s.ServiceId.ToString(),
            User u when u.UserId > 0 => u.UserId.ToString(),
            Campaign cp when cp.CampaignId > 0 => cp.CampaignId.ToString(),
            AutomationRule r when r.RuleId > 0 => r.RuleId.ToString(),
            _ => null
        };
    }

    private void PersistCapturedAuditEntriesSync(List<PendingAuditEntry> pendingAudits)
    {
        try
        {
            _isSavingAuditLogs = true;
            var actor = GetCurrentActorContext();
            long resolvedUserId = actor.UserId ?? Users.AsNoTracking().Where(u => u.Status == "active").OrderBy(u => u.RoleId).Select(u => u.UserId).FirstOrDefault();
            if (resolvedUserId <= 0) return;

            foreach (var item in pendingAudits)
            {
                AuditLogs.Add(new AuditLog
                {
                    UserId = resolvedUserId,
                    ModuleName = item.ModuleName,
                    ActionType = item.ActionType,
                    Description = item.Description,
                    EntityId = ResolveEntityPrimaryKey(item.Entity, item.EntityId),
                    IpAddress = actor.IpAddress,
                    CreatedAt = DateTime.Now
                });
            }

            base.SaveChanges(true);
        }
        catch
        {
            // Không làm gián đoạn luồng nghiệp vụ chính nếu ghi log phụ gặp sự cố
        }
        finally
        {
            _isSavingAuditLogs = false;
        }
    }

    private async Task PersistCapturedAuditEntriesAsync(List<PendingAuditEntry> pendingAudits, CancellationToken cancellationToken)
    {
        try
        {
            _isSavingAuditLogs = true;
            var actor = GetCurrentActorContext();
            long resolvedUserId = actor.UserId ?? await Users.AsNoTracking()
                .Where(u => u.Status == "active")
                .OrderBy(u => u.RoleId)
                .Select(u => u.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (resolvedUserId <= 0) return;

            foreach (var item in pendingAudits)
            {
                var entityId = ResolveEntityPrimaryKey(item.Entity, item.EntityId);
                var desc = item.Description;
                if (item.Entity is Appointment appt && !string.IsNullOrWhiteSpace(entityId) && !desc.Contains($"#{entityId}"))
                {
                    desc = desc.Replace("tạo lịch hẹn mới", $"tạo lịch hẹn mới #{entityId}");
                }

                AuditLogs.Add(new AuditLog
                {
                    UserId = resolvedUserId,
                    ModuleName = item.ModuleName,
                    ActionType = item.ActionType,
                    Description = desc,
                    EntityId = entityId,
                    IpAddress = actor.IpAddress,
                    CreatedAt = DateTime.Now
                });
            }

            await base.SaveChangesAsync(true, cancellationToken);
        }
        catch
        {
            // Không làm gián đoạn luồng nghiệp vụ chính nếu ghi log phụ gặp sự cố
        }
        finally
        {
            _isSavingAuditLogs = false;
        }
    }
}
