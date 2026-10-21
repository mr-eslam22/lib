using System.Collections.ObjectModel;
using System.Windows.Input;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.Core.Services;
using WindowsPrintAutomation.PrintEngine;

namespace WindowsPrintAutomation.UI.ViewModels;

#region RelayCommand Helper
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();
    public void Execute(object? parameter) => _execute();
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Predicate<T>? _canExecute;

    public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || (parameter is T t && _canExecute(t));
    public void Execute(object? parameter)
    {
        if (parameter is T t) _execute(t);
    }
}
#endregion

#region DashboardViewModel
public class DashboardViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly IRecoveryService _recoveryService;
    private readonly IPrinterProfileService _printerProfileService;
    private readonly MainViewModel _mainVM;

    private int _queuedJobsCount;
    private int _printingJobsCount;
    private int _recoveryRequiredCount;
    private int _completedJobsToday;
    private int _totalBooksInLibrary;

    public int QueuedJobsCount { get => _queuedJobsCount; set => SetProperty(ref _queuedJobsCount, value); }
    public int PrintingJobsCount { get => _printingJobsCount; set => SetProperty(ref _printingJobsCount, value); }
    public int RecoveryRequiredCount { get => _recoveryRequiredCount; set => SetProperty(ref _recoveryRequiredCount, value); }
    public int CompletedJobsToday { get => _completedJobsToday; set => SetProperty(ref _completedJobsToday, value); }
    public int TotalBooksInLibrary { get => _totalBooksInLibrary; set => SetProperty(ref _totalBooksInLibrary, value); }

    public ObservableCollection<Printer> Printers { get; } = new();
    public ObservableCollection<PrintJob> RecentJobs { get; } = new();

    public DashboardViewModel(DatabaseService db, IRecoveryService recovery, IPrinterProfileService printerProfile, MainViewModel mainVM)
    {
        _databaseService = db;
        _recoveryService = recovery;
        _printerProfileService = printerProfile;
        _mainVM = mainVM;
    }

    public async Task LoadDataAsync()
    {
        using var conn = _databaseService.CreateConnection();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Books WHERE IsAvailable = 1";
            TotalBooksInLibrary = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM PrintJobs WHERE Status = @st";
            cmd.Parameters.AddWithValue("@st", (int)JobStatus.Queued);
            QueuedJobsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM PrintJobs WHERE Status IN (@prep, @sub, @spool, @print)";
            cmd.Parameters.AddWithValue("@prep", (int)JobStatus.Preparing);
            cmd.Parameters.AddWithValue("@sub", (int)JobStatus.Submitted);
            cmd.Parameters.AddWithValue("@spool", (int)JobStatus.Spooling);
            cmd.Parameters.AddWithValue("@print", (int)JobStatus.Printing);
            PrintingJobsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        var recoveryJobs = await _recoveryService.GetJobsRequiringRecoveryAsync();
        RecoveryRequiredCount = recoveryJobs.Count;

        Printers.Clear();
        var printers = await _printerProfileService.GetPrintersAsync();
        foreach (var p in printers) Printers.Add(p);

        RecentJobs.Clear();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT Id, JobNumber, OrderId, OrderItemId, PrinterName, SpoolerJobId, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, ParentJobId, IsContinuation, CreatedAt
                FROM PrintJobs ORDER BY Id DESC LIMIT 10";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                RecentJobs.Add(new PrintJob
                {
                    Id = reader.GetInt64(0),
                    JobNumber = reader.GetString(1),
                    OrderId = reader.GetInt64(2),
                    OrderItemId = reader.GetInt64(3),
                    PrinterName = reader.GetString(4),
                    SpoolerJobId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    Status = (JobStatus)reader.GetInt32(6),
                    VerificationStatus = (VerificationStatus)reader.GetInt32(7),
                    CopiesRequested = reader.GetInt32(8),
                    CopiesVerified = reader.GetInt32(9),
                    CopiesDamaged = reader.GetInt32(10),
                    CopiesRemaining = reader.GetInt32(11),
                    PageCount = reader.GetInt32(12),
                    StartPage = reader.GetInt32(13),
                    EndPage = reader.GetInt32(14),
                    ParentJobId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                    IsContinuation = reader.GetInt32(16) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(17))
                });
            }
        }
    }
}
#endregion

#region LibraryViewModel
public class LibraryViewModel : ViewModelBase
{
    private readonly ILibraryService _libraryService;

    private string _searchQuery = string.Empty;
    private string _selectedGrade = "الكل";
    private string _selectedSubject = "الكل";
    private string _importStatusMessage = string.Empty;
    private bool _isScanning;
    private Book? _selectedBook;

    public string SearchQuery { get => _searchQuery; set { SetProperty(ref _searchQuery, value); _ = LoadBooksAsync(); } }
    public string SelectedGrade { get => _selectedGrade; set { SetProperty(ref _selectedGrade, value); _ = LoadSubjectsAsync(); _ = LoadBooksAsync(); } }
    public string SelectedSubject { get => _selectedSubject; set { SetProperty(ref _selectedSubject, value); _ = LoadBooksAsync(); } }
    public string ImportStatusMessage { get => _importStatusMessage; set => SetProperty(ref _importStatusMessage, value); }
    public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }
    public Book? SelectedBook { get => _selectedBook; set => SetProperty(ref _selectedBook, value); }

    public ObservableCollection<Book> Books { get; } = new();
    public ObservableCollection<string> Grades { get; } = new();
    public ObservableCollection<string> Subjects { get; } = new();

    public LibraryViewModel(ILibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    public async Task LoadGradesAsync()
    {
        Grades.Clear();
        Grades.Add("الكل");
        var list = await _libraryService.GetGradesAsync();
        foreach (var g in list) Grades.Add(g);
    }

    public async Task LoadSubjectsAsync()
    {
        Subjects.Clear();
        Subjects.Add("الكل");
        var list = await _libraryService.GetSubjectsAsync(SelectedGrade == "الكل" ? null : SelectedGrade);
        foreach (var s in list) Subjects.Add(s);
    }

    public async Task LoadBooksAsync()
    {
        Books.Clear();
        var books = await _libraryService.GetBooksAsync(
            SelectedGrade == "الكل" ? null : SelectedGrade,
            SelectedSubject == "الكل" ? null : SelectedSubject,
            SearchQuery);
        foreach (var b in books) Books.Add(b);
    }

    public async Task ImportFolderAsync(string folderPath)
    {
        IsScanning = true;
        ImportStatusMessage = "جاري فحص المجلد واستخراج ملفات PDF...";

        var progress = new Progress<string>(msg => ImportStatusMessage = msg);
        try
        {
            var result = await _libraryService.ImportFolderAsync(folderPath, progress);
            ImportStatusMessage = $"اكتمل الفحص: إجمالي {result.TotalFilesFound} ملف | استيراد جديد {result.ImportedCount} | تحديث {result.UpdatedCount} | غير مدعوم {result.UnsupportedCount} | تالف {result.CorruptedCount}";
            await LoadGradesAsync();
            await LoadSubjectsAsync();
            await LoadBooksAsync();
        }
        catch (Exception ex)
        {
            ImportStatusMessage = $"خطأ أثناء فحص المجلد: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}
#endregion

#region CustomersViewModel
public class CustomersViewModel : ViewModelBase
{
    private readonly ICustomerService _customerService;
    private string _searchQuery = string.Empty;
    private Customer? _selectedCustomer;

    public string SearchQuery { get => _searchQuery; set { SetProperty(ref _searchQuery, value); _ = LoadCustomersAsync(); } }
    public Customer? SelectedCustomer { get => _selectedCustomer; set => SetProperty(ref _selectedCustomer, value); }
    public ObservableCollection<Customer> Customers { get; } = new();

    public CustomersViewModel(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    public async Task LoadCustomersAsync()
    {
        Customers.Clear();
        var list = await _customerService.SearchCustomersAsync(SearchQuery);
        foreach (var c in list) Customers.Add(c);
    }

    public async Task<Customer> AddCustomerAsync(string name, string phone, string? notes = null)
    {
        var cust = await _customerService.CreateCustomerAsync(name, phone, notes);
        await LoadCustomersAsync();
        return cust;
    }
}
#endregion

#region OrdersViewModel
public class OrderItemDraft : ViewModelBase
{
    private Book _book;
    private int _copies = 1;
    private PaperSizeOption _paperSize = PaperSizeOption.A4;
    private PrintDuplexOption _duplex = PrintDuplexOption.DuplexLongEdge;
    private PrintColorOption _colorMode = PrintColorOption.Monochrome;
    private string? _paperTray = "درج تلقائي";

    public Book Book { get => _book; set => SetProperty(ref _book, value); }
    public int Copies { get => _copies; set { SetProperty(ref _copies, value); OnPropertyChanged(nameof(EstimatedSheets)); } }
    public PaperSizeOption PaperSize { get => _paperSize; set => SetProperty(ref _paperSize, value); }
    public PrintDuplexOption Duplex { get => _duplex; set { SetProperty(ref _duplex, value); OnPropertyChanged(nameof(EstimatedSheets)); } }
    public PrintColorOption ColorMode { get => _colorMode; set => SetProperty(ref _colorMode, value); }
    public string? PaperTray { get => _paperTray; set => SetProperty(ref _paperTray, value); }

    public int EstimatedSheets
    {
        get
        {
            if (Book == null || Book.PageCount <= 0 || Copies <= 0) return 0;
            if (Duplex == PrintDuplexOption.Simplex) return Copies * Book.PageCount;
            return Copies * ((Book.PageCount + 1) / 2);
        }
    }

    public OrderItemDraft(Book book)
    {
        _book = book;
    }
}

public class OrdersViewModel : ViewModelBase
{
    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly ILibraryService _libraryService;
    private readonly IPrinterProfileService _printerProfileService;
    private readonly MainViewModel _mainVM;

    private string _searchQuery = string.Empty;
    private Order? _selectedOrder;

    // Wizard
    private Customer? _selectedCustomer;
    private string _orderNotes = string.Empty;
    private DateTime? _deliveryDate;
    private string? _selectedPrinterName;

    public string SearchQuery { get => _searchQuery; set { SetProperty(ref _searchQuery, value); _ = LoadOrdersAsync(); } }
    public Order? SelectedOrder { get => _selectedOrder; set => SetProperty(ref _selectedOrder, value); }
    public Customer? SelectedCustomer { get => _selectedCustomer; set => SetProperty(ref _selectedCustomer, value); }
    public string OrderNotes { get => _orderNotes; set => SetProperty(ref _orderNotes, value); }
    public DateTime? DeliveryDate { get => _deliveryDate; set => SetProperty(ref _deliveryDate, value); }
    public string? SelectedPrinterName { get => _selectedPrinterName; set => SetProperty(ref _selectedPrinterName, value); }

    public ObservableCollection<Order> Orders { get; } = new();
    public ObservableCollection<OrderItemDraft> DraftItems { get; } = new();
    public ObservableCollection<Customer> AvailableCustomers { get; } = new();
    public ObservableCollection<Printer> AvailablePrinters { get; } = new();

    public int TotalDraftSheets => DraftItems.Sum(i => i.EstimatedSheets);

    public OrdersViewModel(IOrderService orderService, ICustomerService customerService, ILibraryService libraryService, IPrinterProfileService printerProfileService, MainViewModel mainVM)
    {
        _orderService = orderService;
        _customerService = customerService;
        _libraryService = libraryService;
        _printerProfileService = printerProfileService;
        _mainVM = mainVM;
    }

    public async Task LoadOrdersAsync()
    {
        Orders.Clear();
        var list = await _orderService.GetOrdersAsync(searchQuery: SearchQuery);
        foreach (var o in list) Orders.Add(o);
    }

    public async Task PrepareNewOrderAsync()
    {
        DraftItems.Clear();
        OrderNotes = string.Empty;
        DeliveryDate = null;

        AvailableCustomers.Clear();
        var customers = await _customerService.SearchCustomersAsync(string.Empty);
        foreach (var c in customers) AvailableCustomers.Add(c);
        SelectedCustomer = AvailableCustomers.FirstOrDefault();

        AvailablePrinters.Clear();
        var printers = await _printerProfileService.GetPrintersAsync();
        foreach (var p in printers) AvailablePrinters.Add(p);
        SelectedPrinterName = AvailablePrinters.FirstOrDefault()?.WindowsPrinterName;
    }

    public void AddBookToDraft(Book book)
    {
        DraftItems.Add(new OrderItemDraft(book));
        OnPropertyChanged(nameof(TotalDraftSheets));
    }

    public void RemoveDraftItem(OrderItemDraft item)
    {
        DraftItems.Remove(item);
        OnPropertyChanged(nameof(TotalDraftSheets));
    }

    public async Task<Order> SubmitOrderAsync()
    {
        if (SelectedCustomer == null) throw new InvalidOperationException("يجب اختيار أو تسجيل عميل للطلب.");
        if (DraftItems.Count == 0) throw new InvalidOperationException("يجب إضافة كتاب واحد على الأقل للطلب.");

        var order = new Order
        {
            CustomerId = SelectedCustomer.Id,
            CustomerName = SelectedCustomer.FullName,
            CustomerPhone = SelectedCustomer.PhoneNumber,
            Notes = OrderNotes,
            DeliveryDate = DeliveryDate,
            Items = DraftItems.Select(d => new OrderItem
            {
                BookId = d.Book.Id,
                BookTitle = d.Book.Title,
                Grade = d.Book.Grade,
                Subject = d.Book.Subject,
                FilePath = d.Book.FilePath,
                PageCount = d.Book.PageCount,
                Copies = d.Copies,
                PaperSize = d.PaperSize,
                Duplex = d.Duplex,
                ColorMode = d.ColorMode,
                PaperTray = d.PaperTray
            }).ToList()
        };

        var created = await _orderService.CreateOrderAsync(order, SelectedPrinterName);
        await LoadOrdersAsync();
        DraftItems.Clear();
        return created;
    }
}
#endregion

#region PrintersViewModel
public class PrintersViewModel : ViewModelBase
{
    private readonly IPrinterProfileService _printerProfileService;
    private readonly IPrintEngine _printEngine;
    private Printer? _selectedPrinter;
    private string _statusMessage = string.Empty;

    public Printer? SelectedPrinter { get => _selectedPrinter; set { SetProperty(ref _selectedPrinter, value); _ = LoadProfilesAsync(); } }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public ObservableCollection<Printer> Printers { get; } = new();
    public ObservableCollection<PrinterProfile> Profiles { get; } = new();

    public PrintersViewModel(IPrinterProfileService printerProfileService, IPrintEngine printEngine)
    {
        _printerProfileService = printerProfileService;
        _printEngine = printEngine;
    }

    public async Task LoadPrintersAsync()
    {
        Printers.Clear();
        var list = await _printerProfileService.GetPrintersAsync();
        foreach (var p in list) Printers.Add(p);
        SelectedPrinter = Printers.FirstOrDefault();
    }

    public async Task LoadProfilesAsync()
    {
        Profiles.Clear();
        if (SelectedPrinter != null)
        {
            var profiles = await _printerProfileService.GetProfilesForPrinterAsync(SelectedPrinter.Id);
            foreach (var pr in profiles) Profiles.Add(pr);
        }
    }

    public async Task TogglePausePrinterAsync(Printer printer)
    {
        if (printer.IsPaused)
        {
            await _printEngine.ResumePrinterAsync(printer.WindowsPrinterName);
            printer.IsPaused = false;
            printer.CurrentStatusText = "جاهزة";
        }
        else
        {
            await _printEngine.PausePrinterAsync(printer.WindowsPrinterName);
            printer.IsPaused = true;
            printer.CurrentStatusText = "متوقفة يدويًا";
        }
        await _printerProfileService.SaveOrUpdatePrinterAsync(printer);
        await LoadPrintersAsync();
    }
}
#endregion

#region PrintQueueViewModel
public class PrintQueueViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly IRecoveryService _recoveryService;
    private readonly IPrintEngine _printEngine;
    private PrintJob? _selectedJob;

    public PrintJob? SelectedJob { get => _selectedJob; set => SetProperty(ref _selectedJob, value); }
    public ObservableCollection<PrintJob> ActiveJobs { get; } = new();

    public PrintQueueViewModel(DatabaseService db, IRecoveryService recovery, IPrintEngine printEngine)
    {
        _databaseService = db;
        _recoveryService = recovery;
        _printEngine = printEngine;
    }

    public async Task LoadJobsAsync()
    {
        ActiveJobs.Clear();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, JobNumber, OrderId, OrderItemId, PrinterName, SpoolerJobId, Status, VerificationStatus, CopiesRequested, CopiesVerified, CopiesDamaged, CopiesRemaining, PageCount, StartPage, EndPage, ParentJobId, IsContinuation, CreatedAt, SubmittedAt, CompletedAt, LastErrorMessage
            FROM PrintJobs 
            WHERE Status NOT IN (@comp, @canc)
            ORDER BY Id DESC";
        cmd.Parameters.AddWithValue("@comp", (int)JobStatus.Completed);
        cmd.Parameters.AddWithValue("@canc", (int)JobStatus.Cancelled);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ActiveJobs.Add(new PrintJob
            {
                Id = reader.GetInt64(0),
                JobNumber = reader.GetString(1),
                OrderId = reader.GetInt64(2),
                OrderItemId = reader.GetInt64(3),
                PrinterName = reader.GetString(4),
                SpoolerJobId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Status = (JobStatus)reader.GetInt32(6),
                VerificationStatus = (VerificationStatus)reader.GetInt32(7),
                CopiesRequested = reader.GetInt32(8),
                CopiesVerified = reader.GetInt32(9),
                CopiesDamaged = reader.GetInt32(10),
                CopiesRemaining = reader.GetInt32(11),
                PageCount = reader.GetInt32(12),
                StartPage = reader.GetInt32(13),
                EndPage = reader.GetInt32(14),
                ParentJobId = reader.IsDBNull(15) ? null : reader.GetInt64(15),
                IsContinuation = reader.GetInt32(16) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(17)),
                SubmittedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18)),
                CompletedAt = reader.IsDBNull(19) ? null : DateTime.Parse(reader.GetString(19)),
                LastErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20)
            });
        }
    }

    public async Task CancelJobAsync(PrintJob job)
    {
        await _printEngine.CancelJobAsync(job.Id);
        await LoadJobsAsync();
    }
}
#endregion

#region RecoveryViewModel
public class RecoveryViewModel : ViewModelBase
{
    private readonly IRecoveryService _recoveryService;
    private PrintJob? _selectedJob;
    private int _verifiedCountInput;
    private int _damagedCountInput;
    private string _notesInput = string.Empty;
    private string _statusMessage = string.Empty;

    public PrintJob? SelectedJob
    {
        get => _selectedJob;
        set
        {
            SetProperty(ref _selectedJob, value);
            if (value != null)
            {
                VerifiedCountInput = value.CopiesVerified;
                DamagedCountInput = value.CopiesDamaged;
                OnPropertyChanged(nameof(RemainingCalculated));
            }
        }
    }

    public int VerifiedCountInput { get => _verifiedCountInput; set { SetProperty(ref _verifiedCountInput, value); OnPropertyChanged(nameof(RemainingCalculated)); } }
    public int DamagedCountInput { get => _damagedCountInput; set { SetProperty(ref _damagedCountInput, value); OnPropertyChanged(nameof(RemainingCalculated)); } }
    public string NotesInput { get => _notesInput; set => SetProperty(ref _notesInput, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public int RemainingCalculated => SelectedJob == null ? 0 : Math.Max(0, SelectedJob.CopiesRequested - VerifiedCountInput);

    public ObservableCollection<PrintJob> RecoveryJobs { get; } = new();

    public RecoveryViewModel(IRecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
    }

    public async Task LoadRecoveryJobsAsync()
    {
        RecoveryJobs.Clear();
        var jobs = await _recoveryService.GetJobsRequiringRecoveryAsync();
        foreach (var j in jobs) RecoveryJobs.Add(j);
        SelectedJob = RecoveryJobs.FirstOrDefault();
    }

    public async Task ConfirmVerificationAsync()
    {
        if (SelectedJob == null) return;
        try
        {
            var updated = await _recoveryService.ConfirmPhysicalVerificationAsync(
                SelectedJob.Id,
                VerifiedCountInput,
                DamagedCountInput,
                string.IsNullOrWhiteSpace(NotesInput) ? "تأكيد فيزيائي يدوي للنسخ السليمة والتالفة" : NotesInput);

            StatusMessage = $"تم تسجيل التأكيد: {updated.CopiesVerified} سليم | {updated.CopiesRemaining} متبقي.";
            await LoadRecoveryJobsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }

    public async Task ResumeContinuationAsync()
    {
        if (SelectedJob == null) return;
        int remaining = RemainingCalculated;
        if (remaining <= 0)
        {
            StatusMessage = "الكمية المتبقية 0، لا يلزم استكمال.";
            return;
        }

        try
        {
            var continuation = await _recoveryService.CreateContinuationJobAsync(
                SelectedJob.Id,
                remaining,
                1,
                SelectedJob.PageCount);

            StatusMessage = $"تم إنشاء مهمة استكمال جديدة #{continuation.JobNumber} بعدد {remaining} نسخ متبقية.";
            await LoadRecoveryJobsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"خطأ: {ex.Message}";
        }
    }
}
#endregion

#region ErrorLogsViewModel
public class ErrorLogsViewModel : ViewModelBase
{
    private readonly IRecoveryService _recoveryService;
    private bool _unresolvedOnly = false;
    private ErrorLog? _selectedLog;
    private string _resolutionNotes = string.Empty;
    private string _statusMessage = string.Empty;

    public bool UnresolvedOnly { get => _unresolvedOnly; set { SetProperty(ref _unresolvedOnly, value); _ = LoadLogsAsync(); } }
    public ErrorLog? SelectedLog { get => _selectedLog; set => SetProperty(ref _selectedLog, value); }
    public string ResolutionNotes { get => _resolutionNotes; set => SetProperty(ref _resolutionNotes, value); }
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public ObservableCollection<ErrorLog> ErrorLogs { get; } = new();

    public ErrorLogsViewModel(IRecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
    }

    public async Task LoadLogsAsync()
    {
        ErrorLogs.Clear();
        var logs = await _recoveryService.GetErrorLogsAsync(unresolvedOnly: UnresolvedOnly ? true : null);
        foreach (var l in logs) ErrorLogs.Add(l);
        SelectedLog = ErrorLogs.FirstOrDefault();
    }

    public async Task ResolveSelectedErrorAsync()
    {
        if (SelectedLog == null) return;
        var notes = string.IsNullOrWhiteSpace(ResolutionNotes) ? "تمت المعالجة يدويًا من قِبل المشغّل" : ResolutionNotes;
        await _recoveryService.ResolveErrorLogAsync(SelectedLog.Id, notes);
        StatusMessage = "تم تعليم الخطأ كـ 'تمت المعالجة'.";
        await LoadLogsAsync();
    }
}
#endregion

#region SettingsViewModel
public class SettingsViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private string _statusMessage = string.Empty;

    public string DatabasePath => _databaseService.DatabasePath;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public SettingsViewModel(DatabaseService db)
    {
        _databaseService = db;
    }

    public void CreateBackup()
    {
        try
        {
            var path = _databaseService.CreateBackup();
            StatusMessage = $"تم إنشاء نسخة احتياطية بنجاح في:\n{path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"فشل إنشاء النسخة الاحتياطية: {ex.Message}";
        }
    }
}
#endregion
