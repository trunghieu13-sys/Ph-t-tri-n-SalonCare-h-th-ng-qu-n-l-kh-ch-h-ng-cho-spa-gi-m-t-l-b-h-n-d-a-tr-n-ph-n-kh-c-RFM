USE CRM_V2;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRANSACTION;

BEGIN TRY
    PRINT N'1. Đang xóa thông báo liên quan đến khách hàng Kiệt và các lịch hẹn bị hủy liên quan...';
    DELETE FROM dbo.notifications 
    WHERE message LIKE N'%Kiệt%' 
       OR title LIKE N'%Kiệt%'
       OR reference_id IN (SELECT appointment_id FROM dbo.appointments WHERE customer_id = 7)
       OR (reference_type = 'CUSTOMER' AND reference_id = 7)
       OR message LIKE N'%Cạo lông%'
       OR message LIKE N'%Massage chim%';

    PRINT N'2. Đang xóa các dịch vụ cạo lông cu và massage chim khỏi bảng appointment_services...';
    DELETE FROM dbo.appointment_services 
    WHERE service_id IN (7, 8)
       OR service_id IN (SELECT service_id FROM dbo.services WHERE service_name IN (N'Cạo lông cu', N'Massage chim'));

    PRINT N'3. Đang xóa lịch hẹn và dịch vụ lịch hẹn của khách hàng Kiệt (customer_id = 7)...';
    DELETE FROM dbo.appointment_services 
    WHERE appointment_id IN (SELECT appointment_id FROM dbo.appointments WHERE customer_id = 7);

    DELETE FROM dbo.appointments 
    WHERE customer_id = 7;

    PRINT N'4. Đang xóa hồ sơ khách hàng Kiệt khỏi bảng customers...';
    DELETE FROM dbo.customers 
    WHERE customer_id = 7 OR full_name LIKE N'%Kiệt%';

    PRINT N'5. Đang xóa 2 dịch vụ khỏi bảng services...';
    DELETE FROM dbo.services 
    WHERE service_id IN (7, 8) 
       OR service_name IN (N'Cạo lông cu', N'Massage chim');

    PRINT N'6. Đang thêm dịch vụ thay thế chuẩn chuyên nghiệp cho danh mục Body Care...';
    IF NOT EXISTS (SELECT 1 FROM dbo.services WHERE service_name = N'Massage thư giãn toàn thân')
    BEGIN
        INSERT INTO dbo.services (service_name, category_name, duration_minutes, price, description, icon_class, bg_color, is_active, created_at, updated_at)
        VALUES (
            N'Massage thư giãn toàn thân',
            N'Body Care',
            60,
            350000.00,
            N'Liệu trình massage body chuyên sâu kết hợp tinh dầu thảo mộc giúp lưu thông khí huyết, giải tỏa căng thẳng cơ bắp và phục hồi năng lượng.',
            'fa-hands-bubbles',
            'bg-pink-500',
            1,
            GETDATE(),
            GETDATE()
        );
    END

    -- Gán dịch vụ mới vào các lịch hẹn 9, 12, 15 để dữ liệu lịch hẹn luôn đầy đủ và chuyên nghiệp
    DECLARE @NewMassageId INT = (SELECT TOP 1 service_id FROM dbo.services WHERE service_name = N'Massage thư giãn toàn thân');
    IF @NewMassageId IS NOT NULL
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.appointment_services WHERE appointment_id = 9 AND service_id = @NewMassageId)
            INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id, is_hidden) 
            VALUES (9, @NewMassageId, 350000.00, 6, 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.appointment_services WHERE appointment_id = 12 AND service_id = @NewMassageId)
            INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id, is_hidden) 
            VALUES (12, @NewMassageId, 350000.00, 6, 0);

        IF NOT EXISTS (SELECT 1 FROM dbo.appointment_services WHERE appointment_id = 15 AND service_id = @NewMassageId)
            INSERT INTO dbo.appointment_services (appointment_id, service_id, price, employee_id, is_hidden) 
            VALUES (15, @NewMassageId, 350000.00, 6, 0);
    END

    PRINT N'7. Đang đồng bộ cập nhật lại tổng tiền lịch hẹn và chi tiêu khách hàng...';
    UPDATE a
    SET a.total_price = ISNULL((SELECT SUM(aps.price) FROM dbo.appointment_services aps WHERE aps.appointment_id = a.appointment_id), a.total_price)
    FROM dbo.appointments a;

    UPDATE c
    SET c.total_spent = ISNULL((SELECT SUM(a.total_price) FROM dbo.appointments a WHERE a.customer_id = c.customer_id AND a.status = 'Completed'), 0),
        c.total_visits = ISNULL((SELECT COUNT(*) FROM dbo.appointments a WHERE a.customer_id = c.customer_id AND a.status = 'Completed'), 0)
    FROM dbo.customers c;

    COMMIT TRANSACTION;
    PRINT N'==> Hoàn tất xóa sạch dịch vụ không phù hợp và cập nhật dữ liệu thành công!';
END TRY
BEGIN CATCH
    ROLLBACK TRANSACTION;
    PRINT N'LỖI: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
GO
