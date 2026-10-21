using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using Xunit;

namespace WindowsPrintAutomation.Tests;

public class CrashRecoveryAndMathTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseService _dbService;
    private readonly RecoveryService _recoveryService;

    public CrashRecoveryAndMathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinPrintTest_Recovery_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbService = new DatabaseService(Path.Combine(_tempDir, "recovery_test.db"));
        _recoveryService = new RecoveryService(_dbService);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private async Task<(long OrderId, long OrderItemId, long JobId)> SeedJobAsync(JobStatus initialStatus, int copies = 20)
    {
        using var conn = _dbService.CreateConnection();

        long custId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Customers (CustomerCode, FullName, PhoneNumber, CreatedAt) VALUES ('CUST-01', 'عميل تجريبي', '0100', @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            custId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long ordId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Orders (OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt) VALUES ('ORD-TEST-01', @cId, 'عميل تجريبي', '0100', @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@cId", custId);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            ordId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long bookId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Books (Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, LastScannedAt) VALUES ('كتاب', 'الأول', 'عربي', 'path.pdf', 'path.pdf', 'h', 1, 50, @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            bookId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long itemId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO OrderItems (OrderId, BookId, BookTitle, Grade, Subject, FilePath, PageCount, Copies, Status, RemainingCopies) VALUES (@oId, @bId, 'كتاب', 'الأول', 'عربي', 'path.pdf', 50, @c, @st, @c) RETURNING Id;";
            cmd.Parameters.AddWithValue("@oId", ordId);
            cmd.Parameters.AddWithValue("@bId", bookId);
            cmd.Parameters.AddWithValue("@c", copies);
            cmd.Parameters.AddWithValue("@st", (int)initialStatus);
            itemId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        long jobId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO PrintJobs (JobNumber, OrderId, OrderItemId, PrinterName, Status, VerificationStatus, CopiesRequested, CopiesRemaining, PageCount, CreatedAt) VALUES ('JOB-01', @oId, @iId, 'HP LaserJet', @st, @vSt, @c, @c, 50, @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@oId", ordId);
            cmd.Parameters.AddWithValue("@iId", itemId);
            cmd.Parameters.AddWithValue("@st", (int)initialStatus);
            cmd.Parameters.AddWithValue("@vSt", (int)VerificationStatus.NeedsVerification);
            cmd.Parameters.AddWithValue("@c", copies);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            jobId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        return (ordId, itemId, jobId);
    }

    [Fact]
    public async Task CheckAndFlagIncompleteJobsOnStartup_InterruptedJob_SafelyFlagsRecoveryRequiredWithoutAutoResend()
    {
        // Simulate app crash while printing
        var (_, _, jobId) = await SeedJobAsync(JobStatus.Printing, copies: 20);

        // App starts up and runs recovery check
        var result = await _recoveryService.CheckAndFlagIncompleteJobsOnStartupAsync();

        Assert.Equal(1, result.OrphanedJobsCount);
        Assert.Single(result.JobsRequiringAttention);

        var flaggedJob = await _recoveryService.GetJobByIdAsync(jobId);
        Assert.NotNull(flaggedJob);

        // MUST be RecoveryRequired and NOT auto-resubmitted!
        Assert.Equal(JobStatus.RecoveryRequired, flaggedJob.Status);
        Assert.Equal(VerificationStatus.NeedsVerification, flaggedJob.VerificationStatus);

        // Verify audit event exists
        using var conn = _dbService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM PrintJobEvents WHERE PrintJobId = @jId AND NewStatus = @rec";
        cmd.Parameters.AddWithValue("@jId", jobId);
        cmd.Parameters.AddWithValue("@rec", (int)JobStatus.RecoveryRequired);
        var eventsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.True(eventsCount > 0);
    }

    [Fact]
    public async Task ConfirmPhysicalVerificationAsync_PartialGoodCopies_AccuratelyCalculatesRemainingAndRecordsAudit()
    {
        // 20 copies requested
        var (_, _, jobId) = await SeedJobAsync(JobStatus.RecoveryRequired, copies: 20);

        // Operator counts: 7 good copies, 1 damaged copy
        var updatedJob = await _recoveryService.ConfirmPhysicalVerificationAsync(
            jobId,
            verifiedCopies: 7,
            damagedCopies: 1,
            operatorNotes: "تم فحص الملازم يدويًا: 7 خرجت ممتازة و1 انحشرت بالدرج");

        Assert.Equal(7, updatedJob.CopiesVerified);
        Assert.Equal(1, updatedJob.CopiesDamaged);
        Assert.Equal(13, updatedJob.CopiesRemaining); // 20 - 7 = 13!
        Assert.Equal(JobStatus.Paused, updatedJob.Status);

        // Verify audit log has the decision
        using var conn = _dbService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Message, Details FROM PrintJobEvents WHERE PrintJobId = @jId ORDER BY Id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@jId", jobId);
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Contains("سليم 7", reader.GetString(0));
        Assert.Contains("متبقي 13", reader.GetString(0));
        Assert.Contains("تم فحص الملازم يدويًا", reader.GetString(1));
    }

    [Fact]
    public async Task CreateContinuationJobAsync_GivenRemainingCopies_CreatesLinkedSubJob()
    {
        var (_, _, parentJobId) = await SeedJobAsync(JobStatus.Paused, copies: 20);
        await _recoveryService.ConfirmPhysicalVerificationAsync(parentJobId, verifiedCopies: 7, damagedCopies: 0, "فحص جزئي");

        // Create continuation job for the remaining 13 copies
        var continuation = await _recoveryService.CreateContinuationJobAsync(
            parentJobId,
            remainingCopies: 13,
            startPage: 1,
            endPage: 50,
            targetPrinter: "Kyocera TASKalfa 3212i");

        Assert.NotNull(continuation);
        Assert.True(continuation.Id > 0);
        Assert.Equal(parentJobId, continuation.ParentJobId);
        Assert.True(continuation.IsContinuation);
        Assert.Equal(13, continuation.CopiesRequested);
        Assert.Equal(13, continuation.CopiesRemaining);
        Assert.Equal(0, continuation.CopiesVerified);
        Assert.Equal("Kyocera TASKalfa 3212i", continuation.PrinterName);
        Assert.Equal(JobStatus.Queued, continuation.Status);
    }

    [Theory]
    [InlineData(14, 50, PrintDuplexOption.DuplexLongEdge, 15, 50)]  // Even page confirmed: both sides of sheet printed -> start page 15
    [InlineData(13, 50, PrintDuplexOption.DuplexLongEdge, 13, 50)]  // Odd page confirmed: only front printed -> restart sheet at page 13
    [InlineData(20, 50, PrintDuplexOption.Simplex, 21, 50)]         // Simplex -> next page is 21
    public void CalculateSafeDuplexPageRange_GivenLastVerifiedPage_CalculatesCorrectResumePage(int lastVerified, int total, PrintDuplexOption duplex, int expectedStart, int expectedEnd)
    {
        var (start, end) = _recoveryService.CalculateSafeDuplexPageRange(lastVerified, total, duplex);
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }
}
