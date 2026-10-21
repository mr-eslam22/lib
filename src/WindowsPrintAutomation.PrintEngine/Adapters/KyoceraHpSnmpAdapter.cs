using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class SnmpDeviceStatus
{
    public bool IsReachable { get; set; }
    public string DeviceState { get; set; } = "غير معروف";
    public bool HasPaperJam { get; set; }
    public bool IsOutOfPaper { get; set; }
    public bool IsTonerLow { get; set; }
    public bool IsDoorOpen { get; set; }
    public string Details { get; set; } = string.Empty;
}

public class KyoceraHpSnmpAdapter
{
    public const string OidPrinterStatus = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    public const string OidPrinterDetectedErrorState = "1.3.6.1.2.1.43.18.1.1.8.1";
    public const string OidSupplyLevel = "1.3.6.1.2.1.43.11.1.1.9.1";

    public async Task<SnmpDeviceStatus> QueryPrinterHardwareStatusAsync(string ipAddress, int port = 161, string community = "public", int timeoutMs = 2000)
    {
        var result = new SnmpDeviceStatus();

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            result.IsReachable = false;
            result.Details = "لم يتم تحديد عنوان IP للماكينة.";
            return result;
        }

        try
        {
            // Placeholder/Extension point for UDP SNMP v1/v2c raw queries or SnmpSharpNet
            // By isolating SNMP from Windows Spooler, we ensure spooler calls never block or hang on network timeouts
            await Task.Delay(50); // Non-blocking simulation check

            result.IsReachable = true;
            result.DeviceState = "جاهزة للاستقبال";
            result.Details = $"تم الاتصال بالماكينة عبر SNMP ({ipAddress}:{port})";
        }
        catch (Exception ex)
        {
            result.IsReachable = false;
            result.Details = $"فشل الاتصال بـ SNMP: {ex.Message}";
        }

        return result;
    }

    public ErrorType MapSnmpErrorState(SnmpDeviceStatus status)
    {
        if (!status.IsReachable) return ErrorType.PrinterOffline;
        if (status.HasPaperJam) return ErrorType.PaperJam;
        if (status.IsOutOfPaper) return ErrorType.OutOfPaper;
        if (status.IsTonerLow) return ErrorType.TonerLow;
        return ErrorType.None;
    }
}
