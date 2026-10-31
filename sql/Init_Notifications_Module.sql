/* =====================================================================================
   HỆ THỐNG QUẢN LÝ SPA SALONCARE CRM V2
   PHÂN HỆ: TRUNG TÂM THÔNG BÁO & TÌM KIẾM TOÀN CỤC (GLOBAL SEARCH & NOTIFICATIONS)
   CSDL: CRM_V2 (SQL Server / T-SQL)
   ===================================================================================== */

USE [CRM_V2];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- =====================================================================================
-- 1. TẠO BẢNG NOTIFICATIONS (NHẬT KÝ THÔNG BÁO HỆ THỐNG & NHẮC VIỆC)
-- =====================================================================================
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'notifications')
BEGIN
    CREATE TABLE dbo.notifications (
        notification_id BIGINT PRIMARY KEY IDENTITY(1,1),
        user_id BIGINT NULL, -- Người nhận thông báo (NULL: gửi toàn bộ nhân viên/quản trị)
        title NVARCHAR(150) NOT NULL, -- Tiêu đề ngắn gọn
        message NVARCHAR(MAX) NOT NULL, -- Nội dung chi tiết
        type VARCHAR(30) NOT NULL, -- APPOINTMENT_REMINDER, BIRTHDAY_REMINDER, SYSTEM, CANCEL_ALERT
        reference_id BIGINT NULL, -- ID đối tượng liên quan (customer_id hoặc appointment_id)
        reference_type VARCHAR(30) NULL, -- CUSTOMER, APPOINTMENT, SERVICE, SYSTEM
        is_read BIT NOT NULL CONSTRAINT DF_notifications_is_read DEFAULT 0, -- 0: Chưa đọc, 1: Đã đọc
        created_at DATETIME NOT NULL CONSTRAINT DF_notifications_created_at DEFAULT GETDATE(),

        CONSTRAINT FK_notifications_users FOREIGN KEY (user_id) 
            REFERENCES dbo.users(user_id) ON DELETE SET NULL
    );
    PRINT N'==> Đã tạo thành công bảng dbo.notifications';
END
ELSE
BEGIN
    PRINT N'==> Bảng dbo.notifications đã tồn tại';
END
GO

-- =====================================================================================
-- 2. TẠO CHỈ MỤC TỐI ƯU TRUY VẤN CHO TRUNG TÂM THÔNG BÁO
-- =====================================================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notifications_user_is_read' AND object_id = OBJECT_ID('dbo.notifications'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_notifications_user_is_read 
    ON dbo.notifications(user_id, is_read, created_at DESC)
    INCLUDE (title, type, reference_id, reference_type);
    PRINT N'==> Đã tạo chỉ mục IX_notifications_user_is_read';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notifications_ref_scan' AND object_id = OBJECT_ID('dbo.notifications'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_notifications_ref_scan 
    ON dbo.notifications(reference_type, reference_id, type, created_at);
    PRINT N'==> Đã tạo chỉ mục IX_notifications_ref_scan';
END
GO

-- =====================================================================================
-- 3. CHÈN DỮ LIỆU THÔNG BÁO MẪU ĐA DẠNG NẾU BẢNG ĐANG TRỐNG
-- =====================================================================================
IF (SELECT COUNT(*) FROM dbo.notifications) = 0
BEGIN
    DECLARE @SampleCustId BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers ORDER BY customer_id ASC);
    DECLARE @SampleApptId BIGINT = (SELECT TOP 1 appointment_id FROM dbo.appointments ORDER BY appointment_id DESC);

    INSERT INTO dbo.notifications (user_id, title, message, type, reference_id, reference_type, is_read, created_at)
    VALUES
    (
        NULL,
        N'Lịch hẹn sắp diễn ra',
        N'Khách hàng Lê Thị Mai có lịch hẹn Chăm sóc da chuyên sâu lúc 16:30 hôm nay. Vui lòng chuẩn bị phòng dịch vụ!',
        'APPOINTMENT_REMINDER',
        @SampleApptId,
        'APPOINTMENT',
        0,
        DATEADD(MINUTE, -15, GETDATE())
    ),
    (
        NULL,
        N'Sinh nhật khách hàng ngày mai',
        N'Khách hàng Trần Ngọc Anh (0912345678) có sinh nhật vào ngày mai. Hãy gửi voucher chúc mừng 20%!',
        'BIRTHDAY_REMINDER',
        @SampleCustId,
        'CUSTOMER',
        0,
        DATEADD(HOUR, -2, GETDATE())
    ),
    (
        NULL,
        N'Cảnh báo hủy lịch hẹn',
        N'Lịch hẹn của khách hàng Phạm Thu Hà vừa bị hủy do khách bận công tác đột xuất.',
        'CANCEL_ALERT',
        @SampleApptId,
        'APPOINTMENT',
        0,
        DATEADD(HOUR, -5, GETDATE())
    ),
    (
        NULL,
        N'Hệ thống SalonCare CRM',
        N'Chào mừng bạn đến với phiên bản CRM V2! Đã kích hoạt tính năng Tìm kiếm Toàn cục (⌘K) và Trung tâm Thông báo tự động.',
        'SYSTEM',
        NULL,
        'SYSTEM',
        1,
        DATEADD(DAY, -1, GETDATE())
    );

    PRINT N'==> Đã chèn 4 thông báo mẫu ban đầu (chưa đọc & đã đọc)';
END
GO

PRINT N'=== KHỞI TẠO BẢNG NOTIFICATIONS & DỮ LIỆU MẪU THÀNH CÔNG ===';
GO
