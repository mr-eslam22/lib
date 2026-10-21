using System.IO;
using UglyToad.PdfPig.Writer;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using Xunit;

namespace WindowsPrintAutomation.Tests;

public class OrderManagementTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseService _dbService;
    private readonly OrderService _orderService;
    private readonly CustomerService _customerService;

    public OrderManagementTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinPrintTest_Order_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbService = new DatabaseService(Path.Combine(_tempDir, "orders_test.db"));
        _orderService = new OrderService(_dbService);
        _customerService = new CustomerService(_dbService);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreateTestPdf(string filename, int pages = 10)
    {
        var path = Path.Combine(_tempDir, filename);
        var builder = new PdfDocumentBuilder();
        for (int i = 0; i < pages; i++)
        {
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        }
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Theory]
    [InlineData(10, 5, PrintDuplexOption.Simplex, 50)]             // 10 pages * 5 copies = 50 sheets
    [InlineData(10, 5, PrintDuplexOption.DuplexLongEdge, 25)]       // 10 pages duplex = 5 sheets per copy * 5 copies = 25 sheets
    [InlineData(11, 5, PrintDuplexOption.DuplexLongEdge, 30)]       // 11 pages duplex = 6 sheets per copy * 5 copies = 30 sheets
    [InlineData(1, 1, PrintDuplexOption.DuplexLongEdge, 1)]         // 1 page duplex = 1 sheet
    [InlineData(0, 5, PrintDuplexOption.Simplex, 0)]                // 0 pages = 0 sheets
    public void CalculateItemSheets_GivenPagesAndCopies_ReturnsAccurateSheetCount(int pages, int copies, PrintDuplexOption duplex, int expectedSheets)
    {
        int sheets = _orderService.CalculateItemSheets(pages, copies, duplex);
        Assert.Equal(expectedSheets, sheets);
    }

    [Fact]
    public async Task CreateOrderAsync_ValidOrderWithMultipleBooks_CreatesOrderAndLinkedPrintJobs()
    {
        var cust = await _customerService.CreateCustomerAsync("أحمد محمود", "01012345678");

        var pdf1 = CreateTestPdf("arabic.pdf", 20);
        var pdf2 = CreateTestPdf("math.pdf", 15);

        // Seed books in DB
        long book1Id, book2Id;
        using (var conn = _dbService.CreateConnection())
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO Books (Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, LastScannedAt) VALUES ('كتاب العربي', 'الأول الثانوي', 'عربي', @p1, 'arabic.pdf', 'h1', 100, 20, @now) RETURNING Id;";
                cmd.Parameters.AddWithValue("@p1", pdf1);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                book1Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO Books (Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, LastScannedAt) VALUES ('كتاب الرياضيات', 'الأول الثانوي', 'رياضيات', @p2, 'math.pdf', 'h2', 100, 15, @now) RETURNING Id;";
                cmd.Parameters.AddWithValue("@p2", pdf2);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                book2Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
        }

        var order = new Order
        {
            CustomerId = cust.Id,
            CustomerName = cust.FullName,
            CustomerPhone = cust.PhoneNumber,
            Notes = "تسليم عاجل",
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    BookId = book1Id,
                    BookTitle = "كتاب العربي",
                    Grade = "الأول الثانوي",
                    Subject = "عربي",
                    FilePath = pdf1,
                    PageCount = 20,
                    Copies = 10,
                    PaperSize = PaperSizeOption.A4,
                    Duplex = PrintDuplexOption.DuplexLongEdge
                },
                new OrderItem
                {
                    BookId = book2Id,
                    BookTitle = "كتاب الرياضيات",
                    Grade = "الأول الثانوي",
                    Subject = "رياضيات",
                    FilePath = pdf2,
                    PageCount = 15,
                    Copies = 5,
                    PaperSize = PaperSizeOption.B5,
                    Duplex = PrintDuplexOption.Simplex
                }
            }
        };

        var created = await _orderService.CreateOrderAsync(order, defaultPrinterName: "HP LaserJet");

        Assert.True(created.Id > 0);
        Assert.StartsWith("ORD-", created.OrderNumber);
        Assert.Equal(2, created.TotalItemsCount);

        // Item 1 sheets: 20 pages duplex * 10 copies = 100 sheets
        // Item 2 sheets: 15 pages simplex * 5 copies = 75 sheets
        // Total: 175 sheets
        Assert.Equal(175, created.TotalEstimatedSheets);

        // Verify PrintJobs were created in Queued state
        using (var conn = _dbService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM PrintJobs WHERE OrderId = @ordId AND Status = @queued";
            cmd.Parameters.AddWithValue("@ordId", created.Id);
            cmd.Parameters.AddWithValue("@queued", (int)JobStatus.Queued);
            var jobsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(2, jobsCount);
        }
    }

    [Fact]
    public void ValidateOrder_MissingFile_ReturnsValidationIssue()
    {
        var order = new Order
        {
            CustomerName = "عميل تجريبي",
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    BookTitle = "كتاب وهمي مفقود",
                    FilePath = @"C:\NonExistent\file.pdf",
                    PageCount = 50,
                    Copies = 5
                }
            }
        };

        var result = _orderService.ValidateOrder(order);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Contains("غير موجود"));
    }

    [Fact]
    public async Task DeleteOrderAsync_ExistingOrderWithPrintJobs_DeletesSuccessfullyWithoutForeignKeyViolation()
    {
        var cust = await _customerService.CreateCustomerAsync("خالد علي", "01234567890");
        var pdf = CreateTestPdf("history.pdf", 10);

        long bookId;
        using (var conn = _dbService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Books (Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, LastScannedAt) VALUES ('تاريخ', 'الثاني الثانوي', 'تاريخ', @p, 'history.pdf', 'h3', 100, 10, @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@p", pdf);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            bookId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        var order = new Order
        {
            CustomerId = cust.Id,
            CustomerName = cust.FullName,
            CustomerPhone = cust.PhoneNumber,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    BookId = bookId,
                    BookTitle = "تاريخ",
                    Grade = "الثاني الثانوي",
                    Subject = "تاريخ",
                    FilePath = pdf,
                    PageCount = 10,
                    Copies = 2,
                    PaperSize = PaperSizeOption.A4,
                    Duplex = PrintDuplexOption.Simplex
                }
            }
        };

        var created = await _orderService.CreateOrderAsync(order);
        Assert.True(created.Id > 0);

        // Verify order exists
        var fetched = await _orderService.GetOrderByIdAsync(created.Id);
        Assert.NotNull(fetched);

        // Execute Delete
        var deleted = await _orderService.DeleteOrderAsync(created.Id);
        Assert.True(deleted);

        // Verify order is removed from database
        var afterDelete = await _orderService.GetOrderByIdAsync(created.Id);
        Assert.Null(afterDelete);

        // Verify child tables were cleaned up
        using (var conn = _dbService.CreateConnection())
        {
            using var cmd1 = conn.CreateCommand();
            cmd1.CommandText = "SELECT COUNT(*) FROM OrderItems WHERE OrderId = @id";
            cmd1.Parameters.AddWithValue("@id", created.Id);
            Assert.Equal(0L, Convert.ToInt64(await cmd1.ExecuteScalarAsync()));

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT COUNT(*) FROM PrintJobs WHERE OrderId = @id";
            cmd2.Parameters.AddWithValue("@id", created.Id);
            Assert.Equal(0L, Convert.ToInt64(await cmd2.ExecuteScalarAsync()));
        }
    }
}

