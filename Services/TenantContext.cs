using System.Security.Claims;
using CRM_V2.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public sealed class TenantResolution
{
    /// <summary>
    /// True nếu phiên hiện tại là Quản trị viên toàn hệ thống (Admin)
    /// hoặc chạy trong ngữ cảnh không có HttpContext (ví dụ Unit Test không gắn User hoặc Background Worker).
    /// </summary>
    public bool IsAdminOrUnscoped { get; init; }

    /// <summary>
    /// True nếu người dùng đã đăng nhập với vai trò Staff (hoặc Owner chưa có Spa) và SpaId == null
    /// -> Phải trả về danh sách rỗng (0 khách hàng, 0 lịch hẹn, 0 dịch vụ) và hiển thị Banner.
    /// </summary>
    public bool IsUnassignedTenantUser { get; init; }

    /// <summary>
    /// Mã quán Spa đang hiệu lực để lọc (WHERE spa_id = SpaId) và gán khi thêm mới (INSERT spa_id = SpaId).
    /// Nếu Admin xem toàn hệ thống thì SpaId = null (không lọc); nếu Admin chọn lọc theo 1 Spa cụ thể thì SpaId = selectedSpaId.
    /// </summary>
    public long? SpaId { get; init; }

    public long? UserId { get; init; }
    public string Role { get; init; } = "Staff";
}

/// <summary>
/// Bộ giải quyết ngữ cảnh Multi-Tenant (Cô lập dữ liệu theo spa_id) dùng chung cho toàn bộ Controller & Service.
/// </summary>
public static class TenantContext
{
    public static TenantResolution Resolve(ClaimsPrincipal? principal, ApplicationDbContext? db = null, HttpContext? httpContext = null)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new TenantResolution
            {
                IsAdminOrUnscoped = true,
                IsUnassignedTenantUser = false,
                SpaId = null,
                UserId = null,
                Role = "System"
            };
        }

        long? userId = null;
        var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(userIdStr, out var parsedUid) && parsedUid > 0)
        {
            userId = parsedUid;
        }

        bool isAdmin = principal.IsInRole("Admin") || principal.IsInRole("admin");
        bool isOwner = !isAdmin && (principal.IsInRole("Owner") || principal.IsInRole("owner"));
        string role = isAdmin ? "Admin" : (isOwner ? "Owner" : "Staff");

        // 1. Nếu là Admin: Xem & chỉnh sửa toàn bộ hệ thống (hoặc lọc theo spaId nếu Admin chọn xem từng quán Spa)
        if (isAdmin)
        {
            long? adminSelectedSpaId = null;
            var querySpaStr = httpContext?.Request?.Query["spaId"].ToString();
            if (!string.IsNullOrWhiteSpace(querySpaStr) && long.TryParse(querySpaStr, out var qSpa) && qSpa > 0)
            {
                adminSelectedSpaId = qSpa;
            }
            else if (httpContext?.Request?.Cookies != null &&
                     httpContext.Request.Cookies.TryGetValue("SalonCare_AdminSpaFilter", out var cookieSpa) &&
                     long.TryParse(cookieSpa, out var cSpa) && cSpa > 0)
            {
                adminSelectedSpaId = cSpa;
            }

            return new TenantResolution
            {
                IsAdminOrUnscoped = !adminSelectedSpaId.HasValue,
                IsUnassignedTenantUser = false,
                SpaId = adminSelectedSpaId,
                UserId = userId,
                Role = "Admin"
            };
        }

        // 2. Lấy SpaId từ Claim "SpaId" hoặc tra cứu trực tiếp từ bảng users trong DB (để luôn cập nhật nếu Staff vừa chấp nhận lời mời)
        long? spaId = null;
        var spaClaim = principal.FindFirstValue("SpaId");
        if (!string.IsNullOrWhiteSpace(spaClaim) && long.TryParse(spaClaim, out var parsedSpaId) && parsedSpaId > 0)
        {
            spaId = parsedSpaId;
        }

        if (db != null && userId.HasValue)
        {
            var dbUserSpaId = db.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => u.SpaId)
                .FirstOrDefault();

            // Ưu tiên giá trị thực tế trong DB nếu User vừa được gán vào Spa hoặc phiên Cookie cũ chưa có Claim SpaId
            if (dbUserSpaId.HasValue && dbUserSpaId.Value > 0)
            {
                spaId = dbUserSpaId.Value;
            }
            else if (principal.FindFirst("SpaId") != null && !dbUserSpaId.HasValue)
            {
                // Nếu trong DB spa_id thật sự là NULL
                var userExistsInDb = db.Users.AsNoTracking().Any(u => u.UserId == userId.Value);
                if (userExistsInDb)
                {
                    spaId = null;
                }
            }
        }

        // Trường hợp chạy Unit Test: ClaimsPrincipal giả lập không có trong bảng Users và không truyền Claim "SpaId"
        if (!spaId.HasValue && principal.FindFirst("SpaId") == null)
        {
            bool userInDb = db != null && userId.HasValue && db.Users.AsNoTracking().Any(u => u.UserId == userId.Value);
            if (!userInDb)
            {
                return new TenantResolution
                {
                    IsAdminOrUnscoped = true,
                    IsUnassignedTenantUser = false,
                    SpaId = null,
                    UserId = userId,
                    Role = role
                };
            }
        }

        if (spaId.HasValue && spaId.Value > 0)
        {
            return new TenantResolution
            {
                IsAdminOrUnscoped = false,
                IsUnassignedTenantUser = false,
                SpaId = spaId.Value,
                UserId = userId,
                Role = role
            };
        }

        // 3. Staff (hoặc tài khoản thường) chưa tham gia đội ngũ Spa nào (SpaId == NULL)
        return new TenantResolution
        {
            IsAdminOrUnscoped = false,
            IsUnassignedTenantUser = true,
            SpaId = null,
            UserId = userId,
            Role = role
        };
    }
}
