using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.PrintEngine.Interfaces;

public class PrintJobRequest
{
    public required PrintJob Job { get; set; }
    public required string PdfPath { get; set; }
    public required string PrinterName { get; set; }
    public PaperSizeOption PaperSize { get; set; } = PaperSizeOption.A4;
    public PrintDuplexOption Duplex { get; set; } = PrintDuplexOption.DuplexLongEdge;
    public PrintColorOption ColorMode { get; set; } = PrintColorOption.Monochrome;
    public string? PaperTray { get; set; }
    public int Copies { get; set; } = 1;
    public int StartPage { get; set; } = 1;
    public int EndPage { get; set; } = 1;
}

public class PrintExecutionResult
{
    public bool Success { get; set; }
    public int? SpoolerJobId { get; set; }
    public string? ErrorMessage { get; set; }
    public ErrorType ErrorType { get; set; } = ErrorType.None;
    public VerificationStatus InitialVerificationStatus { get; set; } = VerificationStatus.Accepted;
}

public class PrinterStatusInfo
{
    public string PrinterName { get; set; } = string.Empty;
    public bool IsOnline { get; set; } = true;
    public bool HasError { get; set; }
    public string StatusText { get; set; } = "جاهزة";
    public ErrorType DetectedError { get; set; } = ErrorType.None;
    public int QueuedJobsCount { get; set; }
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
}

public class SpoolerJobInfo
{
    public int JobId { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public int PagesPrinted { get; set; }
    public bool IsPaused { get; set; }
    public bool IsError { get; set; }
}

public interface IPrinterAdapter
{
    string AdapterId { get; }
    string DisplayName { get; }
    string VendorFamily { get; }
    string AdapterName => DisplayName;

    DeviceCapabilitiesReport ProbeCapabilities(string printerName, string? driverName = null);
    Task<ConnectionTestResult> TestConnectionAsync(string printerName, string? connectionAddress);
    Task<PrinterStatusInfo> CheckStatusAsync(string printerName);
    Task<PrintExecutionResult> SendPdfJobAsync(PrintJobRequest request);
    Task<bool> CancelJobAsync(string printerName, int spoolerJobId);
    Task<bool> PauseJobAsync(string printerName, int spoolerJobId);
    Task<bool> ResumeJobAsync(string printerName, int spoolerJobId);
    Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName);
}
