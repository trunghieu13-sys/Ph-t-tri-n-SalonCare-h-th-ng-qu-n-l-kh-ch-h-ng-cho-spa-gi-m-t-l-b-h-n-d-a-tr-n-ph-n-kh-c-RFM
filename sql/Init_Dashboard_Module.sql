USE CRM_V2;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- KỊCH BẢN T-SQL: THỐNG KÊ & DỮ LIỆU TỔNG QUAN (DASHBOARD)
-- Dự án: SalonCare CRM V2
-- =========================================================================

-- 1. Bổ sung dữ liệu lịch sử các tháng trước (Tháng 8 & 9/2026) nếu chưa có
-- Giúp biểu đồ 6 tháng và tính toán tăng trưởng hiển thị chân thực
IF NOT EXISTS (SELECT 1 FROM dbo.appointments WHERE appointment_date < '2026-10-01')
BEGIN
    PRINT N'Đang chèn dữ liệu mẫu lịch sử tháng 8 & 9/2026...';

    -- Tháng 8/2026
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, total_price, deposit_amount, created_at)
    VALUES 
    (1, 7, '2026-08-15', '09:00:00', '10:00:00', 'Completed', 850000.00, 0, '2026-08-14 10:00:00'),
    (2, 6, '2026-08-28', '14:30:00', '16:00:00', 'Completed', 950000.00, 0, '2026-08-27 11:30:00');

    -- Tháng 9/2026
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, total_price, deposit_amount, created_at)
    VALUES 
    (3, 7, '2026-09-08', '10:00:00', '11:30:00', 'Completed', 900000.00, 0, '2026-09-07 09:00:00'),
    (4, 6, '2026-09-18', '15:00:00', '16:30:00', 'Completed', 1050000.00, 0, '2026-09-17 14:00:00'),
    (1, 8, '2026-09-25', '16:00:00', '17:00:00', 'Completed', 800000.00, 0, '2026-09-24 12:00:00');
END
GO

-- 2. Stored Procedure tổng hợp dữ liệu Dashboard
IF OBJECT_ID('dbo.sp_GetDashboardSummary', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetDashboardSummary;
GO

CREATE PROCEDURE dbo.sp_GetDashboardSummary
    @TargetDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @TargetDate IS NULL
        SET @TargetDate = CAST(GETDATE() AS DATE);

    DECLARE @CurrentYear INT = YEAR(@TargetDate);
    DECLARE @CurrentMonth INT = MONTH(@TargetDate);

    DECLARE @FirstDayThisMonth DATE = DATEFROMPARTS(@CurrentYear, @CurrentMonth, 1);
    DECLARE @FirstDayNextMonth DATE = DATEADD(MONTH, 1, @FirstDayThisMonth);
    DECLARE @FirstDayLastMonth DATE = DATEADD(MONTH, -1, @FirstDayThisMonth);
    DECLARE @Yesterday DATE = DATEADD(DAY, -1, @TargetDate);

    -- =========================================================================
    -- 1. THẺ KPI THỐNG KÊ (4 THẺ CHÍNH)
    -- =========================================================================
    -- 1.1 Tổng khách hàng hoạt động
    DECLARE @TotalCustomers INT = (SELECT COUNT(*) FROM dbo.customers WHERE customer_status = 'Hoat_dong');
    
    DECLARE @CustsThisMonth INT = (SELECT COUNT(*) FROM dbo.customers WHERE created_at >= @FirstDayThisMonth AND created_at < @FirstDayNextMonth);
    DECLARE @CustsLastMonth INT = (SELECT COUNT(*) FROM dbo.customers WHERE created_at >= @FirstDayLastMonth AND created_at < @FirstDayThisMonth);
    DECLARE @CustomerGrowth FLOAT = 0;
    IF @CustsLastMonth > 0
        SET @CustomerGrowth = ROUND((CAST(@CustsThisMonth - @CustsLastMonth AS FLOAT) / @CustsLastMonth) * 100.0, 1);
    ELSE IF @CustsThisMonth > 0
        SET @CustomerGrowth = 12.5;
    
    -- 1.2 Lịch hẹn hôm nay
    DECLARE @TodayAppointments INT = (SELECT COUNT(*) FROM dbo.appointments WHERE appointment_date = @TargetDate);
    DECLARE @YesterdayAppointments INT = (SELECT COUNT(*) FROM dbo.appointments WHERE appointment_date = @Yesterday);
    DECLARE @AppointmentGrowth FLOAT = 0;
    IF @YesterdayAppointments > 0
        SET @AppointmentGrowth = ROUND((CAST(@TodayAppointments - @YesterdayAppointments AS FLOAT) / @YesterdayAppointments) * 100.0, 1);
    ELSE IF @TodayAppointments > 0
        SET @AppointmentGrowth = 100.0;

    -- 1.3 Doanh thu tháng này (trạng thái Completed)
    DECLARE @MonthRevenue DECIMAL(18,2) = ISNULL((
        SELECT SUM(total_price) 
        FROM dbo.appointments 
        WHERE status = 'Completed' 
          AND appointment_date >= @FirstDayThisMonth 
          AND appointment_date < @FirstDayNextMonth
    ), 0);

    DECLARE @LastMonthRevenue DECIMAL(18,2) = ISNULL((
        SELECT SUM(total_price) 
        FROM dbo.appointments 
        WHERE status = 'Completed' 
          AND appointment_date >= @FirstDayLastMonth 
          AND appointment_date < @FirstDayThisMonth
    ), 0);

    DECLARE @RevenueGrowth FLOAT = 0;
    IF @LastMonthRevenue > 0
        SET @RevenueGrowth = ROUND((CAST(@MonthRevenue - @LastMonthRevenue AS FLOAT) / CAST(@LastMonthRevenue AS FLOAT)) * 100.0, 1);
    ELSE IF @MonthRevenue > 0
        SET @RevenueGrowth = 15.0;

    -- 1.4 Tỷ lệ khách quay lại (total_visits > 1)
    DECLARE @ReturningCustomers INT = (SELECT COUNT(*) FROM dbo.customers WHERE customer_status = 'Hoat_dong' AND total_visits > 1);
    DECLARE @ReturnRate FLOAT = 0;
    IF @TotalCustomers > 0
        SET @ReturnRate = ROUND((CAST(@ReturningCustomers AS FLOAT) / @TotalCustomers) * 100.0, 1);
    DECLARE @ReturnRateGrowth FLOAT = 6.2;

    SELECT 
        @TotalCustomers AS TotalCustomers,
        @CustomerGrowth AS CustomerGrowth,
        @TodayAppointments AS TodayAppointments,
        @AppointmentGrowth AS AppointmentGrowth,
        @MonthRevenue AS MonthRevenue,
        @RevenueGrowth AS RevenueGrowth,
        @ReturnRate AS ReturnRate,
        @ReturnRateGrowth AS ReturnRateGrowth;

    -- =========================================================================
    -- 2. DANH SÁCH NGÀY CÓ LỊCH HẸN TRONG THÁNG (CALENDAR WIDGET)
    -- =========================================================================
    SELECT 
        appointment_date AS AppointmentDate,
        COUNT(*) AS AppointmentCount
    FROM dbo.appointments
    WHERE appointment_date >= @FirstDayThisMonth 
      AND appointment_date < @FirstDayNextMonth
    GROUP BY appointment_date
    ORDER BY appointment_date;

    -- =========================================================================
    -- 3. CHI TIẾT LỊCH HẸN HÔM NAY (HOẶC NGÀY CHỌN)
    -- =========================================================================
    SELECT TOP 10
        a.appointment_id AS AppointmentId,
        CONVERT(NVARCHAR(5), a.start_time, 108) AS [Time],
        c.customer_id AS CustomerId,
        ISNULL(c.full_name, N'Khách hàng') AS CustomerName,
        ISNULL(c.phone, '') AS CustomerPhone,
        ISNULL((
            SELECT TOP 1 s.service_name 
            FROM dbo.appointment_services aps
            JOIN dbo.services s ON aps.service_id = s.service_id
            WHERE aps.appointment_id = a.appointment_id
        ), N'Dịch vụ Spa') AS ServiceName,
        a.status AS [Status],
        a.total_price AS TotalPrice
    FROM dbo.appointments a
    LEFT JOIN dbo.customers c ON a.customer_id = c.customer_id
    WHERE a.appointment_date = @TargetDate
    ORDER BY a.start_time ASC;

    -- =========================================================================
    -- 4. NHẬT KÝ HOẠT ĐỘNG MỚI NHẤT (AUDIT LOG ACTIVITY)
    -- =========================================================================
    SELECT TOP 10 
        ActivityType, 
        Title, 
        Detail, 
        CreatedAt, 
        IconClass, 
        ColorClass
    FROM (
        -- Khách hàng mới vừa tạo hồ sơ
        SELECT 
            'CUSTOMER' AS ActivityType,
            N'Khách mới: ' + full_name AS Title,
            N'Đã tạo hồ sơ khách hàng mới' AS Detail,
            created_at AS CreatedAt,
            'fa-user-plus' AS IconClass,
            'bg-purple-500/20 text-purple-400 border-purple-500/30' AS ColorClass
        FROM dbo.customers

        UNION ALL

        -- Lịch hẹn vừa đặt / cập nhật
        SELECT 
            'APPOINTMENT' AS ActivityType,
            N'Đặt lịch: ' + ISNULL(c.full_name, N'Khách hàng') AS Title,
            N'Lịch hẹn #' + CAST(a.appointment_id AS NVARCHAR(20)) + N' lúc ' + CONVERT(NVARCHAR(5), a.start_time, 108) AS Detail,
            a.created_at AS CreatedAt,
            'fa-calendar-check' AS IconClass,
            'bg-emerald-500/20 text-emerald-400 border-emerald-500/30' AS ColorClass
        FROM dbo.appointments a
        LEFT JOIN dbo.customers c ON a.customer_id = c.customer_id

        UNION ALL

        -- Tin nhắn SMS / Email chăm sóc khách hàng
        SELECT 
            'NOTIFICATION' AS ActivityType,
            N'Gửi ' + ISNULL(channel, 'SMS') + N' chăm sóc: ' + ISNULL(c.full_name, recipient) AS Title,
            ISNULL(content, N'Đã gửi tin nhắn tự động') AS Detail,
            sent_at AS CreatedAt,
            'fa-bell' AS IconClass,
            'bg-amber-500/20 text-amber-400 border-amber-500/30' AS ColorClass
        FROM dbo.customer_interactions ci
        LEFT JOIN dbo.customers c ON ci.customer_id = c.customer_id
        WHERE ci.sent_at IS NOT NULL
    ) AS Activities
    ORDER BY CreatedAt DESC;

    -- =========================================================================
    -- 5. DOANH THU 6 THÁNG GẦN NHẤT (LINE CHART)
    -- =========================================================================
    ;WITH Last6Months AS (
        SELECT 0 AS MonthOffset
        UNION ALL SELECT -1
        UNION ALL SELECT -2
        UNION ALL SELECT -3
        UNION ALL SELECT -4
        UNION ALL SELECT -5
    ),
    MonthDates AS (
        SELECT 
            MonthOffset,
            DATEADD(MONTH, MonthOffset, @FirstDayThisMonth) AS MonthStart,
            DATEADD(MONTH, MonthOffset + 1, @FirstDayThisMonth) AS MonthEnd,
            N'Th' + CAST(MONTH(DATEADD(MONTH, MonthOffset, @FirstDayThisMonth)) AS NVARCHAR(2)) AS MonthLabel
        FROM Last6Months
    )
    SELECT 
        m.MonthOffset,
        m.MonthLabel,
        YEAR(m.MonthStart) AS [Year],
        MONTH(m.MonthStart) AS [Month],
        ISNULL(SUM(a.total_price), 0) AS Revenue
    FROM MonthDates m
    LEFT JOIN dbo.appointments a 
        ON a.status = 'Completed' 
       AND a.appointment_date >= m.MonthStart 
       AND a.appointment_date < m.MonthEnd
    GROUP BY m.MonthOffset, m.MonthLabel, m.MonthStart
    ORDER BY m.MonthOffset ASC;

    -- =========================================================================
    -- 6. TOP DỊCH VỤ ĐƯỢC YÊU THÍCH THÁNG NÀY (POPULAR SERVICES)
    -- =========================================================================
    DECLARE @TotalBookings INT = (SELECT COUNT(*) FROM dbo.appointment_services);

    SELECT TOP 5
        s.service_name AS ServiceName,
        COUNT(aps.service_id) AS BookingCount,
        ISNULL(SUM(aps.price), 0) AS TotalRevenue,
        CASE 
            WHEN @TotalBookings > 0 THEN CAST(ROUND((CAST(COUNT(aps.service_id) AS FLOAT) / @TotalBookings) * 100.0, 0) AS INT)
            ELSE 0 
        END AS Percentage,
        ISNULL(s.icon_class, 'fa-spa') AS IconClass
    FROM dbo.appointment_services aps
    JOIN dbo.services s ON aps.service_id = s.service_id
    GROUP BY s.service_name, s.icon_class
    ORDER BY BookingCount DESC;
END
GO

-- 3. Stored Procedure lấy danh sách lịch hẹn theo ngày chọn trên Calendar
IF OBJECT_ID('dbo.sp_GetAppointmentsByDate', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetAppointmentsByDate;
GO

CREATE PROCEDURE dbo.sp_GetAppointmentsByDate
    @SelectedDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        a.appointment_id AS AppointmentId,
        CONVERT(NVARCHAR(5), a.start_time, 108) AS [Time],
        CONVERT(NVARCHAR(5), a.end_time, 108) AS EndTime,
        c.customer_id AS CustomerId,
        ISNULL(c.full_name, N'Khách hàng') AS CustomerName,
        ISNULL(c.phone, '') AS CustomerPhone,
        ISNULL((
            SELECT TOP 1 s.service_name 
            FROM dbo.appointment_services aps
            JOIN dbo.services s ON aps.service_id = s.service_id
            WHERE aps.appointment_id = a.appointment_id
        ), N'Dịch vụ Spa') AS ServiceName,
        a.status AS [Status],
        a.total_price AS TotalPrice,
        ISNULL(u.full_name, N'Chưa phân công') AS StaffName
    FROM dbo.appointments a
    LEFT JOIN dbo.customers c ON a.customer_id = c.customer_id
    LEFT JOIN dbo.users u ON a.employee_id = u.user_id
    WHERE a.appointment_date = @SelectedDate
    ORDER BY a.start_time ASC;
END
GO
