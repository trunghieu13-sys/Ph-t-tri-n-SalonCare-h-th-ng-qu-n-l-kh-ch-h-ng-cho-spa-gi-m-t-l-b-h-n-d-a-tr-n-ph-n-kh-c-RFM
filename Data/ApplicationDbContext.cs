using CRM_V2.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
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
    }
}
