using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CRM_V2.Data;
using CRM_V2.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CRM_V2.Services;

public class LegacyImportService : ILegacyImportService
{
    private readonly ApplicationDbContext _context;
    private readonly IRfmService _rfmService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<LegacyImportService> _logger;

    public LegacyImportService(
        ApplicationDbContext context,
        IRfmService rfmService,
        IAuditLogService auditLogService,
        ILogger<LegacyImportService> logger)
    {
        _context = context;
        _rfmService = rfmService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public byte[] GenerateExcelTemplate()
    {
        var headers = new[]
        {
            "FullName",
            "PhoneNumber",
            "AppointmentDate",
            "StartTime",
            "ServiceName",
            "Price",
            "Status"
        };

        var sampleRows = new[]
        {
            new[] { "Nguyễn Thị Ngọc Bích", "0912345689", "25/10/2026", "09:30", "Chăm sóc da mặt chuyên sâu", "450000", "Completed" },
            new[] { "Nguyễn Thị Ngọc Bích", "0912345689", "05/11/2026", "14:00", "Trị mụn chuyên sâu", "850000", "Completed" },
            new[] { "Trần Hoàng Minh Thư", "0987654312", "15/08/2026", "10:00", "Massage thư giãn toàn thân", "550000", "Completed" },
            new[] { "Trần Hoàng Minh Thư", "0987654312", "28/08/2026", "15:30", "Gội đầu dưỡng sinh Thảo dược", "250000", "Completed" },
            new[] { "Lê Phương Thảo", "0933445577", "01/11/2026", "16:00", "Triệt lông công nghệ IPL", "800000", "NoShow" },
            new[] { "Lê Phương Thảo", "0933445577", "08/11/2026", "11:00", "Triệt lông công nghệ IPL", "800000", "NoShow" }
        };

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Legacy_Appointments");

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#6D28D9");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.Row(1).Height = 26;
        ws.Column(2).Style.NumberFormat.Format = "@";
        ws.Column(4).Style.NumberFormat.Format = "@";

        for (int r = 0; r < sampleRows.Length; r++)
        {
            var row = sampleRows[r];
            ws.Cell(r + 2, 1).Value = row[0];
            ws.Cell(r + 2, 2).SetValue(row[1]);
            ws.Cell(r + 2, 3).Value = row[2];
            ws.Cell(r + 2, 4).SetValue(row[3]);
            ws.Cell(r + 2, 5).Value = row[4];
            ws.Cell(r + 2, 6).Value = decimal.Parse(row[5]);
            ws.Cell(r + 2, 6).Style.NumberFormat.Format = "#,##0";
            ws.Cell(r + 2, 7).Value = row[6];
        }

        ws.Columns().AdjustToContents();
        for (int c = 1; c <= headers.Length; c++)
        {
            if (ws.Column(c).Width < 18)
                ws.Column(c).Width = 18;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] GenerateCsvTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("FullName,PhoneNumber,AppointmentDate,StartTime,ServiceName,Price,Status");
        sb.AppendLine("\"Nguyễn Thị Ngọc Bích\",0912345689,25/10/2026,09:30,\"Chăm sóc da mặt chuyên sâu\",450000,Completed");
        sb.AppendLine("\"Nguyễn Thị Ngọc Bích\",0912345689,05/11/2026,14:00,\"Trị mụn chuyên sâu\",850000,Completed");
        sb.AppendLine("\"Trần Hoàng Minh Thư\",0987654312,15/08/2026,10:00,\"Massage thư giãn toàn thân\",550000,Completed");
        sb.AppendLine("\"Lê Phương Thảo\",0933445577,01/11/2026,16:00,\"Triệt lông công nghệ IPL\",800000,NoShow");
        sb.AppendLine("\"Lê Phương Thảo\",0933445577,08/11/2026,11:00,\"Triệt lông công nghệ IPL\",800000,NoShow");

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }

    public async Task<LegacyImportResultDto> ImportLegacyFileAsync(IFormFile file)
    {
        var result = new LegacyImportResultDto();
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".csv")
        {
            result.Success = false;
            result.Message = "Định dạng file không hợp lệ. Chỉ chấp nhận .xlsx hoặc .csv.";
            return result;
        }

        var parsedRows = new List<RawLegacyRow>();

        if (ext == ".xlsx")
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws == null)
            {
                result.Message = "File Excel rỗng.";
                return result;
            }

            var range = ws.RangeUsed();
            if (range != null)
            {
                var headerRow = range.FirstRowUsed();
                bool hasStartTimeCol = headerRow != null && headerRow.CellCount() >= 7;

                int rowNumber = 1;
                foreach (var row in range.RowsUsed().Skip(1))
                {
                    rowNumber++;
                    var fullName = row.Cell(1).GetString().Trim();
                    var phone = row.Cell(2).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
                        continue;

                    DateTime apptDate = ParseCellDate(row.Cell(3));
                    TimeSpan startTime = new(9, 30, 0);
                    string serviceName;
                    decimal price;
                    string statusText;

                    if (hasStartTimeCol)
                    {
                        startTime = ParseTime(row.Cell(4).GetString());
                        serviceName = row.Cell(5).GetString().Trim();
                        price = ParseAmount(row.Cell(6).GetValue<string>());
                        statusText = row.Cell(7).GetString().Trim();
                    }
                    else
                    {
                        serviceName = row.Cell(4).GetString().Trim();
                        price = ParseAmount(row.Cell(5).GetValue<string>());
                        statusText = row.Cell(6).GetString().Trim();
                    }

                    parsedRows.Add(new RawLegacyRow
                    {
                        RowIndex = rowNumber,
                        FullName = fullName,
                        Phone = NormalizePhone(phone),
                        AppointmentDate = apptDate,
                        StartTime = startTime,
                        ServiceName = string.IsNullOrWhiteSpace(serviceName) ? "Dịch vụ Spa" : serviceName,
                        Price = price,
                        Status = NormalizeStatus(statusText)
                    });
                }
            }
        }
        else
        {
            var content = await new StreamReader(file.OpenReadStream(), Encoding.UTF8, true).ReadToEndAsync();
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool hasStartTimeCol = true;

            for (int i = 0; i < lines.Length; i++)
            {
                var cols = SplitCsvLine(lines[i]);
                if (cols.Count < 2) continue;

                if (i == 0 && (cols[0].Contains("FullName", StringComparison.OrdinalIgnoreCase) ||
                               cols[0].Contains("Tên", StringComparison.OrdinalIgnoreCase)))
                {
                    hasStartTimeCol = cols.Count >= 7;
                    continue;
                }

                string fullName = cols.ElementAtOrDefault(0)?.Trim() ?? "";
                string phone = cols.ElementAtOrDefault(1)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(phone))
                    continue;

                DateTime apptDate = ParseDateString(cols.ElementAtOrDefault(2));
                TimeSpan startTime = new(9, 30, 0);
                string serviceName;
                decimal price;
                string statusText;

                if (hasStartTimeCol && cols.Count >= 7)
                {
                    startTime = ParseTime(cols.ElementAtOrDefault(3));
                    serviceName = cols.ElementAtOrDefault(4)?.Trim() ?? "Dịch vụ Spa";
                    price = ParseAmount(cols.ElementAtOrDefault(5));
                    statusText = cols.ElementAtOrDefault(6)?.Trim() ?? "Completed";
                }
                else
                {
                    serviceName = cols.ElementAtOrDefault(3)?.Trim() ?? "Dịch vụ Spa";
                    price = ParseAmount(cols.ElementAtOrDefault(4));
                    statusText = cols.ElementAtOrDefault(5)?.Trim() ?? "Completed";
                }

                parsedRows.Add(new RawLegacyRow
                {
                    RowIndex = i + 1,
                    FullName = fullName,
                    Phone = NormalizePhone(phone),
                    AppointmentDate = apptDate,
                    StartTime = startTime,
                    ServiceName = string.IsNullOrWhiteSpace(serviceName) ? "Dịch vụ Spa" : serviceName,
                    Price = price,
                    Status = NormalizeStatus(statusText)
                });
            }
        }

        result.TotalRows = parsedRows.Count;
        if (result.TotalRows == 0)
        {
            result.Success = false;
            result.Message = "Không tìm thấy dòng dữ liệu hợp lệ nào trong file.";
            return result;
        }

        var existingCustomers = await _context.Customers.ToDictionaryAsync(c => c.Phone, c => c);
        var existingServices = await _context.Services.ToListAsync();
        var touchedExistingPhones = new HashSet<string>();
        var pendingAppointments = new List<(Customer Customer, Service Service, RawLegacyRow Row)>();

        foreach (var row in parsedRows)
        {
            if (string.IsNullOrWhiteSpace(row.FullName) || string.IsNullOrWhiteSpace(row.Phone) || row.Phone.Length < 9)
            {
                result.ErrorRows++;
                result.Errors.Add($"Dòng {row.RowIndex}: Thiếu họ tên hoặc SĐT không hợp lệ ({row.Phone}).");
                continue;
            }

            var service = existingServices.FirstOrDefault(s =>
                string.Equals(s.ServiceName.Trim(), row.ServiceName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (service == null)
            {
                service = new Service
                {
                    ServiceName = row.ServiceName.Trim(),
                    CategoryName = "Chăm sóc da",
                    DurationMinutes = 60,
                    Price = row.Price > 0 ? row.Price : 350000m,
                    IconClass = "fa-spa",
                    BgColor = "bg-purple-500",
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _context.Services.Add(service);
                existingServices.Add(service);
            }

            if (!existingCustomers.TryGetValue(row.Phone, out var customer))
            {
                customer = new Customer
                {
                    FullName = row.FullName,
                    Phone = row.Phone,
                    Gender = "Nữ",
                    CustomerSource = "Truc_tiep",
                    CustomerStatus = "Hoat_dong",
                    Notes = "Đồng bộ từ dữ liệu lịch sử cũ (Legacy Import)",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _context.Customers.Add(customer);
                existingCustomers[row.Phone] = customer;
                result.NewCustomersCount++;
            }
            else
            {
                if (customer.CustomerId > 0 && touchedExistingPhones.Add(row.Phone))
                {
                    result.ExistingCustomersCount++;
                }
            }

            if (row.Status == "NoShow")
                result.NoShowAppointmentsCount++;
            else
                result.CompletedAppointmentsCount++;

            pendingAppointments.Add((customer, service, row));
            result.SuccessRows++;
        }

        await _context.SaveChangesAsync();

        foreach (var item in pendingAppointments)
        {
            bool isNoShow = item.Row.Status == "NoShow";
            var appt = new Appointment
            {
                CustomerId = item.Customer.CustomerId,
                AppointmentDate = item.Row.AppointmentDate.Date,
                StartTime = item.Row.StartTime,
                EndTime = item.Row.StartTime.Add(TimeSpan.FromMinutes(item.Service.DurationMinutes > 0 ? item.Service.DurationMinutes : 60)),
                Status = isNoShow ? "NoShow" : "Completed",
                BookingSource = "Direct",
                TotalPrice = isNoShow ? 0m : item.Row.Price,
                CompletedAt = isNoShow ? null : item.Row.AppointmentDate.Date.Add(item.Row.StartTime).AddHours(1),
                CancelledAt = isNoShow ? item.Row.AppointmentDate.Date.Add(item.Row.StartTime) : null,
                CancellationReason = isNoShow ? "Khách bỏ hẹn (Dữ liệu lịch sử cũ)" : null,
                IsHiddenFromBoard = true,
                HiddenAt = DateTime.Now,
                ConfirmationToken = Guid.NewGuid(),
                Notes = $"Import lịch sử: {item.Row.ServiceName} ({item.Row.Status})",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            appt.AppointmentServices.Add(new AppointmentService
            {
                ServiceId = item.Service.ServiceId,
                Price = item.Row.Price
            });

            _context.Appointments.Add(appt);
        }

        await _context.SaveChangesAsync();

        // Tự động tính toán lại toàn bộ phân khúc RFM ngay sau khi import xong
        await _rfmService.RecalculateAllAsync();

        // Ghi log kiểm toán IMPORT_LEGACY_DATA vào bảng audit_logs
        await _auditLogService.LogActionAsync(
            "CUSTOMERS",
            "IMPORT_LEGACY_DATA",
            $"Import dữ liệu lịch sử từ file \"{file.FileName}\": Thành công {result.SuccessRows}/{result.TotalRows} dòng (Khách mới: {result.NewCustomersCount}, Khách cũ: {result.ExistingCustomersCount}, Hoàn thành: {result.CompletedAppointmentsCount}, Bỏ hẹn: {result.NoShowAppointmentsCount}, Lỗi: {result.ErrorRows}).",
            file.FileName);

        result.Success = true;
        result.Message = $"Import thành công {result.SuccessRows}/{result.TotalRows} dòng lịch hẹn và đã tự động phân nhóm RFM cho toàn bộ khách hàng!";
        return result;
    }

    private static string NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var digits = Regex.Replace(raw.Trim(), @"[^\d+]", "");
        if (digits.StartsWith("+84"))
            digits = "0" + digits.Substring(3);
        else if (digits.StartsWith("84") && digits.Length == 11)
            digits = "0" + digits.Substring(2);
        else if (digits.Length == 9 && !digits.StartsWith("0"))
            digits = "0" + digits;
        return digits;
    }

    private static string NormalizeStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Completed";
        var s = raw.Trim().ToLowerInvariant();
        if (s.Contains("noshow") || s.Contains("no-show") || s.Contains("bỏ hẹn") || s.Contains("bo hen") || s.Contains("vắng") || s.Contains("hủy"))
            return "NoShow";
        return "Completed";
    }

    private static DateTime ParseCellDate(IXLCell cell)
    {
        if (cell.IsEmpty()) return DateTime.Today;
        if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime().Date;
        if (cell.DataType == XLDataType.Number)
        {
            try { return DateTime.FromOADate(cell.GetDouble()).Date; }
            catch { /* ignore */ }
        }
        return ParseDateString(cell.GetString());
    }

    private static DateTime ParseDateString(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return DateTime.Today;
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "MM/dd/yyyy" };
        if (DateTime.TryParseExact(str.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        if (DateTime.TryParse(str.Trim(), new CultureInfo("vi-VN"), DateTimeStyles.None, out dt))
            return dt.Date;
        return DateTime.Today;
    }

    private static TimeSpan ParseTime(string? str)
    {
        if (!string.IsNullOrWhiteSpace(str) && TimeSpan.TryParse(str.Trim(), out var ts))
            return ts;
        return new TimeSpan(9, 30, 0);
    }

    private static decimal ParseAmount(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return 0m;
        var cleaned = Regex.Replace(str, @"[^\d]", "");
        return decimal.TryParse(cleaned, out var val) ? val : 0m;
    }

    private static List<string> SplitCsvLine(string line)
    {
        char delimiter = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return result;
    }

    private sealed class RawLegacyRow
    {
        public int RowIndex { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public DateTime AppointmentDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public string ServiceName { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string Status { get; set; } = "Completed";
    }
}
