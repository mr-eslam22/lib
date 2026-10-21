using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Domain.Entities;

public class Book
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public bool IsAvailable { get; set; } = true;
    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;
    public long? FolderId { get; set; }
}

public class LibraryFolder
{
    public long Id { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastScanAt { get; set; }
    public int BookCount { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Customer
{
    public long Id { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Order
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveryDate { get; set; }
    public string? Notes { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public int TotalItemsCount { get; set; }
    public int TotalEstimatedSheets { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public long BookId { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public int Copies { get; set; } = 1;
    public PaperSizeOption PaperSize { get; set; } = PaperSizeOption.A4;
    public PrintDuplexOption Duplex { get; set; } = PrintDuplexOption.DuplexLongEdge;
    public PrintColorOption ColorMode { get; set; } = PrintColorOption.Monochrome;
    public string? PaperTray { get; set; }
    public int EstimatedSheets { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public int VerifiedCopies { get; set; }
    public int RemainingCopies { get; set; }
}

public class Printer
{
    public long Id { get; set; }
    public string WindowsPrinterName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string PortName { get; set; } = string.Empty;
    public string AdapterFamily { get; set; } = "GenericWindowsSpooler";
    public string? ConnectionAddress { get; set; }
    public bool IsOnline { get; set; } = true;
    public bool HasError { get; set; }
    public bool IsActive { get; set; } = true;
    public string CurrentStatusText { get; set; } = "جاهزة";
    public string SupportedPaperSizes { get; set; } = "A4,B5";
    public bool SupportsDuplex { get; set; } = true;
    public bool SupportsColor { get; set; }
    public string AvailableTrays { get; set; } = "درج تلقائي,درج 1,درج 2";
    public string? CapabilitiesJson { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTestedAt { get; set; }
    public string? TestResultStatus { get; set; }
    public string? LastErrorMessage { get; set; }
    public bool IsPaused { get; set; }
}

public class PrinterProfile
{
    public long Id { get; set; }
    public long PrinterId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string TargetBookType { get; set; } = "عام";
    public PaperSizeOption PaperSize { get; set; } = PaperSizeOption.A4;
    public PrintDuplexOption Duplex { get; set; } = PrintDuplexOption.DuplexLongEdge;
    public PrintColorOption ColorMode { get; set; } = PrintColorOption.Monochrome;
    public string? PaperTray { get; set; }
    public string? PrintQuality { get; set; } = "عادية";
    public bool IsDefault { get; set; }
}

public class PrintJob
{
    public long Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public long OrderItemId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public int? SpoolerJobId { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.NeedsVerification;
    public int CopiesRequested { get; set; } = 1;
    public int CopiesVerified { get; set; }
    public int CopiesDamaged { get; set; }
    public int CopiesRemaining { get; set; } = 1;
    public int PageCount { get; set; }
    public int StartPage { get; set; } = 1;
    public int EndPage { get; set; } = 1;
    public long? ParentJobId { get; set; }
    public bool IsContinuation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastErrorMessage { get; set; }
}

public class PrintJobEvent
{
    public long Id { get; set; }
    public long PrintJobId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public JobStatus OldStatus { get; set; }
    public JobStatus NewStatus { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public string EventSource { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class ErrorLog
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long? OrderId { get; set; }
    public long? PrintJobId { get; set; }
    public string? PrinterName { get; set; }
    public string? BookTitle { get; set; }
    public ErrorType ErrorType { get; set; } = ErrorType.None;
    public ErrorSource ErrorSource { get; set; } = ErrorSource.WindowsSpooler;
    public string? RawErrorCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string SystemActionTaken { get; set; } = string.Empty;
    public string? UserActionTaken { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class BackupRecord
{
    public long Id { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long SizeBytes { get; set; }
    public string Status { get; set; } = "Success";
}
