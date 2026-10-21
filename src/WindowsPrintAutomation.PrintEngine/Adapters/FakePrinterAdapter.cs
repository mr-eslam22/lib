using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class FakePrinterAdapter : IPrinterAdapter
{
    public string AdapterName => "FakePrinterAdapter (Simulated for CI & Tests)";

    private readonly Dictionary<string, PrinterStatusInfo> _printerStatuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SpoolerJobInfo>> _queues = new(StringComparer.OrdinalIgnoreCase);
    private int _nextJobId = 1001;

    public ErrorType ForcedErrorNextJob { get; set; } = ErrorType.None;
    public string? ForcedErrorMessage { get; set; }

    public FakePrinterAdapter()
    {
        // Default simulated printers
        SetPrinterStatus("HP LaserJet Pro MFP M428fdw", isOnline: true, hasError: false, "جاهزة للطباعة");
        SetPrinterStatus("Kyocera TASKalfa 3212i", isOnline: true, hasError: false, "جاهزة للطباعة");
        SetPrinterStatus("HP Color LaserJet Enterprise M553", isOnline: true, hasError: false, "جاهزة للطباعة");
    }

    public void SetPrinterStatus(string printerName, bool isOnline, bool hasError, string statusText, ErrorType detectedError = ErrorType.None)
    {
        _printerStatuses[printerName] = new PrinterStatusInfo
        {
            PrinterName = printerName,
            IsOnline = isOnline,
            HasError = hasError,
            StatusText = statusText,
            DetectedError = detectedError,
            QueuedJobsCount = _queues.TryGetValue(printerName, out var q) ? q.Count : 0,
            LastCheckedAt = DateTime.UtcNow
        };
    }

    public Task<PrinterStatusInfo> CheckStatusAsync(string printerName)
    {
        if (_printerStatuses.TryGetValue(printerName, out var status))
        {
            status.QueuedJobsCount = _queues.TryGetValue(printerName, out var q) ? q.Count : 0;
            status.LastCheckedAt = DateTime.UtcNow;
            return Task.FromResult(status);
        }

        // Return a default online status for unknown simulated printer
        var def = new PrinterStatusInfo
        {
            PrinterName = printerName,
            IsOnline = true,
            HasError = false,
            StatusText = "جاهزة (محاكاة)",
            QueuedJobsCount = 0,
            LastCheckedAt = DateTime.UtcNow
        };
        return Task.FromResult(def);
    }

    public Task<PrintExecutionResult> SendPdfJobAsync(PrintJobRequest request)
    {
        if (ForcedErrorNextJob != ErrorType.None)
        {
            var errType = ForcedErrorNextJob;
            var errMsg = ForcedErrorMessage ?? $"خطأ محاكاة: {errType}";
            ForcedErrorNextJob = ErrorType.None;
            ForcedErrorMessage = null;

            return Task.FromResult(new PrintExecutionResult
            {
                Success = false,
                ErrorType = errType,
                ErrorMessage = errMsg,
                InitialVerificationStatus = VerificationStatus.Failed
            });
        }

        if (!_printerStatuses.TryGetValue(request.PrinterName, out var status) || !status.IsOnline)
        {
            return Task.FromResult(new PrintExecutionResult
            {
                Success = false,
                ErrorType = ErrorType.PrinterOffline,
                ErrorMessage = $"الطابعة '{request.PrinterName}' غير متصلة بالشبكة أو غير متاحة.",
                InitialVerificationStatus = VerificationStatus.Failed
            });
        }

        if (status.HasError)
        {
            return Task.FromResult(new PrintExecutionResult
            {
                Success = false,
                ErrorType = status.DetectedError != ErrorType.None ? status.DetectedError : ErrorType.UnknownPrinterError,
                ErrorMessage = $"الطابعة '{request.PrinterName}' في حالة خطأ: {status.StatusText}",
                InitialVerificationStatus = VerificationStatus.Failed
            });
        }

        var spoolerJobId = Interlocked.Increment(ref _nextJobId);

        if (!_queues.ContainsKey(request.PrinterName))
        {
            _queues[request.PrinterName] = new List<SpoolerJobInfo>();
        }

        _queues[request.PrinterName].Add(new SpoolerJobInfo
        {
            JobId = spoolerJobId,
            DocumentName = Path.GetFileName(request.PdfPath),
            StatusText = "تم إرسال الملف إلى طابور الطباعة (Spooling)",
            TotalPages = request.Job.PageCount * request.Copies,
            PagesPrinted = 0,
            IsPaused = false,
            IsError = false
        });

        return Task.FromResult(new PrintExecutionResult
        {
            Success = true,
            SpoolerJobId = spoolerJobId,
            InitialVerificationStatus = VerificationStatus.Accepted
        });
    }

    public Task<bool> CancelJobAsync(string printerName, int spoolerJobId)
    {
        if (_queues.TryGetValue(printerName, out var queue))
        {
            var item = queue.FirstOrDefault(j => j.JobId == spoolerJobId);
            if (item != null)
            {
                queue.Remove(item);
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public Task<bool> PauseJobAsync(string printerName, int spoolerJobId)
    {
        if (_queues.TryGetValue(printerName, out var queue))
        {
            var item = queue.FirstOrDefault(j => j.JobId == spoolerJobId);
            if (item != null)
            {
                item.IsPaused = true;
                item.StatusText = "متوقفة مؤقتًا";
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public Task<bool> ResumeJobAsync(string printerName, int spoolerJobId)
    {
        if (_queues.TryGetValue(printerName, out var queue))
        {
            var item = queue.FirstOrDefault(j => j.JobId == spoolerJobId);
            if (item != null)
            {
                item.IsPaused = false;
                item.StatusText = "قيد التنفيذ";
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName)
    {
        if (_queues.TryGetValue(printerName, out var queue))
        {
            return Task.FromResult(new List<SpoolerJobInfo>(queue));
        }
        return Task.FromResult(new List<SpoolerJobInfo>());
    }
}
