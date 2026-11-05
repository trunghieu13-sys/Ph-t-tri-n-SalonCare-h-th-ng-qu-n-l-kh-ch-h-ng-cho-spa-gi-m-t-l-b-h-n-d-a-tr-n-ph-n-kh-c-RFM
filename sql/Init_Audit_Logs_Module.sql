-- =============================================================================
-- SALONCARE CRM V2 - SYSTEM AUDIT LOGS MODULE (NHẬT KÝ HỆ THỐNG & BẢO MẬT)
-- Mục tiêu:
--   1. Khởi tạo bảng dbo.audit_logs lưu vết toàn bộ thao tác Thêm/Sửa/Xóa/Đăng nhập/Xuất file
--   2. Tạo đầy đủ Khóa chính (PK), Khóa ngoại (FK -> dbo.users) và Chỉ mục (Indexes)
--   3. Chèn dữ liệu mẫu thực tế từ các nhân viên trong hệ thống trên 8 phân hệ
-- =============================================================================

USE CRM_V2;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. TẠO BẢNG dbo.audit_logs
IF OBJECT_ID('dbo.audit_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.audit_logs (
        log_id          BIGINT          IDENTITY(1,1) NOT NULL,
        user_id         BIGINT          NOT NULL,
        module_name     VARCHAR(50)     NOT NULL, -- CUSTOMERS, APPOINTMENTS, SERVICES, STAFF, CUSTOMER_CARE, REPORTS, SETTINGS, AUTH
        action_type     VARCHAR(20)     NOT NULL, -- CREATE, UPDATE, DELETE, LOGIN, EXPORT
        description     NVARCHAR(MAX)   NOT NULL,
        entity_id       VARCHAR(50)     NULL,
        ip_address      VARCHAR(50)     NULL DEFAULT '127.0.0.1',
        created_at      DATETIME        NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_audit_logs PRIMARY KEY CLUSTERED (log_id ASC),
        CONSTRAINT FK_audit_logs_users FOREIGN KEY (user_id) REFERENCES dbo.users(user_id) ON DELETE CASCADE
    );

    PRINT N'Đã tạo bảng dbo.audit_logs thành công.';
END
GO

-- 2. TẠO CHỈ MỤC TỐI ƯU HÓA TRUY VẤN LỌC THEO NHÂN VIÊN, PHÂN HỆ VÀ THỜI GIAN
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_audit_logs_user_id_created_at' AND object_id = OBJECT_ID('dbo.audit_logs'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_audit_logs_user_id_created_at
    ON dbo.audit_logs (user_id ASC, created_at DESC)
    INCLUDE (module_name, action_type, entity_id, ip_address);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_audit_logs_module_action_created_at' AND object_id = OBJECT_ID('dbo.audit_logs'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_audit_logs_module_action_created_at
    ON dbo.audit_logs (module_name ASC, action_type ASC, created_at DESC)
    INCLUDE (user_id, entity_id);
END
GO

-- 3. CHÈN DỮ LIỆU MẪU THỰC TẾ NẾU BẢNG ĐANG TRỐNG
IF NOT EXISTS (SELECT 1 FROM dbo.audit_logs)
BEGIN
    DECLARE @AdminId BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE role_id = 1 AND status = 'active' ORDER BY user_id);
    DECLARE @OwnerId BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE role_id = 2 AND status = 'active' ORDER BY user_id);
    DECLARE @Staff1Id BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE user_id = 7);
    DECLARE @Staff2Id BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE user_id = 8);
    DECLARE @Staff3Id BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE user_id = 9);
    DECLARE @Staff4Id BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE user_id = 10);

    IF @AdminId IS NULL SET @AdminId = (SELECT TOP 1 user_id FROM dbo.users ORDER BY user_id);
    IF @OwnerId IS NULL SET @OwnerId = @AdminId;
    IF @Staff1Id IS NULL SET @Staff1Id = @AdminId;
    IF @Staff2Id IS NULL SET @Staff2Id = @AdminId;
    IF @Staff3Id IS NULL SET @Staff3Id = @AdminId;
    IF @Staff4Id IS NULL SET @Staff4Id = @AdminId;

    INSERT INTO dbo.audit_logs (user_id, module_name, action_type, description, entity_id, ip_address, created_at)
    VALUES
        (@AdminId, 'AUTH', 'LOGIN', N'Quản trị viên Hoàng Khắc Trung Hiếu đăng nhập thành công vào hệ thống SalonCare CRM.', CAST(@AdminId AS VARCHAR(50)), '127.0.0.1', DATEADD(MINUTE, -180, GETDATE())),
        (@OwnerId, 'AUTH', 'LOGIN', N'Chủ Spa Trần Ngọc Anh đăng nhập vào hệ thống điều hành.', CAST(@OwnerId AS VARCHAR(50)), '192.168.1.15', DATEADD(MINUTE, -165, GETDATE())),
        (@Staff1Id, 'CUSTOMERS', 'CREATE', N'Nhân viên Nguyễn Thị Lan thêm mới hồ sơ khách hàng Lê Thị Mai (SĐT: 0987654321 - Phân hạng: VIP).', '1', '192.168.1.22', DATEADD(MINUTE, -150, GETDATE())),
        (@Staff4Id, 'APPOINTMENTS', 'CREATE', N'Nhân viên Đặng Bảo Trâm tạo lịch hẹn mới #23 cho khách hàng Hoàng Khắc Trung Hiếu (Khung giờ 09:30 - 11:00).', '23', '192.168.1.28', DATEADD(MINUTE, -135, GETDATE())),
        (@Staff1Id, 'APPOINTMENTS', 'UPDATE', N'Nhân viên Nguyễn Thị Lan cập nhật trạng thái lịch hẹn #24 thành Đã xác nhận (Confirmed).', '24', '192.168.1.22', DATEADD(MINUTE, -120, GETDATE())),
        (@OwnerId, 'SERVICES', 'UPDATE', N'Chủ Spa Trần Ngọc Anh cập nhật giá và quy trình dịch vụ "Chăm sóc da chuyên sâu" (60 phút - 199.000đ).', '9', '192.168.1.15', DATEADD(MINUTE, -95, GETDATE())),
        (@Staff2Id, 'CUSTOMER_CARE', 'CREATE', N'Nhân viên Lê Mai Phương gửi tin nhắn SMS nhắc lịch hẹn tự động cho khách hàng Lê Thị Mai (0987654321).', '1', '192.168.1.24', DATEADD(MINUTE, -80, GETDATE())),
        (@OwnerId, 'REPORTS', 'EXPORT', N'Chủ Spa Trần Ngọc Anh xuất báo cáo doanh thu & danh sách lịch hẹn ra file Excel/CSV.', 'REPORT_CSV', '192.168.1.15', DATEADD(MINUTE, -60, GETDATE())),
        (@AdminId, 'STAFF', 'UPDATE', N'Quản trị viên Hoàng Khắc Trung Hiếu cập nhật chuyên môn của nhân viên Lê Mai Phương thành "Massage Trị liệu & Chăm sóc Body".', '8', '127.0.0.1', DATEADD(MINUTE, -45, GETDATE())),
        (@AdminId, 'STAFF', 'DELETE', N'Quản trị viên Hoàng Khắc Trung Hiếu chuyển trạng thái nhân viên Phạm Hải Yến sang Đã nghỉ việc (resigned).', '11', '127.0.0.1', DATEADD(MINUTE, -30, GETDATE())),
        (@Staff3Id, 'CUSTOMERS', 'UPDATE', N'Nhân viên Vũ Thùy Linh cập nhật ghi chú tình trạng da và lịch sử chăm sóc cho khách hàng Trần Ngọc Anh.', '2', '192.168.1.26', DATEADD(MINUTE, -15, GETDATE())),
        (@AdminId, 'SETTINGS', 'UPDATE', N'Quản trị viên Hoàng Khắc Trung Hiếu cấu hình tích hợp cổng gửi Email SMTP (Gmail) & SMS Automation.', 'GATEWAY_CFG', '127.0.0.1', DATEADD(MINUTE, -5, GETDATE()));

    PRINT N'Đã khởi tạo 12 bản ghi nhật ký mẫu thực tế vào dbo.audit_logs.';
END
GO
