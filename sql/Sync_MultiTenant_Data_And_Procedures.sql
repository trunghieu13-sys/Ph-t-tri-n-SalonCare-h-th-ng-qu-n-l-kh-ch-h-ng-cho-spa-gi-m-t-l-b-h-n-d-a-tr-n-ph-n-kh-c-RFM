-- =============================================================================
-- SALONCARE CRM (CRM_V2) - MULTI-TENANT DATA & STORED PROCEDURE SYNC
-- 1. Tạo Spa mặc định cho Chủ Spa hiện có (user_id = 6 - Trần Ngọc Anh) và gắn
--    dữ liệu lịch sử hiện có vào Spa #1 (ngoại trừ Admin và nhân viên test chưa vào quán)
-- 2. Cập nhật sp_Report_NoShowAndRetentionBySegment nhận thêm @SpaId BIGINT = NULL
-- =============================================================================
USE CRM_V2;
GO

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- 1. Khởi tạo Spa cho Chủ Spa Trần Ngọc Anh (nếu bảng spas đang trống)
DECLARE @DefaultOwnerId BIGINT;
SELECT TOP 1 @DefaultOwnerId = user_id FROM dbo.users WHERE role_id = 2 ORDER BY user_id;

IF @DefaultOwnerId IS NOT NULL
BEGIN
    DECLARE @DefaultSpaId BIGINT;
    SELECT TOP 1 @DefaultSpaId = spa_id FROM dbo.spas WHERE owner_user_id = @DefaultOwnerId;

    IF @DefaultSpaId IS NULL
    BEGIN
        DECLARE @OwnerFullName NVARCHAR(100);
        DECLARE @OwnerPhone VARCHAR(15);
        SELECT @OwnerFullName = full_name, @OwnerPhone = phone FROM dbo.users WHERE user_id = @DefaultOwnerId;

        INSERT INTO dbo.spas (spa_name, owner_user_id, address, phone, status, created_at)
        VALUES (N'Spa của ' + ISNULL(@OwnerFullName, N'Trần Ngọc Anh'), @DefaultOwnerId, N'Quận 1, TP. Hồ Chí Minh', @OwnerPhone, 'active', GETDATE());

        SET @DefaultSpaId = SCOPE_IDENTITY();
    END

    -- Gán spa_id cho Chủ Spa và đội ngũ KTV mẫu (trừ Admin role_id = 1 và tài khoản 'test')
    UPDATE dbo.users
    SET spa_id = @DefaultSpaId
    WHERE spa_id IS NULL
      AND role_id IN (2, 3)
      AND username NOT IN ('test');

    -- Gán spa_id cho toàn bộ dữ liệu nghiệp vụ hiện có chưa có spa_id
    UPDATE dbo.customers        SET spa_id = @DefaultSpaId WHERE spa_id IS NULL;
    UPDATE dbo.services         SET spa_id = @DefaultSpaId WHERE spa_id IS NULL;
    UPDATE dbo.appointments     SET spa_id = @DefaultSpaId WHERE spa_id IS NULL;
    UPDATE dbo.audit_logs       SET spa_id = @DefaultSpaId WHERE spa_id IS NULL;
    UPDATE dbo.notifications    SET spa_id = @DefaultSpaId WHERE spa_id IS NULL;
    UPDATE dbo.automation_rules SET spa_id = @DefaultSpaId WHERE spa_id IS NULL;
END
GO

-- 2. Cập nhật Stored Procedure sp_Report_NoShowAndRetentionBySegment hỗ trợ @SpaId
CREATE OR ALTER PROCEDURE [dbo].[sp_Report_NoShowAndRetentionBySegment]
    @FromDate DATE,
    @ToDate   DATE,
    @SpaId    BIGINT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @FromDate IS NULL
        SET @FromDate = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);

    IF @ToDate IS NULL
        SET @ToDate = CAST(GETDATE() AS DATE);

    ;WITH SegmentCatalog AS (
        SELECT 1 AS SortOrder, 'VIP'        AS SegmentCode, N'Khách hàng VIP'         AS SegmentName
        UNION ALL
        SELECT 2 AS SortOrder, 'Than_thiet' AS SegmentCode, N'Khách hàng Thân thiết'  AS SegmentName
        UNION ALL
        SELECT 3 AS SortOrder, 'Moi'        AS SegmentCode, N'Khách hàng Mới'         AS SegmentName
        UNION ALL
        SELECT 4 AS SortOrder, 'Nguy_co'    AS SegmentCode, N'Khách Nguy cơ rời bỏ'   AS SegmentName
    ),
    CustomerRealVisits AS (
        SELECT
            c.customer_id,
            CASE
                WHEN ISNULL(a_all.CompletedCountAll, 0) > ISNULL(c.total_visits, 0)
                THEN ISNULL(a_all.CompletedCountAll, 0)
                ELSE ISNULL(c.total_visits, 0)
            END AS EffectiveTotalVisits,
            CASE
                WHEN c.customer_type IN ('VIP', 'Vip') OR r.segment_group = 'VIP' THEN 'VIP'
                WHEN c.customer_type IN ('Than_thiet', 'Loyal') OR r.segment_group = 'Loyal' THEN 'Than_thiet'
                WHEN c.customer_type IN ('Nguy_co', 'AtRisk', 'At_Risk') OR r.segment_group = 'AtRisk' THEN 'Nguy_co'
                ELSE 'Moi'
            END AS NormalizedSegmentCode
        FROM dbo.customers c
        LEFT JOIN dbo.rfm_scores r ON r.customer_id = c.customer_id
        OUTER APPLY (
            SELECT COUNT(*) AS CompletedCountAll
            FROM dbo.appointments a0
            WHERE a0.customer_id = c.customer_id
              AND a0.status = 'Completed'
              AND (@SpaId IS NULL OR a0.spa_id = @SpaId)
        ) a_all
        WHERE c.customer_status = 'Hoat_dong'
          AND (@SpaId IS NULL OR c.spa_id = @SpaId)
    ),
    CustomerAgg AS (
        SELECT
            crv.NormalizedSegmentCode AS SegmentCode,
            COUNT(DISTINCT crv.customer_id) AS TotalCustomers,
            COUNT(DISTINCT CASE WHEN crv.EffectiveTotalVisits > 1 THEN crv.customer_id END) AS ReturningCustomers
        FROM CustomerRealVisits crv
        GROUP BY crv.NormalizedSegmentCode
    ),
    AppointmentAgg AS (
        SELECT
            crv.NormalizedSegmentCode AS SegmentCode,
            COUNT(a.appointment_id) AS TotalAppointments,
            SUM(CASE WHEN a.status = 'Completed' THEN 1 ELSE 0 END) AS CompletedCount,
            SUM(CASE WHEN a.status = 'NoShow'    THEN 1 ELSE 0 END) AS NoShowCount,
            SUM(CASE WHEN a.status = 'Cancelled' THEN 1 ELSE 0 END) AS CancelledCount,
            SUM(CASE WHEN a.status = 'Completed' THEN ISNULL(a.total_price, 0) ELSE 0 END) AS ActualRevenue
        FROM CustomerRealVisits crv
        INNER JOIN dbo.appointments a
            ON a.customer_id = crv.customer_id
           AND a.appointment_date BETWEEN @FromDate AND @ToDate
           AND (@SpaId IS NULL OR a.spa_id = @SpaId)
        GROUP BY crv.NormalizedSegmentCode
    )
    SELECT
        sc.SegmentCode,
        sc.SegmentName,
        ISNULL(ca.TotalCustomers, 0) AS TotalCustomers,
        ISNULL(ca.ReturningCustomers, 0) AS ReturningCustomers,
        CAST(
            CASE
                WHEN ISNULL(ca.TotalCustomers, 0) > 0
                THEN (ISNULL(ca.ReturningCustomers, 0) * 100.0 / ca.TotalCustomers)
                ELSE 0.0
            END AS DECIMAL(10, 2)
        ) AS RetentionRate,
        ISNULL(aa.TotalAppointments, 0) AS TotalAppointments,
        ISNULL(aa.CompletedCount, 0) AS CompletedCount,
        ISNULL(aa.NoShowCount, 0) AS NoShowCount,
        ISNULL(aa.CancelledCount, 0) AS CancelledCount,
        CAST(
            CASE
                WHEN ISNULL(aa.TotalAppointments, 0) > 0
                THEN (ISNULL(aa.NoShowCount, 0) * 100.0 / aa.TotalAppointments)
                ELSE 0.0
            END AS DECIMAL(10, 2)
        ) AS NoShowRate,
        CAST(ISNULL(aa.ActualRevenue, 0.00) AS DECIMAL(18, 2)) AS ActualRevenue
    FROM SegmentCatalog sc
    LEFT JOIN CustomerAgg ca ON ca.SegmentCode = sc.SegmentCode
    LEFT JOIN AppointmentAgg aa ON aa.SegmentCode = sc.SegmentCode
    ORDER BY sc.SortOrder;
END;
GO
