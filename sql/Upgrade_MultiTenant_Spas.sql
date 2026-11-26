-- =============================================================================
-- SALONCARE CRM (CRM_V2) - MULTI-TENANT SCHEMA UPGRADE
-- Mục tiêu: Nâng cấp cấu trúc CSDL CRM_V2 sang mô hình Multi-Tenant
--   1. Tạo bảng dbo.spas (Thông tin quán Spa)
--   2. Tạo bảng dbo.spa_invitations (Lời mời nhân viên vào đội ngũ Spa)
--   3. Thêm cột spa_id (BIGINT NULL, FK -> spas(spa_id)) vào:
--      users, customers, services, appointments, audit_logs, notifications, automation_rules
-- =============================================================================

USE CRM_V2;
GO

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =============================================================================
-- 1. TẠO BẢNG dbo.spas (Thông tin quán Spa)
-- =============================================================================
IF OBJECT_ID('dbo.spas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.spas (
        spa_id BIGINT IDENTITY(1,1) NOT NULL,
        spa_name NVARCHAR(150) NOT NULL,
        owner_user_id BIGINT NOT NULL,
        address NVARCHAR(255) NULL,
        phone VARCHAR(15) NULL,
        status VARCHAR(20) NOT NULL CONSTRAINT DF_spas_status DEFAULT ('active'),
        created_at DATETIME NOT NULL CONSTRAINT DF_spas_created_at DEFAULT (GETDATE()),
        CONSTRAINT PK_spas PRIMARY KEY CLUSTERED (spa_id),
        CONSTRAINT FK_spas_owner_user FOREIGN KEY (owner_user_id) REFERENCES dbo.users(user_id)
    );

    CREATE INDEX IX_spas_owner_user_id ON dbo.spas(owner_user_id);
    CREATE INDEX IX_spas_status ON dbo.spas(status);

    PRINT N'Đã tạo bảng dbo.spas thành công.';
END
ELSE
BEGIN
    PRINT N'Bảng dbo.spas đã tồn tại.';
END
GO

-- =============================================================================
-- 2. TẠO BẢNG dbo.spa_invitations (Lời mời nhân viên vào đội ngũ Spa)
-- =============================================================================
IF OBJECT_ID('dbo.spa_invitations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.spa_invitations (
        invitation_id BIGINT IDENTITY(1,1) NOT NULL,
        spa_id BIGINT NOT NULL,
        inviter_user_id BIGINT NOT NULL,
        invitee_user_id BIGINT NOT NULL,
        status VARCHAR(20) NOT NULL CONSTRAINT DF_spa_invitations_status DEFAULT ('Pending'),
        created_at DATETIME NOT NULL CONSTRAINT DF_spa_invitations_created_at DEFAULT (GETDATE()),
        responded_at DATETIME NULL,
        CONSTRAINT PK_spa_invitations PRIMARY KEY CLUSTERED (invitation_id),
        CONSTRAINT FK_spa_invitations_spa FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id),
        CONSTRAINT FK_spa_invitations_inviter FOREIGN KEY (inviter_user_id) REFERENCES dbo.users(user_id),
        CONSTRAINT FK_spa_invitations_invitee FOREIGN KEY (invitee_user_id) REFERENCES dbo.users(user_id),
        CONSTRAINT CK_spa_invitations_status CHECK (status IN ('Pending', 'Accepted', 'Rejected'))
    );

    CREATE INDEX IX_spa_invitations_spa_id ON dbo.spa_invitations(spa_id);
    CREATE INDEX IX_spa_invitations_invitee_status ON dbo.spa_invitations(invitee_user_id, status);

    PRINT N'Đã tạo bảng dbo.spa_invitations thành công.';
END
ELSE
BEGIN
    PRINT N'Bảng dbo.spa_invitations đã tồn tại.';
END
GO

-- =============================================================================
-- 3. CẬP NHẬT BẢNG dbo.users: THÊM CỘT spa_id
-- =============================================================================
IF COL_LENGTH('dbo.users', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.users ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.users.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_users_spas')
BEGIN
    ALTER TABLE dbo.users
    ADD CONSTRAINT FK_users_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_users_spa_id' AND object_id = OBJECT_ID('dbo.users'))
BEGIN
    CREATE INDEX IX_users_spa_id ON dbo.users(spa_id);
END
GO

-- =============================================================================
-- 4. THÊM CỘT spa_id VÀO CÁC BẢNG NGHIỆP VỤ:
--    customers, services, appointments, audit_logs, notifications, automation_rules
-- =============================================================================

-- 4.1. Bảng dbo.customers
IF COL_LENGTH('dbo.customers', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.customers ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.customers.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_customers_spas')
BEGIN
    ALTER TABLE dbo.customers
    ADD CONSTRAINT FK_customers_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_customers_spa_id' AND object_id = OBJECT_ID('dbo.customers'))
BEGIN
    CREATE INDEX IX_customers_spa_id ON dbo.customers(spa_id);
END
GO

-- 4.2. Bảng dbo.services
IF COL_LENGTH('dbo.services', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.services ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.services.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_services_spas')
BEGIN
    ALTER TABLE dbo.services
    ADD CONSTRAINT FK_services_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_services_spa_id' AND object_id = OBJECT_ID('dbo.services'))
BEGIN
    CREATE INDEX IX_services_spa_id ON dbo.services(spa_id);
END
GO

-- 4.3. Bảng dbo.appointments
IF COL_LENGTH('dbo.appointments', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.appointments ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.appointments.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_appointments_spas')
BEGIN
    ALTER TABLE dbo.appointments
    ADD CONSTRAINT FK_appointments_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_appointments_spa_id' AND object_id = OBJECT_ID('dbo.appointments'))
BEGIN
    CREATE INDEX IX_appointments_spa_id ON dbo.appointments(spa_id);
END
GO

-- 4.4. Bảng dbo.audit_logs
IF COL_LENGTH('dbo.audit_logs', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.audit_logs ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.audit_logs.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_audit_logs_spas')
BEGIN
    ALTER TABLE dbo.audit_logs
    ADD CONSTRAINT FK_audit_logs_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_audit_logs_spa_id' AND object_id = OBJECT_ID('dbo.audit_logs'))
BEGIN
    CREATE INDEX IX_audit_logs_spa_id ON dbo.audit_logs(spa_id);
END
GO

-- 4.5. Bảng dbo.notifications
IF COL_LENGTH('dbo.notifications', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.notifications ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.notifications.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_notifications_spas')
BEGIN
    ALTER TABLE dbo.notifications
    ADD CONSTRAINT FK_notifications_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notifications_spa_id' AND object_id = OBJECT_ID('dbo.notifications'))
BEGIN
    CREATE INDEX IX_notifications_spa_id ON dbo.notifications(spa_id);
END
GO

-- 4.6. Bảng dbo.automation_rules
IF COL_LENGTH('dbo.automation_rules', 'spa_id') IS NULL
BEGIN
    ALTER TABLE dbo.automation_rules ADD spa_id BIGINT NULL;
    PRINT N'Đã thêm cột spa_id vào bảng dbo.automation_rules.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_automation_rules_spas')
BEGIN
    ALTER TABLE dbo.automation_rules
    ADD CONSTRAINT FK_automation_rules_spas FOREIGN KEY (spa_id) REFERENCES dbo.spas(spa_id);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_automation_rules_spa_id' AND object_id = OBJECT_ID('dbo.automation_rules'))
BEGIN
    CREATE INDEX IX_automation_rules_spa_id ON dbo.automation_rules(spa_id);
END
GO

PRINT N'=== HOÀN TẤT NÂNG CẤP MULTI-TENANT CHO CSDL CRM_V2 ===';
GO
