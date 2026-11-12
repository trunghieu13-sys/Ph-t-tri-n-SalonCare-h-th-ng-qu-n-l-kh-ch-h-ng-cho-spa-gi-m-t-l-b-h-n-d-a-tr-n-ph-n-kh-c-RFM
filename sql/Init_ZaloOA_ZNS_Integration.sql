-- =================================================================================
-- SCRIPT CẬP NHẬT CƠ SỞ DỮ LIỆU: TÍCH HỢP KÊNH ZALO OA / ZNS (ZALO NOTIFICATION SERVICE)
-- DỰ ÁN: SALONCARE CRM (CRM_V2) - ASP.NET CORE MVC & SQL SERVER 2022
-- =================================================================================
USE [CRM_V2];
GO

-- 1. Bổ sung các cấu hình Zalo OA / ZNS và Kênh gửi ưu tiên vào bảng dbo.system_configs
MERGE INTO dbo.system_configs AS target
USING (VALUES
    ('Notification_PreferredChannel', N'SmartFallback', N'Kênh gửi nhắc lịch ưu tiên: SmartFallback (ZaloOA -> SMS -> Email), ZaloOA, SMS, hoặc Email'),
    ('Zalo_UseSandboxMode', N'true', N'Chế độ Sandbox giả lập Zalo ZNS (true = Giả lập đóng gói JSON & tạo link 1-Click, false = Gọi REST API Zalo thật)'),
    ('Zalo_AppId', N'428910384719203841', N'Zalo Official Account App ID (Dùng để làm mới AccessToken tự động)'),
    ('Zalo_AppSecret', N'zalo_secret_demo_key_2026', N'Zalo App Secret Key (Dùng để xác thực chữ ký SHA256 X-ZEvent-Signature tại Webhook)'),
    ('Zalo_AccessToken', N'zalo_oa_sandbox_access_token_demo', N'Zalo OA Access Token hiện hành (Hiệu lực 25 giờ, tự động làm mới khi gặp lỗi -216)'),
    ('Zalo_RefreshToken', N'zalo_oa_sandbox_refresh_token_demo', N'Zalo OA Refresh Token (Dùng để lấy Access Token mới qua OAuth v4)'),
    ('Zalo_ReminderTemplateId', N'318940', N'Mã mẫu tin ZNS Nhắc lịch hẹn (Zalo Reminder Template ID)'),
    ('Zalo_PromoTemplateId', N'318941', N'Mã mẫu tin ZNS Ưu đãi Win-back cho nhóm Nguy cơ rời bỏ (Zalo Promo Template ID)')
) AS source (config_key, config_value, description)
ON target.config_key = source.config_key
WHEN NOT MATCHED THEN
    INSERT (config_key, config_value, description, updated_at)
    VALUES (source.config_key, source.config_value, source.description, SYSDATETIME());
GO

-- 2. Bổ sung cột zalo_user_id vào bảng dbo.customers
IF COL_LENGTH('dbo.customers', 'zalo_user_id') IS NULL
BEGIN
    ALTER TABLE dbo.customers
    ADD zalo_user_id NVARCHAR(100) NULL;
END
GO

-- 3. Bổ sung các cột external_message_id, payload_json, error_message vào bảng dbo.notification_logs
IF COL_LENGTH('dbo.notification_logs', 'external_message_id') IS NULL
BEGIN
    ALTER TABLE dbo.notification_logs
    ADD external_message_id NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.notification_logs', 'payload_json') IS NULL
BEGIN
    ALTER TABLE dbo.notification_logs
    ADD payload_json NVARCHAR(MAX) NULL;
END
GO

IF COL_LENGTH('dbo.notification_logs', 'error_message') IS NULL
BEGIN
    ALTER TABLE dbo.notification_logs
    ADD error_message NVARCHAR(500) NULL;
END
GO

PRINT N'=== ĐÃ CẬP NHẬT SCHEMA ZALO OA / ZNS TRÊN CSDL CRM_V2 THÀNH CÔNG! ===';
GO
