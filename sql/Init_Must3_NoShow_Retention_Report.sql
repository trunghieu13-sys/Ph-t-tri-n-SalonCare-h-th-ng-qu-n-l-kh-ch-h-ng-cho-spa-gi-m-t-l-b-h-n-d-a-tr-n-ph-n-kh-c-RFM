-- =============================================================================
-- SALONCARE CRM (CSDL: CRM_V2)
-- BÁO CÁO TỈ LỆ BỎ HẸN (NO-SHOW) & KHÁCH QUAY LẠI (RETENTION) THEO PHÂN KHÚC
-- Stored Procedure: dbo.sp_Report_NoShowAndRetentionBySegment
-- =============================================================================
USE [CRM_V2];
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Mở rộng ràng buộc CK_customers_customer_type để hỗ trợ đầy đủ 4 nhóm: VIP, Than_thiet, Moi, Nguy_co
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_customers_customer_type' AND parent_object_id = OBJECT_ID('dbo.customers'))
BEGIN
    ALTER TABLE dbo.customers DROP CONSTRAINT CK_customers_customer_type;
END;
GO

ALTER TABLE dbo.customers WITH CHECK ADD CONSTRAINT CK_customers_customer_type
    CHECK (customer_type IN (N'VIP', N'Than_thiet', N'Moi', N'Nguy_co'));
GO

-- Tạo / Cập nhật Stored Procedure: sp_Report_NoShowAndRetentionBySegment
-- Truy vấn 100% dữ liệu thực tế từ bảng dbo.customers và dbo.appointments
CREATE OR ALTER PROCEDURE [dbo].[sp_Report_NoShowAndRetentionBySegment]
    @FromDate DATE,
    @ToDate   DATE
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
        ) a_all
        WHERE c.customer_status = 'Hoat_dong'
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
