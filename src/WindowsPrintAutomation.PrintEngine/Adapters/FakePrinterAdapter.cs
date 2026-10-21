using System.IO;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class FakePrinterAdapter : IPrinterAdapter
{
    public string AdapterId => "FakeSimulated";
    public string DisplayName => "محاكي الطابعات المتعددة (CI Simulation Adapter)";
    public string VendorFamily => "Simulation";
    public string AdapterName => DisplayName;

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
        SetPrinterStatus("Canon imageRUNNER ADVANCE C5535", isOnline: true, hasError: false, "جاهزة للطباعة");
        SetPrinterStatus("Generic Windows Virtual Printer", isOnline: true, hasError: false, "جاهزة للطباعة");
    }

    public DeviceCapabilitiesReport ProbeCapabilities(string printerName, string? driverName = null)
    {
        var report = new DeviceCapabilitiesReport
        {
            PrinterName = printerName,
            DriverName = driverName ?? "Simulated Driver",
            ProbedAt = DateTime.UtcNow,
            A4Support = CapabilityStatus.VerifiedSupported
        };

        if (printerName.Contains("Kyocera", StringComparison.OrdinalIgnoreCase))
        {
            report.B5Support = CapabilityStatus.VerifiedSupported;
            report.DuplexSupport = CapabilityStatus.VerifiedSupported;
            report.ColorSupport = CapabilityStatus.VerifiedUnsupported;
            report.ConfirmedTrays = new List<string> { "درج تلقائي", "درج 1 (A4)", "درج 2 (B5)", "درج يدوي" };
            report.HasHardwareJamSensor = true;
            report.HasHardwarePaperOutSensor = true;
            report.HasPageCounterSensor = true;

            report.ConfirmedCapabilitiesList.Add("مقاس A4 مؤكد");
            report.ConfirmedCapabilitiesList.Add("مقاس B5 مؤكد عبر أدراج TASKalfa");
            report.ConfirmedCapabilitiesList.Add("الطباعة على الوجهين مؤكدة");
            report.ConfirmedCapabilitiesList.Add("طباعة أبيض وأسود مؤكدة");
        }
        else if (printerName.Contains("HP", StringComparison.OrdinalIgnoreCase))
        {
            report.B5Support = CapabilityStatus.UnconfirmedOrUnknown;
            report.DuplexSupport = CapabilityStatus.VerifiedSupported;
            report.ColorSupport = printerName.Contains("Color", StringComparison.OrdinalIgnoreCase) ? CapabilityStatus.VerifiedSupported : CapabilityStatus.VerifiedUnsupported;
            report.ConfirmedTrays = new List<string> { "درج تلقائي", "درج 1", "درج 2" };
            report.HasHardwareJamSensor = true;
            report.HasHardwarePaperOutSensor = true;

            report.ConfirmedCapabilitiesList.Add("مقاس A4 مؤكد");
            report.UnconfirmedCapabilitiesList.Add("مقاس B5 غير مؤكد من التعريف — يلزم مراجعة درج الورق");
            report.ConfirmedCapabilitiesList.Add("الطباعة على الوجهين مؤكدة");
            if (report.ColorSupport == CapabilityStatus.VerifiedSupported) report.ConfirmedCapabilitiesList.Add("طباعة ألوان مؤكدة");
        }
        else if (printerName.Contains("Canon", StringComparison.OrdinalIgnoreCase))
        {
            report.B5Support = CapabilityStatus.VerifiedSupported;
            report.DuplexSupport = CapabilityStatus.VerifiedSupported;
            report.ColorSupport = CapabilityStatus.VerifiedSupported;
            report.ConfirmedTrays = new List<string> { "درج 1", "درج 2" };
            report.HasHardwareJamSensor = true;
            report.ConfirmedCapabilitiesList.Add("مقاس A4 وB5 مؤكدين");
            report.ConfirmedCapabilitiesList.Add("دعم كامل للألوان والوجهين");
        }
        else
        {
            // Generic fallback
            report.B5Support = CapabilityStatus.UnconfirmedOrUnknown;
            report.DuplexSupport = CapabilityStatus.UnconfirmedOrUnknown;
            report.ColorSupport = CapabilityStatus.UnconfirmedOrUnknown;
            report.UnconfirmedCapabilitiesList.Add("طابعة افتراضية: مقاس B5 والوجهين غير مؤكدين");
        }

        return report;
    }

    public Task<ConnectionTestResult> TestConnectionAsync(string printerName, string? connectionAddress)
    {
        var report = ProbeCapabilities(printerName);
        var isOnline = !_printerStatuses.TryGetValue(printerName, out var s) || s.IsOnline;

        var result = new ConnectionTestResult
        {
            PrinterName = printerName,
            IsSuccess = isOnline,
            LatencyMs = 12,
            SpoolerResponding = true,
            HardwareResponding = isOnline,
            StatusMessage = isOnline ? "الاتصال ناجح ومستقر (محاكاة CI)" : "الطابعة غير متصلة (محاكاة)",
            CapabilitiesReport = report,
            DiagnosticMessages = new List<string>
            {
                "تم الاتصال بنجاح مع المحاكي",
                isOnline ? "الطابعة جاهزة لاستقبال الأوامر" : "الطابعة في وضع غير متصل"
            }
        };

        return Task.FromResult(result);
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
