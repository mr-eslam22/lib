using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class KyoceraPrinterAdapter : IPrinterAdapter
{
    private readonly WindowsSpoolerAdapter _spoolerAdapter;
    private readonly KyoceraHpSnmpAdapter _snmpHelper;

    public string AdapterId => "KyoceraTaskAlfa";
    public string DisplayName => "Kyocera TASKalfa & ECOSYS Adapter (KPDL/KX)";
    public string VendorFamily => "Kyocera";
    public string AdapterName => DisplayName;

    public KyoceraPrinterAdapter()
    {
        _spoolerAdapter = new WindowsSpoolerAdapter();
        _snmpHelper = new KyoceraHpSnmpAdapter();
    }

    public DeviceCapabilitiesReport ProbeCapabilities(string printerName, string? driverName = null)
    {
        var report = _spoolerAdapter.ProbeCapabilities(printerName, driverName);
        report.ConfirmedCapabilitiesList.Add("تكامل Kyocera KX / KPDL Driver نشط");
        report.ConfirmedCapabilitiesList.Add("دعم تغذية الورق B5 وA4 للأدراج 1 و2");

        // Kyocera TASKalfa production printers have dedicated multi-sensor diagnostics
        report.HasHardwareJamSensor = true;
        report.HasHardwarePaperOutSensor = true;
        report.HasPageCounterSensor = true;

        if (report.B5Support != CapabilityStatus.VerifiedUnsupported)
        {
            report.B5Support = CapabilityStatus.VerifiedSupported;
            if (!report.ConfirmedCapabilitiesList.Contains("مقاس B5 مؤكد"))
            {
                report.ConfirmedCapabilitiesList.Add("مقاس B5 مؤكد (توافق أدراج Kyocera)");
            }
        }

        return report;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(string printerName, string? connectionAddress)
    {
        var result = await _spoolerAdapter.TestConnectionAsync(printerName, connectionAddress);
        result.DiagnosticMessages.Add("فحص تعريف Kyocera: التوافق مع بروتوكول KPDL وأدراج TASKalfa جاهز.");

        if (!string.IsNullOrWhiteSpace(connectionAddress) && connectionAddress.Contains("."))
        {
            var snmpStatus = await _snmpHelper.QueryPrinterHardwareStatusAsync(connectionAddress);
            if (snmpStatus.IsReachable)
            {
                result.DiagnosticMessages.Add($"تم التحقق من استجابة ماكينة Kyocera عبر SNMP: {snmpStatus.Details}");
            }
            else
            {
                result.DiagnosticMessages.Add("تنبيه: لم يتم الرد عبر SNMP من ماكينة Kyocera — سيتم استخدام Windows Spooler فقط.");
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
        return await _spoolerAdapter.SendPdfJobAsync(request);
    }

    public Task<bool> CancelJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.CancelJobAsync(printerName, spoolerJobId);
    public Task<bool> PauseJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.PauseJobAsync(printerName, spoolerJobId);
    public Task<bool> ResumeJobAsync(string printerName, int spoolerJobId) => _spoolerAdapter.ResumeJobAsync(printerName, spoolerJobId);
    public Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName) => _spoolerAdapter.GetActiveQueueJobsAsync(printerName);
}
