-- ======================================================================================
-- T-SQL SCRIPT: KHỞI TẠO PHÂN HỆ QUẢN LÝ LỊCH HẸN (APPOINTMENTS & APPOINTMENT_SERVICES)
-- Tác giả: Senior Full-Stack Developer & DBA
-- Cơ sở dữ liệu: CRM_V2 (Microsoft SQL Server)
-- ======================================================================================

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [CRM_V2];
GO

-- --------------------------------------------------------------------------------------
-- 1. BẢNG SERVICES (DANH MỤC DỊCH VỤ SPA - BẢNG THAM CHIẾU CHO APPOINTMENT_SERVICES)
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.services', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.services
    (
        service_id       INT IDENTITY(1,1) NOT NULL,
        service_name     NVARCHAR(150)     NOT NULL,
        category         NVARCHAR(100)     NULL,
        duration_minutes INT               NOT NULL CONSTRAINT DF_services_duration DEFAULT (60),
        price            DECIMAL(12,2)     NOT NULL CONSTRAINT DF_services_price DEFAULT (0.00),
        description      NVARCHAR(500)     NULL,
        is_active        BIT               NOT NULL CONSTRAINT DF_services_is_active DEFAULT (1),
        created_at       DATETIME          NOT NULL CONSTRAINT DF_services_created_at DEFAULT GETDATE(),

        CONSTRAINT PK_services PRIMARY KEY CLUSTERED (service_id ASC)
    );
    PRINT N'==> Bảng dbo.services đã được tạo thành công.';
END
GO

-- Chèn dữ liệu dịch vụ mẫu nếu bảng đang trống
IF NOT EXISTS (SELECT 1 FROM dbo.services)
BEGIN
    INSERT INTO dbo.services (service_name, category, duration_minutes, price, description)
    VALUES
    (N'Chăm sóc da mặt chuyên sâu', N'Chăm sóc da', 60, 450000.00, N'Làm sạch sâu, hút bã nhờn, đắp mặt nạ phục hồi chuyên sâu'),
    (N'Massage thư giãn toàn thân', N'Body & Massage', 90, 550000.00, N'Massage đá nóng kết hợp tinh dầu thiên nhiên giảm đau mỏi'),
    (N'Triệt lông công nghệ IPL', N'Công nghệ cao', 45, 800000.00, N'Triệt lông lạnh không đau rát, bảo hành trọn gói'),
    (N'Gội đầu dưỡng sinh Thảo dược', N'Dưỡng sinh', 60, 250000.00, N'Gội đầu bồ kết thảo mộc kết hợp bấm huyệt cổ vai gáy'),
    (N'Trị mụn chuẩn Y khoa', N'Điều trị da', 75, 650000.00, N'Lấy nhân mụn chuẩn y khoa, chiếu ánh sáng sinh học kháng khuẩn'),
    (N'Tắm trắng phi thuyền Collagen', N'Body & Massage', 90, 1200000.00, N'Ủ trắng tinh chất Collagen tươi kết hợp phi thuyền hồng ngoại');
    PRINT N'==> Đã chèn 6 dịch vụ Spa mẫu vào dbo.services.';
END
GO

-- --------------------------------------------------------------------------------------
-- 2. BẢNG APPOINTMENTS (LỊCH HẸN CHÍNH)
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.appointments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.appointments
    (
        appointment_id      BIGINT IDENTITY(1,1) NOT NULL,
        customer_id         BIGINT               NOT NULL,
        employee_id         BIGINT               NULL,
        appointment_date    DATE                 NOT NULL,
        start_time          TIME(0)              NOT NULL,
        end_time            TIME(0)              NOT NULL,
        [status]            NVARCHAR(30)         NOT NULL CONSTRAINT DF_appointments_status DEFAULT N'Pending',
        booking_source      NVARCHAR(30)         NOT NULL CONSTRAINT DF_appointments_booking_source DEFAULT N'Direct',
        deposit_amount      DECIMAL(12,2)        NOT NULL CONSTRAINT DF_appointments_deposit DEFAULT (0.00),
        total_price         DECIMAL(12,2)        NOT NULL CONSTRAINT DF_appointments_total_price DEFAULT (0.00),
        notes               NVARCHAR(MAX)        NULL,
        confirmed_at        DATETIME             NULL,
        check_in_at         DATETIME             NULL,
        completed_at        DATETIME             NULL,
        cancelled_at        DATETIME             NULL,
        cancellation_reason NVARCHAR(MAX)        NULL,
        created_by          BIGINT               NULL,
        created_at          DATETIME             NOT NULL CONSTRAINT DF_appointments_created_at DEFAULT GETDATE(),
        updated_at          DATETIME             NOT NULL CONSTRAINT DF_appointments_updated_at DEFAULT GETDATE(),

        CONSTRAINT PK_appointments PRIMARY KEY CLUSTERED (appointment_id ASC),
        CONSTRAINT FK_appointments_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(customer_id) ON DELETE NO ACTION ON UPDATE CASCADE,
        CONSTRAINT FK_appointments_employee FOREIGN KEY (employee_id) REFERENCES dbo.users(user_id) ON DELETE NO ACTION,
        CONSTRAINT FK_appointments_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(user_id) ON DELETE NO ACTION,
        CONSTRAINT CK_appointments_status CHECK ([status] IN (N'Pending', N'Confirmed', N'InService', N'Completed', N'NoShow', N'Cancelled')),
        CONSTRAINT CK_appointments_deposit CHECK (deposit_amount >= 0),
        CONSTRAINT CK_appointments_total_price CHECK (total_price >= 0)
    );
    PRINT N'==> Bảng dbo.appointments đã được tạo thành công.';
END
ELSE
BEGIN
    PRINT N'==> Bảng dbo.appointments đã tồn tại sẵn.';
END
GO

-- --------------------------------------------------------------------------------------
-- 3. BẢNG TRUNG GIAN APPOINTMENT_SERVICES (NHIỀU DỊCH VỤ CHO 1 LỊCH HẸN)
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.appointment_services', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.appointment_services
    (
        appointment_id BIGINT        NOT NULL,
        service_id     INT           NOT NULL,
        price          DECIMAL(12,2) NOT NULL CONSTRAINT DF_appointment_services_price DEFAULT (0.00),
        employee_id    BIGINT        NULL,

        CONSTRAINT PK_appointment_services PRIMARY KEY CLUSTERED (appointment_id ASC, service_id ASC),
        CONSTRAINT FK_appointment_services_appointments FOREIGN KEY (appointment_id) REFERENCES dbo.appointments(appointment_id) ON DELETE CASCADE ON UPDATE CASCADE,
        CONSTRAINT FK_appointment_services_services FOREIGN KEY (service_id) REFERENCES dbo.services(service_id) ON DELETE NO ACTION ON UPDATE CASCADE,
        CONSTRAINT FK_appointment_services_employee FOREIGN KEY (employee_id) REFERENCES dbo.users(user_id) ON DELETE NO ACTION
    );
    PRINT N'==> Bảng trung gian dbo.appointment_services đã được tạo thành công.';
END
ELSE
BEGIN
    PRINT N'==> Bảng dbo.appointment_services đã tồn tại sẵn.';
END
GO

-- --------------------------------------------------------------------------------------
-- 4. TẠO CÁC INDEX TỐI ƯU HÓA TÌM KIẾM & LỌC KANBAN
-- --------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_appointments_date_status' AND object_id = OBJECT_ID(N'dbo.appointments'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_appointments_date_status 
    ON dbo.appointments (appointment_date DESC, [status] ASC)
    INCLUDE (customer_id, employee_id, start_time, end_time, total_price, deposit_amount);
    PRINT N'==> Đã tạo Index IX_appointments_date_status.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_appointments_customer_id' AND object_id = OBJECT_ID(N'dbo.appointments'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_appointments_customer_id ON dbo.appointments (customer_id);
    PRINT N'==> Đã tạo Index IX_appointments_customer_id.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_appointments_employee_id' AND object_id = OBJECT_ID(N'dbo.appointments'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_appointments_employee_id ON dbo.appointments (employee_id) WHERE employee_id IS NOT NULL;
    PRINT N'==> Đã tạo Index IX_appointments_employee_id.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_appointment_services_service_id' AND object_id = OBJECT_ID(N'dbo.appointment_services'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_appointment_services_service_id ON dbo.appointment_services (service_id);
    PRINT N'==> Đã tạo Index IX_appointment_services_service_id.';
END
GO

-- --------------------------------------------------------------------------------------
-- 5. TRIGGER TỰ ĐỘNG CẬP NHẬT UPDATED_AT CHO BẢNG APPOINTMENTS
-- --------------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.trg_appointments_update_audit', N'TR') IS NOT NULL
    DROP TRIGGER dbo.trg_appointments_update_audit;
GO

CREATE TRIGGER dbo.trg_appointments_update_audit
ON dbo.appointments
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(updated_at)
    BEGIN
        UPDATE a
        SET a.updated_at = GETDATE()
        FROM dbo.appointments a
        INNER JOIN inserted i ON a.appointment_id = i.appointment_id;
    END
END
GO

-- --------------------------------------------------------------------------------------
-- 6. CHÈN DỮ LIỆU MẪU LỊCH HẸN TRẢI ĐỀU CÁC TRẠNG THÁI KANBAN
-- --------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.appointments)
BEGIN
    DECLARE @cust1 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers ORDER BY customer_id ASC);
    DECLARE @cust2 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE customer_id > @cust1 ORDER BY customer_id ASC);
    DECLARE @cust3 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE customer_id > ISNULL(@cust2, @cust1) ORDER BY customer_id ASC);
    DECLARE @cust4 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers ORDER BY customer_id DESC);

    SET @cust2 = ISNULL(@cust2, @cust1);
    SET @cust3 = ISNULL(@cust3, @cust1);
    SET @cust4 = ISNULL(@cust4, @cust1);

    DECLARE @empStaff BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE username = 'lanktv');
    DECLARE @empOwner BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE username = 'ngocanh');
    SET @empStaff = ISNULL(@empStaff, (SELECT TOP 1 user_id FROM dbo.users));
    SET @empOwner = ISNULL(@empOwner, @empStaff);

    DECLARE @today DATE = CAST(GETDATE() AS DATE);

    -- Lịch hẹn 1: Pending (Chờ xác nhận)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, [status], booking_source, deposit_amount, total_price, notes, created_by)
    VALUES (@cust1, @empStaff, @today, '09:30:00', '11:00:00', N'Pending', N'Facebook', 100000.00, 700000.00, N'Khách đặt qua Fanpage, yêu cầu phòng yên tĩnh', @empOwner);
    DECLARE @app1 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id)
    VALUES (@app1, 1, 450000.00, @empStaff), (@app1, 4, 250000.00, @empStaff);

    -- Lịch hẹn 2: Confirmed (Đã xác nhận)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, [status], booking_source, deposit_amount, total_price, notes, confirmed_at, created_by)
    VALUES (@cust2, @empStaff, @today, '10:30:00', '12:00:00', N'Confirmed', N'Zalo', 200000.00, 550000.00, N'Đã gọi điện xác nhận lịch với khách', DATEADD(HOUR, -2, GETDATE()), @empOwner);
    DECLARE @app2 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id)
    VALUES (@app2, 2, 550000.00, @empStaff);

    -- Lịch hẹn 3: InService (Đang phục vụ)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, [status], booking_source, deposit_amount, total_price, notes, confirmed_at, check_in_at, created_by)
    VALUES (@cust3, @empStaff, @today, '13:30:00', '15:00:00', N'InService', N'Direct', 0.00, 1100000.00, N'Khách đang làm liệu trình tầng 2 - Phòng VIP 1', DATEADD(HOUR, -3, GETDATE()), DATEADD(MINUTE, -25, GETDATE()), @empOwner);
    DECLARE @app3 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id)
    VALUES (@app3, 1, 450000.00, @empStaff), (@app3, 5, 650000.00, @empStaff);

    -- Lịch hẹn 4: Completed (Hoàn thành)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, [status], booking_source, deposit_amount, total_price, notes, confirmed_at, check_in_at, completed_at, created_by)
    VALUES (@cust4, @empStaff, @today, '08:30:00', '09:30:00', N'Completed', N'Website', 300000.00, 800000.00, N'Khách rất hài lòng, hẹn tái khám sau 3 tuần', DATEADD(DAY, -1, GETDATE()), DATEADD(HOUR, -5, GETDATE()), DATEADD(HOUR, -4, GETDATE()), @empOwner);
    DECLARE @app4 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id)
    VALUES (@app4, 3, 800000.00, @empStaff);

    -- Lịch hẹn 5: Cancelled (Đã hủy)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, [status], booking_source, deposit_amount, total_price, notes, cancelled_at, cancellation_reason, created_by)
    VALUES (@cust1, @empStaff, @today, '16:00:00', '17:30:00', N'Cancelled', N'App', 0.00, 1200000.00, N'Đặt qua App di động', DATEADD(MINUTE, -45, GETDATE()), N'Khách báo bận họp đột xuất, xin dời sang cuối tuần', @empOwner);
    DECLARE @app5 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id)
    VALUES (@app5, 6, 1200000.00, @empStaff);

    PRINT N'==> Đã chèn 5 lịch hẹn mẫu kèm dịch vụ chi tiết.';
END
GO

PRINT N'=========================================================================';
PRINT N'==> KHỞI TẠO HOÀN TẤT BẢNG SERVICES, APPOINTMENTS & APPOINTMENT_SERVICES';
PRINT N'=========================================================================';
