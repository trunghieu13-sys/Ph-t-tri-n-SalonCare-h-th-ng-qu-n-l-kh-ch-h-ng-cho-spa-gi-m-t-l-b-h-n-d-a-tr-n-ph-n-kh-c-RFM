/* =====================================================================================
   HỆ THỐNG QUẢN LÝ SPA SALONCARE CRM V2
   PHÂN HỆ: CHĂM SÓC KHÁCH HÀNG TỰ ĐỘNG & CHIẾN DỊCH MARKETING (CUSTOMER CARE & AUTOMATION)
   CSDL: CRM_V2 (SQL Server / T-SQL)
   ===================================================================================== */

USE [CRM_V2];
GO

SET NOCOUNT ON;
GO

-- =====================================================================================
-- 1. BẢNG AUTOMATION_RULES (Cấu hình kịch bản tự động Email / SMS / Zalo)
-- =====================================================================================
IF OBJECT_ID('dbo.automation_rules', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.automation_rules (
        rule_id                 INT IDENTITY(1,1) NOT NULL,
        rule_name               NVARCHAR(100) NOT NULL,
        description             NVARCHAR(500) NULL,
        channel                 VARCHAR(20) NOT NULL CONSTRAINT DF_automation_rules_channel DEFAULT ('SMS'),
        trigger_type            VARCHAR(50) NOT NULL,
        trigger_offset_minutes  INT NOT NULL CONSTRAINT DF_automation_rules_offset DEFAULT (0),
        is_active               BIT NOT NULL CONSTRAINT DF_automation_rules_is_active DEFAULT (1),
        message_template        NVARCHAR(MAX) NOT NULL,
        created_at              DATETIME NOT NULL CONSTRAINT DF_automation_rules_created_at DEFAULT (GETDATE()),
        updated_at              DATETIME NOT NULL CONSTRAINT DF_automation_rules_updated_at DEFAULT (GETDATE()),

        CONSTRAINT PK_automation_rules PRIMARY KEY CLUSTERED (rule_id ASC),
        CONSTRAINT CK_automation_rules_channel CHECK (channel IN ('EMAIL', 'SMS', 'ZALO')),
        CONSTRAINT CK_automation_rules_trigger_type CHECK (trigger_type IN ('BEFORE_APPOINTMENT', 'BIRTHDAY', 'AFTER_LAST_VISIT'))
    );

    CREATE NONCLUSTERED INDEX IX_automation_rules_active_trigger
        ON dbo.automation_rules (is_active, trigger_type);
END
GO

-- =====================================================================================
-- 2. BẢNG CAMPAIGNS (Chiến dịch gửi tin hàng loạt - Bulk Broadcast)
-- =====================================================================================
IF OBJECT_ID('dbo.campaigns', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.campaigns (
        campaign_id         BIGINT IDENTITY(1,1) NOT NULL,
        campaign_name       NVARCHAR(150) NOT NULL,
        channel             VARCHAR(20) NOT NULL,
        target_group        VARCHAR(50) NOT NULL,
        message_content     NVARCHAR(MAX) NOT NULL,
        total_recipients    INT NOT NULL CONSTRAINT DF_campaigns_total DEFAULT (0),
        success_count       INT NOT NULL CONSTRAINT DF_campaigns_success DEFAULT (0),
        failed_count        INT NOT NULL CONSTRAINT DF_campaigns_failed DEFAULT (0),
        status              VARCHAR(20) NOT NULL CONSTRAINT DF_campaigns_status DEFAULT ('PENDING'),
        created_by          BIGINT NULL,
        created_at          DATETIME NOT NULL CONSTRAINT DF_campaigns_created_at DEFAULT (GETDATE()),

        CONSTRAINT PK_campaigns PRIMARY KEY CLUSTERED (campaign_id ASC),
        CONSTRAINT FK_campaigns_users FOREIGN KEY (created_by) REFERENCES dbo.users(user_id) ON DELETE NO ACTION
    );

    CREATE NONCLUSTERED INDEX IX_campaigns_created_at
        ON dbo.campaigns (created_at DESC);
END
GO

-- =====================================================================================
-- 3. BẢNG CUSTOMER_INTERACTIONS (Nhật ký gửi Email/SMS & Lịch sử chăm sóc)
-- =====================================================================================
IF OBJECT_ID('dbo.customer_interactions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.customer_interactions (
        interaction_id      BIGINT IDENTITY(1,1) NOT NULL,
        customer_id         BIGINT NOT NULL,
        rule_id             INT NULL,
        campaign_id         BIGINT NULL,
        channel             VARCHAR(20) NOT NULL,
        recipient           VARCHAR(100) NOT NULL,
        content             NVARCHAR(MAX) NOT NULL,
        status              VARCHAR(20) NOT NULL CONSTRAINT DF_customer_interactions_status DEFAULT ('PENDING'),
        sent_at             DATETIME NOT NULL CONSTRAINT DF_customer_interactions_sent_at DEFAULT (GETDATE()),
        error_message       NVARCHAR(MAX) NULL,

        CONSTRAINT PK_customer_interactions PRIMARY KEY CLUSTERED (interaction_id ASC),
        CONSTRAINT FK_customer_interactions_customers FOREIGN KEY (customer_id)
            REFERENCES dbo.customers(customer_id) ON DELETE CASCADE,
        CONSTRAINT FK_customer_interactions_rules FOREIGN KEY (rule_id)
            REFERENCES dbo.automation_rules(rule_id) ON DELETE SET NULL,
        CONSTRAINT FK_customer_interactions_campaigns FOREIGN KEY (campaign_id)
            REFERENCES dbo.campaigns(campaign_id) ON DELETE SET NULL,
        CONSTRAINT CK_customer_interactions_channel CHECK (channel IN ('EMAIL', 'SMS', 'ZALO')),
        CONSTRAINT CK_customer_interactions_status CHECK (status IN ('SUCCESS', 'FAILED', 'PENDING'))
    );

    CREATE NONCLUSTERED INDEX IX_customer_interactions_customer_sent
        ON dbo.customer_interactions (customer_id, sent_at DESC);

    CREATE NONCLUSTERED INDEX IX_customer_interactions_status_sent
        ON dbo.customer_interactions (status, sent_at DESC);
END
GO

-- =====================================================================================
-- 4. SEED DỮ LIỆU MẪU: 3 KỊCH BẢN TỰ ĐỘNG CHUẨN UI SALONCARE CRM
-- =====================================================================================
IF NOT EXISTS (SELECT 1 FROM dbo.automation_rules)
BEGIN
    INSERT INTO dbo.automation_rules
        (rule_name, description, channel, trigger_type, trigger_offset_minutes, is_active, message_template)
    VALUES
        (
            N'Gửi SMS Nhắc Lịch Hẹn Trước 2 Giờ',
            N'Tự động gửi tin nhắn SMS tới khách hàng trước giờ hẹn 120 phút qua Twilio/eSMS.',
            'SMS',
            'BEFORE_APPOINTMENT',
            -120,
            1,
            N'[SalonCare Spa] Xin chào {full_name}, nhắc lịch hẹn làm đẹp của quý khách lúc {appointment_time} hôm nay. Rất hân hạnh được đón tiếp! Hotline: 1900-6868.'
        ),
        (
            N'Email Chúc Mừng Sinh Nhật & Voucher',
            N'Tự động gửi Email quà tặng giảm 20% vào đúng 08:00 sáng ngày sinh nhật khách hàng qua MailKit/SendGrid.',
            'EMAIL',
            'BIRTHDAY',
            0,
            1,
            N'<h3>Chúc mừng sinh nhật {full_name}!</h3><p>SalonCare Spa kính chúc quý khách tuổi mới rạng rỡ và hạnh phúc. Tặng bạn mã Voucher giảm <b>20%</b> cho mọi liệu trình: <b>{voucher_code}</b> (Hạn dùng 30 ngày).</p>'
        ),
        (
            N'Khảo Sát Đánh Giá Tái Ghé Sau 30 Ngày',
            N'Gửi lời nhắn nhắc nhở chăm sóc lại da sau 30 ngày kể từ lần phục hồi gần nhất.',
            'SMS',
            'AFTER_LAST_VISIT',
            43200,
            0,
            N'[SalonCare Spa] Chào {full_name}, đã 30 ngày kể từ lần chăm sóc da gần nhất của bạn. Đặt lịch tái khám ngay hôm nay để nhận ưu đãi 15% nhé! Mã: {voucher_code}'
        );
END
GO

-- =====================================================================================
-- 5. SEED DỮ LIỆU MẪU: NHẬT KÝ GỬI TIN BAN ĐẦU (NẾU CÓ KHÁCH HÀNG)
-- =====================================================================================
IF NOT EXISTS (SELECT 1 FROM dbo.customer_interactions)
BEGIN
    DECLARE @FirstCustId BIGINT = (SELECT TOP 1 customer_id FROM dbo.customers ORDER BY customer_id ASC);
    DECLARE @FirstCustPhone VARCHAR(15) = (SELECT TOP 1 phone FROM dbo.customers ORDER BY customer_id ASC);
    DECLARE @FirstCustName NVARCHAR(100) = (SELECT TOP 1 full_name FROM dbo.customers ORDER BY customer_id ASC);

    IF @FirstCustId IS NOT NULL
    BEGIN
        INSERT INTO dbo.customer_interactions (customer_id, rule_id, channel, recipient, content, status, sent_at)
        VALUES
        (
            @FirstCustId,
            1,
            'SMS',
            @FirstCustPhone,
            N'[SalonCare Spa] Xin chào ' + @FirstCustName + N', nhắc lịch hẹn làm đẹp của quý khách lúc 14:30 hôm nay.',
            'SUCCESS',
            DATEADD(MINUTE, -45, GETDATE())
        );
    END
END
GO

PRINT N'=== Khởi tạo thành công các bảng automation_rules, campaigns, customer_interactions trong CRM_V2 ===';
GO
