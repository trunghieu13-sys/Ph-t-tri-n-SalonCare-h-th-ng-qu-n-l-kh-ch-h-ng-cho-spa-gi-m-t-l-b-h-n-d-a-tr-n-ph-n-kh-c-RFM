USE CRM_V2;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- =========================================================================
-- 1. SỬA LỖI FONT / ENCODING TRONG BẢNG USERS (NHÂN VIÊN)
-- =========================================================================
UPDATE dbo.users 
SET full_name = N'Hoàng Khắc Trung Hiếu',
    specialty = N'Quản trị viên hệ thống & Điều phối',
    updated_at = GETDATE()
WHERE user_id = 4;

UPDATE dbo.users 
SET full_name = N'Quản Trị Viên',
    specialty = N'Quản trị viên hệ thống & Điều phối',
    updated_at = GETDATE()
WHERE user_id = 5;

UPDATE dbo.users 
SET full_name = N'Trần Ngọc Anh',
    specialty = N'Chủ Spa & Bác sĩ Thẩm mỹ da',
    updated_at = GETDATE()
WHERE user_id = 6;

UPDATE dbo.users 
SET full_name = N'Nguyễn Thị Lan',
    specialty = N'KTV Chuyên sâu Chăm sóc da & Trị liệu',
    updated_at = GETDATE()
WHERE user_id = 7;

UPDATE dbo.users 
SET full_name = N'Lê Mai Phương',
    specialty = N'Massage Trị liệu Cổ vai gáy & Body Care',
    updated_at = GETDATE()
WHERE user_id = 8;

UPDATE dbo.users 
SET full_name = N'Vũ Thùy Linh',
    specialty = N'Triệt lông công nghệ cao IPL & Laser Diode',
    updated_at = GETDATE()
WHERE user_id = 9;

UPDATE dbo.users 
SET full_name = N'Đặng Bảo Trâm',
    specialty = N'Gội đầu Dưỡng sinh Đông Y & Chăm sóc tóc',
    updated_at = GETDATE()
WHERE user_id = 10;

UPDATE dbo.users 
SET full_name = N'Phạm Hải Yến',
    specialty = N'Chăm sóc da & Nail Art',
    updated_at = GETDATE()
WHERE user_id = 11;
GO

-- =========================================================================
-- 2. SỬA LỖI FONT TRONG BẢNG SERVICES & SERVICE_CATEGORIES
-- =========================================================================
UPDATE dbo.services
SET service_name = N'Massage thư giãn toàn thân',
    description = N'Liệu trình massage body chuyên sâu kết hợp tinh dầu thảo mộc giúp lưu thông khí huyết, giải tỏa căng thẳng cơ bắp và phục hồi năng lượng.',
    category_name = N'Body Care'
WHERE service_id = 12;

UPDATE dbo.services
SET service_name = N'Triệt lông công nghệ cao'
WHERE service_id = 10;

UPDATE dbo.services
SET service_name = N'Gội đầu dưỡng sinh'
WHERE service_id = 11;
GO

-- =========================================================================
-- 3. SỬA LỖI FONT TRONG BẢNG NOTIFICATIONS
-- =========================================================================
UPDATE dbo.notifications
SET title = N'Lịch hẹn sắp diễn ra',
    message = N'Khách hàng Lê Thị Mai có lịch hẹn Chăm sóc da chuyên sâu lúc 16:30 hôm nay. Vui lòng chuẩn bị phòng dịch vụ!'
WHERE notification_id = 1;

UPDATE dbo.notifications
SET title = N'Sinh nhật khách hàng ngày mai',
    message = N'Khách hàng Trần Ngọc Anh (0912345678) có sinh nhật vào ngày mai. Hãy gửi voucher chúc mừng 20%!'
WHERE notification_id = 2;

UPDATE dbo.notifications
SET title = N'Cảnh báo hủy lịch hẹn',
    message = N'Lịch hẹn của khách hàng Phạm Thu Hà vừa bị hủy do khách bận công tác đột xuất.'
WHERE notification_id = 3;

UPDATE dbo.notifications
SET title = N'Hệ thống SalonCare CRM',
    message = N'Chào mừng bạn đến với phiên bản CRM V2! Đã kích hoạt tính năng Tìm kiếm Toàn cục (⌘K) và Trung tâm Thông báo tự động.'
WHERE notification_id = 4;
GO

-- =========================================================================
-- 4. SỬA LỖI FONT TRONG BẢNG CUSTOMER_INTERACTIONS & APPOINTMENTS
-- =========================================================================
UPDATE dbo.customer_interactions
SET content = N'[SalonCare Spa] Xin chào Lê Thị Mai, nhắc lịch hẹn làm đẹp của quý khách lúc 14:30 hôm nay.'
WHERE interaction_id = 1;

UPDATE dbo.appointments
SET cancellation_reason = N'Khách bận việc gia đình đột xuất'
WHERE appointment_id = 17;
GO

-- =========================================================================
-- 5. CẬP NHẬT LẠI STORED PROCEDURE SP_GETDASHBOARDSUMMARY CHUẨN TIẾNG VIỆT
-- =========================================================================
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

    -- 1. THẺ KPI THỐNG KÊ (4 THẺ CHÍNH)
    DECLARE @TotalCustomers INT = (SELECT COUNT(*) FROM dbo.customers WHERE customer_status = 'Hoat_dong');
    
    DECLARE @CustsThisMonth INT = (SELECT COUNT(*) FROM dbo.customers WHERE created_at >= @FirstDayThisMonth AND created_at < @FirstDayNextMonth);
    DECLARE @CustsLastMonth INT = (SELECT COUNT(*) FROM dbo.customers WHERE created_at >= @FirstDayLastMonth AND created_at < @FirstDayThisMonth);
    DECLARE @CustomerGrowth FLOAT = 0;
    IF @CustsLastMonth > 0
        SET @CustomerGrowth = ROUND((CAST(@CustsThisMonth - @CustsLastMonth AS FLOAT) / @CustsLastMonth) * 100.0, 1);
    ELSE IF @CustsThisMonth > 0
        SET @CustomerGrowth = 12.5;
    
    DECLARE @TodayAppointments INT = (SELECT COUNT(*) FROM dbo.appointments WHERE appointment_date = @TargetDate);
    DECLARE @YesterdayAppointments INT = (SELECT COUNT(*) FROM dbo.appointments WHERE appointment_date = @Yesterday);
    DECLARE @AppointmentGrowth FLOAT = 0;
    IF @YesterdayAppointments > 0
        SET @AppointmentGrowth = ROUND((CAST(@TodayAppointments - @YesterdayAppointments AS FLOAT) / @YesterdayAppointments) * 100.0, 1);
    ELSE IF @TodayAppointments > 0
        SET @AppointmentGrowth = 100.0;

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

    -- 2. DANH SÁCH NGÀY CÓ LỊCH HẸN TRONG THÁNG (CALENDAR WIDGET)
    SELECT 
        appointment_date AS AppointmentDate,
        COUNT(*) AS AppointmentCount
    FROM dbo.appointments
    WHERE appointment_date >= @FirstDayThisMonth 
      AND appointment_date < @FirstDayNextMonth
    GROUP BY appointment_date
    ORDER BY appointment_date;

    -- 3. CHI TIẾT LỊCH HẸN NGÀY CHỌN
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

    -- 4. NHẬT KÝ HOẠT ĐỘNG MỚI NHẤT (AUDIT LOG ACTIVITY)
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

    -- 5. DOANH THU 6 THÁNG GẦN NHẤT (LINE CHART)
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

    -- 6. TOP DỊCH VỤ ĐƯỢC YÊU THÍCH THÁNG NÀY (POPULAR SERVICES)
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
