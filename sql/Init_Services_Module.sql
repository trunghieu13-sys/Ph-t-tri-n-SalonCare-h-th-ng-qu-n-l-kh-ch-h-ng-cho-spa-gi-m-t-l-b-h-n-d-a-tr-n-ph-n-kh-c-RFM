/* =====================================================================================
   HỆ THỐNG QUẢN LÝ SPA SALONCARE CRM V2
   PHÂN HỆ: QUẢN LÝ DANH MỤC DỊCH VỤ (SERVICES & SERVICE_CATEGORIES)
   CSDL: CRM_V2 (SQL Server / T-SQL)
   ===================================================================================== */

USE [CRM_V2];
GO

SET NOCOUNT ON;
GO

-- =====================================================================================
-- 1. BẢNG SERVICE_CATEGORIES (Danh mục nhóm dịch vụ Spa)
-- =====================================================================================
IF OBJECT_ID('dbo.service_categories', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.service_categories (
        category_id         INT IDENTITY(1,1) NOT NULL,
        category_name       NVARCHAR(50) NOT NULL,
        default_icon        VARCHAR(50) NOT NULL CONSTRAINT DF_service_categories_icon DEFAULT ('fa-spa'),
        default_bg_color    VARCHAR(30) NOT NULL CONSTRAINT DF_service_categories_bg DEFAULT ('bg-purple-500'),
        display_order       INT NOT NULL CONSTRAINT DF_service_categories_order DEFAULT (0),
        is_active           BIT NOT NULL CONSTRAINT DF_service_categories_active DEFAULT (1),
        created_at          DATETIME NOT NULL CONSTRAINT DF_service_categories_created DEFAULT (GETDATE()),

        CONSTRAINT PK_service_categories PRIMARY KEY CLUSTERED (category_id ASC),
        CONSTRAINT UQ_service_categories_name UNIQUE (category_name)
    );
END
GO

-- Chèn dữ liệu mẫu cho bảng service_categories
IF NOT EXISTS (SELECT 1 FROM dbo.service_categories)
BEGIN
    INSERT INTO dbo.service_categories (category_name, default_icon, default_bg_color, display_order)
    VALUES
        (N'Chăm sóc da', 'fa-spa', 'bg-purple-500', 1),
        (N'Body Care', 'fa-hands-bubbles', 'bg-pink-500', 2),
        (N'Triệt lông', 'fa-wand-magic-sparkles', 'bg-cyan-500', 3),
        (N'Gội đầu', 'fa-shower', 'bg-emerald-500', 4),
        (N'Trị liệu chuyên sâu', 'fa-heart-pulse', 'bg-amber-500', 5);
END
GO

-- =====================================================================================
-- 2. BẢNG SERVICES (Danh mục dịch vụ)
-- =====================================================================================
IF OBJECT_ID('dbo.services', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.services (
        service_id          INT IDENTITY(1,1) NOT NULL,
        service_name        NVARCHAR(150) NOT NULL,
        category_name       NVARCHAR(50) NOT NULL CONSTRAINT DF_services_category_name DEFAULT (N'Chăm sóc da'),
        duration_minutes    INT NOT NULL CONSTRAINT DF_services_duration DEFAULT (60),
        price               DECIMAL(12,2) NOT NULL CONSTRAINT DF_services_price DEFAULT (0.00),
        description         NVARCHAR(MAX) NULL,
        icon_class          VARCHAR(50) NOT NULL CONSTRAINT DF_services_icon_class DEFAULT ('fa-spa'),
        bg_color            VARCHAR(30) NOT NULL CONSTRAINT DF_services_bg_color DEFAULT ('bg-purple-500'),
        is_active           BIT NOT NULL CONSTRAINT DF_services_is_active DEFAULT (1),
        created_at          DATETIME NOT NULL CONSTRAINT DF_services_created_at DEFAULT (GETDATE()),
        updated_at          DATETIME NOT NULL CONSTRAINT DF_services_updated_at DEFAULT (GETDATE()),

        CONSTRAINT PK_services PRIMARY KEY CLUSTERED (service_id ASC),
        CONSTRAINT CK_services_duration CHECK (duration_minutes > 0),
        CONSTRAINT CK_services_price CHECK (price >= 0)
    );
END
ELSE
BEGIN
    -- Đồng bộ nâng cấp cấu trúc nếu bảng services đã được tạo từ phiên bản trước
    IF COL_LENGTH('dbo.services', 'category_name') IS NULL
    BEGIN
        ALTER TABLE dbo.services ADD category_name NVARCHAR(50) NOT NULL CONSTRAINT DF_services_category_name DEFAULT (N'Chăm sóc da');
        IF COL_LENGTH('dbo.services', 'category') IS NOT NULL
        BEGIN
            EXEC('UPDATE dbo.services SET category_name = ISNULL(category, N''Chăm sóc da'')');
        END
    END

    IF COL_LENGTH('dbo.services', 'icon_class') IS NULL
        ALTER TABLE dbo.services ADD icon_class VARCHAR(50) NOT NULL CONSTRAINT DF_services_icon_class DEFAULT ('fa-spa');

    IF COL_LENGTH('dbo.services', 'bg_color') IS NULL
        ALTER TABLE dbo.services ADD bg_color VARCHAR(30) NOT NULL CONSTRAINT DF_services_bg_color DEFAULT ('bg-purple-500');

    IF COL_LENGTH('dbo.services', 'updated_at') IS NULL
        ALTER TABLE dbo.services ADD updated_at DATETIME NOT NULL CONSTRAINT DF_services_updated_at DEFAULT (GETDATE());

    ALTER TABLE dbo.services ALTER COLUMN description NVARCHAR(MAX) NULL;
END
GO

-- =====================================================================================
-- 3. CHỈ MỤC TỐI ƯU TRUY VẤN (INDEXES)
-- =====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_services_active_category' AND object_id = OBJECT_ID('dbo.services'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_services_active_category
        ON dbo.services (is_active, category_name)
        INCLUDE (service_name, duration_minutes, price, icon_class, bg_color);
END
GO

-- =====================================================================================
-- 4. SEED / ĐỒNG BỘ 4 DỊCH VỤ MẪU CHUẨN THEO HÌNH ẢNH GIAO DIỆN
-- =====================================================================================
-- Ẩn các dịch vụ mẫu dư thừa (nếu có từ trước) để hiển thị chuẩn xác 4 dịch vụ mẫu ban đầu
UPDATE dbo.services SET is_active = 0 WHERE service_id > 4;

IF EXISTS (SELECT 1 FROM dbo.services WHERE service_id = 1)
BEGIN
    UPDATE dbo.services
    SET service_name = N'Chăm sóc da mặt chuyên sâu',
        category_name = N'Chăm sóc da',
        duration_minutes = 60,
        price = 450000.00,
        description = N'Làm sạch sâu 12 bước, hút bã nhờn, đắp mặt nạ tảo biển và chiếu ánh sáng sinh học phục hồi da.',
        icon_class = 'fa-spa',
        bg_color = 'bg-purple-500',
        is_active = 1,
        updated_at = GETDATE()
    WHERE service_id = 1;
END
ELSE
BEGIN
    INSERT INTO dbo.services (service_name, category_name, duration_minutes, price, description, icon_class, bg_color, is_active)
    VALUES (N'Chăm sóc da mặt chuyên sâu', N'Chăm sóc da', 60, 450000.00, N'Làm sạch sâu 12 bước, hút bã nhờn, đắp mặt nạ tảo biển và chiếu ánh sáng sinh học phục hồi da.', 'fa-spa', 'bg-purple-500', 1);
END

IF EXISTS (SELECT 1 FROM dbo.services WHERE service_id = 2)
BEGIN
    UPDATE dbo.services
    SET service_name = N'Massage thư giãn toàn thân',
        category_name = N'Body Care',
        duration_minutes = 90,
        price = 550000.00,
        description = N'Massage đá nóng kết hợp tinh dầu thiên nhiên giúp đả thông kinh lạc và giảm đau mỏi cơ sâu.',
        icon_class = 'fa-hands-bubbles',
        bg_color = 'bg-pink-500',
        is_active = 1,
        updated_at = GETDATE()
    WHERE service_id = 2;
END
ELSE
BEGIN
    INSERT INTO dbo.services (service_name, category_name, duration_minutes, price, description, icon_class, bg_color, is_active)
    VALUES (N'Massage thư giãn toàn thân', N'Body Care', 90, 550000.00, N'Massage đá nóng kết hợp tinh dầu thiên nhiên giúp đả thông kinh lạc và giảm đau mỏi cơ sâu.', 'fa-hands-bubbles', 'bg-pink-500', 1);
END

IF EXISTS (SELECT 1 FROM dbo.services WHERE service_id = 3)
BEGIN
    UPDATE dbo.services
    SET service_name = N'Triệt lông công nghệ IPL',
        category_name = N'Triệt lông',
        duration_minutes = 45,
        price = 800000.00,
        description = N'Triệt lông lạnh Diode Laser/IPL không đau rát, se khít lỗ chân lông và trẻ hóa vùng da điều trị.',
        icon_class = 'fa-wand-magic-sparkles',
        bg_color = 'bg-cyan-500',
        is_active = 1,
        updated_at = GETDATE()
    WHERE service_id = 3;
END
ELSE
BEGIN
    INSERT INTO dbo.services (service_name, category_name, duration_minutes, price, description, icon_class, bg_color, is_active)
    VALUES (N'Triệt lông công nghệ IPL', N'Triệt lông', 45, 800000.00, N'Triệt lông lạnh Diode Laser/IPL không đau rát, se khít lỗ chân lông và trẻ hóa vùng da điều trị.', 'fa-wand-magic-sparkles', 'bg-cyan-500', 1);
END

IF EXISTS (SELECT 1 FROM dbo.services WHERE service_id = 4)
BEGIN
    UPDATE dbo.services
    SET service_name = N'Gội đầu dưỡng sinh Thảo dược',
        category_name = N'Gội đầu',
        duration_minutes = 45,
        price = 250000.00,
        description = N'Gội đầu nước bồ kết thảo dược ấm, bấm huyệt vùng đầu - cổ - vai - gáy giúp ngủ ngon.',
        icon_class = 'fa-shower',
        bg_color = 'bg-emerald-500',
        is_active = 1,
        updated_at = GETDATE()
    WHERE service_id = 4;
END
ELSE
BEGIN
    INSERT INTO dbo.services (service_name, category_name, duration_minutes, price, description, icon_class, bg_color, is_active)
    VALUES (N'Gội đầu dưỡng sinh Thảo dược', N'Gội đầu', 45, 250000.00, N'Gội đầu nước bồ kết thảo dược ấm, bấm huyệt vùng đầu - cổ - vai - gáy giúp ngủ ngon.', 'fa-shower', 'bg-emerald-500', 1);
END
GO

PRINT N'=== Khởi tạo & đồng bộ thành công bảng service_categories và services trong CRM_V2 ===';
GO
