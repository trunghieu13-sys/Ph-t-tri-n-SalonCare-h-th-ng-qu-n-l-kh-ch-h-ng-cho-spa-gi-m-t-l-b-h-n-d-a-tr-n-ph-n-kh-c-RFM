-- ======================================================================================
-- T-SQL SCRIPT: KHỞI TẠO CƠ SỞ DỮ LIỆU & BẢNG CUSTOMERS CHO HỆ THỐNG SALONCARE CRM
-- Tác giả: Senior Database Administrator (DBA)
-- Phiên bản: 1.0 (SQL Server 2016 / 2019 / 2022)
-- Hệ thống: SalonCare CRM Spa Management System
-- ======================================================================================

-- --------------------------------------------------------------------------------------
-- 1. TẠO CƠ SỞ DỮ LIỆU CRM_V2 (NẾU CHƯA TỒN TẠI)
-- --------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'CRM_V2')
BEGIN
    CREATE DATABASE [CRM_V2]
    COLLATE Vietnamese_CI_AS; -- Chuẩn hóa tiếng Việt không phân biệt hoa thường và dấu
    PRINT N'==> Database CRM_V2 đã được tạo thành công với Collation Vietnamese_CI_AS.';
END
ELSE
BEGIN
    PRINT N'==> Database CRM_V2 đã tồn tại sẵn.';
END
GO

USE [CRM_V2];
GO

-- --------------------------------------------------------------------------------------
-- 2. TẠO BẢNG CUSTOMERS VỚI ĐẦY ĐỦ RÀNG BUỘC (CONSTRAINTS)
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.customers', N'U') IS NOT NULL
BEGIN
    PRINT N'==> Bảng dbo.customers đã tồn tại trong CSDL CRM_V2.';
END
ELSE
BEGIN
    CREATE TABLE dbo.customers
    (
        -- ==========================================
        -- NHÓM 1: THÔNG TIN GỐC (CORE INFO)
        -- ==========================================
        customer_id     BIGINT IDENTITY(1,1) NOT NULL,
        full_name       NVARCHAR(100)        NOT NULL,
        phone           VARCHAR(15)          NOT NULL,
        email           VARCHAR(100)         NULL,
        gender          NVARCHAR(20)         NULL,
        date_of_birth   DATE                 NULL,
        [address]       NVARCHAR(255)        NULL,
        avatar_url      VARCHAR(500)         NULL,
        customer_source NVARCHAR(50)         NOT NULL CONSTRAINT DF_customers_customer_source DEFAULT N'Truc_tiep',
        customer_type   NVARCHAR(30)         NOT NULL CONSTRAINT DF_customers_customer_type   DEFAULT N'Moi',
        customer_status NVARCHAR(20)         NOT NULL CONSTRAINT DF_customers_customer_status DEFAULT N'Hoat_dong',
        notes           NVARCHAR(MAX)        NULL,
        created_at      DATETIME             NOT NULL CONSTRAINT DF_customers_created_at      DEFAULT GETDATE(),
        updated_at      DATETIME             NOT NULL CONSTRAINT DF_customers_updated_at      DEFAULT GETDATE(),

        -- ==========================================
        -- NHÓM 2: THÔNG TIN TỔNG HỢP CRM (ANALYTICS)
        -- ==========================================
        last_visit_at   DATETIME             NULL,
        total_visits    INT                  NOT NULL CONSTRAINT DF_customers_total_visits    DEFAULT 0,
        total_spent     DECIMAL(18,2)        NOT NULL CONSTRAINT DF_customers_total_spent     DEFAULT 0.00,
        last_booking_at DATETIME             NULL,
        no_show_count   INT                  NOT NULL CONSTRAINT DF_customers_no_show_count   DEFAULT 0,

        -- ==========================================
        -- KHÓA CHÍNH VÀ RÀNG BUỘC TOÀN VẸN DỮ LIỆU
        -- ==========================================
        CONSTRAINT PK_customers_customer_id PRIMARY KEY CLUSTERED (customer_id ASC),
        CONSTRAINT UQ_customers_phone UNIQUE NONCLUSTERED (phone ASC),
        
        -- Kiểm tra giá trị hợp lệ của Giới tính
        CONSTRAINT CK_customers_gender CHECK (gender IS NULL OR gender IN (N'Nam', N'Nữ', N'Khác')),
        
        -- Kiểm tra phân loại khách hàng
        CONSTRAINT CK_customers_customer_type CHECK (customer_type IN (N'Moi', N'Than_thiet', N'VIP')),
        
        -- Kiểm tra trạng thái hoạt động
        CONSTRAINT CK_customers_customer_status CHECK (customer_status IN (N'Hoat_dong', N'Ngung_hoat_dong')),
        
        -- Kiểm tra các chỉ số tài chính và lịch hẹn không âm
        CONSTRAINT CK_customers_metrics_positive CHECK (total_visits >= 0 AND total_spent >= 0.00 AND no_show_count >= 0)
    );

    PRINT N'==> Tạo bảng dbo.customers thành công với các ràng buộc khóa và kiểm tra dữ liệu.';
END
GO

-- --------------------------------------------------------------------------------------
-- 3. THIẾT LẬP CHỈ MỤC (INDEXES) TỐI ƯU HIỆU NĂNG TÌM KIẾM
-- --------------------------------------------------------------------------------------

-- Index 1: Tìm kiếm theo họ tên khách hàng (Thường xuyên lọc trên thanh tìm kiếm)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = N'IX_customers_full_name' AND object_id = OBJECT_ID(N'dbo.customers'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_customers_full_name
    ON dbo.customers (full_name ASC)
    INCLUDE (phone, customer_type, customer_status, total_spent);
    PRINT N'==> Đã tạo Index IX_customers_full_name (Covering Index phục vụ tìm kiếm nhanh).';
END
GO

-- Index 2: Lọc theo hạng khách hàng và trạng thái (Phân đoạn nhóm khách hàng CRM)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = N'IX_customers_customer_type_status' AND object_id = OBJECT_ID(N'dbo.customers'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_customers_customer_type_status
    ON dbo.customers (customer_type ASC, customer_status ASC)
    INCLUDE (full_name, phone, total_spent, last_visit_at);
    PRINT N'==> Đã tạo Index IX_customers_customer_type_status.';
END
GO

-- Index 3: Truy vấn phân tích khách hàng theo doanh thu chi tiêu (Top VIP Spenders)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = N'IX_customers_total_spent' AND object_id = OBJECT_ID(N'dbo.customers'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_customers_total_spent
    ON dbo.customers (total_spent DESC)
    INCLUDE (full_name, phone, customer_type);
    PRINT N'==> Đã tạo Index IX_customers_total_spent (Hỗ trợ xếp hạng Top chi tiêu).';
END
GO

-- --------------------------------------------------------------------------------------
-- 4. TRIGGER TỰ ĐỘNG CẬP NHẬT UPDATED_AT KHI CÓ THAO TÁC UPDATE
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.trg_customers_update_audit', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER dbo.trg_customers_update_audit;
END
GO

CREATE TRIGGER dbo.trg_customers_update_audit
ON dbo.customers
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Chỉ cập nhật khi không phải bản ghi recursive và có dòng bị thay đổi
    IF NOT UPDATE(updated_at)
    BEGIN
        UPDATE c
        SET c.updated_at = GETDATE()
        FROM dbo.customers c
        INNER JOIN inserted i ON c.customer_id = i.customer_id;
    END
END;
GO
PRINT N'==> Đã tạo Trigger dbo.trg_customers_update_audit tự động làm mới updated_at.';
GO

-- --------------------------------------------------------------------------------------
-- 5. THÊM CHÚ THÍCH SIÊU DỮ LIỆU (EXTENDED PROPERTIES / COLUMN COMMENTS)
-- --------------------------------------------------------------------------------------
-- Chú thích bảng
IF NOT EXISTS (SELECT NULL FROM fn_listextendedproperty(N'MS_Description', N'SCHEMA', N'dbo', N'TABLE', N'customers', NULL, NULL))
BEGIN
    EXEC sys.sp_addextendedproperty 
        @name = N'MS_Description', 
        @value = N'Bảng quản lý hồ sơ và chỉ số hành vi khách hàng cho SalonCare Spa CRM', 
        @level0type = N'SCHEMA', @level0name = N'dbo', 
        @level1type = N'TABLE',  @level1name = N'customers';
END
GO

-- Chú thích các cột chính
DECLARE @colDesc TABLE (colName SYSNAME, descValue NVARCHAR(500));
INSERT INTO @colDesc VALUES
(N'customer_id',     N'Khóa chính tự tăng định danh duy nhất khách hàng'),
(N'full_name',       N'Họ và tên khách hàng (hỗ trợ tiếng Việt có dấu)'),
(N'phone',           N'Số điện thoại liên lạc duy nhất (Unique Key)'),
(N'email',           N'Địa chỉ email dùng cho Email Marketing / Thông báo'),
(N'gender',          N'Giới tính (Nam, Nữ, Khác)'),
(N'date_of_birth',   N'Ngày sinh (dùng để gửi SMS/Email chúc mừng sinh nhật)'),
(N'customer_source', N'Nguồn tiếp cận: Facebook, TikTok, Gioi_thieu, Truc_tiep...'),
(N'customer_type',   N'Phân hạng khách hàng CRM: Moi, Than_thiet, VIP'),
(N'customer_status', N'Trạng thái: Hoat_dong, Ngung_hoat_dong'),
(N'last_visit_at',   N'Ngày giờ khách ghé spa sử dụng dịch vụ gần nhất'),
(N'total_visits',    N'Tổng số lần khách đã hoàn tất dịch vụ tại spa'),
(N'total_spent',     N'Tổng số tiền (VNĐ) khách đã chi trả tích lũy'),
(N'no_show_count',   N'Số lần khách đặt lịch nhưng bùng/không đến (No-show)');

DECLARE @cName SYSNAME, @cDesc NVARCHAR(500);
DECLARE col_cursor CURSOR FOR SELECT colName, descValue FROM @colDesc;
OPEN col_cursor;
FETCH NEXT FROM col_cursor INTO @cName, @cDesc;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT NULL FROM fn_listextendedproperty(N'MS_Description', N'SCHEMA', N'dbo', N'TABLE', N'customers', N'COLUMN', @cName))
    BEGIN
        EXEC sys.sp_addextendedproperty 
            @name = N'MS_Description', @value = @cDesc, 
            @level0type = N'SCHEMA', @level0name = N'dbo', 
            @level1type = N'TABLE',  @level1name = N'customers', 
            @level2type = N'COLUMN', @level2name = @cName;
    END
    FETCH NEXT FROM col_cursor INTO @cName, @cDesc;
END

CLOSE col_cursor;
DEALLOCATE col_cursor;
GO
PRINT N'==> Đã bổ sung đầy đủ Extended Properties (Comments) cho bảng và các cột.';
GO

-- --------------------------------------------------------------------------------------
-- 6. DỮ LIỆU MẪU ĐA DẠNG (MOCK DATA - 5 KHÁCH HÀNG TEST)
-- --------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.customers)
BEGIN
    INSERT INTO dbo.customers 
    (
        full_name, phone, email, gender, date_of_birth, [address], avatar_url,
        customer_source, customer_type, customer_status, notes,
        last_visit_at, total_visits, total_spent, last_booking_at, no_show_count
    )
    VALUES
    (
        N'Lê Thị Mai', 
        '0987654321', 
        'mai.le@gmail.com', 
        N'Nữ', 
        '1992-05-15', 
        N'45/12 Lê Lợi, Phường Bến Nghé, Quận 1, TP. HCM', 
        'https://images.unsplash.com/photo-1544005313-94ddf0286df2?w=150',
        N'Facebook', 
        N'VIP', 
        N'Hoat_dong', 
        N'Khách quen của viện trưởng, thích dịch vụ Chăm sóc da chuyên sâu và Massage đá nóng.',
        DATEADD(DAY, -2, GETDATE()), 
        18, 
        8500000.00, 
        DATEADD(DAY, -2, GETDATE()), 
        0
    ),
    (
        N'Trần Ngọc Anh', 
        '0912345678', 
        'ngocanh.tran@yahoo.com', 
        N'Nữ', 
        '1995-10-22', 
        N'128 Nguyễn Trãi, Phường 3, Quận 5, TP. HCM', 
        'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=150',
        N'TikTok', 
        N'VIP', 
        N'Hoat_dong', 
        N'Thường đặt lịch cuối tuần, dị ứng tinh dầu quế, yêu cầu KTV tay nghề cao.',
        DATEADD(DAY, -5, GETDATE()), 
        12, 
        6200000.00, 
        DATEADD(DAY, -5, GETDATE()), 
        1
    ),
    (
        N'Phạm Thu Hà', 
        '0909876543', 
        'thuha.pham98@gmail.com', 
        N'Nữ', 
        '1998-03-08', 
        N'88 Điện Biên Phủ, Phường Đa Kao, Quận 1, TP. HCM', 
        'https://images.unsplash.com/photo-1517841905240-472988babdf9?w=150',
        N'Gioi_thieu', 
        N'Moi', 
        N'Hoat_dong', 
        N'Khách mới được bạn Mai giới thiệu, quan tâm liệu trình Trị mụn và Triệt lông IPL.',
        DATEADD(DAY, -12, GETDATE()), 
        2, 
        1200000.00, 
        DATEADD(DAY, -12, GETDATE()), 
        0
    ),
    (
        N'Vũ Bảo Trầm', 
        '0987321654', 
        'baotram.vu@outlook.com', 
        N'Nữ', 
        '1990-12-01', 
        N'234 Hoàng Diệu, Phường 6, Quận 4, TP. HCM', 
        'https://images.unsplash.com/photo-1524504388940-b1c1722653e1?w=150',
        N'Truc_tiep', 
        N'Than_thiet', 
        N'Hoat_dong', 
        N'Khách thân thiết văn phòng, chuyên gội đầu dưỡng sinh trưa các ngày trong tuần.',
        DATEADD(DAY, -8, GETDATE()), 
        9, 
        4500000.00, 
        DATEADD(DAY, -8, GETDATE()), 
        0
    ),
    (
        N'Hoàng Minh Châu', 
        '0933112233', 
        'minhchau.hoang@company.vn', 
        N'Nam', 
        '1987-07-19', 
        N'56 Landmark 81, Vinhomes Central Park, Bình Thạnh, TP. HCM', 
        'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=150',
        N'Facebook', 
        N'VIP', 
        N'Hoat_dong', 
        N'Doanh nhân, thường xuyên đi công tác, hay sử dụng gói Massage thư giãn cổ vai gáy.',
        DATEADD(DAY, -15, GETDATE()), 
        14, 
        7100000.00, 
        DATEADD(DAY, -15, GETDATE()), 
        2
    );

    PRINT N'==> Đã nạp thành công 5 bản ghi dữ liệu mẫu kiểm thử!';
END
ELSE
BEGIN
    PRINT N'==> Bảng customers đã có sẵn dữ liệu, bỏ qua bước INSERT mẫu.';
END
GO

-- --------------------------------------------------------------------------------------
-- 7. TRUY VẤN KIỂM TRA DỮ LIỆU SAU KHI KHỞI TẠO
-- --------------------------------------------------------------------------------------
SELECT 
    customer_id,
    full_name,
    phone,
    email,
    gender,
    customer_type,
    customer_status,
    total_visits,
    FORMAT(total_spent, 'N0') + ' đ' AS formatted_total_spent,
    no_show_count,
    CONVERT(VARCHAR(10), created_at, 103) AS ngay_tao,
    CONVERT(VARCHAR(10), updated_at, 103) AS ngay_cap_nhat
FROM dbo.customers
ORDER BY total_spent DESC;
GO
