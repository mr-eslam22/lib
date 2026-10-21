using System.IO;
using UglyToad.PdfPig.Writer;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using WindowsPrintAutomation.PrintEngine;
using WindowsPrintAutomation.PrintEngine.Adapters;
using Xunit;

namespace WindowsPrintAutomation.Tests;

public class PrintEngineAndQueueTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseService _dbService;
    private readonly RecoveryService _recoveryService;
    private readonly FakePrinterAdapter _fakeAdapter;
    private readonly PrintEngine.PrintEngine _printEngine;

    public PrintEngineAndQueueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinPrintTest_Engine_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbService = new DatabaseService(Path.Combine(_tempDir, "engine_test.db"));
        _recoveryService = new RecoveryService(_dbService);
        _fakeAdapter = new FakePrinterAdapter();
        _printEngine = new PrintEngine.PrintEngine(_dbService, _recoveryService, _fakeAdapter);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreatePdf(string name, int pages = 5)
    {
        var path = Path.Combine(_tempDir, name);
        var builder = new PdfDocumentBuilder();
        for (int i = 0; i < pages; i++)
        {
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        }
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private async Task<(long OrderId, long OrderItemId, long JobId)> CreateJobAsync(string printerName, string pdfPath, int copies = 10)
    {
        using var conn = _dbService.CreateConnection();
        var now = DateTime.UtcNow.ToString("o");

        long custId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Customers (CustomerCode, FullName, PhoneNumber, CreatedAt) VALUES (@code, 'عميل', '010', @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@code", $"CUST-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("@now", now);
            custId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long bookId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Books (Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, LastScannedAt) VALUES ('كتاب', 'الأول', 'عربي', @p, 'book.pdf', @hash, 100, 5, @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@p", pdfPath);
            cmd.Parameters.AddWithValue("@hash", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@now", now);
            bookId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long ordId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Orders (OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt) VALUES (@ordNum, @cId, 'عميل', '010', @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@ordNum", $"ORD-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("@cId", custId);
            cmd.Parameters.AddWithValue("@now", now);
            ordId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long itemId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO OrderItems (OrderId, BookId, BookTitle, Grade, Subject, FilePath, PageCount, Copies, Status, RemainingCopies) VALUES (@oId, @bId, 'كتاب', 'الأول', 'عربي', @p, 5, @c, 0, @c) RETURNING Id;";
            cmd.Parameters.AddWithValue("@oId", ordId);
            cmd.Parameters.AddWithValue("@bId", bookId);
            cmd.Parameters.AddWithValue("@p", pdfPath);
            cmd.Parameters.AddWithValue("@c", copies);
            itemId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long jobId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO PrintJobs (JobNumber, OrderId, OrderItemId, PrinterName, Status, VerificationStatus, CopiesRequested, CopiesRemaining, PageCount, CreatedAt) VALUES (@jobNum, @oId, @iId, @pr, 0, @vSt, @c, @c, 5, @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@jobNum", $"JOB-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("@oId", ordId);
            cmd.Parameters.AddWithValue("@iId", itemId);
            cmd.Parameters.AddWithValue("@pr", printerName);
            cmd.Parameters.AddWithValue("@vSt", (int)VerificationStatus.NeedsVerification);
            cmd.Parameters.AddWithValue("@c", copies);
            cmd.Parameters.AddWithValue("@now", now);
            jobId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        return (ordId, itemId, jobId);
    }

    [Fact]
    public async Task ProcessSingleJobAsync_SuccessfulSpool_RequiresPhysicalVerificationAndNeverAutoMarksPhysicallyVerified()
    {
        var pdfPath = CreatePdf("book.pdf", 5);
        var (_, _, jobId) = await CreateJobAsync("HP LaserJet Pro MFP M428fdw", pdfPath, copies: 10);

        var result = await _printEngine.ProcessSingleJobAsync(jobId);

        Assert.True(result.Success);
        Assert.NotNull(result.SpoolerJobId);

        var finishedJob = await _recoveryService.GetJobByIdAsync(jobId);
        Assert.NotNull(finishedJob);

        // Verification MUST NOT be PhysicallyVerified automatically!
        Assert.NotEqual(VerificationStatus.PhysicallyVerified, finishedJob.VerificationStatus);
        Assert.Equal(VerificationStatus.NeedsVerification, finishedJob.VerificationStatus);
        Assert.Equal(JobStatus.NeedsAttention, finishedJob.Status);
    }

    [Fact]
    public async Task ProcessSingleJobAsync_MachineError_IsolatesFaultyPrinterAndLogsError()
    {
        var pdfPath = CreatePdf("book_jam.pdf", 5);
        var (_, _, jobId) = await CreateJobAsync("HP LaserJet Pro MFP M428fdw", pdfPath, copies: 10);

        // Simulate Paper Jam on next job
        _fakeAdapter.ForcedErrorNextJob = ErrorType.PaperJam;
        _fakeAdapter.ForcedErrorMessage = "انحشار ورق في وحدة التغذية المزدوجة";

        var result = await _printEngine.ProcessSingleJobAsync(jobId);

        Assert.False(result.Success);
        Assert.Equal(ErrorType.PaperJam, result.ErrorType);

        // Verify error logged in ErrorLogs table
        var errorLogs = await _recoveryService.GetErrorLogsAsync();
        Assert.NotEmpty(errorLogs);

        var jamLog = errorLogs.First(e => e.PrintJobId == jobId);
        Assert.Equal(ErrorType.PaperJam, jamLog.ErrorType);
        Assert.Equal(ErrorSource.WindowsSpooler, jamLog.ErrorSource);
        Assert.Contains("انحشار ورق", jamLog.ErrorMessage);
        Assert.Contains("إيقاف", jamLog.SystemActionTaken);
    }
}
