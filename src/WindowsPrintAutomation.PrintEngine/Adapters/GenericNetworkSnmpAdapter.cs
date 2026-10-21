using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class GenericNetworkSnmpAdapter : IPrinterAdapter
{
    private readonly WindowsSpoolerAdapter _spoolerAdapter;
    private readonly KyoceraHpSnmpAdapter _snmpHelper;

    public string AdapterId => "NetworkSnmp";
    public string DisplayName => "محول الشبكة العام (Standard Printer-MIB over IP)";
    public string VendorFamily => "NetworkSnmp";
    public string AdapterName => DisplayName;

    public GenericNetworkSnmpAdapter()
    {
        _spoolerAdapter = new WindowsSpoolerAdapter();
        _snmpHelper = new KyoceraHpSnmpAdapter();
    }

    public DeviceCapabilitiesReport ProbeCapabilities(string printerName, string? driverName = null)
    {
        var report = _spoolerAdapter.ProbeCapabilities(printerName, driverName);
        report.ConfirmedCapabilitiesList.Add("دعم بروتوكول Standard Network Printer-MIB (RFC 3805)");
        return report;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(string printerName, string? connectionAddress)
    {
        var result = await _spoolerAdapter.TestConnectionAsync(printerName, connectionAddress);

        if (!string.IsNullOrWhiteSpace(connectionAddress) && connectionAddress.Contains("."))
        {
            var snmpStatus = await _snmpHelper.QueryPrinterHardwareStatusAsync(connectionAddress);
            if (snmpStatus.IsReachable)
            {
                result.DiagnosticMessages.Add($"استجابة MIB الشبكي سريعة: {snmpStatus.Details}");
            }
            else
            {
                result.DiagnosticMessages.Add("تنبيه: تعذر الاستعلام عبر SNMP؛ سيتم إرسال الأوامر عبر طابور Windows Spooler القياسي.");
            }
        }

        return result;
    }

    public Task<PrinterStatusInfo> CheckStatusAsync(string printerName) => _spoolerAdapter.CheckStatusAsync(printerName);
    public Task<PrintExecutionResult> SendPdfJobAsync(PrintJobRequest request) => _spoolerAdapter.SendPdfJobAsync(request);
    public Task<bool> CancelJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.CancelJobAsync(printerName, spoolerJobId);
    public Task<bool> PauseJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.PauseJobAsync(printerName, spoolerJobId);
    public Task<bool> ResumeJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.ResumeJobAsync(printerName, spoolerJobId);
    public Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName) => _spoolerAdapter.GetActiveQueueJobsAsync(printerName);
}
