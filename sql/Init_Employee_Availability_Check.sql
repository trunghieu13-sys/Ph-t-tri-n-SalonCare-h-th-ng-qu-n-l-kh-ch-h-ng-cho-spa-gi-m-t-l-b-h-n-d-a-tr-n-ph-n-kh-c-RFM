-- =============================================================================
-- SALONCARE CRM V2 - DATABASE CONCURRENCY & TIME OVERLAP CONTROL
-- Mục tiêu:
--   1. Tạo Index tối ưu hóa truy vấn kiểm tra trùng lịch KTV (Concurrency Control)
--   2. Tạo hàm T-SQL fn_CheckEmployeeAvailability kiểm tra xung đột khung giờ
--   3. Tạo Stored Procedure sp_GetAvailableEmployees lấy danh sách KTV rảnh/bận
--   4. Chuẩn hóa dữ liệu lịch hẹn đang bị trùng KTV trước đó
-- =============================================================================

USE CRM_V2;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. TẠO CHỈ MỤC TỐI ƯU HÓA KIỂM TRA XUNG ĐỘT LỊCH HẸN (CONCURRENCY INDEX)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes 
    WHERE name = 'IX_Appointments_Employee_Date_Time_Status' 
      AND object_id = OBJECT_ID('dbo.appointments')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Appointments_Employee_Date_Time_Status
    ON dbo.appointments (employee_id, appointment_date, start_time, end_time, status)
    INCLUDE (customer_id);
    PRINT N'Đã tạo chỉ mục IX_Appointments_Employee_Date_Time_Status thành công.';
END
GO

-- 2. HÀM T-SQL KIỂM TRA XUNG ĐỘT THỜI GIAN CỦA KTV: dbo.fn_CheckEmployeeAvailability
-- Công thức toán học: [Start_A, End_A] giao với [Start_B, End_B] <=> Start_A < End_B AND End_A > Start_B
-- Chỉ xét các lịch có status IN ('Pending', 'Confirmed', 'InService')
-- Trả về: 1 (Rảnh / Hợp lệ), 0 (Bị trùng lịch)
CREATE OR ALTER FUNCTION dbo.fn_CheckEmployeeAvailability
(
    @EmployeeId BIGINT,
    @AppointmentDate DATE,
    @StartTime TIME,
    @EndTime TIME,
    @ExcludeAppointmentId BIGINT = NULL
)
RETURNS BIT
AS
BEGIN
    DECLARE @IsAvailable BIT = 1;

    -- Nếu khung giờ không hợp lệ (StartTime >= EndTime) thì trả về 0
    IF (@StartTime >= @EndTime)
        RETURN 0;

    IF EXISTS (
        SELECT 1
        FROM dbo.appointments WITH (NOLOCK)
        WHERE employee_id = @EmployeeId
          AND appointment_date = @AppointmentDate
          AND status IN ('Pending', 'Confirmed', 'InService')
          AND (@ExcludeAppointmentId IS NULL OR appointment_id <> @ExcludeAppointmentId)
          AND (start_time < @EndTime AND end_time > @StartTime)
    )
    BEGIN
        SET @IsAvailable = 0;
    END

    RETURN @IsAvailable;
END
GO

-- 3. STORED PROCEDURE LẤY DANH SÁCH KTV KÈM TRẠNG THÁI RẢNH / BẬN CHI TIẾT
CREATE OR ALTER PROCEDURE dbo.sp_GetAvailableEmployees
    @AppointmentDate DATE,
    @StartTime TIME,
    @EndTime TIME,
    @ExcludeAppointmentId BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        u.user_id AS EmployeeId,
        u.full_name AS FullName,
        ISNULL(u.specialty, N'Chăm sóc tổng hợp') AS Specialty,
        ISNULL(r.role_name, 'staff') AS RoleName,
        dbo.fn_CheckEmployeeAvailability(u.user_id, @AppointmentDate, @StartTime, @EndTime, @ExcludeAppointmentId) AS IsAvailable,
        conflict.appointment_id AS ConflictAppointmentId,
        CONVERT(VARCHAR(5), conflict.start_time, 108) AS ConflictStartTime,
        CONVERT(VARCHAR(5), conflict.end_time, 108) AS ConflictEndTime,
        conflict.status AS ConflictStatus,
        c.full_name AS ConflictCustomerName
    FROM dbo.users u
    LEFT JOIN dbo.roles r ON u.role_id = r.role_id
    OUTER APPLY (
        SELECT TOP 1 
            a.appointment_id,
            a.start_time,
            a.end_time,
            a.status,
            a.customer_id
        FROM dbo.appointments a
        WHERE a.employee_id = u.user_id
          AND a.appointment_date = @AppointmentDate
          AND a.status IN ('Pending', 'Confirmed', 'InService')
          AND (@ExcludeAppointmentId IS NULL OR a.appointment_id <> @ExcludeAppointmentId)
          AND (a.start_time < @EndTime AND a.end_time > @StartTime)
        ORDER BY a.start_time ASC
    ) conflict
    LEFT JOIN dbo.customers c ON conflict.customer_id = c.customer_id
    WHERE u.status = 'active'
    ORDER BY u.role_id DESC, u.full_name ASC;
END
GO

-- 4. XỬ LÝ DỮ LIỆU CŨ ĐANG BỊ TRÙNG KTV (Nếu có 2 lịch cùng KTV cùng giờ thì tự động điều phối sang KTV đang rảnh)
;WITH OverlappingCTE AS (
    SELECT 
        a1.appointment_id,
        a1.employee_id,
        a1.appointment_date,
        a1.start_time,
        a1.end_time,
        ROW_NUMBER() OVER (
            PARTITION BY a1.employee_id, a1.appointment_date, a1.start_time 
            ORDER BY a1.appointment_id ASC
        ) AS rn
    FROM dbo.appointments a1
    WHERE a1.employee_id IS NOT NULL
      AND a1.status IN ('Pending', 'Confirmed', 'InService')
)
UPDATE a
SET a.employee_id = freeEmp.user_id,
    a.updated_at = GETDATE()
FROM dbo.appointments a
INNER JOIN OverlappingCTE cte ON a.appointment_id = cte.appointment_id
CROSS APPLY (
    SELECT TOP 1 u.user_id
    FROM dbo.users u
    WHERE u.status = 'active'
      AND dbo.fn_CheckEmployeeAvailability(u.user_id, a.appointment_date, a.start_time, a.end_time, a.appointment_id) = 1
    ORDER BY u.role_id DESC, u.user_id ASC
) freeEmp
WHERE cte.rn > 1;
GO

PRINT N'=== Hoàn tất cài đặt fn_CheckEmployeeAvailability & sp_GetAvailableEmployees ===';
GO
