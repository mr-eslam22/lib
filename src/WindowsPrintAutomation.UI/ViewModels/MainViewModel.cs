using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using WindowsPrintAutomation.PrintEngine;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.UI.ViewModels;

public class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class MainViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly ILibraryService _libraryService;
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;
    private readonly IPrinterProfileService _printerProfileService;
    private readonly IRecoveryService _recoveryService;
    private readonly IPrintEngine _printEngine;

    private object _currentView;
    private string _activeSectionTitle = "لوحة التحكم";
    private int _unverifiedJobsCount;
    private int _unresolvedErrorsCount;
    private string _systemStatusText = "النظام جاهز ومستقر";
    private bool _hasActiveRecoveryAlert;

    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string ActiveSectionTitle
    {
        get => _activeSectionTitle;
        set => SetProperty(ref _activeSectionTitle, value);
    }

    public int UnverifiedJobsCount
    {
        get => _unverifiedJobsCount;
        set => SetProperty(ref _unverifiedJobsCount, value);
    }

    public int UnresolvedErrorsCount
    {
        get => _unresolvedErrorsCount;
        set
        {
            if (SetProperty(ref _unresolvedErrorsCount, value))
            {
                OnPropertyChanged(nameof(HasUnresolvedErrors));
            }
        }
    }

    public bool HasUnresolvedErrors => _unresolvedErrorsCount > 0;

    public string SystemStatusText
    {
        get => _systemStatusText;
        set => SetProperty(ref _systemStatusText, value);
    }

    public bool HasActiveRecoveryAlert
    {
        get => _hasActiveRecoveryAlert;
        set => SetProperty(ref _hasActiveRecoveryAlert, value);
    }

    // ViewModels
    public DashboardViewModel DashboardVM { get; }
    public LibraryViewModel LibraryVM { get; }
    public CustomersViewModel CustomersVM { get; }
    public OrdersViewModel OrdersVM { get; }
    public PrintersViewModel PrintersVM { get; }
    public PrintQueueViewModel PrintQueueVM { get; }
    public RecoveryViewModel RecoveryVM { get; }
    public ErrorLogsViewModel ErrorLogsVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public MainViewModel(
        DatabaseService databaseService,
        ILibraryService libraryService,
        ICustomerService customerService,
        IOrderService orderService,
        IPrinterProfileService printerProfileService,
        IRecoveryService recoveryService,
        IPrintEngine printEngine)
    {
        _databaseService = databaseService;
        _libraryService = libraryService;
        _customerService = customerService;
        _orderService = orderService;
        _printerProfileService = printerProfileService;
        _recoveryService = recoveryService;
        _printEngine = printEngine;

        DashboardVM = new DashboardViewModel(_databaseService, _recoveryService, _printerProfileService, this);
        LibraryVM = new LibraryViewModel(_libraryService);
        CustomersVM = new CustomersViewModel(_customerService);
        OrdersVM = new OrdersViewModel(_orderService, _customerService, _libraryService, _printerProfileService, this);
        PrintersVM = new PrintersViewModel(_printerProfileService, _printEngine);
        PrintQueueVM = new PrintQueueViewModel(_databaseService, _recoveryService, _printEngine);
        RecoveryVM = new RecoveryViewModel(_recoveryService);
        ErrorLogsVM = new ErrorLogsViewModel(_recoveryService);
        SettingsVM = new SettingsViewModel(_databaseService);

        _currentView = DashboardVM;

        _printEngine.JobStatusChanged += (s, e) =>
        {
            _ = RefreshBadgesAsync();
        };

        _printEngine.PrinterErrorOccurred += (s, e) =>
        {
            _ = RefreshBadgesAsync();
        };
    }

    public async Task InitializeAsync()
    {
        // 1. Startup Recovery Check
        var recoveryCheck = await _recoveryService.CheckAndFlagIncompleteJobsOnStartupAsync();
        if (recoveryCheck.OrphanedJobsCount > 0)
        {
            HasActiveRecoveryAlert = true;
            SystemStatusText = $"تنبيه: تم العثور على {recoveryCheck.OrphanedJobsCount} مهمة غير مكتملة بعد إعادة التشغيل.";
            NavigateTo("Recovery");
        }

        // 2. Refresh Badges
        await RefreshBadgesAsync();
        await DashboardVM.LoadDataAsync();
    }

    public async Task RefreshBadgesAsync()
    {
        var recoveryJobs = await _recoveryService.GetJobsRequiringRecoveryAsync();
        UnverifiedJobsCount = recoveryJobs.Count;
        HasActiveRecoveryAlert = recoveryJobs.Count > 0;

        var errors = await _recoveryService.GetErrorLogsAsync(unresolvedOnly: true);
        UnresolvedErrorsCount = errors.Count;
    }

    public void NavigateTo(string destination)
    {
        switch (destination)
        {
            case "Dashboard":
                CurrentView = DashboardVM;
                ActiveSectionTitle = "لوحة التحكم الرئيسية";
                _ = DashboardVM.LoadDataAsync();
                break;
            case "Library":
                CurrentView = LibraryVM;
                ActiveSectionTitle = "إدارة الكتب والمجلدات";
                _ = LibraryVM.LoadBooksAsync();
                break;
            case "Customers":
                CurrentView = CustomersVM;
                ActiveSectionTitle = "دليل العملاء";
                _ = CustomersVM.LoadCustomersAsync();
                break;
            case "Orders":
                CurrentView = OrdersVM;
                ActiveSectionTitle = "الطلبات والإنشاء";
                _ = OrdersVM.LoadOrdersAsync();
                break;
            case "Printers":
                CurrentView = PrintersVM;
                ActiveSectionTitle = "الماكينات وملفات التعريف (HP & Kyocera)";
                _ = PrintersVM.LoadPrintersAsync();
                break;
            case "Queue":
                CurrentView = PrintQueueVM;
                ActiveSectionTitle = "طابور الطباعة والتأكيد الفيزيائي";
                _ = PrintQueueVM.LoadJobsAsync();
                break;
            case "Recovery":
                CurrentView = RecoveryVM;
                ActiveSectionTitle = "استعادة المهام المتوقفة والتعطل";
                _ = RecoveryVM.LoadRecoveryJobsAsync();
                break;
            case "ErrorLogs":
                CurrentView = ErrorLogsVM;
                ActiveSectionTitle = "سجل الأخطاء والتدقيق";
                _ = ErrorLogsVM.LoadLogsAsync();
                break;
            case "Settings":
                CurrentView = SettingsVM;
                ActiveSectionTitle = "الإعدادات والنسخ الاحتياطي";
                break;
        }
    }
}
