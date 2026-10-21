namespace WindowsPrintAutomation.Core.Domain.Enums;

public enum JobStatus
{
    Queued,
    Preparing,
    Submitted,
    Spooling,
    Printing,
    Paused,
    NeedsAttention,
    Completed,
    Failed,
    Cancelled,
    RecoveryRequired
}

public enum VerificationStatus
{
    Accepted,
    Processing,
    DeviceReportedComplete,
    PhysicallyVerified,
    NeedsVerification,
    Failed,
    Cancelled
}

public enum ErrorType
{
    None,
    PaperJam,
    OutOfPaper,
    TonerLow,
    TonerOut,
    PrinterOffline,
    SpoolerError,
    InvalidPDF,
    UnsupportedPaperSize,
    UnknownPrinterError,
    UserReportedError
}

public enum ErrorSource
{
    WindowsSpooler,
    HardwareSnmp,
    UserReported,
    SystemPreflight
}

public enum PaperSizeOption
{
    A4,
    B5,
    Custom
}

public enum PrintDuplexOption
{
    Simplex,
    DuplexLongEdge,
    DuplexShortEdge
}

public enum PrintColorOption
{
    Monochrome,
    Color
}

public enum OrderStatus
{
    Draft,
    Pending,
    InProgress,
    PartiallyCompleted,
    Completed,
    Cancelled,
    RecoveryRequired
}
