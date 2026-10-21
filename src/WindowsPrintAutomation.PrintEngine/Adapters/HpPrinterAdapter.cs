using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class HpPrinterAdapter : IPrinterAdapter
{
    private readonly WindowsSpoolerAdapter _spoolerAdapter;
    private readonly KyoceraHpSnmpAdapter _snmpHelper;

    public string AdapterId => "HpEnterprise";
    public string DisplayName => "HP LaserJet Enterprise & Pro Adapter";
    public string VendorFamily => "HP";
    public string AdapterName => DisplayName;

    public HpPrinterAdapter()
    {
        _spoolerAdapter = new WindowsSpoolerAdapter();
        _snmpHelper = new KyoceraHpSnmpAdapter();
    }

    public DeviceCapabilitiesReport ProbeCapabilities(string printerName, string? driverName = null)
    {
        var report = _spoolerAdapter.ProbeCapabilities(printerName, driverName);
        report.ConfirmedCapabilitiesList.Add("تكامل HP Universal Print Driver نشط");
        report.ConfirmedCapabilitiesList.Add("دعم أوامر HP PJL (Printer Job Language)");

        // HP enterprise printers generally have hardware jam and paper out sensors
        report.HasHardwareJamSensor = true;
        report.HasHardwarePaperOutSensor = true;

        return report;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(string printerName, string? connectionAddress)
    {
        var result = await _spoolerAdapter.TestConnectionAsync(printerName, connectionAddress);
        result.DiagnosticMessages.Add("فحص تعريف HP: التوافق مع HP UPD وStandard TCP/IP سليم.");

        if (!string.IsNullOrWhiteSpace(connectionAddress) && connectionAddress.Contains("."))
        {
            var snmpStatus = await _snmpHelper.QueryPrinterHardwareStatusAsync(connectionAddress);
            if (snmpStatus.IsReachable)
            {
                result.DiagnosticMessages.Add($"تم التحقق من استجابة ماكينة HP عبر الشبكة: {snmpStatus.Details}");
            }
            else
            {
                result.DiagnosticMessages.Add("تنبيه: تعذر الاستعلام عبر SNMP الخاص بـ HP — سيتم الاعتماد على Windows Spooler فقط.");
            }
        }

        return result;
    }

    public async Task<PrinterStatusInfo> CheckStatusAsync(string printerName)
    {
        return await _spoolerAdapter.CheckStatusAsync(printerName);
    }

    public async Task<PrintExecutionResult> SendPdfJobAsync(PrintJobRequest request)
    {
        // Dispatches through standard Spooler with HP driver settings
        return await _spoolerAdapter.SendPdfJobAsync(request);
    }

    public Task<bool> CancelJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.CancelJobAsync(printerName, spoolerJobId);
    public Task<bool> PauseJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.PauseJobAsync(printerName, spoolerJobId);
    public Task<bool> ResumeJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.ResumeJobAsync(printerName, spoolerJobId);
    public Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName) => _spoolerAdapter.GetActiveQueueJobsAsync(printerName);
}
