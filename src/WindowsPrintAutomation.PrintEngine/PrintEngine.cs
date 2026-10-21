using Microsoft.Data.Sqlite;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine;

public class JobStatusChangedEventArgs : EventArgs
{
    public required PrintJob Job { get; set; }
    public JobStatus OldStatus { get; set; }
    public JobStatus NewStatus { get; set; }
    public string? Message { get; set; }
}

public class PrinterErrorEventArgs : EventArgs
{
    public required string PrinterName { get; set; }
    public ErrorType ErrorType { get; set; }
    public required string Message { get; set; }
    public long? JobId { get; set; }
}

public interface IPrintEngine
{
    event EventHandler<JobStatusChangedEventArgs>? JobStatusChanged;
    event EventHandler<PrinterErrorEventArgs>? PrinterErrorOccurred;

    void SetAdapter(IPrinterAdapter adapter);
    IPrinterAdapter GetAdapter();
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<PrintExecutionResult> ProcessSingleJobAsync(long jobId);
    Task PausePrinterAsync(string printerName);
    Task ResumePrinterAsync(string printerName);
    Task<bool> CancelJobAsync(long jobId);
}

public class PrintEngine : IPrintEngine
{
    private readonly DatabaseService _databaseService;
    private readonly IRecoveryService _recoveryService;
    private IPrinterAdapter _printerAdapter;
    private readonly HashSet<string> _pausedPrinters = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _engineCts;
    private Task? _workerTask;
    private readonly SemaphoreSlim _loopLock = new(1, 1);

    public event EventHandler<JobStatusChangedEventArgs>? JobStatusChanged;
    public event EventHandler<PrinterErrorEventArgs>? PrinterErrorOccurred;

    public PrintEngine(DatabaseService databaseService, IRecoveryService recoveryService, IPrinterAdapter? adapter = null)
    {
        _databaseService = databaseService;
        _recoveryService = recoveryService;
        _printerAdapter = adapter ?? new Adapters.WindowsSpoolerAdapter();
    }

    public void SetAdapter(IPrinterAdapter adapter)
    {
        _printerAdapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public IPrinterAdapter GetAdapter() => _printerAdapter;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_workerTask != null && !_workerTask.IsCompleted)
        {
            return Task.CompletedTask;
        }

        _engineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workerTask = Task.Run(() => QueueWorkerLoopAsync(_engineCts.Token), _engineCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_engineCts != null)
        {
            _engineCts.Cancel();
            if (_workerTask != null)
            {
                try
                {
                    await _workerTask;
                }
                catch (OperationCanceledException) { }
            }
            _engineCts.Dispose();
            _engineCts = null;
        }
    }

    private async Task QueueWorkerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ProcessNextQueuedJobAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Prevent crash loop
                await Task.Delay(2000, token);
            }

            await Task.Delay(1500, token);
        }
    }

    private async Task ProcessNextQueuedJobAsync(CancellationToken token)
    {
        long? nextJobId = null;

        using (var conn = _databaseService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, PrinterName FROM PrintJobs 
                WHERE Status = @queued 
                ORDER BY Id ASC LIMIT 1";
            cmd.Parameters.AddWithValue("@queued", (int)JobStatus.Queued);

            using var reader = await cmd.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var id = reader.GetInt64(0);
                var printerName = reader.GetString(1);

                // Machine Isolation check: if this printer is paused or has error, skip it for now!
                if (_pausedPrinters.Contains(printerName))
                {
                    return;
                }

                nextJobId = id;
            }
        }

        if (nextJobId.HasValue)
        {
            await ProcessSingleJobAsync(nextJobId.Value);
        }
    }

    public async Task<PrintExecutionResult> ProcessSingleJobAsync(long jobId)
    {
        await _loopLock.WaitAsync();
        try
        {
            PrintJob? job = null;
            string? filePath = null;
            PaperSizeOption paperSize = PaperSizeOption.A4;
            PrintDuplexOption duplex = PrintDuplexOption.DuplexLongEdge;
            PrintColorOption colorMode = PrintColorOption.Monochrome;
            string? paperTray = null;

            // Load Job details and OrderItem specs
            using (var conn = _databaseService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT j.Id, j.JobNumber, j.OrderId, j.OrderItemId, j.PrinterName, j.SpoolerJobId, j.Status, j.VerificationStatus, j.CopiesRequested, j.CopiesVerified, j.CopiesDamaged, j.CopiesRemaining, j.PageCount, j.StartPage, j.EndPage, j.ParentJobId, j.IsContinuation, j.CreatedAt,
                           i.FilePath, i.PaperSize, i.Duplex, i.ColorMode, i.PaperTray
                    FROM PrintJobs j
                    JOIN OrderItems i ON j.OrderItemId = i.Id
                    WHERE j.Id = @id";
                cmd.Parameters.AddWithValue("@id", jobId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    job = new PrintJob
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
                        CreatedAt = DateTime.Parse(reader.GetString(17))
                    };
                    filePath = reader.GetString(18);
                    paperSize = (PaperSizeOption)reader.GetInt32(19);
                    duplex = (PrintDuplexOption)reader.GetInt32(20);
                    colorMode = (PrintColorOption)reader.GetInt32(21);
                    paperTray = reader.IsDBNull(22) ? null : reader.GetString(22);
                }
            }

            if (job == null || string.IsNullOrWhiteSpace(filePath))
            {
                return new PrintExecutionResult
                {
                    Success = false,
                    ErrorMessage = "بيانات المهمة أو مسار الملف غير موجودة."
                };
            }

            // 1. Transition to Preparing
            await UpdateJobStateAsync(job, JobStatus.Preparing, VerificationStatus.NeedsVerification, "جاري تحضير ملف الطباعة والتأكد من إمكانية الوصول");

            // Verify file existence
            if (!File.Exists(filePath))
            {
                var errMsg = $"ملف PDF غير موجود على المسار المحدد: {filePath}";
                await HandleJobErrorAsync(job, ErrorType.InvalidPDF, ErrorSource.SystemPreflight, errMsg, "إيقاف المهمة وطلب التحقق من الملف");
                return new PrintExecutionResult { Success = false, ErrorMessage = errMsg, ErrorType = ErrorType.InvalidPDF };
            }

            // Check printer status first
            var printerStatus = await _printerAdapter.CheckStatusAsync(job.PrinterName);
            if (!printerStatus.IsOnline || printerStatus.HasError)
            {
                var errType = printerStatus.DetectedError != ErrorType.None ? printerStatus.DetectedError : ErrorType.PrinterOffline;
                var errMsg = $"الطابعة '{job.PrinterName}' غير جاهزة: {printerStatus.StatusText}";

                // Pause this specific printer to isolate it
                _pausedPrinters.Add(job.PrinterName);

                await HandleJobErrorAsync(job, errType, ErrorSource.WindowsSpooler, errMsg, $"إيقاف طابور الطابعة '{job.PrinterName}' مؤقتًا وعزل المشكلة");
                return new PrintExecutionResult { Success = false, ErrorMessage = errMsg, ErrorType = errType };
            }

            // 2. Transition to Submitted
            await UpdateJobStateAsync(job, JobStatus.Submitted, VerificationStatus.NeedsVerification, $"إرسال البيانات إلى تعريف الطابعة '{job.PrinterName}'");

            // Dispatch to Adapter
            var request = new PrintJobRequest
            {
                Job = job,
                PdfPath = filePath,
                PrinterName = job.PrinterName,
                PaperSize = paperSize,
                Duplex = duplex,
                ColorMode = colorMode,
                PaperTray = paperTray,
                Copies = job.CopiesRemaining,
                StartPage = job.StartPage,
                EndPage = job.EndPage
            };

            var result = await _printerAdapter.SendPdfJobAsync(request);

            if (!result.Success)
            {
                // Machine Isolation on error
                if (result.ErrorType == ErrorType.PaperJam || result.ErrorType == ErrorType.OutOfPaper || result.ErrorType == ErrorType.PrinterOffline)
                {
                    _pausedPrinters.Add(job.PrinterName);
                }

                await HandleJobErrorAsync(job, result.ErrorType, ErrorSource.WindowsSpooler, result.ErrorMessage ?? "فشل غير معروف في معالجة الطباعة", "إيقاف المهمة مؤقتًا وتسجيل الخطأ");
                return result;
            }

            // 3. Spooler Accepted
            job.SpoolerJobId = result.SpoolerJobId;
            using (var conn = _databaseService.CreateConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE PrintJobs SET SpoolerJobId = @sId, SubmittedAt = @subAt WHERE Id = @id";
                cmd.Parameters.AddWithValue("@sId", (object?)job.SpoolerJobId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@subAt", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@id", job.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            // CRITICAL: We advance to DeviceReportedComplete with VerificationStatus.NeedsVerification!
            // We do NOT mark PhysicallyVerified automatically!
            await UpdateJobStateAsync(job, JobStatus.Printing, VerificationStatus.Accepted, $"تم قبول المهمة في طابور Windows بنجاح (Spooler Job #{job.SpoolerJobId}). جاري الضخ للماكينة.");

            // Simulated or confirmed spooler finish
            await Task.Delay(200);
            await UpdateJobStateAsync(job, JobStatus.NeedsAttention, VerificationStatus.NeedsVerification, "تم انتهاء إرسال البيانات من Spooler. يلزم قيام الموظف بعدّ النسخ وتأكيد الخروج السليم.");

            return result;
        }
        finally
        {
            _loopLock.Release();
        }
    }

    private async Task UpdateJobStateAsync(PrintJob job, JobStatus newStatus, VerificationStatus newVerStatus, string message)
    {
        var oldStatus = job.Status;
        job.Status = newStatus;
        job.VerificationStatus = newVerStatus;

        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE PrintJobs SET 
                Status = @st,
                VerificationStatus = @vSt
            WHERE Id = @id;

            INSERT INTO PrintJobEvents (PrintJobId, Timestamp, OldStatus, NewStatus, VerificationStatus, EventSource, Message)
            VALUES (@id, @time, @oldSt, @newSt, @vSt, @src, @msg);";
        cmd.Parameters.AddWithValue("@st", (int)newStatus);
        cmd.Parameters.AddWithValue("@vSt", (int)newVerStatus);
        cmd.Parameters.AddWithValue("@id", job.Id);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@oldSt", (int)oldStatus);
        cmd.Parameters.AddWithValue("@newSt", (int)newStatus);
        cmd.Parameters.AddWithValue("@src", "PrintEngine");
        cmd.Parameters.AddWithValue("@msg", message);
        await cmd.ExecuteNonQueryAsync();

        JobStatusChanged?.Invoke(this, new JobStatusChangedEventArgs
        {
            Job = job,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            Message = message
        });
    }

    private async Task HandleJobErrorAsync(PrintJob job, ErrorType errorType, ErrorSource source, string message, string systemAction)
    {
        var oldStatus = job.Status;
        job.Status = JobStatus.NeedsAttention;
        job.VerificationStatus = VerificationStatus.Failed;
        job.LastErrorMessage = message;

        using (var conn = _databaseService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE PrintJobs SET 
                    Status = @st,
                    VerificationStatus = @vSt,
                    LastErrorMessage = @msg
                WHERE Id = @id;

                INSERT INTO PrintJobEvents (PrintJobId, Timestamp, OldStatus, NewStatus, VerificationStatus, EventSource, Message, Details)
                VALUES (@id, @time, @oldSt, @newSt, @vSt, @src, @msg, @details);";
            cmd.Parameters.AddWithValue("@st", (int)JobStatus.NeedsAttention);
            cmd.Parameters.AddWithValue("@vSt", (int)VerificationStatus.Failed);
            cmd.Parameters.AddWithValue("@msg", message);
            cmd.Parameters.AddWithValue("@id", job.Id);
            cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@oldSt", (int)oldStatus);
            cmd.Parameters.AddWithValue("@newSt", (int)JobStatus.NeedsAttention);
            cmd.Parameters.AddWithValue("@src", "PrintEngine.Error");
            cmd.Parameters.AddWithValue("@details", $"كود الخطأ: {errorType}");
            await cmd.ExecuteNonQueryAsync();
        }

        await _recoveryService.LogErrorAsync(
            job.OrderId,
            job.Id,
            job.PrinterName,
            null,
            errorType,
            source,
            message,
            systemAction,
            errorType.ToString()
        );

        PrinterErrorOccurred?.Invoke(this, new PrinterErrorEventArgs
        {
            PrinterName = job.PrinterName,
            ErrorType = errorType,
            Message = message,
            JobId = job.Id
        });

        JobStatusChanged?.Invoke(this, new JobStatusChangedEventArgs
        {
            Job = job,
            OldStatus = oldStatus,
            NewStatus = JobStatus.NeedsAttention,
            Message = message
        });
    }

    public async Task PausePrinterAsync(string printerName)
    {
        _pausedPrinters.Add(printerName);
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Printers SET IsPaused = 1 WHERE WindowsPrinterName = @name";
        cmd.Parameters.AddWithValue("@name", printerName);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResumePrinterAsync(string printerName)
    {
        _pausedPrinters.Remove(printerName);
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Printers SET IsPaused = 0 WHERE WindowsPrinterName = @name";
        cmd.Parameters.AddWithValue("@name", printerName);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> CancelJobAsync(long jobId)
    {
        var job = await _recoveryService.GetJobByIdAsync(jobId);
        if (job == null) return false;

        if (job.SpoolerJobId.HasValue)
        {
            await _printerAdapter.CancelJobAsync(job.PrinterName, job.SpoolerJobId.Value);
        }

        await UpdateJobStateAsync(job, JobStatus.Cancelled, VerificationStatus.Cancelled, "تم إلغاء المهمة من قِبل المستخدم.");
        return true;
    }
}
