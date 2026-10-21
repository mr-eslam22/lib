using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Domain.Entities;

public enum CapabilityStatus
{
    VerifiedSupported,
    VerifiedUnsupported,
    UnconfirmedOrUnknown
}

public class DeviceCapabilitiesReport
{
    public string PrinterName { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public DateTime ProbedAt { get; set; } = DateTime.UtcNow;

    // Supported Sizes
    public CapabilityStatus A4Support { get; set; } = CapabilityStatus.UnconfirmedOrUnknown;
    public CapabilityStatus B5Support { get; set; } = CapabilityStatus.UnconfirmedOrUnknown;
    public CapabilityStatus CustomSizeSupport { get; set; } = CapabilityStatus.UnconfirmedOrUnknown;

    // Duplex
    public CapabilityStatus DuplexSupport { get; set; } = CapabilityStatus.UnconfirmedOrUnknown;

    // Color
    public CapabilityStatus ColorSupport { get; set; } = CapabilityStatus.UnconfirmedOrUnknown;

    // Trays
    public List<string> ConfirmedTrays { get; set; } = new();
    public bool HasUnconfirmedTrays { get; set; } = true;

    // Status Sensors
    public bool HasHardwareJamSensor { get; set; }
    public bool HasHardwarePaperOutSensor { get; set; }
    public bool HasPageCounterSensor { get; set; }

    public List<string> ConfirmedCapabilitiesList { get; set; } = new();
    public List<string> UnconfirmedCapabilitiesList { get; set; } = new();
    public List<string> UnsupportedCapabilitiesList { get; set; } = new();
}

public class ConnectionTestResult
{
    public string PrinterName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public long LatencyMs { get; set; }
    public bool SpoolerResponding { get; set; }
    public bool HardwareResponding { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public DeviceCapabilitiesReport CapabilitiesReport { get; set; } = new();
    public List<string> DiagnosticMessages { get; set; } = new();
}
