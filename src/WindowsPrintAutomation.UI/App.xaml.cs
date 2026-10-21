using System.Windows;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using WindowsPrintAutomation.PrintEngine;
using WindowsPrintAutomation.PrintEngine.Adapters;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI;

public partial class App : Application
{
    private DatabaseService? _databaseService;
    private PrintEngine.PrintEngine? _printEngine;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Setup SQLite Database Service
        _databaseService = new DatabaseService();

        // Setup Core Services
        var libraryService = new LibraryService(_databaseService);
        var customerService = new CustomerService(_databaseService);
        var orderService = new OrderService(_databaseService);
        var printerProfileService = new PrinterProfileService(_databaseService);
        var recoveryService = new RecoveryService(_databaseService);

        // Auto-seed or detect printers if empty
        var existingPrinters = await printerProfileService.GetPrintersAsync();
        if (existingPrinters.Count == 0)
        {
            var installedPrinters = WindowsSpoolerAdapter.GetInstalledWindowsPrinters();
            if (installedPrinters.Count > 0)
            {
                foreach (var pName in installedPrinters)
                {
                    bool isKyocera = pName.Contains("Kyocera", StringComparison.OrdinalIgnoreCase) || pName.Contains("TASKalfa", StringComparison.OrdinalIgnoreCase);
                    bool isHp = pName.Contains("HP", StringComparison.OrdinalIgnoreCase) || pName.Contains("LaserJet", StringComparison.OrdinalIgnoreCase);

                    await printerProfileService.SaveOrUpdatePrinterAsync(new Printer
                    {
                        WindowsPrinterName = pName,
                        Manufacturer = isKyocera ? "Kyocera" : (isHp ? "HP" : "Generic"),
                        Model = pName,
                        IsOnline = true,
                        SupportsDuplex = true,
                        SupportedPaperSizes = "A4,B5,Custom",
                        AvailableTrays = "درج تلقائي,درج 1,درج 2,درج جانبي"
                    });
                }
            }
            else
            {
                // Fallback default printers (HP & Kyocera)
                await printerProfileService.SaveOrUpdatePrinterAsync(new Printer
                {
                    WindowsPrinterName = "HP LaserJet Enterprise M553",
                    Manufacturer = "HP",
                    Model = "LaserJet Enterprise M553",
                    IsOnline = true,
                    SupportsDuplex = true,
                    SupportsColor = true,
                    SupportedPaperSizes = "A4,B5",
                    AvailableTrays = "درج تلقائي,درج 1,درج 2"
                });

                await printerProfileService.SaveOrUpdatePrinterAsync(new Printer
                {
                    WindowsPrinterName = "Kyocera TASKalfa 3212i",
                    Manufacturer = "Kyocera",
                    Model = "TASKalfa 3212i",
                    IsOnline = true,
                    SupportsDuplex = true,
                    SupportsColor = false,
                    SupportedPaperSizes = "A4,B5,Custom",
                    AvailableTrays = "درج تلقائي,درج 1,درج 2,درج 3"
                });
            }
        }

        // Initialize Print Engine with Windows Spooler Adapter
        _printEngine = new PrintEngine.PrintEngine(_databaseService, recoveryService, new WindowsSpoolerAdapter());
        await _printEngine.StartAsync();

        // Create Main ViewModel
        var mainViewModel = new MainViewModel(
            _databaseService,
            libraryService,
            customerService,
            orderService,
            printerProfileService,
            recoveryService,
            _printEngine);

        await mainViewModel.InitializeAsync();

        var mainWindow = new MainWindow(mainViewModel);
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_printEngine != null)
        {
            await _printEngine.StopAsync();
        }
        _databaseService?.Dispose();
        base.OnExit(e);
    }
}
