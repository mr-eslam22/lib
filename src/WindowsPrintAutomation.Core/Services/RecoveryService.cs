using System.Data;
using Microsoft.Data.Sqlite;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Services;

public class RecoveryAnalysisResult
{
    public int OrphanedJobsCount { get; set; }
    public List<PrintJob> JobsRequiringAttention { get; set; } = new();
    public List<string> Messages { get; set; } = new();
}

public interface IRecoveryService
{
    Task<RecoveryAnalysisResult> CheckAndFlagIncompleteJobsOnStartupAsync();
    Task<List<PrintJob>> GetJobsRequiringRecoveryAsync();
    Task<PrintJob?> GetJobByIdAsync(long jobId);
    Task<PrintJob> ConfirmPhysicalVerificationAsync(long jobId, int verifiedCopies, int damagedCopies, string operatorNotes);
    Task<PrintJob> CreateContinuationJobAsync(long parentJobId, int remainingCopies, int startPage, int endPage, string? targetPrinter = null);
    Task LogErrorAsync(long? orderId, long? printJobId, string? printerName, string? bookTitle, ErrorType errorType, ErrorSource errorSource, string message, string systemAction, string? rawCode = null);
    Task<List<ErrorLog>> GetErrorLogsAsync(bool? unresolvedOnly = null, string? printerName = null, int limit = 100);
    Task<bool> ResolveErrorLogAsync(long errorLogId, string userActionTaken);
    (int startPage, int endPage) CalculateSafeDuplexPageRange(int lastVerifiedPage, int totalPages, PrintDuplexOption duplex);
}

public class RecoveryService : IRecoveryService
{
    private readonly DatabaseService _databaseService;

    public RecoveryService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<RecoveryAnalysisResult> CheckAndFlagIncompleteJobsOnStartupAsync()
    {
        var result = new RecoveryAnalysisResult();

        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();

        // Find jobs that were left in active/uncertain states when app closed
        cmd.CommandText = @"
            SELECT Id, JobNumber, OrderId, OrderItemId, PrinterName, SpoolerJobId, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, ParentJobId, IsContinuation, CreatedAt, SubmittedAt, CompletedAt, LastErrorMessage
            FROM PrintJobs 
            WHERE Status IN (@prep, @sub, @spool, @print, @attn)
            ORDER BY Id ASC";
        cmd.Parameters.AddWithValue("@prep", (int)JobStatus.Preparing);
        cmd.Parameters.AddWithValue("@sub", (int)JobStatus.Submitted);
        cmd.Parameters.AddWithValue("@spool", (int)JobStatus.Spooling);
        cmd.Parameters.AddWithValue("@print", (int)JobStatus.Printing);
        cmd.Parameters.AddWithValue("@attn", (int)JobStatus.NeedsAttention);

        var incompleteJobs = new List<PrintJob>();
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                incompleteJobs.Add(new PrintJob
                {
                    Id = reader.GetInt64(0),
                    JobNumber = reader.GetString(1),
                    OrderId = reader.GetInt64(2),
                    OrderItemId = reader.GetInt64(3),
                    PrinterName = reader.GetString(4),
                    SpoolerJobId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    Status = (JobStatus)reader.GetInt32(6),
                    VerificationStatus = (VerificationStatus)reader.GetInt32(7),
                    CopiesRequested = reader.GetInt32(8),
                    CopiesVerified = reader.GetInt32(9),
                    CopiesDamaged = reader.GetInt32(10),
                    CopiesRemaining = reader.GetInt32(11),
                    PageCount = reader.GetInt32(12),
                    StartPage = reader.GetInt32(13),
                    EndPage = reader.GetInt32(14),
                    ParentJobId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                    IsContinuation = reader.GetInt32(16) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(17)),
                    SubmittedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18)),
                    CompletedAt = reader.IsDBNull(19) ? null : DateTime.Parse(reader.GetString(19)),
                    LastErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20)
                });
            }
        }

        // Transition them safely to RecoveryRequired
        foreach (var job in incompleteJobs)
        {
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE PrintJobs SET 
                    Status = @recStatus,
                    VerificationStatus = @verStatus,
                    LastErrorMessage = @msg
                WHERE Id = @id";
            updateCmd.Parameters.AddWithValue("@recStatus", (int)JobStatus.RecoveryRequired);
            updateCmd.Parameters.AddWithValue("@verStatus", (int)VerificationStatus.NeedsVerification);
            updateCmd.Parameters.AddWithValue("@msg", "توقف البرنامج أو انقطاع غير متوقع أثناء المعالجة — يلزم تأكيد الموظف");
            updateCmd.Parameters.AddWithValue("@id", job.Id);
            await updateCmd.ExecuteNonQueryAsync();

            // Record event
            using var eventCmd = conn.CreateCommand();
            eventCmd.CommandText = @"
                INSERT INTO PrintJobEvents (PrintJobId, Timestamp, OldStatus, NewStatus, VerificationStatus, EventSource, Message, Details)
                VALUES (@jId, @time, @oldSt, @newSt, @vSt, @src, @msg, @details)";
            eventCmd.Parameters.AddWithValue("@jId", job.Id);
            eventCmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
            eventCmd.Parameters.AddWithValue("@oldSt", (int)job.Status);
            eventCmd.Parameters.AddWithValue("@newSt", (int)JobStatus.RecoveryRequired);
            eventCmd.Parameters.AddWithValue("@vSt", (int)VerificationStatus.NeedsVerification);
            eventCmd.Parameters.AddWithValue("@src", "RecoveryService.StartupCheck");
            eventCmd.Parameters.AddWithValue("@msg", "تحويل تلقائي لحالة استعادة بعد إعادة تشغيل التطبيق لمنع التكرار الصامت");
            eventCmd.Parameters.AddWithValue("@details", $"الحالة السابقة: {job.Status} | المطلوب: {job.CopiesRequested} نسخ");
            await eventCmd.ExecuteNonQueryAsync();

            job.Status = JobStatus.RecoveryRequired;
            job.VerificationStatus = VerificationStatus.NeedsVerification;
            result.JobsRequiringAttention.Add(job);
        }

        result.OrphanedJobsCount = incompleteJobs.Count;
        if (incompleteJobs.Count > 0)
        {
            result.Messages.Add($"تم العثور على {incompleteJobs.Count} مهمة غير مكتملة تم تحويلها إلى 'يلزم الاستعادة' لمنع تكرار الطباعة دون مراجعة.");
        }

        return result;
    }

    public async Task<List<PrintJob>> GetJobsRequiringRecoveryAsync()
    {
        var list = new List<PrintJob>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, JobNumber, OrderId, OrderItemId, PrinterName, SpoolerJobId, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, ParentJobId, IsContinuation, CreatedAt, SubmittedAt, CompletedAt, LastErrorMessage
            FROM PrintJobs 
            WHERE Status IN (@rec, @paused, @attn, @fail)
            ORDER BY Id DESC";
        cmd.Parameters.AddWithValue("@rec", (int)JobStatus.RecoveryRequired);
        cmd.Parameters.AddWithValue("@paused", (int)JobStatus.Paused);
        cmd.Parameters.AddWithValue("@attn", (int)JobStatus.NeedsAttention);
        cmd.Parameters.AddWithValue("@fail", (int)JobStatus.Failed);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PrintJob
            {
                Id = reader.GetInt64(0),
                JobNumber = reader.GetString(1),
                OrderId = reader.GetInt64(2),
                OrderItemId = reader.GetInt64(3),
                PrinterName = reader.GetString(4),
                SpoolerJobId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Status = (JobStatus)reader.GetInt32(6),
                VerificationStatus = (VerificationStatus)reader.GetInt32(7),
                CopiesRequested = reader.GetInt32(8),
                CopiesVerified = reader.GetInt32(9),
                CopiesDamaged = reader.GetInt32(10),
                CopiesRemaining = reader.GetInt32(11),
                PageCount = reader.GetInt32(12),
                StartPage = reader.GetInt32(13),
                EndPage = reader.GetInt32(14),
                ParentJobId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                IsContinuation = reader.GetInt32(16) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(17)),
                SubmittedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18)),
                CompletedAt = reader.IsDBNull(19) ? null : DateTime.Parse(reader.GetString(19)),
                LastErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20)
            });
        }
        return list;
    }

    public async Task<PrintJob?> GetJobByIdAsync(long jobId)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, JobNumber, OrderId, OrderItemId, PrinterName, SpoolerJobId, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, ParentJobId, IsContinuation, CreatedAt, SubmittedAt, CompletedAt, LastErrorMessage
            FROM PrintJobs WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", jobId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new PrintJob
            {
                Id = reader.GetInt64(0),
                JobNumber = reader.GetString(1),
                OrderId = reader.GetInt64(2),
                OrderItemId = reader.GetInt64(3),
                PrinterName = reader.GetString(4),
                SpoolerJobId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Status = (JobStatus)reader.GetInt32(6),
                VerificationStatus = (VerificationStatus)reader.GetInt32(7),
                CopiesRequested = reader.GetInt32(8),
                CopiesVerified = reader.GetInt32(9),
                CopiesDamaged = reader.GetInt32(10),
                CopiesRemaining = reader.GetInt32(11),
                PageCount = reader.GetInt32(12),
                StartPage = reader.GetInt32(13),
                EndPage = reader.GetInt32(14),
                ParentJobId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                IsContinuation = reader.GetInt32(16) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(17)),
                SubmittedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18)),
                CompletedAt = reader.IsDBNull(19) ? null : DateTime.Parse(reader.GetString(19)),
                LastErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20)
            };
        }
        return null;
    }

    public async Task<PrintJob> ConfirmPhysicalVerificationAsync(long jobId, int verifiedCopies, int damagedCopies, string operatorNotes)
    {
        var job = await GetJobByIdAsync(jobId);
        if (job == null) throw new KeyNotFoundException($"مهمة الطباعة #{jobId} غير موجودة");

        if (verifiedCopies < 0) throw new ArgumentOutOfRangeException(nameof(verifiedCopies), "عدد النسخ المؤكدة لا يمكن أن يكون سالبًا");
        if (damagedCopies < 0) throw new ArgumentOutOfRangeException(nameof(damagedCopies), "عدد النسخ التالفة لا يمكن أن يكون سالبًا");

        // Safe copy calculation
        int remaining = Math.Max(0, job.CopiesRequested - verifiedCopies);
        var oldStatus = job.Status;
        var newStatus = remaining == 0 ? JobStatus.Completed : JobStatus.Paused;
        var verStatus = remaining == 0 ? VerificationStatus.PhysicallyVerified : VerificationStatus.NeedsVerification;

        job.CopiesVerified = verifiedCopies;
        job.CopiesDamaged = damagedCopies;
        job.CopiesRemaining = remaining;
        job.Status = newStatus;
        job.VerificationStatus = verStatus;

        using var conn = _databaseService.CreateConnection();
        using var transaction = conn.BeginTransaction();

        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    UPDATE PrintJobs SET
                        CopiesVerified = @ver,
                        CopiesDamaged = @dmg,
                        CopiesRemaining = @rem,
                        Status = @st,
                        VerificationStatus = @vSt,
                        CompletedAt = @comp
                    WHERE Id = @id";
                cmd.Parameters.AddWithValue("@ver", verifiedCopies);
                cmd.Parameters.AddWithValue("@dmg", damagedCopies);
                cmd.Parameters.AddWithValue("@rem", remaining);
                cmd.Parameters.AddWithValue("@st", (int)newStatus);
                cmd.Parameters.AddWithValue("@vSt", (int)verStatus);
                cmd.Parameters.AddWithValue("@comp", (object?)(remaining == 0 ? DateTime.UtcNow.ToString("o") : null) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", job.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            // Update OrderItem verified and remaining
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    UPDATE OrderItems SET
                        VerifiedCopies = @ver,
                        RemainingCopies = @rem,
                        Status = @st
                    WHERE Id = @itemId";
                cmd.Parameters.AddWithValue("@ver", verifiedCopies);
                cmd.Parameters.AddWithValue("@rem", remaining);
                cmd.Parameters.AddWithValue("@st", (int)newStatus);
                cmd.Parameters.AddWithValue("@itemId", job.OrderItemId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Log Audit Event
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO PrintJobEvents (PrintJobId, Timestamp, OldStatus, NewStatus, VerificationStatus, EventSource, Message, Details)
                    VALUES (@jId, @time, @oldSt, @newSt, @vSt, @src, @msg, @details)";
                cmd.Parameters.AddWithValue("@jId", job.Id);
                cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@oldSt", (int)oldStatus);
                cmd.Parameters.AddWithValue("@newSt", (int)newStatus);
                cmd.Parameters.AddWithValue("@vSt", (int)verStatus);
                cmd.Parameters.AddWithValue("@src", "OperatorVerification");
                cmd.Parameters.AddWithValue("@msg", $"تأكيد فيزيائي: سليم {verifiedCopies} | تالف {damagedCopies} | متبقي {remaining}");
                cmd.Parameters.AddWithValue("@details", operatorNotes);
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return job;
    }

    public async Task<PrintJob> CreateContinuationJobAsync(long parentJobId, int remainingCopies, int startPage, int endPage, string? targetPrinter = null)
    {
        var parentJob = await GetJobByIdAsync(parentJobId);
        if (parentJob == null) throw new KeyNotFoundException($"المهمة الأصلية #{parentJobId} غير موجودة");

        if (remainingCopies <= 0)
        {
            throw new InvalidOperationException("الكمية المتبقية 0، لا حاجة لإنشاء مهمة استكمال.");
        }

        var continuationJobNumber = $"{parentJob.JobNumber}-R";
        var printer = string.IsNullOrWhiteSpace(targetPrinter) ? parentJob.PrinterName : targetPrinter;

        var continuationJob = new PrintJob
        {
            JobNumber = continuationJobNumber,
            OrderId = parentJob.OrderId,
            OrderItemId = parentJob.OrderItemId,
            PrinterName = printer,
            Status = JobStatus.Queued,
            VerificationStatus = VerificationStatus.NeedsVerification,
            CopiesRequested = remainingCopies,
            CopiesVerified = 0,
            CopiesDamaged = 0,
            CopiesRemaining = remainingCopies,
            PageCount = parentJob.PageCount,
            StartPage = startPage,
            EndPage = endPage > 0 ? endPage : parentJob.PageCount,
            ParentJobId = parentJob.Id,
            IsContinuation = true,
            CreatedAt = DateTime.UtcNow
        };

        using var conn = _databaseService.CreateConnection();
        using var transaction = conn.BeginTransaction();

        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO PrintJobs (JobNumber, OrderId, OrderItemId, PrinterName, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, ParentJobId, IsContinuation, CreatedAt)
                    VALUES (@num, @ordId, @itemId, @printer, @status, @vStatus, @req, 0, 0, @rem, @pages, @start, @end, @pId, 1, @created)
                    RETURNING Id;";
                cmd.Parameters.AddWithValue("@num", continuationJob.JobNumber);
                cmd.Parameters.AddWithValue("@ordId", continuationJob.OrderId);
                cmd.Parameters.AddWithValue("@itemId", continuationJob.OrderItemId);
                cmd.Parameters.AddWithValue("@printer", continuationJob.PrinterName);
                cmd.Parameters.AddWithValue("@status", (int)JobStatus.Queued);
                cmd.Parameters.AddWithValue("@vStatus", (int)VerificationStatus.NeedsVerification);
                cmd.Parameters.AddWithValue("@req", continuationJob.CopiesRequested);
                cmd.Parameters.AddWithValue("@rem", continuationJob.CopiesRemaining);
                cmd.Parameters.AddWithValue("@pages", continuationJob.PageCount);
                cmd.Parameters.AddWithValue("@start", continuationJob.StartPage);
                cmd.Parameters.AddWithValue("@end", continuationJob.EndPage);
                cmd.Parameters.AddWithValue("@pId", parentJob.Id);
                cmd.Parameters.AddWithValue("@created", continuationJob.CreatedAt.ToString("o"));

                continuationJob.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            // Log event
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO PrintJobEvents (PrintJobId, Timestamp, OldStatus, NewStatus, VerificationStatus, EventSource, Message, Details)
                    VALUES (@jId, @time, @oldSt, @newSt, @vSt, @src, @msg, @details)";
                cmd.Parameters.AddWithValue("@jId", continuationJob.Id);
                cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@oldSt", (int)JobStatus.Queued);
                cmd.Parameters.AddWithValue("@newSt", (int)JobStatus.Queued);
                cmd.Parameters.AddWithValue("@vSt", (int)VerificationStatus.NeedsVerification);
                cmd.Parameters.AddWithValue("@src", "RecoveryService.Continuation");
                cmd.Parameters.AddWithValue("@msg", $"إنشاء مهمة استكمال للمهمة #{parentJob.JobNumber}");
                cmd.Parameters.AddWithValue("@details", $"نسخ متبقية: {remainingCopies} | الصفحات من {startPage} إلى {continuationJob.EndPage}");
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return continuationJob;
    }

    public (int startPage, int endPage) CalculateSafeDuplexPageRange(int lastVerifiedPage, int totalPages, PrintDuplexOption duplex)
    {
        if (lastVerifiedPage < 0) lastVerifiedPage = 0;
        if (lastVerifiedPage >= totalPages) return (totalPages, totalPages);

        int nextPage = lastVerifiedPage + 1;

        if (duplex != PrintDuplexOption.Simplex)
        {
            // For duplex printing, physical sheets have 2 pages: (1,2), (3,4), (5,6)
            // If last verified printed page was odd (e.g. 1), the other side (2) may be incomplete or unprinted.
            // Safe resumption begins on the start of the next sheet (e.g. if last confirmed was page 1, page 1 might need re-check, or if 2 was finished, resume at 3).
            // If the operator verified up to page X:
            // If X is odd, X is the front of the sheet. The back (X+1) hasn't printed yet or failed.
            // In duplex, you can't feed a half-printed sheet into a standard spooler without duplex alignment issues.
            // Therefore, safe sheet resumption starts at the odd page of that sheet!
            if (lastVerifiedPage % 2 == 1)
            {
                // Operator verified odd page only -> safely restart that entire sheet:
                nextPage = lastVerifiedPage;
            }
            else
            {
                // Operator verified even page (both sides printed) -> start next sheet:
                nextPage = lastVerifiedPage + 1;
            }
        }

        return (Math.Max(1, nextPage), totalPages);
    }

    public async Task LogErrorAsync(long? orderId, long? printJobId, string? printerName, string? bookTitle, ErrorType errorType, ErrorSource errorSource, string message, string systemAction, string? rawCode = null)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ErrorLogs (Timestamp, OrderId, PrintJobId, PrinterName, BookTitle, ErrorType, ErrorSource, RawErrorCode, ErrorMessage, SystemActionTaken, IsResolved)
            VALUES (@time, @ordId, @jId, @pName, @title, @type, @src, @code, @msg, @act, 0)";
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@ordId", (object?)orderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@jId", (object?)printJobId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pName", (object?)printerName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@title", (object?)bookTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", (int)errorType);
        cmd.Parameters.AddWithValue("@src", (int)errorSource);
        cmd.Parameters.AddWithValue("@code", (object?)rawCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@msg", message);
        cmd.Parameters.AddWithValue("@act", systemAction);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ErrorLog>> GetErrorLogsAsync(bool? unresolvedOnly = null, string? printerName = null, int limit = 100)
    {
        var list = new List<ErrorLog>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();

        var query = "SELECT Id, Timestamp, OrderId, PrintJobId, PrinterName, BookTitle, ErrorType, ErrorSource, RawErrorCode, ErrorMessage, SystemActionTaken, UserActionTaken, IsResolved, ResolvedAt FROM ErrorLogs WHERE 1=1";

        if (unresolvedOnly == true)
        {
            query += " AND IsResolved = 0";
        }

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            query += " AND PrinterName = @pName";
            cmd.Parameters.AddWithValue("@pName", printerName);
        }

        query += " ORDER BY Timestamp DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.CommandText = query;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ErrorLog
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                OrderId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                PrintJobId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                PrinterName = reader.IsDBNull(4) ? null : reader.GetString(4),
                BookTitle = reader.IsDBNull(5) ? null : reader.GetString(5),
                ErrorType = (ErrorType)reader.GetInt32(6),
                ErrorSource = (ErrorSource)reader.GetInt32(7),
                RawErrorCode = reader.IsDBNull(8) ? null : reader.GetString(8),
                ErrorMessage = reader.GetString(9),
                SystemActionTaken = reader.GetString(10),
                UserActionTaken = reader.IsDBNull(11) ? null : reader.GetString(11),
                IsResolved = reader.GetInt32(12) == 1,
                ResolvedAt = reader.IsDBNull(13) ? null : DateTime.Parse(reader.GetString(13))
            });
        }
        return list;
    }

    public async Task<bool> ResolveErrorLogAsync(long errorLogId, string userActionTaken)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ErrorLogs SET
                IsResolved = 1,
                ResolvedAt = @time,
                UserActionTaken = @act
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", errorLogId);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@act", userActionTaken);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
