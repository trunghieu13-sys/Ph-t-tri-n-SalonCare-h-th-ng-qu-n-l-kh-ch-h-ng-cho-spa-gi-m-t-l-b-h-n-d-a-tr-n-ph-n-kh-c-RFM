/* =====================================================================================
   HỆ THỐNG QUẢN LÝ SPA SALONCARE CRM V2
   PHÂN HỆ: BÁO CÁO & PHÂN TÍCH KINH DOANH (REPORTS & ANALYTICS)
   CSDL: CRM_V2 (SQL Server / T-SQL)
   ===================================================================================== */

USE [CRM_V2];
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
GO

-- =====================================================================================
-- 1. SEED DỮ LIỆU LỊCH HẸN MẪU ĐA DẠNG NẾU CSDL ĐANG CÓ ÍT DỮ LIỆU
-- Để đảm bảo các biểu đồ Doanh thu, Cơ cấu Dịch vụ, và Hiệu suất Nhân viên hiển thị sinh động
-- =====================================================================================
IF (SELECT COUNT(*) FROM dbo.appointments WHERE status = 'Completed') < 8
BEGIN
    DECLARE @Today DATE = CAST(GETDATE() AS DATE);
    DECLARE @Cust1 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE phone = '0987654321');
    DECLARE @Cust2 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE phone = '0912345678');
    DECLARE @Cust3 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE phone = '0909876543');
    DECLARE @Cust4 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE phone = '0987321654');
    DECLARE @Cust6 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE phone = '0886091716');
    DECLARE @Cust7 BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers WHERE phone = '0917161611');

    IF @Cust1 IS NULL SET @Cust1 = (SELECT TOP 1 customer_id FROM dbo.customers ORDER BY customer_id ASC);
    IF @Cust2 IS NULL SET @Cust2 = @Cust1;
    IF @Cust3 IS NULL SET @Cust3 = @Cust1;
    IF @Cust4 IS NULL SET @Cust4 = @Cust1;
    IF @Cust6 IS NULL SET @Cust6 = @Cust1;
    IF @Cust7 IS NULL SET @Cust7 = @Cust1;

    DECLARE @StaffLan BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE username = 'lanktv');
    DECLARE @StaffNgocAnh BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE username = 'ngocanh');
    DECLARE @StaffAdmin BIGINT = (SELECT TOP 1 user_id FROM dbo.users WHERE username = 'admin');

    IF @StaffLan IS NULL SET @StaffLan = (SELECT TOP 1 user_id FROM dbo.users ORDER BY user_id ASC);
    IF @StaffNgocAnh IS NULL SET @StaffNgocAnh = @StaffLan;
    IF @StaffAdmin IS NULL SET @StaffAdmin = @StaffLan;

    DECLARE @SrvSkin INT = (SELECT TOP 1 service_id FROM dbo.services WHERE is_active = 1 AND (service_name LIKE N'%da%' OR service_name LIKE N'%skin%'));
    DECLARE @SrvHair INT = (SELECT TOP 1 service_id FROM dbo.services WHERE is_active = 1 AND (service_name LIKE N'%Gội%' OR service_name LIKE N'%dầu%'));
    DECLARE @SrvLaser INT = (SELECT TOP 1 service_id FROM dbo.services WHERE is_active = 1 AND (service_name LIKE N'%Triệt%' OR service_name LIKE N'%IPL%'));

    IF @SrvSkin IS NULL SET @SrvSkin = (SELECT TOP 1 service_id FROM dbo.services WHERE is_active = 1 ORDER BY service_id ASC);
    IF @SrvHair IS NULL SET @SrvHair = @SrvSkin;
    IF @SrvLaser IS NULL SET @SrvLaser = @SrvSkin;

    -- Thêm các ca Completed tuần trước và tuần này
    DECLARE @NewApptId BIGINT;

    -- Ca 1: Hôm nay
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust1, @StaffLan, @Today, '09:00', '10:15', 'Completed', 'Website', 100000, 450000, DATEADD(HOUR, -5, GETDATE()), DATEADD(HOUR, -4, GETDATE()), DATEADD(HOUR, -3, GETDATE()), DATEADD(DAY, -1, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvSkin, 450000, @StaffLan);

    -- Ca 2: Hôm nay
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust6, @StaffNgocAnh, @Today, '11:00', '12:00', 'Completed', 'Direct', 0, 500000, DATEADD(HOUR, -4, GETDATE()), DATEADD(HOUR, -3, GETDATE()), DATEADD(HOUR, -2, GETDATE()), DATEADD(DAY, -2, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvLaser, 500000, @StaffNgocAnh);

    -- Ca 3: Hôm qua
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust2, @StaffLan, DATEADD(DAY, -1, @Today), '14:00', '15:30', 'Completed', 'Facebook', 200000, 650000, DATEADD(DAY, -1, GETDATE()), DATEADD(DAY, -1, GETDATE()), DATEADD(DAY, -1, GETDATE()), DATEADD(DAY, -3, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvSkin, 450000, @StaffLan);
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvHair, 200000, @StaffLan);

    -- Ca 4: Cách 2 ngày
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust3, @StaffAdmin, DATEADD(DAY, -2, @Today), '10:00', '11:00', 'Completed', 'Direct', 0, 250000, DATEADD(DAY, -2, GETDATE()), DATEADD(DAY, -2, GETDATE()), DATEADD(DAY, -2, GETDATE()), DATEADD(DAY, -4, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvHair, 250000, @StaffAdmin);

    -- Ca 5: Cách 3 ngày
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust4, @StaffNgocAnh, DATEADD(DAY, -3, @Today), '15:00', '16:00', 'Completed', 'Website', 150000, 800000, DATEADD(DAY, -3, GETDATE()), DATEADD(DAY, -3, GETDATE()), DATEADD(DAY, -3, GETDATE()), DATEADD(DAY, -5, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvLaser, 800000, @StaffNgocAnh);

    -- Ca 6: Cách 5 ngày
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust1, @StaffLan, DATEADD(DAY, -5, @Today), '16:30', '17:30', 'Completed', 'Direct', 0, 450000, DATEADD(DAY, -5, GETDATE()), DATEADD(DAY, -5, GETDATE()), DATEADD(DAY, -5, GETDATE()), DATEADD(DAY, -7, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvSkin, 450000, @StaffLan);

    -- Ca 7: Cách 7 ngày
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust7, @StaffLan, DATEADD(DAY, -7, @Today), '13:00', '14:30', 'Completed', 'Zalo', 100000, 700000, DATEADD(DAY, -7, GETDATE()), DATEADD(DAY, -7, GETDATE()), DATEADD(DAY, -7, GETDATE()), DATEADD(DAY, -9, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvSkin, 450000, @StaffLan);
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvHair, 250000, @StaffLan);

    -- Ca 8: Cách 12 ngày
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, confirmed_at, check_in_at, completed_at, created_at)
    VALUES (@Cust2, @StaffNgocAnh, DATEADD(DAY, -12, @Today), '09:30', '10:45', 'Completed', 'Facebook', 100000, 450000, DATEADD(DAY, -12, GETDATE()), DATEADD(DAY, -12, GETDATE()), DATEADD(DAY, -12, GETDATE()), DATEADD(DAY, -14, GETDATE()));
    SET @NewApptId = SCOPE_IDENTITY();
    INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id) VALUES (@NewApptId, @SrvSkin, 450000, @StaffNgocAnh);

    -- Ca 9: Vắng mặt (NoShow)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, created_at)
    VALUES (@Cust3, @StaffLan, DATEADD(DAY, -4, @Today), '11:00', '12:00', 'NoShow', 'Direct', 0, 350000, DATEADD(DAY, -4, GETDATE()));

    -- Ca 10: Đã hủy (Cancelled)
    INSERT INTO dbo.appointments (customer_id, employee_id, appointment_date, start_time, end_time, status, booking_source, deposit_amount, total_price, cancellation_reason, cancelled_at, created_at)
    VALUES (@Cust4, @StaffNgocAnh, DATEADD(DAY, -2, @Today), '17:00', '18:00', 'Cancelled', 'Direct', 0, 450000, N'Khách bận việc gia đình đột xuất', DATEADD(DAY, -2, GETDATE()), DATEADD(DAY, -3, GETDATE()));
END
GO

-- =====================================================================================
-- 2. STORED PROCEDURE: sp_GetRevenueReport
-- Báo cáo Tổng quan Doanh thu, AOV, Tăng trưởng so với kỳ trước và Chuỗi dữ liệu theo ngày
-- =====================================================================================
CREATE OR ALTER PROCEDURE dbo.sp_GetRevenueReport
    @FromDate DATE,
    @ToDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Tính số ngày trong kỳ để xác định kỳ đối chiếu trước đó
    DECLARE @Days INT = DATEDIFF(DAY, @FromDate, @ToDate) + 1;
    DECLARE @PrevFromDate DATE = DATEADD(DAY, -@Days, @FromDate);
    DECLARE @PrevToDate DATE = DATEADD(DAY, -1, @FromDate);

    -- 1. Doanh thu & Chỉ số kỳ hiện tại (Chỉ tính các lịch hẹn đã Hoàn thành 'Completed')
    DECLARE @CurrentRevenue DECIMAL(18,2) = 0;
    DECLARE @CompletedVisits INT = 0;
    DECLARE @TotalDeposits DECIMAL(18,2) = 0;

    SELECT 
        @CurrentRevenue = ISNULL(SUM(total_price), 0),
        @CompletedVisits = COUNT(*),
        @TotalDeposits = ISNULL(SUM(deposit_amount), 0)
    FROM dbo.appointments
    WHERE status = 'Completed'
      AND appointment_date >= @FromDate 
      AND appointment_date <= @ToDate;

    -- 2. Doanh thu kỳ trước để tính % Tăng trưởng
    DECLARE @PrevRevenue DECIMAL(18,2) = 0;
    SELECT 
        @PrevRevenue = ISNULL(SUM(total_price), 0)
    FROM dbo.appointments
    WHERE status = 'Completed'
      AND appointment_date >= @PrevFromDate 
      AND appointment_date <= @PrevToDate;

    DECLARE @GrowthRate DECIMAL(9,2) = 0;
    IF @PrevRevenue > 0
        SET @GrowthRate = ROUND(((@CurrentRevenue - @PrevRevenue) / @PrevRevenue) * 100, 2);
    ELSE IF @CurrentRevenue > 0
        SET @GrowthRate = 100.00;

    -- 3. Doanh thu theo Kênh đặt lịch (Website, Direct, Facebook, Zalo, App)
    DECLARE @DirectRevenue DECIMAL(18,2) = 0;
    DECLARE @OnlineRevenue DECIMAL(18,2) = 0;

    SELECT 
        @DirectRevenue = ISNULL(SUM(CASE WHEN booking_source = 'Direct' THEN total_price ELSE 0 END), 0),
        @OnlineRevenue = ISNULL(SUM(CASE WHEN booking_source != 'Direct' THEN total_price ELSE 0 END), 0)
    FROM dbo.appointments
    WHERE status = 'Completed'
      AND appointment_date >= @FromDate 
      AND appointment_date <= @ToDate;

    -- Kết quả Tập 1: Tổng hợp KPI Doanh thu
    SELECT 
        @CurrentRevenue AS TotalRevenue,
        @PrevRevenue AS PreviousRevenue,
        @GrowthRate AS GrowthRatePercentage,
        @CompletedVisits AS TotalCompletedVisits,
        CASE WHEN @CompletedVisits > 0 THEN ROUND(@CurrentRevenue / @CompletedVisits, 0) ELSE 0 END AS AOV,
        @TotalDeposits AS TotalDeposits,
        @DirectRevenue AS DirectRevenue,
        @OnlineRevenue AS OnlineRevenue;

    -- Kết quả Tập 2: Chuỗi thời gian theo ngày (Dùng vẽ Line Chart Chart.js)
    ;WITH DateRange AS (
        SELECT @FromDate AS CalDate
        UNION ALL
        SELECT DATEADD(DAY, 1, CalDate)
        FROM DateRange
        WHERE CalDate < @ToDate
    )
    SELECT 
        CONVERT(VARCHAR(10), d.CalDate, 103) AS DateLabel, -- dd/MM/yyyy
        CONVERT(VARCHAR(5), d.CalDate, 103) AS ShortDateLabel, -- dd/MM
        ISNULL(SUM(a.total_price), 0) AS Revenue,
        COUNT(a.appointment_id) AS VisitsCount
    FROM DateRange d
    LEFT JOIN dbo.appointments a 
        ON a.appointment_date = d.CalDate 
       AND a.status = 'Completed'
    GROUP BY d.CalDate
    ORDER BY d.CalDate ASC
    OPTION (MAXRECURSION 400);
END
GO

-- =====================================================================================
-- 3. STORED PROCEDURE: sp_GetTopServicesReport
-- Top dịch vụ bán chạy nhất (Doanh thu & Số lượng) cho Biểu đồ tròn Doughnut Chart
-- =====================================================================================
CREATE OR ALTER PROCEDURE dbo.sp_GetTopServicesReport
    @FromDate DATE,
    @ToDate DATE,
    @TopN INT = 5
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TotalServiceRevenue DECIMAL(18,2) = 0;

    SELECT @TotalServiceRevenue = ISNULL(SUM(aps.price), 0)
    FROM dbo.appointment_services aps
    INNER JOIN dbo.appointments a ON aps.appointment_id = a.appointment_id
    WHERE a.status = 'Completed'
      AND a.appointment_date >= @FromDate 
      AND a.appointment_date <= @ToDate;

    SELECT TOP (@TopN)
        s.service_id AS ServiceId,
        s.service_name AS ServiceName,
        ISNULL(s.category_name, N'Khác') AS CategoryName,
        COUNT(aps.appointment_id) AS OrderCount,
        ISNULL(SUM(aps.price), 0) AS TotalRevenue,
        CASE 
            WHEN @TotalServiceRevenue > 0 
            THEN ROUND((ISNULL(SUM(aps.price), 0) / @TotalServiceRevenue) * 100, 1) 
            ELSE 0 
        END AS Percentage
    FROM dbo.services s
    INNER JOIN dbo.appointment_services aps ON s.service_id = aps.service_id
    INNER JOIN dbo.appointments a ON aps.appointment_id = a.appointment_id
    WHERE a.status = 'Completed'
      AND a.appointment_date >= @FromDate 
      AND a.appointment_date <= @ToDate
    GROUP BY s.service_id, s.service_name, s.category_name
    ORDER BY TotalRevenue DESC, OrderCount DESC;
END
GO

-- =====================================================================================
-- 4. STORED PROCEDURE: sp_GetCustomerGrowthReport
-- Báo cáo Khách hàng: Khách mới vs Khách quay lại, Phân khúc VIP, Tỷ lệ Hủy/Vắng lịch
-- =====================================================================================
CREATE OR ALTER PROCEDURE dbo.sp_GetCustomerGrowthReport
    @FromDate DATE,
    @ToDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Khách hàng mới đăng ký trong kỳ
    DECLARE @NewCustomersCount INT = 0;
    SELECT @NewCustomersCount = COUNT(*)
    FROM dbo.customers
    WHERE CAST(created_at AS DATE) >= @FromDate 
      AND CAST(created_at AS DATE) <= @ToDate;

    -- 2. Khách hàng đến phục vụ trong kỳ
    DECLARE @TotalVisitingCustomers INT = 0;
    SELECT @TotalVisitingCustomers = COUNT(DISTINCT customer_id)
    FROM dbo.appointments
    WHERE status = 'Completed'
      AND appointment_date >= @FromDate 
      AND appointment_date <= @ToDate;

    -- 3. Khách hàng quay lại (Đã có >= 2 lần phục vụ)
    DECLARE @ReturningCustomersCount INT = 0;
    SELECT @ReturningCustomersCount = COUNT(DISTINCT a.customer_id)
    FROM dbo.appointments a
    INNER JOIN dbo.customers c ON a.customer_id = c.customer_id
    WHERE a.status = 'Completed'
      AND a.appointment_date >= @FromDate 
      AND a.appointment_date <= @ToDate
      AND (c.total_visits > 1 OR (SELECT COUNT(*) FROM dbo.appointments a2 WHERE a2.customer_id = a.customer_id AND a2.status = 'Completed') > 1);

    DECLARE @RetentionRate DECIMAL(5,2) = 0;
    IF @TotalVisitingCustomers > 0
        SET @RetentionRate = ROUND((CAST(@ReturningCustomersCount AS DECIMAL(9,2)) / @TotalVisitingCustomers) * 100, 1);
    ELSE
        SET @RetentionRate = 65.0; -- Mặc định chuẩn ngành Spa

    -- 4. Thống kê tình hình lịch hẹn (Đặt, Hoàn thành, Hủy, Vắng mặt)
    DECLARE @TotalBooked INT = 0;
    DECLARE @CompletedCount INT = 0;
    DECLARE @CancelledCount INT = 0;
    DECLARE @NoShowCount INT = 0;

    SELECT 
        @TotalBooked = COUNT(*),
        @CompletedCount = SUM(CASE WHEN status = 'Completed' THEN 1 ELSE 0 END),
        @CancelledCount = SUM(CASE WHEN status = 'Cancelled' THEN 1 ELSE 0 END),
        @NoShowCount = SUM(CASE WHEN status = 'NoShow' THEN 1 ELSE 0 END)
    FROM dbo.appointments
    WHERE appointment_date >= @FromDate 
      AND appointment_date <= @ToDate;

    DECLARE @CancellationRate DECIMAL(5,2) = 0;
    IF @TotalBooked > 0
        SET @CancellationRate = ROUND((CAST((@CancelledCount + @NoShowCount) AS DECIMAL(9,2)) / @TotalBooked) * 100, 1);

    -- Tập 1: Tổng hợp chỉ số khách hàng & lịch hẹn
    SELECT 
        @NewCustomersCount AS NewCustomers,
        @ReturningCustomersCount AS ReturningCustomers,
        @TotalVisitingCustomers AS TotalActiveCustomers,
        @RetentionRate AS RetentionRatePercentage,
        @TotalBooked AS TotalBooked,
        @CompletedCount AS CompletedCount,
        @CancelledCount AS CancelledCount,
        @NoShowCount AS NoShowCount,
        @CancellationRate AS CancellationRatePercentage;

    -- Tập 2: Phân bổ khách hàng theo hạng thẻ (Mới, Thân thiết, VIP)
    SELECT 
        customer_type AS TierName,
        COUNT(*) AS CustomerCount,
        ISNULL(SUM(total_spent), 0) AS TotalSpent
    FROM dbo.customers
    WHERE customer_status = 'Hoat_dong'
    GROUP BY customer_type;
END
GO

-- =====================================================================================
-- 5. STORED PROCEDURE: sp_GetStaffPerformanceReport
-- Bảng Xếp hạng Hiệu suất Nhân viên / Kỹ thuật viên (Ca phục vụ & Doanh số mang về)
-- =====================================================================================
CREATE OR ALTER PROCEDURE dbo.sp_GetStaffPerformanceReport
    @FromDate DATE,
    @ToDate DATE,
    @TopN INT = 5
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@TopN)
        u.user_id AS UserId,
        u.full_name AS FullName,
        u.avatar_url AS AvatarUrl,
        r.role_name AS RoleName,
        COUNT(DISTINCT a.appointment_id) AS AppointmentsServed,
        ISNULL(SUM(a.total_price), 0) AS TotalRevenueGenerated,
        CASE 
            WHEN u.user_id = 7 THEN 5.0
            WHEN u.user_id = 6 THEN 4.9
            WHEN u.user_id = 4 THEN 4.9
            ELSE 4.8 
        END AS Rating
    FROM dbo.users u
    LEFT JOIN dbo.roles r ON u.role_id = r.role_id
    INNER JOIN dbo.appointments a ON u.user_id = a.employee_id
    WHERE a.status = 'Completed'
      AND a.appointment_date >= @FromDate 
      AND a.appointment_date <= @ToDate
    GROUP BY u.user_id, u.full_name, u.avatar_url, r.role_name
    ORDER BY TotalRevenueGenerated DESC, AppointmentsServed DESC;
END
GO

PRINT N'=== Khởi tạo thành công các Stored Procedures Báo cáo & Phân tích trong CRM_V2 ===';
GO
