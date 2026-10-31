using CRM_V2.Services;
using Microsoft.AspNetCore.Mvc;

namespace CRM_V2.Controllers;

public class StaffController : Controller
{
    private readonly IStaffService _staffService;
    private readonly ILogger<StaffController> _logger;

    public StaffController(IStaffService staffService, ILogger<StaffController> logger)
    {
        _staffService = staffService;
        _logger = logger;
    }

    /// <summary>
    /// GET /Staff/GetList
    /// Lấy danh sách nhân viên thực tế từ CSDL (status != 'resigned') kèm chuyên môn, vai trò và số ca làm
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(string? search = null, int? roleId = null, string? status = null)
    {
        try
        {
            var list = await _staffService.GetStaffListAsync(search, roleId, status);
            return Json(new
            {
                success = true,
                data = list,
                total = list.Count,
                activeCount = list.Count(s => s.Status == "active"),
                lockedCount = list.Count(s => s.Status == "locked")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy danh sách nhân viên");
            return Json(new { success = false, message = "Lỗi khi tải danh sách nhân viên: " + ex.Message });
        }
    }

    /// <summary>
    /// GET /Staff/GetDetail/{id}
    /// Lấy chi tiết thông tin 1 nhân viên
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDetail(long id)
    {
        try
        {
            var staff = await _staffService.GetStaffByIdAsync(id);
            if (staff == null)
                return Json(new { success = false, message = "Không tìm thấy nhân viên." });

            return Json(new { success = true, data = staff });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lấy chi tiết nhân viên: {Id}", id);
            return Json(new { success = false, message = "Lỗi máy chủ: " + ex.Message });
        }
    }

    /// <summary>
    /// POST /Staff/Create
    /// Thêm nhân viên mới vào hệ thống (Mã hóa mật khẩu BCrypt, gán chức vụ và chuyên môn)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStaffDto? dto)
    {
        dto ??= new CreateStaffDto();

        if (string.IsNullOrWhiteSpace(dto.FullName) && Request.HasFormContentType)
        {
            dto.FullName = Request.Form["fullName"].ToString();
            dto.Username = Request.Form["username"].ToString();
            dto.Email = Request.Form["email"].ToString();
            dto.Phone = Request.Form["phone"].ToString();
            dto.Password = Request.Form["password"].ToString();
            dto.Specialty = Request.Form["specialty"].ToString();
            if (int.TryParse(Request.Form["roleId"], out var rId)) dto.RoleId = rId;
            dto.AvatarUrl = Request.Form["avatarUrl"].ToString();
        }

        var (success, message, userId) = await _staffService.CreateStaffAsync(dto);
        return Json(new { success, message, userId });
    }

    /// <summary>
    /// POST /Staff/UpdateRole
    /// Nâng cấp hoặc hạ cấp vai trò của tài khoản (Admin 1, Owner 2, Staff 3)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateRole([FromBody] UpdateRoleRequest? request)
    {
        long userId = request?.UserId ?? 0;
        int newRoleId = request?.NewRoleId ?? 0;

        if (userId <= 0 || newRoleId <= 0)
        {
            if (Request.HasFormContentType)
            {
                if (long.TryParse(Request.Form["userId"], out var fUid)) userId = fUid;
                if (int.TryParse(Request.Form["newRoleId"], out var fRid)) newRoleId = fRid;
            }
        }

        if (userId <= 0 || newRoleId <= 0)
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var reader = new StreamReader(Request.Body, leaveOpen: true);
                var raw = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("userId", out var u) || root.TryGetProperty("UserId", out u))
                        userId = u.GetInt64();
                    if (root.TryGetProperty("newRoleId", out var r) || root.TryGetProperty("NewRoleId", out r))
                        newRoleId = r.GetInt32();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi đọc raw JSON trong UpdateRole");
            }
        }

        if (userId <= 0 || newRoleId <= 0)
            return Json(new { success = false, message = "Thiếu thông tin mã nhân viên hoặc vai trò mới." });

        var (success, message) = await _staffService.UpdateRoleAsync(userId, newRoleId);
        return Json(new { success, message });
    }

    /// <summary>
    /// POST /Staff/UpdateSpecialty
    /// Cập nhật chuyên môn / kỹ năng phụ trách của nhân viên
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateSpecialty([FromBody] UpdateSpecialtyRequest? request)
    {
        long userId = request?.UserId ?? 0;
        string specialty = request?.Specialty ?? string.Empty;

        if (userId <= 0 && Request.HasFormContentType)
        {
            if (long.TryParse(Request.Form["userId"], out var fUid)) userId = fUid;
            if (Request.Form.ContainsKey("specialty")) specialty = Request.Form["specialty"].ToString();
        }

        if (userId <= 0)
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var reader = new StreamReader(Request.Body, leaveOpen: true);
                var raw = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("userId", out var u) || root.TryGetProperty("UserId", out u))
                        userId = u.GetInt64();
                    if (root.TryGetProperty("specialty", out var s) || root.TryGetProperty("Specialty", out s))
                        specialty = s.GetString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi đọc raw JSON trong UpdateSpecialty");
            }
        }

        if (userId <= 0)
            return Json(new { success = false, message = "Thiếu mã định danh nhân viên." });

        var (success, message) = await _staffService.UpdateSpecialtyAsync(userId, specialty);
        return Json(new { success, message });
    }

    /// <summary>
    /// POST /Staff/ToggleStatus/{id}
    /// Chuyển đổi trạng thái tài khoản giữa 'active' (đang làm) và 'locked' (tạm khóa)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ToggleStatus(long id)
    {
        var (success, message, newStatus) = await _staffService.ToggleStatusAsync(id);
        return Json(new { success, message, newStatus });
    }

    /// <summary>
    /// POST /Staff/Delete/{id}
    /// Xóa nhân viên (Đánh dấu trạng thái 'resigned' - đã nghỉ việc)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Delete(long id)
    {
        var (success, message) = await _staffService.DeleteStaffAsync(id);
        return Json(new { success, message });
    }

    /// <summary>
    /// GET /Staff/GetPermissionsMatrix
    /// Lấy ma trận quyền hạn tương ứng cho từng vai trò (Admin, Owner, Staff)
    /// </summary>
    [HttpGet]
    public IActionResult GetPermissionsMatrix()
    {
        var matrix = _staffService.GetRolePermissionsMatrix();
        return Json(new { success = true, data = matrix });
    }
}
