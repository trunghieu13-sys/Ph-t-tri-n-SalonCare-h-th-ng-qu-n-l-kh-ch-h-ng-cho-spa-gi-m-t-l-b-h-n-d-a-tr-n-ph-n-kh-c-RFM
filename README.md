# Phát triển SalonCare: Hệ thống quản lý khách hàng cho Spa và Salon nhỏ giảm tỉ lệ bỏ hẹn dựa trên phân khúc RFM

## 1. Giới thiệu đề tài
- **Tên đề tài**: Phát triển SalonCare: hệ thống quản lý khách hàng cho spa và salon nhỏ giảm tỉ lệ bỏ hẹn dựa trên phân khúc RFM.
- **Câu hỏi nghiên cứu**:
  - **CH1**: Nhắc lịch theo phân khúc RFM có giảm tỉ lệ khách bỏ hẹn so với cách nhắc hiện tại của cửa hàng không?
  - **CH2**: Gửi ưu đãi theo phân khúc RFM có tăng tỉ lệ khách cũ quay lại trong 30 ngày không?
- **Chỉ số đo lường cốt lõi**:
  - **Chỉ số 1**: Tỉ lệ bỏ hẹn (%) = Số hẹn khách không đến (`NoShow`) ÷ Tổng số hẹn.
  - **Chỉ số 2**: Tỉ lệ khách cũ quay lại (`Retention Rate` %).

## 2. Các phân hệ chức năng chính
- **Có sẵn từ nền tảng CRM**:
  - Dashboard tổng quan thời gian thực & Lịch làm việc
  - Quản lý khách hàng & Lịch sử dịch vụ
  - Điều phối lịch hẹn Kanban (Drag & Drop) & Kiểm tra KTV rảnh chống trùng lịch (`Serializable Transaction`)
  - Phân quyền đa cấp (`Admin`, `Owner`, `Staff`) & Kiến trúc **Multi-Tenant** (nhiều quán Spa độc lập dữ liệu theo `spa_id`, lời mời nhân viên gia nhập đội ngũ)
  - Bảo mật chuẩn Cloud Security (Least Privilege RBAC `403 Forbidden`, `MaliciousInputFilter` chống XSS & SQL Injection `400 Bad Request`, Nhật ký hệ thống `AuditLog`)
- **Tính năng nâng cao mới**:
  - Phân khúc khách hàng tự động theo mô hình **RFM + NoShow** (`VIP`, `Thân thiết`, `Mới`, `Nguy cơ rời bỏ`) kèm giải thích lý do (`ReasonText`)
  - Hệ thống nhắc lịch tự động đa kênh (**Zalo OA / ZNS**, **Twilio SMS**, **Gmail SMTP**) & Xác nhận lịch hẹn 1-Click
  - Báo cáo chuyên sâu Tỉ lệ bỏ hẹn & Khách quay lại theo phân khúc RFM và Xuất file Excel (`.xlsx`) động qua **ClosedXML**
  - Bộ kiểm thử tự động **xUnit (`SalonCare.Tests`)** tích hợp CI/CD GitHub Actions

---

## 3. Hướng dẫn chạy bằng Docker

Dự án đã được đóng gói sẵn với **Dockerfile** (multi-stage build) và **docker-compose.yml** gồm 2 container:
- `saloncare-web`: Ứng dụng web ASP.NET Core MVC chạy trên cổng `8080`.
- `saloncare-db`: Hệ quản trị cơ sở dữ liệu Microsoft SQL Server 2022 (`CRM_V2`).

### Bước 1: Khởi chạy toàn bộ hệ thống bằng Docker Compose
Mở Terminal / PowerShell tại thư mục gốc của dự án và chạy lệnh:

```bash
docker compose up -d --build
```

### Bước 2: Kiểm tra trạng thái các Container
```bash
docker compose ps
```

### Bước 3: Truy cập ứng dụng trên trình duyệt
- **Đường dẫn truy cập Web App**: [http://localhost:8080](http://localhost:8080)
- **Trang Đăng nhập**: [http://localhost:8080/Account/Login](http://localhost:8080/Account/Login)
- **Trang Đăng ký (Chủ Spa / Nhân viên)**: [http://localhost:8080/Account/Register](http://localhost:8080/Account/Register)
- **Kết nối SQL Server từ máy host (SSMS / Azure Data Studio)**:
  - **Server**: `localhost,14333`
  - **Login**: `sa`
  - **Password**: `SalonCare@2026!`

### Bước 4: Dừng hệ thống
```bash
docker compose down
```

---

## 4. Hướng dẫn chạy trực tiếp trên máy Local & Kiểm thử tự động (xUnit)

```bash
# 1. Khôi phục thư viện và biên dịch dự án
dotnet restore
dotnet build "CRM V2.csproj"

# 2. Chạy bộ kiểm thử tự động xUnit (10 Test Cases)
dotnet test "SalonCare.Tests/SalonCare.Tests.csproj" --logger "console;verbosity=normal"

# 3. Khởi chạy Web Server tại http://localhost:5164
dotnet run --project "CRM V2.csproj" --launch-profile http
```
