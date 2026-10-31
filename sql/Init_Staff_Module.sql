/* =====================================================================================
   HỆ THỐNG QUẢN LÝ SPA SALONCARE CRM V2
   PHÂN HỆ: QUẢN LÝ NHÂN SỰ & MA TRẬN PHÂN QUYỀN (STAFF & PERMISSIONS MATRIX)
   CSDL: CRM_V2 (SQL Server / T-SQL)
   ===================================================================================== */

USE [CRM_V2];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- =====================================================================================
-- 1. CẬP NHẬT CẤU TRÚC BẢNG USERS (MỞ RỘNG CHUYÊN MÔN & QUẢN LÝ NHÂN SỰ)
-- =====================================================================================
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'users' AND COLUMN_NAME = 'specialty'
)
BEGIN
    ALTER TABLE dbo.users ADD specialty NVARCHAR(100) NULL;
    PRINT N'==> Đã thêm cột specialty vào bảng dbo.users';
END
ELSE
BEGIN
    PRINT N'==> Cột specialty đã tồn tại trong bảng dbo.users';
END
GO

-- Đảm bảo chỉ mục tối ưu hóa truy vấn cho nhân sự
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_users_role_status' AND object_id = OBJECT_ID('dbo.users'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_users_role_status ON dbo.users(role_id, status)
    INCLUDE (full_name, phone, email, specialty, avatar_url);
    PRINT N'==> Đã tạo chỉ mục IX_users_role_status';
END
GO

-- Cập nhật CHECK constraint cho cột status để hỗ trợ trạng thái 'resigned' (Đã nghỉ việc)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_users_status' AND parent_object_id = OBJECT_ID('dbo.users'))
BEGIN
    ALTER TABLE dbo.users DROP CONSTRAINT CK_users_status;
END
ALTER TABLE dbo.users ADD CONSTRAINT CK_users_status CHECK (status IN ('active', 'locked', 'pending', 'resigned'));
PRINT N'==> Đã cập nhật ràng buộc CK_users_status hỗ trợ: active, locked, pending, resigned';
GO

-- =====================================================================================
-- 2. CẬP NHẬT DỮ LIỆU VAI TRÒ (ROLES) CHUẨN
-- =====================================================================================
IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE role_id = 1)
    INSERT INTO dbo.roles (role_id, role_name, description) VALUES (1, 'admin', N'Quản trị viên toàn quyền hệ thống');

IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE role_id = 2)
    INSERT INTO dbo.roles (role_id, role_name, description) VALUES (2, 'owner', N'Chủ Spa / Quản lý điều hành');

IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE role_id = 3)
    INSERT INTO dbo.roles (role_id, role_name, description) VALUES (3, 'staff', N'Nhân viên kỹ thuật / KTV dịch vụ');
GO

-- =====================================================================================
-- 3. CẬP NHẬT CHUYÊN MÔN CHO CÁC TÀI KHOẢN HIỆN CÓ
-- =====================================================================================
UPDATE dbo.users
SET specialty = N'Quản trị viên hệ thống & Điều phối'
WHERE (username = 'admin' OR username = 'hieudzgg13') AND (specialty IS NULL OR specialty = '');

UPDATE dbo.users
SET specialty = N'Chủ Spa & Bác sĩ Thẩm mỹ da'
WHERE username = 'ngocanh' AND (specialty IS NULL OR specialty = '');

UPDATE dbo.users
SET specialty = N'KTV Chuyên sâu Chăm sóc da & Trị liệu'
WHERE username = 'lanktv' AND (specialty IS NULL OR specialty = '');
GO

-- =====================================================================================
-- 4. THÊM NHÂN VIÊN MẪU CHUẨN ĐỂ TEST ĐẦY ĐỦ VAI TRÒ & CHUYÊN MÔN
-- Mật khẩu mặc định: 123456 (BCrypt hash)
-- =====================================================================================
DECLARE @DefaultHash VARCHAR(255) = '$2a$11$4/muy9Dj1xStKMnroyBvDudn61FuS8mlk4z6/zcmyamBf5HZZgN7.';

-- Nhân viên 1: Lê Mai Phương (KTV Massage & Body)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE username = 'phuongktv' OR email = 'phuong.le@saloncare.vn')
BEGIN
    INSERT INTO dbo.users (full_name, username, email, phone, password_hash, role_id, specialty, avatar_url, status, created_at, updated_at)
    VALUES (
        N'Lê Mai Phương',
        'phuongktv',
        'phuong.le@saloncare.vn',
        '0934567891',
        @DefaultHash,
        3, -- Staff
        N'Massage Trị liệu Cổ vai gáy & Body Care',
        'https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=150',
        'active',
        GETDATE(),
        GETDATE()
    );
    PRINT N'==> Đã thêm nhân viên mẫu: Lê Mai Phương';
END

-- Nhân viên 2: Vũ Thùy Linh (KTV Triệt lông & Laser)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE username = 'linhktv' OR email = 'linh.vu@saloncare.vn')
BEGIN
    INSERT INTO dbo.users (full_name, username, email, phone, password_hash, role_id, specialty, avatar_url, status, created_at, updated_at)
    VALUES (
        N'Vũ Thùy Linh',
        'linhktv',
        'linh.vu@saloncare.vn',
        '0945678912',
        @DefaultHash,
        3, -- Staff
        N'Triệt lông công nghệ cao IPL & Laser Diode',
        'https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=150',
        'active',
        GETDATE(),
        GETDATE()
    );
    PRINT N'==> Đã thêm nhân viên mẫu: Vũ Thùy Linh';
END

-- Nhân viên 3: Đặng Bảo Trâm (KTV Gội đầu dưỡng sinh)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE username = 'tramktv' OR email = 'tram.dang@saloncare.vn')
BEGIN
    INSERT INTO dbo.users (full_name, username, email, phone, password_hash, role_id, specialty, avatar_url, status, created_at, updated_at)
    VALUES (
        N'Đặng Bảo Trâm',
        'tramktv',
        'tram.dang@saloncare.vn',
        '0956789123',
        @DefaultHash,
        3, -- Staff
        N'Gội đầu Dưỡng sinh Đông Y & Chăm sóc tóc',
        'https://images.unsplash.com/photo-1580489944761-15a19d654956?w=150',
        'active',
        GETDATE(),
        GETDATE()
    );
    PRINT N'==> Đã thêm nhân viên mẫu: Đặng Bảo Trâm';
END
GO

PRINT N'=== KHỞI TẠO VÀ CẬP NHẬT PHÂN HỆ NHÂN SỰ & PHÂN QUYỀN THÀNH CÔNG ===';
GO
