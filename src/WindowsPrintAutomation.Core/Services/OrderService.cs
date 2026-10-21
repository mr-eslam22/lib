using System.Data;
using Microsoft.Data.Sqlite;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Services;

public class PreflightValidationResult
{
    public bool IsValid => Issues.Count == 0;
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int TotalEstimatedSheets { get; set; }
}

public interface IOrderService
{
    Task<Order> CreateOrderAsync(Order order, string? defaultPrinterName = null);
    Task<Order?> GetOrderByIdAsync(long id);
    Task<Order?> GetOrderByNumberAsync(string orderNumber);
    Task<List<Order>> GetOrdersAsync(OrderStatus? status = null, string? searchQuery = null, int limit = 100);
    Task<bool> UpdateOrderStatusAsync(long orderId, OrderStatus status);
    PreflightValidationResult ValidateOrder(Order order, Printer? targetPrinter = null);
    int CalculateItemSheets(int pageCount, int copies, PrintDuplexOption duplex);
    Task<string> GenerateNextOrderNumberAsync();
}

public class OrderService : IOrderService
{
    private readonly DatabaseService _databaseService;

    public OrderService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public int CalculateItemSheets(int pageCount, int copies, PrintDuplexOption duplex)
    {
        if (pageCount <= 0 || copies <= 0) return 0;

        if (duplex == PrintDuplexOption.Simplex)
        {
            return copies * pageCount;
        }

        // For duplex: 2 pages per physical sheet
        int sheetsPerCopy = (pageCount + 1) / 2;
        return copies * sheetsPerCopy;
    }

    public PreflightValidationResult ValidateOrder(Order order, Printer? targetPrinter = null)
    {
        var result = new PreflightValidationResult();

        if (order.CustomerId <= 0 && string.IsNullOrWhiteSpace(order.CustomerName))
        {
            result.Issues.Add("بيانات العميل غير محددة.");
        }

        if (order.Items == null || order.Items.Count == 0)
        {
            result.Issues.Add("الطلب لا يحتوي على أي كتب مختارة.");
            return result;
        }

        if (targetPrinter != null)
        {
            if (!targetPrinter.IsActive)
            {
                result.Issues.Add($"الماكينة المحددة '{targetPrinter.WindowsPrinterName}' معطلة في النظام.");
            }
            if (!targetPrinter.IsOnline)
            {
                result.Warnings.Add($"الماكينة المحددة '{targetPrinter.WindowsPrinterName}' غير متصلة حاليًا (Offline).");
            }
        }

        int totalSheets = 0;

        for (int i = 0; i < order.Items.Count; i++)
        {
            var item = order.Items[i];
            var bookLabel = string.IsNullOrWhiteSpace(item.BookTitle) ? $"عنصر #{i + 1}" : item.BookTitle;

            if (item.Copies <= 0)
            {
                result.Issues.Add($"{bookLabel}: عدد النسخ يجب أن يكون 1 على الأقل.");
            }

            if (item.PageCount <= 0)
            {
                result.Warnings.Add($"{bookLabel}: عدد الصفحات المسجل هو 0، سيتم الفحص عند المعالجة.");
            }

            if (!string.IsNullOrWhiteSpace(item.FilePath))
            {
                if (!File.Exists(item.FilePath))
                {
                    result.Issues.Add($"{bookLabel}: ملف PDF غير موجود على المسار ({item.FilePath}).");
                }
                else
                {
                    try
                    {
                        using var fs = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (fs.Length == 0)
                        {
                            result.Issues.Add($"{bookLabel}: ملف PDF فارغ (0 بايت).");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Issues.Add($"{bookLabel}: لا يمكن قراءة الملف (مقفل أو تالف): {ex.Message}");
                    }
                }
            }
            else
            {
                result.Issues.Add($"{bookLabel}: مسار الملف فارغ.");
            }

            // Target printer capability check
            if (targetPrinter != null)
            {
                var sizeStr = item.PaperSize.ToString();
                if (!targetPrinter.SupportedPaperSizes.Contains(sizeStr, StringComparison.OrdinalIgnoreCase))
                {
                    result.Issues.Add($"{bookLabel}: مقاس الورق ({sizeStr}) غير مدعوم على الماكينة المحددة ({targetPrinter.WindowsPrinterName}).");
                }

                if (item.Duplex != PrintDuplexOption.Simplex && !targetPrinter.SupportsDuplex)
                {
                    result.Issues.Add($"{bookLabel}: الطباعة على الوجهين غير مدعومة على الماكينة المحددة ({targetPrinter.WindowsPrinterName}).");
                }

                if (item.ColorMode == PrintColorOption.Color && !targetPrinter.SupportsColor)
                {
                    result.Issues.Add($"{bookLabel}: الطباعة بالألوان غير مدعومة على الماكينة المحددة ({targetPrinter.WindowsPrinterName}).");
                }
            }

            item.EstimatedSheets = CalculateItemSheets(item.PageCount, item.Copies, item.Duplex);
            totalSheets += item.EstimatedSheets;
        }

        result.TotalEstimatedSheets = totalSheets;
        return result;
    }

    public async Task<string> GenerateNextOrderNumberAsync()
    {
        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Orders WHERE OrderNumber LIKE @prefix";
        cmd.Parameters.AddWithValue("@prefix", $"ORD-{datePrefix}-%");
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return $"ORD-{datePrefix}-{(count + 1):D4}";
    }

    public async Task<Order> CreateOrderAsync(Order order, string? defaultPrinterName = null)
    {
        var validation = ValidateOrder(order);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"فشل الفحص المسبق للطلب:\n{string.Join("\n", validation.Issues)}");
        }

        if (string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            order.OrderNumber = await GenerateNextOrderNumberAsync();
        }

        order.CreatedAt = DateTime.UtcNow;
        order.Status = OrderStatus.Pending;
        order.TotalItemsCount = order.Items.Count;
        order.TotalEstimatedSheets = validation.TotalEstimatedSheets;

        using var conn = _databaseService.CreateConnection();
        using var transaction = conn.BeginTransaction();

        try
        {
            // 1. Insert Order
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO Orders (OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt, DeliveryDate, Notes, Status, TotalItemsCount, TotalEstimatedSheets)
                    VALUES (@num, @custId, @custName, @custPhone, @created, @delivery, @notes, @status, @totalItems, @totalSheets)
                    RETURNING Id;";
                cmd.Parameters.AddWithValue("@num", order.OrderNumber);
                cmd.Parameters.AddWithValue("@custId", order.CustomerId);
                cmd.Parameters.AddWithValue("@custName", order.CustomerName);
                cmd.Parameters.AddWithValue("@custPhone", order.CustomerPhone);
                cmd.Parameters.AddWithValue("@created", order.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@delivery", (object?)order.DeliveryDate?.ToString("o") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", (object?)order.Notes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@status", (int)order.Status);
                cmd.Parameters.AddWithValue("@totalItems", order.TotalItemsCount);
                cmd.Parameters.AddWithValue("@totalSheets", order.TotalEstimatedSheets);

                order.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            // 2. Insert Items and create corresponding PrintJobs
            int itemIndex = 1;
            foreach (var item in order.Items)
            {
                item.OrderId = order.Id;
                item.EstimatedSheets = CalculateItemSheets(item.PageCount, item.Copies, item.Duplex);
                item.Status = JobStatus.Queued;
                item.VerifiedCopies = 0;
                item.RemainingCopies = item.Copies;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO OrderItems (OrderId, BookId, BookTitle, Grade, Subject, FilePath, PageCount, Copies, PaperSize, Duplex, ColorMode, PaperTray, EstimatedSheets, Status, VerifiedCopies, RemainingCopies)
                        VALUES (@ordId, @bId, @title, @grade, @subj, @path, @pages, @copies, @size, @duplex, @color, @tray, @estSheets, @status, @verified, @remaining)
                        RETURNING Id;";
                    cmd.Parameters.AddWithValue("@ordId", item.OrderId);
                    cmd.Parameters.AddWithValue("@bId", item.BookId);
                    cmd.Parameters.AddWithValue("@title", item.BookTitle);
                    cmd.Parameters.AddWithValue("@grade", item.Grade);
                    cmd.Parameters.AddWithValue("@subj", item.Subject);
                    cmd.Parameters.AddWithValue("@path", item.FilePath);
                    cmd.Parameters.AddWithValue("@pages", item.PageCount);
                    cmd.Parameters.AddWithValue("@copies", item.Copies);
                    cmd.Parameters.AddWithValue("@size", (int)item.PaperSize);
                    cmd.Parameters.AddWithValue("@duplex", (int)item.Duplex);
                    cmd.Parameters.AddWithValue("@color", (int)item.ColorMode);
                    cmd.Parameters.AddWithValue("@tray", (object?)item.PaperTray ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@estSheets", item.EstimatedSheets);
                    cmd.Parameters.AddWithValue("@status", (int)item.Status);
                    cmd.Parameters.AddWithValue("@verified", item.VerifiedCopies);
                    cmd.Parameters.AddWithValue("@remaining", item.RemainingCopies);

                    item.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                }

                // Create PrintJob
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    var jobNumber = $"{order.OrderNumber}-J{itemIndex:D2}";
                    cmd.CommandText = @"
                        INSERT INTO PrintJobs (JobNumber, OrderId, OrderItemId, PrinterName, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, CreatedAt)
                        VALUES (@jobNum, @ordId, @itemId, @printer, @status, @verStatus, @req, 0, 0, @rem, @pages, 1, @pages, @created)
                        RETURNING Id;";
                    cmd.Parameters.AddWithValue("@jobNum", jobNumber);
                    cmd.Parameters.AddWithValue("@ordId", order.Id);
                    cmd.Parameters.AddWithValue("@itemId", item.Id);
                    cmd.Parameters.AddWithValue("@printer", defaultPrinterName ?? "افتراضية");
                    cmd.Parameters.AddWithValue("@status", (int)JobStatus.Queued);
                    cmd.Parameters.AddWithValue("@verStatus", (int)VerificationStatus.NeedsVerification);
                    cmd.Parameters.AddWithValue("@req", item.Copies);
                    cmd.Parameters.AddWithValue("@rem", item.Copies);
                    cmd.Parameters.AddWithValue("@pages", item.PageCount);
                    cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));

                    var jobId = Convert.ToInt64(await cmd.ExecuteScalarAsync());

                    // Record initial event
                    using var eventCmd = conn.CreateCommand();
                    eventCmd.Transaction = transaction;
                    eventCmd.CommandText = @"
                        INSERT INTO PrintJobEvents (PrintJobId, Timestamp, OldStatus, NewStatus, VerificationStatus, EventSource, Message, Details)
                        VALUES (@jId, @time, @oldSt, @newSt, @vSt, @src, @msg, @details)";
                    eventCmd.Parameters.AddWithValue("@jId", jobId);
                    eventCmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
                    eventCmd.Parameters.AddWithValue("@oldSt", (int)JobStatus.Queued);
                    eventCmd.Parameters.AddWithValue("@newSt", (int)JobStatus.Queued);
                    eventCmd.Parameters.AddWithValue("@vSt", (int)VerificationStatus.NeedsVerification);
                    eventCmd.Parameters.AddWithValue("@src", "OrderService");
                    eventCmd.Parameters.AddWithValue("@msg", "تم إنشاء مهمة الطباعة وإدراجها في الطابور");
                    eventCmd.Parameters.AddWithValue("@details", $"كتاب: {item.BookTitle} | نسخ: {item.Copies}");
                    await eventCmd.ExecuteNonQueryAsync();
                }

                itemIndex++;
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return order;
    }

    public async Task<Order?> GetOrderByIdAsync(long id)
    {
        using var conn = _databaseService.CreateConnection();
        Order? order = null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt, DeliveryDate, Notes, Status, TotalItemsCount, TotalEstimatedSheets FROM Orders WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                order = new Order
                {
                    Id = reader.GetInt64(0),
                    OrderNumber = reader.GetString(1),
                    CustomerId = reader.GetInt64(2),
                    CustomerName = reader.GetString(3),
                    CustomerPhone = reader.GetString(4),
                    CreatedAt = DateTime.Parse(reader.GetString(5)),
                    DeliveryDate = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                    Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Status = (OrderStatus)reader.GetInt32(8),
                    TotalItemsCount = reader.GetInt32(9),
                    TotalEstimatedSheets = reader.GetInt32(10)
                };
            }
        }

        if (order != null)
        {
            order.Items = await GetOrderItemsAsync(order.Id);
        }

        return order;
    }

    public async Task<Order?> GetOrderByNumberAsync(string orderNumber)
    {
        using var conn = _databaseService.CreateConnection();
        Order? order = null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt, DeliveryDate, Notes, Status, TotalItemsCount, TotalEstimatedSheets FROM Orders WHERE OrderNumber = @num";
            cmd.Parameters.AddWithValue("@num", orderNumber);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                order = new Order
                {
                    Id = reader.GetInt64(0),
                    OrderNumber = reader.GetString(1),
                    CustomerId = reader.GetInt64(2),
                    CustomerName = reader.GetString(3),
                    CustomerPhone = reader.GetString(4),
                    CreatedAt = DateTime.Parse(reader.GetString(5)),
                    DeliveryDate = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                    Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Status = (OrderStatus)reader.GetInt32(8),
                    TotalItemsCount = reader.GetInt32(9),
                    TotalEstimatedSheets = reader.GetInt32(10)
                };
            }
        }

        if (order != null)
        {
            order.Items = await GetOrderItemsAsync(order.Id);
        }

        return order;
    }

    public async Task<List<OrderItem>> GetOrderItemsAsync(long orderId)
    {
        var items = new List<OrderItem>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, OrderId, BookId, BookTitle, Grade, Subject, FilePath, PageCount, Copies, PaperSize, Duplex, ColorMode, PaperTray, EstimatedSheets, Status, VerifiedCopies, RemainingCopies 
            FROM OrderItems WHERE OrderId = @ordId";
        cmd.Parameters.AddWithValue("@ordId", orderId);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new OrderItem
            {
                Id = reader.GetInt64(0),
                OrderId = reader.GetInt64(1),
                BookId = reader.GetInt64(2),
                BookTitle = reader.GetString(3),
                Grade = reader.GetString(4),
                Subject = reader.GetString(5),
                FilePath = reader.GetString(6),
                PageCount = reader.GetInt32(7),
                Copies = reader.GetInt32(8),
                PaperSize = (PaperSizeOption)reader.GetInt32(9),
                Duplex = (PrintDuplexOption)reader.GetInt32(10),
                ColorMode = (PrintColorOption)reader.GetInt32(11),
                PaperTray = reader.IsDBNull(12) ? null : reader.GetString(12),
                EstimatedSheets = reader.GetInt32(13),
                Status = (JobStatus)reader.GetInt32(14),
                VerifiedCopies = reader.GetInt32(15),
                RemainingCopies = reader.GetInt32(16)
            });
        }
        return items;
    }

    public async Task<List<Order>> GetOrdersAsync(OrderStatus? status = null, string? searchQuery = null, int limit = 100)
    {
        var list = new List<Order>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();

        var query = "SELECT Id, OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt, DeliveryDate, Notes, Status, TotalItemsCount, TotalEstimatedSheets FROM Orders WHERE 1=1";

        if (status.HasValue)
        {
            query += " AND Status = @status";
            cmd.Parameters.AddWithValue("@status", (int)status.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            query += " AND (OrderNumber LIKE @q OR CustomerName LIKE @q OR CustomerPhone LIKE @q)";
            cmd.Parameters.AddWithValue("@q", $"%{searchQuery}%");
        }

        query += " ORDER BY CreatedAt DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.CommandText = query;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Order
            {
                Id = reader.GetInt64(0),
                OrderNumber = reader.GetString(1),
                CustomerId = reader.GetInt64(2),
                CustomerName = reader.GetString(3),
                CustomerPhone = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5)),
                DeliveryDate = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                Status = (OrderStatus)reader.GetInt32(8),
                TotalItemsCount = reader.GetInt32(9),
                TotalEstimatedSheets = reader.GetInt32(10)
            });
        }

        return list;
    }

    public async Task<bool> UpdateOrderStatusAsync(long orderId, OrderStatus status)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Orders SET Status = @st WHERE Id = @id";
        cmd.Parameters.AddWithValue("@st", (int)status);
        cmd.Parameters.AddWithValue("@id", orderId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
