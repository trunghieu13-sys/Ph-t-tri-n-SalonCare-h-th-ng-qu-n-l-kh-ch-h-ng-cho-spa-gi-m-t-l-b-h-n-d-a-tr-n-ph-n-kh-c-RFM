USE CRM_V2;
GO

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- =================================================================================
-- 1. BẢNG SYSTEM_CONFIGS (LƯU TRỮ ĐỘNG NGƯỠNG RFM & THỜI GIAN NHẮC LỊCH)
-- =================================================================================
IF OBJECT_ID('dbo.system_configs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.system_configs (
        config_key   VARCHAR(100) NOT NULL,
        config_value NVARCHAR(500) NOT NULL,
        description  NVARCHAR(500) NULL,
        updated_at   DATETIME NOT NULL CONSTRAINT DF_system_configs_updated_at DEFAULT (GETDATE()),

        CONSTRAINT PK_system_configs PRIMARY KEY CLUSTERED (config_key ASC)
    );
END
GO

-- Seed 8 cấu hình RFM mặc định (Không ghi đè nếu Chủ salon đã chỉnh sửa)
MERGE INTO dbo.system_configs AS target
USING (VALUES
    ('RFM_AnalysisWindowDays', '180', N'Khoảng thời gian xét lịch sử dịch vụ để tính điểm RFM (số ngày)'),
    ('RFM_VipMinMonetary',     '3000000', N'Ngưỡng tổng chi tiêu tối thiểu để đạt hạng VIP (VNĐ)'),
    ('RFM_VipMinFrequency',    '6',   N'Ngưỡng số lần đến tối thiểu để xét hạng VIP kết hợp độ mới (lần)'),
    ('RFM_VipMaxRecency',      '30',  N'Số ngày tối đa từ lần đến gần nhất kết hợp tần suất để đạt VIP (ngày)'),
    ('RFM_LoyalMinFrequency',  '3',   N'Ngưỡng số lần đến tối thiểu để đạt hạng Thân thiết - Loyal (lần)'),
    ('RFM_LoyalMaxRecency',    '45',  N'Số ngày tối đa từ lần đến gần nhất của nhóm Thân thiết - Loyal (ngày)'),
    ('RFM_AtRiskMinRecency',   '60',  N'Số ngày vắng mặt tối thiểu để cảnh báo Nguy cơ rời bỏ - AtRisk (áp dụng cho khách có F >= 2)'),
    ('RFM_NoShowRiskThreshold','2',   N'Ngưỡng số lần bỏ hẹn tối thiểu để gắn cờ Cảnh báo hay bỏ hẹn (lần)')
) AS source (config_key, config_value, description)
ON target.config_key = source.config_key
WHEN NOT MATCHED THEN
    INSERT (config_key, config_value, description, updated_at)
    VALUES (source.config_key, source.config_value, source.description, GETDATE());
GO

-- =================================================================================
-- 2. BẢNG RFM_SCORES (QUAN HỆ 1-1 VỚI CUSTOMERS, CÓ LÝ DO GIẢI THÍCH TƯỜNG MINH)
-- =================================================================================
IF OBJECT_ID('dbo.rfm_scores', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.rfm_scores (
        customer_id        BIGINT NOT NULL,
        recency_days       INT NOT NULL CONSTRAINT DF_rfm_scores_recency DEFAULT (999),
        frequency_count    INT NOT NULL CONSTRAINT DF_rfm_scores_frequency DEFAULT (0),
        monetary_total     DECIMAL(18, 2) NOT NULL CONSTRAINT DF_rfm_scores_monetary DEFAULT (0.00),
        no_show_count      INT NOT NULL CONSTRAINT DF_rfm_scores_noshow DEFAULT (0),
        segment_group      VARCHAR(30) NOT NULL CONSTRAINT DF_rfm_scores_segment DEFAULT ('New'), -- VIP, Loyal, AtRisk, New
        is_no_show_risk    BIT NOT NULL CONSTRAINT DF_rfm_scores_risk DEFAULT (0),
        reason_text        NVARCHAR(1000) NOT NULL,
        last_calculated_at DATETIME NOT NULL CONSTRAINT DF_rfm_scores_calc_at DEFAULT (GETDATE()),

        CONSTRAINT PK_rfm_scores PRIMARY KEY CLUSTERED (customer_id ASC),
        CONSTRAINT FK_rfm_scores_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(customer_id) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX IX_rfm_scores_segment_group
    ON dbo.rfm_scores (segment_group ASC, is_no_show_risk ASC);
END
GO

-- =================================================================================
-- 3. BỔ SUNG 3 CỘT NHẮC LỊCH & TOKEN XÁC NHẬN 1-CLICK VÀO BẢNG APPOINTMENTS
-- =================================================================================
IF COL_LENGTH('dbo.appointments', 'is_reminder_24h_sent') IS NULL
BEGIN
    ALTER TABLE dbo.appointments
    ADD is_reminder_24h_sent BIT NOT NULL CONSTRAINT DF_appointments_rem24h DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.appointments', 'is_reminder_2h_sent') IS NULL
BEGIN
    ALTER TABLE dbo.appointments
    ADD is_reminder_2h_sent BIT NOT NULL CONSTRAINT DF_appointments_rem2h DEFAULT (0);
END
GO

IF COL_LENGTH('dbo.appointments', 'confirmation_token') IS NULL
BEGIN
    ALTER TABLE dbo.appointments
    ADD confirmation_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_appointments_conf_token DEFAULT (NEWID());
END
GO

-- =================================================================================
-- 4. BẢNG NOTIFICATION_LOGS (NHẬT KÝ NHẮC LỊCH TỰ ĐỘNG & XÁC NHẬN 1-CLICK)
-- =================================================================================
IF OBJECT_ID('dbo.notification_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_logs (
        log_id               BIGINT IDENTITY(1,1) NOT NULL,
        customer_id          BIGINT NOT NULL,
        appointment_id       BIGINT NULL,
        channel              VARCHAR(30) NOT NULL, -- ZaloOA, Email, SMS
        message_type         VARCHAR(30) NOT NULL, -- Reminder24h, Reminder2h, WinBackPromo
        segment_at_send_time VARCHAR(30) NOT NULL, -- VIP, Loyal, AtRisk, New
        content              NVARCHAR(MAX) NOT NULL,
        status               VARCHAR(30) NOT NULL CONSTRAINT DF_notification_logs_status DEFAULT ('Sent'), -- Sent, Failed, ConfirmedByCustomer, CanceledByCustomer
        sent_at              DATETIME NOT NULL CONSTRAINT DF_notification_logs_sent_at DEFAULT (GETDATE()),

        CONSTRAINT PK_notification_logs PRIMARY KEY CLUSTERED (log_id ASC),
        CONSTRAINT FK_notification_logs_customers FOREIGN KEY (customer_id) REFERENCES dbo.customers(customer_id) ON DELETE CASCADE,
        CONSTRAINT FK_notification_logs_appointments FOREIGN KEY (appointment_id) REFERENCES dbo.appointments(appointment_id) ON DELETE SET NULL
    );

    CREATE NONCLUSTERED INDEX IX_notification_logs_customer_sent
    ON dbo.notification_logs (customer_id ASC, sent_at DESC);
END
GO
