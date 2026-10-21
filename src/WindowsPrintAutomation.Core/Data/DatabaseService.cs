using System.Data;
using Microsoft.Data.Sqlite;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Data;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbFilePath;

    public DatabaseService(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(appData, "WindowsPrintAutomation");
            Directory.CreateDirectory(appDir);
            _dbFilePath = Path.Combine(appDir, "library_print_system.db");
        }
        else
        {
            _dbFilePath = Path.GetFullPath(dbPath);
            var dir = Path.GetDirectoryName(_dbFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeDatabase();
    }

    public string DatabasePath => _dbFilePath;

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    private void InitializeDatabase()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();

        // Enable WAL mode and normal synchronous for resilience & speed
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS LibraryFolders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderPath TEXT NOT NULL UNIQUE,
                AddedAt TEXT NOT NULL,
                LastScanAt TEXT,
                BookCount INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Books (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Grade TEXT NOT NULL,
                Subject TEXT NOT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                FileHash TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                PageCount INTEGER NOT NULL DEFAULT 0,
                IsAvailable INTEGER NOT NULL DEFAULT 1,
                LastScannedAt TEXT NOT NULL,
                FolderId INTEGER,
                FOREIGN KEY (FolderId) REFERENCES LibraryFolders(Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Books_Grade_Subject ON Books(Grade, Subject);
            CREATE INDEX IF NOT EXISTS IX_Books_FileHash ON Books(FileHash);

            CREATE TABLE IF NOT EXISTS Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerCode TEXT NOT NULL UNIQUE,
                FullName TEXT NOT NULL,
                PhoneNumber TEXT NOT NULL,
                Notes TEXT,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Customers_Phone ON Customers(PhoneNumber);
            CREATE INDEX IF NOT EXISTS IX_Customers_Code ON Customers(CustomerCode);

            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderNumber TEXT NOT NULL UNIQUE,
                CustomerId INTEGER NOT NULL,
                CustomerName TEXT NOT NULL,
                CustomerPhone TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                DeliveryDate TEXT,
                Notes TEXT,
                Status INTEGER NOT NULL DEFAULT 0,
                TotalItemsCount INTEGER NOT NULL DEFAULT 0,
                TotalEstimatedSheets INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_Orders_OrderNumber ON Orders(OrderNumber);
            CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status);

            CREATE TABLE IF NOT EXISTS OrderItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                BookId INTEGER NOT NULL,
                BookTitle TEXT NOT NULL,
                Grade TEXT NOT NULL,
                Subject TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                PageCount INTEGER NOT NULL,
                Copies INTEGER NOT NULL DEFAULT 1,
                PaperSize INTEGER NOT NULL DEFAULT 0,
                Duplex INTEGER NOT NULL DEFAULT 1,
                ColorMode INTEGER NOT NULL DEFAULT 0,
                PaperTray TEXT,
                EstimatedSheets INTEGER NOT NULL DEFAULT 0,
                Status INTEGER NOT NULL DEFAULT 0,
                VerifiedCopies INTEGER NOT NULL DEFAULT 0,
                RemainingCopies INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
                FOREIGN KEY (BookId) REFERENCES Books(Id)
            );

            CREATE TABLE IF NOT EXISTS Printers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WindowsPrinterName TEXT NOT NULL UNIQUE,
                Manufacturer TEXT NOT NULL,
                Model TEXT NOT NULL,
                DriverName TEXT NOT NULL DEFAULT '',
                PortName TEXT NOT NULL DEFAULT '',
                AdapterFamily TEXT NOT NULL DEFAULT 'GenericWindowsSpooler',
                ConnectionAddress TEXT,
                IsOnline INTEGER NOT NULL DEFAULT 1,
                HasError INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CurrentStatusText TEXT NOT NULL,
                SupportedPaperSizes TEXT NOT NULL,
                SupportsDuplex INTEGER NOT NULL DEFAULT 1,
                SupportsColor INTEGER NOT NULL DEFAULT 0,
                AvailableTrays TEXT NOT NULL,
                CapabilitiesJson TEXT,
                LastSeenAt TEXT NOT NULL,
                LastTestedAt TEXT,
                TestResultStatus TEXT,
                LastErrorMessage TEXT,
                IsPaused INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS PrinterProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PrinterId INTEGER NOT NULL,
                ProfileName TEXT NOT NULL,
                TargetBookType TEXT NOT NULL DEFAULT 'عام',
                PaperSize INTEGER NOT NULL DEFAULT 0,
                Duplex INTEGER NOT NULL DEFAULT 1,
                ColorMode INTEGER NOT NULL DEFAULT 0,
                PaperTray TEXT,
                PrintQuality TEXT,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (PrinterId) REFERENCES Printers(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS PrintJobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobNumber TEXT NOT NULL UNIQUE,
                OrderId INTEGER NOT NULL,
                OrderItemId INTEGER NOT NULL,
                PrinterName TEXT NOT NULL,
                SpoolerJobId INTEGER,
                Status INTEGER NOT NULL DEFAULT 0,
                VerificationStatus INTEGER NOT NULL DEFAULT 4,
                CopiesRequested INTEGER NOT NULL DEFAULT 1,
                CopiesVerified INTEGER NOT NULL DEFAULT 0,
                CopiesDamaged INTEGER NOT NULL DEFAULT 0,
                CopiesRemaining INTEGER NOT NULL DEFAULT 1,
                PageCount INTEGER NOT NULL DEFAULT 0,
                StartPage INTEGER NOT NULL DEFAULT 1,
                EndPage INTEGER NOT NULL DEFAULT 1,
                ParentJobId INTEGER,
                IsContinuation INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                SubmittedAt TEXT,
                CompletedAt TEXT,
                LastErrorMessage TEXT,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id),
                FOREIGN KEY (OrderItemId) REFERENCES OrderItems(Id),
                FOREIGN KEY (ParentJobId) REFERENCES PrintJobs(Id)
            );

            CREATE INDEX IF NOT EXISTS IX_PrintJobs_Status ON PrintJobs(Status);
            CREATE INDEX IF NOT EXISTS IX_PrintJobs_Order ON PrintJobs(OrderId);

            CREATE TABLE IF NOT EXISTS PrintJobEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PrintJobId INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                OldStatus INTEGER NOT NULL,
                NewStatus INTEGER NOT NULL,
                VerificationStatus INTEGER NOT NULL,
                EventSource TEXT NOT NULL,
                Message TEXT NOT NULL,
                Details TEXT,
                FOREIGN KEY (PrintJobId) REFERENCES PrintJobs(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ErrorLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                OrderId INTEGER,
                PrintJobId INTEGER,
                PrinterName TEXT,
                BookTitle TEXT,
                ErrorType INTEGER NOT NULL,
                ErrorSource INTEGER NOT NULL,
                RawErrorCode TEXT,
                ErrorMessage TEXT NOT NULL,
                SystemActionTaken TEXT NOT NULL,
                UserActionTaken TEXT,
                IsResolved INTEGER NOT NULL DEFAULT 0,
                ResolvedAt TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_ErrorLogs_Timestamp ON ErrorLogs(Timestamp);

            CREATE TABLE IF NOT EXISTS Backups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BackupPath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                Status TEXT NOT NULL
            );
        ";
        cmd.ExecuteNonQuery();

        // Safe column migrations for existing databases
        string[] migrations = new[]
        {
            "ALTER TABLE Printers ADD COLUMN DriverName TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Printers ADD COLUMN PortName TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Printers ADD COLUMN AdapterFamily TEXT NOT NULL DEFAULT 'GenericWindowsSpooler';",
            "ALTER TABLE Printers ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE Printers ADD COLUMN CapabilitiesJson TEXT;",
            "ALTER TABLE Printers ADD COLUMN LastTestedAt TEXT;",
            "ALTER TABLE Printers ADD COLUMN TestResultStatus TEXT;",
            "ALTER TABLE PrinterProfiles ADD COLUMN TargetBookType TEXT NOT NULL DEFAULT 'عام';"
        };

        foreach (var mig in migrations)
        {
            try
            {
                using var migCmd = conn.CreateCommand();
                migCmd.CommandText = mig;
                migCmd.ExecuteNonQuery();
            }
            catch
            {
                // Column already exists, safe to ignore
            }
        }
    }

    public string CreateBackup(string? destinationFolder = null)
    {
        var targetDir = destinationFolder;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            var dbDir = Path.GetDirectoryName(_dbFilePath)!;
            targetDir = Path.Combine(dbDir, "Backups");
        }
        Directory.CreateDirectory(targetDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(targetDir, $"Backup_{timestamp}.db");

        using (var source = CreateConnection())
        using (var destination = new SqliteConnection($"Data Source={backupPath}"))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        var fileInfo = new FileInfo(backupPath);
        using (var conn = CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO Backups (BackupPath, CreatedAt, SizeBytes, Status)
                VALUES (@path, @time, @size, @status)";
            cmd.Parameters.AddWithValue("@path", backupPath);
            cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@size", fileInfo.Length);
            cmd.Parameters.AddWithValue("@status", "Success");
            cmd.ExecuteNonQuery();
        }

        return backupPath;
    }

    public void RestoreBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود", backupPath);
        }

        SqliteConnection.ClearAllPools();

        using (var source = new SqliteConnection($"Data Source={backupPath}"))
        using (var destination = CreateConnection())
        {
            source.Open();
            source.BackupDatabase(destination);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
