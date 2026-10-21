using UglyToad.PdfPig.Writer;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using WindowsPrintAutomation.PrintEngine;
using WindowsPrintAutomation.PrintEngine.Adapters;
using WindowsPrintAutomation.PrintEngine.Interfaces;
using Xunit;

namespace WindowsPrintAutomation.Tests;

public class MultiVendorAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseService _dbService;
    private readonly RecoveryService _recoveryService;
    private readonly PrinterProfileService _printerProfileService;

    public MultiVendorAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinPrintTest_MultiVendor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbService = new DatabaseService(Path.Combine(_tempDir, "multivendor_test.db"));
        _recoveryService = new RecoveryService(_dbService);
        _printerProfileService = new PrinterProfileService(_dbService);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("HP Color LaserJet Enterprise M553", "HP PCL6 Driver", "HP")]
    [InlineData("Kyocera TASKalfa 3212i", "Kyocera KPDL Driver", "Kyocera")]
    [InlineData("Canon imageRUNNER ADVANCE C5535", "Canon UFR II", "Canon")]
    [InlineData("Ricoh Aficio MP 5055", "Ricoh PCL", "Ricoh")]
    [InlineData("Xerox VersaLink B400", "Xerox PS", "Xerox")]
    [InlineData("Brother MFC-L8900CDW", "Brother BR-Script3", "Brother")]
    [InlineData("Epson WorkForce Pro", "Epson ESC/P", "Epson")]
    [InlineData("Standard Office Printer", "Generic Text Only", "Generic")]
    public void DetectManufacturer_VariousDriverNames_DetectsCorrectVendor(string printerName, string driverName, string expectedVendor)
    {
        var vendor = PrinterDiscoveryService.DetectManufacturer(printerName, driverName);
        Assert.Equal(expectedVendor, vendor);
    }

    [Fact]
    public void ProbeCapabilities_KyoceraVsHp_DistinguishesConfirmedVsUnconfirmedSettings()
    {
        var fakeAdapter = new FakePrinterAdapter();

        // Kyocera TASKalfa probe:
        var kyoReport = fakeAdapter.ProbeCapabilities("Kyocera TASKalfa 3212i");
        Assert.Equal(CapabilityStatus.VerifiedSupported, kyoReport.A4Support);
        Assert.Equal(CapabilityStatus.VerifiedSupported, kyoReport.B5Support); // B5 confirmed!
        Assert.Equal(CapabilityStatus.VerifiedSupported, kyoReport.DuplexSupport);
        Assert.Contains(kyoReport.ConfirmedCapabilitiesList, c => c.Contains("B5"));

        // HP LaserJet probe:
        var hpReport = fakeAdapter.ProbeCapabilities("HP Color LaserJet Enterprise M553");
        Assert.Equal(CapabilityStatus.VerifiedSupported, hpReport.A4Support);
        Assert.Equal(CapabilityStatus.UnconfirmedOrUnknown, hpReport.B5Support); // B5 unconfirmed by default on standard A4 tray!
        Assert.Contains(hpReport.UnconfirmedCapabilitiesList, c => c.Contains("B5"));

        // Generic printer probe:
        var genReport = fakeAdapter.ProbeCapabilities("Generic Windows Virtual Printer");
        Assert.Equal(CapabilityStatus.UnconfirmedOrUnknown, genReport.DuplexSupport);
        Assert.Equal(CapabilityStatus.UnconfirmedOrUnknown, genReport.B5Support);
    }

    [Fact]
    public async Task TestConnectionAsync_SimulatedPrinter_ReturnsAccurateLatencyAndDiagnostics()
    {
        var fakeAdapter = new FakePrinterAdapter();
        var result = await fakeAdapter.TestConnectionAsync("Kyocera TASKalfa 3212i", "192.168.1.120");

        Assert.True(result.IsSuccess);
        Assert.True(result.SpoolerResponding);
        Assert.True(result.HardwareResponding);
        Assert.True(result.LatencyMs >= 0);
        Assert.NotEmpty(result.DiagnosticMessages);
        Assert.NotNull(result.CapabilitiesReport);
    }

    [Fact]
    public void PrinterAdapterRegistry_Extensibility_AllowsRegisteringCustomAdapterWithoutCoreChanges()
    {
        var defaultAdapter = new WindowsSpoolerAdapter();
        var registry = new PrinterAdapterRegistry(defaultAdapter);

        // Define and register a brand new vendor adapter dynamically
        var customPressAdapter = new CustomPressTestAdapter();
        registry.RegisterAdapter(customPressAdapter);

        var retrieved = registry.GetAdapter("KonicaPressIndustrial");
        Assert.Same(customPressAdapter, retrieved);

        var printer = new Printer
        {
            WindowsPrinterName = "Konica Minolta AccurioPress C3080",
            AdapterFamily = "KonicaPressIndustrial"
        };

        var resolved = registry.ResolveAdapterForPrinter(printer);
        Assert.Same(customPressAdapter, resolved);
    }

    [Fact]
    public async Task MachineIsolation_MultiVendorFleet_ErrorOnOnePrinterDoesNotHaltOtherPrinters()
    {
        var fakeAdapter = new FakePrinterAdapter();
        var registry = new PrinterAdapterRegistry(fakeAdapter);
        var printEngine = new PrintEngine.PrintEngine(_dbService, _recoveryService, fakeAdapter, registry);

        // Seed HP and Kyocera printers in DB
        await _printerProfileService.SaveOrUpdatePrinterAsync(new Printer
        {
            WindowsPrinterName = "HP LaserJet Pro MFP M428fdw",
            Manufacturer = "HP",
            Model = "M428fdw",
            AdapterFamily = "FakeSimulated",
            IsOnline = true
        });

        await _printerProfileService.SaveOrUpdatePrinterAsync(new Printer
        {
            WindowsPrinterName = "Kyocera TASKalfa 3212i",
            Manufacturer = "Kyocera",
            Model = "3212i",
            AdapterFamily = "FakeSimulated",
            IsOnline = true
        });

        var pdfPath = Path.Combine(_tempDir, "sample.pdf");
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        File.WriteAllBytes(pdfPath, builder.Build());

        // Create Job 1 on HP, Job 2 on Kyocera
        long hpJobId, kyoJobId;
        using (var conn = _dbService.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Orders (OrderNumber, CustomerId, CustomerName, CustomerPhone, CreatedAt) VALUES ('ORD-MV-1', 1, 'عميل', '010', @now) RETURNING Id;";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            var ordId = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            using var itemCmd = conn.CreateCommand();
            itemCmd.CommandText = "INSERT INTO OrderItems (OrderId, BookId, BookTitle, Grade, Subject, FilePath, PageCount, Copies, Status, RemainingCopies) VALUES (@oId, 1, 'كتاب', 'الأول', 'عربي', @p, 1, 5, 0, 5) RETURNING Id;";
            itemCmd.Parameters.AddWithValue("@oId", ordId);
            itemCmd.Parameters.AddWithValue("@p", pdfPath);
            var itemId = Convert.ToInt64(await itemCmd.ExecuteScalarAsync());

            using var jCmd1 = conn.CreateCommand();
            jCmd1.CommandText = "INSERT INTO PrintJobs (JobNumber, OrderId, OrderItemId, PrinterName, Status, VerificationStatus, CopiesRequested, CopiesRemaining, PageCount, CreatedAt) VALUES ('JOB-HP', @oId, @iId, 'HP LaserJet Pro MFP M428fdw', 0, 4, 5, 5, 1, @now) RETURNING Id;";
            jCmd1.Parameters.AddWithValue("@oId", ordId);
            jCmd1.Parameters.AddWithValue("@iId", itemId);
            jCmd1.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            hpJobId = Convert.ToInt64(await jCmd1.ExecuteScalarAsync());

            using var jCmd2 = conn.CreateCommand();
            jCmd2.CommandText = "INSERT INTO PrintJobs (JobNumber, OrderId, OrderItemId, PrinterName, Status, VerificationStatus, CopiesRequested, CopiesRemaining, PageCount, CreatedAt) VALUES ('JOB-KYO', @oId, @iId, 'Kyocera TASKalfa 3212i', 0, 4, 5, 5, 1, @now) RETURNING Id;";
            jCmd2.Parameters.AddWithValue("@oId", ordId);
            jCmd2.Parameters.AddWithValue("@iId", itemId);
            jCmd2.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            kyoJobId = Convert.ToInt64(await jCmd2.ExecuteScalarAsync());
        }

        // Simulate error on HP
        fakeAdapter.ForcedErrorNextJob = ErrorType.PaperJam;
        fakeAdapter.ForcedErrorMessage = "انحشار ورق في وحدة HP";

        var hpResult = await printEngine.ProcessSingleJobAsync(hpJobId);
        Assert.False(hpResult.Success);
        Assert.Equal(ErrorType.PaperJam, hpResult.ErrorType);

        // Now process Kyocera job: Kyocera MUST succeed independently without being halted!
        var kyoResult = await printEngine.ProcessSingleJobAsync(kyoJobId);
        Assert.True(kyoResult.Success);
        Assert.NotNull(kyoResult.SpoolerJobId);

        var kyoJob = await _recoveryService.GetJobByIdAsync(kyoJobId);
        Assert.NotNull(kyoJob);
        Assert.Equal(JobStatus.NeedsAttention, kyoJob.Status); // Finished spooling, awaiting physical verification
    }

    // Custom test adapter demonstrating extensibility
    private class CustomPressTestAdapter : IPrinterAdapter
    {
        public string AdapterId => "KonicaPressIndustrial";
        public string DisplayName => "Konica Minolta AccurioPress Adapter";
        public string VendorFamily => "Konica Minolta";
        public string AdapterName => DisplayName;

        public DeviceCapabilitiesReport ProbeCapabilities(string printerName, string? driverName = null)
        {
            return new DeviceCapabilitiesReport
            {
                PrinterName = printerName,
                A4Support = CapabilityStatus.VerifiedSupported,
                B5Support = CapabilityStatus.VerifiedSupported,
                DuplexSupport = CapabilityStatus.VerifiedSupported,
                ColorSupport = CapabilityStatus.VerifiedSupported
            };
        }

        public Task<ConnectionTestResult> TestConnectionAsync(string printerName, string? connectionAddress)
        {
            return Task.FromResult(new ConnectionTestResult { IsSuccess = true, LatencyMs = 5 });
        }

        public Task<PrinterStatusInfo> CheckStatusAsync(string printerName) => Task.FromResult(new PrinterStatusInfo { IsOnline = true });
        public Task<PrintExecutionResult> SendPdfJobAsync(PrintJobRequest request) => Task.FromResult(new PrintExecutionResult { Success = true, SpoolerJobId = 9999 });
        public Task<bool> CancelJobAsync(string printerName, int spoolerJobId) => Task.FromResult(true);
        public Task<bool> PauseJobAsync(string printerName, int spoolerJobId) => Task.FromResult(true);
        public Task<bool> ResumeJobAsync(string printerName, int spoolerJobId) => Task.FromResult(true);
        public Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName) => Task.FromResult(new List<SpoolerJobInfo>());
    }
}
