-- ======================================================================================
-- T-SQL SCRIPT: KHỞI TẠO BẢNG ROLES VÀ USERS CHO HỆ THỐNG SALONCARE CRM (CRM_V2)
-- Tác giả: Senior Full-Stack Developer & DBA
-- Phiên bản: 1.0 (SQL Server 2016 / 2019 / 2022)
-- Cơ sở dữ liệu: CRM_V2
-- ======================================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [CRM_V2];
GO

-- --------------------------------------------------------------------------------------
-- 1. BẢNG ROLES (VAI TRÒ / QUYỀN HẠN HỆ THỐNG)
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.roles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.roles
    (
        role_id     INT IDENTITY(1,1) NOT NULL,
        role_name   NVARCHAR(50)      NOT NULL,
        description NVARCHAR(255)     NULL,
        created_at  DATETIME          NOT NULL CONSTRAINT DF_roles_created_at DEFAULT GETDATE(),

        CONSTRAINT PK_roles PRIMARY KEY CLUSTERED (role_id ASC),
        CONSTRAINT UQ_roles_role_name UNIQUE NONCLUSTERED (role_name ASC)
    );
    PRINT N'==> Bảng dbo.roles đã được tạo thành công.';
END
ELSE
BEGIN
    PRINT N'==> Bảng dbo.roles đã tồn tại sẵn.';
END
GO

-- --------------------------------------------------------------------------------------
-- 2. BẢNG USERS (TÀI KHOẢN NGƯỜI DÙNG)
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.users
    (
        user_id                     BIGINT IDENTITY(1,1) NOT NULL,
        full_name                   NVARCHAR(100)        NOT NULL,
        username                    VARCHAR(50)          NOT NULL,
        email                       VARCHAR(100)         NOT NULL,
        phone                       VARCHAR(15)          NULL,
        password_hash               VARCHAR(255)         NOT NULL,
        role_id                     INT                  NOT NULL,
        avatar_url                  VARCHAR(255)         NULL,
        [status]                    NVARCHAR(20)         NOT NULL CONSTRAINT DF_users_status DEFAULT N'active',
        email_verified_at           DATETIME             NULL,
        last_login_at               DATETIME             NULL,
        password_reset_token        VARCHAR(255)         NULL,
        password_reset_expires_at   DATETIME             NULL,
        created_at                  DATETIME             NOT NULL CONSTRAINT DF_users_created_at DEFAULT GETDATE(),
        updated_at                  DATETIME             NOT NULL CONSTRAINT DF_users_updated_at DEFAULT GETDATE(),

        CONSTRAINT PK_users PRIMARY KEY CLUSTERED (user_id ASC),
        CONSTRAINT UQ_users_username UNIQUE NONCLUSTERED (username ASC),
        CONSTRAINT UQ_users_email UNIQUE NONCLUSTERED (email ASC),
        CONSTRAINT FK_users_roles FOREIGN KEY (role_id) REFERENCES dbo.roles(role_id) ON DELETE NO ACTION ON UPDATE CASCADE,
        CONSTRAINT CK_users_status CHECK ([status] IN (N'active', N'locked', N'pending'))
    );
    PRINT N'==> Bảng dbo.users đã được tạo thành công.';
END
ELSE
BEGIN
    PRINT N'==> Bảng dbo.users đã tồn tại sẵn.';

    -- Đảm bảo có đủ 2 cột token quên mật khẩu
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users') AND name = N'password_reset_token')
    BEGIN
        ALTER TABLE dbo.users ADD password_reset_token VARCHAR(255) NULL;
        PRINT N'==> Đã thêm cột password_reset_token vào dbo.users.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.users') AND name = N'password_reset_expires_at')
    BEGIN
        ALTER TABLE dbo.users ADD password_reset_expires_at DATETIME NULL;
        PRINT N'==> Đã thêm cột password_reset_expires_at vào dbo.users.';
    END
END
GO

-- --------------------------------------------------------------------------------------
-- 3. TẠO INDEXES TỐI ƯU HÓA HIỆU NĂNG TÌM KIẾM
-- --------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_users_role_id' AND object_id = OBJECT_ID(N'dbo.users'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_users_role_id ON dbo.users(role_id);
    PRINT N'==> Đã tạo Index IX_users_role_id.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_users_phone' AND object_id = OBJECT_ID(N'dbo.users'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_users_phone ON dbo.users(phone) WHERE phone IS NOT NULL;
    PRINT N'==> Đã tạo Index IX_users_phone.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_users_status' AND object_id = OBJECT_ID(N'dbo.users'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_users_status ON dbo.users([status]);
    PRINT N'==> Đã tạo Index IX_users_status.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_users_reset_token' AND object_id = OBJECT_ID(N'dbo.users'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_users_reset_token ON dbo.users(password_reset_token) WHERE password_reset_token IS NOT NULL;
    PRINT N'==> Đã tạo Index IX_users_reset_token.';
END
GO

-- --------------------------------------------------------------------------------------
-- 4. TRIGGER TỰ ĐỘNG CẬP NHẬT UPDATED_AT CHO USERS
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.trg_users_update_audit', N'TR') IS NOT NULL
    DROP TRIGGER dbo.trg_users_update_audit;
GO

CREATE TRIGGER dbo.trg_users_update_audit
ON dbo.users
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(updated_at)
    BEGIN
        UPDATE u
        SET u.updated_at = GETDATE()
        FROM dbo.users u
        INNER JOIN inserted i ON u.user_id = i.user_id;
    END
END
GO
PRINT N'==> Trigger trg_users_update_audit đã được cài đặt thành công.';
GO

-- --------------------------------------------------------------------------------------
-- 5. CHÈN DỮ LIỆU MẪU: ROLES (VAI TRÒ)
-- --------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE role_name = N'admin')
BEGIN
    INSERT INTO dbo.roles (role_name, description)
    VALUES (N'admin', N'Quản trị hệ thống (Toàn quyền quản lý CRM)');
END

IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE role_name = N'owner')
BEGIN
    INSERT INTO dbo.roles (role_name, description)
    VALUES (N'owner', N'Chủ spa (Quản lý vận hành, tài chính, báo cáo & nhân sự)');
END

IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE role_name = N'staff')
BEGIN
    INSERT INTO dbo.roles (role_name, description)
    VALUES (N'staff', N'Nhân viên spa (Đặt lịch, quản lý khách hàng & dịch vụ)');
END
GO

-- --------------------------------------------------------------------------------------
-- 6. CHÈN & ĐỒNG BỘ DỮ LIỆU TÀI KHOẢN MẪU (MÃ HÓA BCRYPT)
-- Mật khẩu mặc định:
--   1. admin    / admin@saloncare.vn     : Admin@123  (hoặc 123456)
--   2. ngocanh  / ngocanh@saloncare.vn   : Owner@123  (hoặc 123456)
--   3. lanktv   / lan.nguyen@saloncare.vn: Staff@123  (hoặc 123456)
-- --------------------------------------------------------------------------------------
DECLARE @adminRoleId INT = (SELECT TOP 1 role_id FROM dbo.roles WHERE role_name = N'admin');
DECLARE @ownerRoleId INT = (SELECT TOP 1 role_id FROM dbo.roles WHERE role_name = N'owner');
DECLARE @staffRoleId INT = (SELECT TOP 1 role_id FROM dbo.roles WHERE role_name = N'staff');

-- 1. Tài khoản Admin hệ thống (Mật khẩu: Admin@123)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE username = 'admin')
BEGIN
    INSERT INTO dbo.users 
    (
        full_name, username, email, phone, password_hash, role_id, 
        avatar_url, [status], email_verified_at, created_at, updated_at
    )
    VALUES 
    (
        N'Quản Trị Viên',
        'admin',
        'admin@saloncare.vn',
        '0900000001',
        '$2a$11$4/muy9Dj1xStKMnroyBvDudn61FuS8mlk4z6/zcmyamBf5HZZgN7.', -- Admin@123
        @adminRoleId,
        'https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?w=150',
        N'active',
        GETDATE(),
        GETDATE(),
        GETDATE()
    );
END
ELSE
BEGIN
    UPDATE dbo.users 
    SET password_hash = '$2a$11$4/muy9Dj1xStKMnroyBvDudn61FuS8mlk4z6/zcmyamBf5HZZgN7.', -- Admin@123
        [status] = N'active'
    WHERE username = 'admin';
END

-- 2. Tài khoản Chủ Salon - Trần Ngọc Anh (Mật khẩu: Owner@123)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE username = 'ngocanh')
BEGIN
    INSERT INTO dbo.users 
    (
        full_name, username, email, phone, password_hash, role_id, 
        avatar_url, [status], email_verified_at, created_at, updated_at
    )
    VALUES 
    (
        N'Trần Ngọc Anh',
        'ngocanh',
        'ngocanh@saloncare.vn',
        '0912345678',
        '$2a$11$DWB1aPn7UKI5iLW6WUMnJeXOd.hcTffjI6jqOYctNovJT0TVyk5j6', -- Owner@123
        @ownerRoleId,
        'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150',
        N'active',
        GETDATE(),
        GETDATE(),
        GETDATE()
    );
END
ELSE
BEGIN
    UPDATE dbo.users 
    SET password_hash = '$2a$11$DWB1aPn7UKI5iLW6WUMnJeXOd.hcTffjI6jqOYctNovJT0TVyk5j6', -- Owner@123
        full_name = N'Trần Ngọc Anh',
        avatar_url = 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150',
        [status] = N'active'
    WHERE username = 'ngocanh';
END

-- 3. Tài khoản Nhân viên Spa - Nguyễn Thị Lan (Mật khẩu: Staff@123)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE username = 'lanktv')
BEGIN
    INSERT INTO dbo.users 
    (
        full_name, username, email, phone, password_hash, role_id, 
        avatar_url, [status], email_verified_at, created_at, updated_at
    )
    VALUES 
    (
        N'Nguyễn Thị Lan',
        'lanktv',
        'lan.nguyen@saloncare.vn',
        '0987654321',
        '$2a$11$Z8gZFwy.NsNSSm9RjYbrAOuf3rAt/L8BWfqQlMrBXxvQqdN43aapW', -- Staff@123
        @staffRoleId,
        'https://images.unsplash.com/photo-1494790108377-be9c29b29330?w=150',
        N'active',
        GETDATE(),
        GETDATE(),
        GETDATE()
    );
END
ELSE
BEGIN
    UPDATE dbo.users 
    SET password_hash = '$2a$11$Z8gZFwy.NsNSSm9RjYbrAOuf3rAt/L8BWfqQlMrBXxvQqdN43aapW', -- Staff@123
        [status] = N'active'
    WHERE username = 'lanktv';
END
GO

PRINT N'================================================================';
PRINT N'==> KHỞI TẠO THÀNH CÔNG ROLES & USERS TRONG DATABASE CRM_V2 <==';
PRINT N'================================================================';
