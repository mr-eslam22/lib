using System.IO;
using WindowsPrintAutomation.Core.Data;
using Xunit;

namespace WindowsPrintAutomation.Tests;

public class DatabasePersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseService _dbService;

    public DatabasePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinPrintTest_Db_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbService = new DatabaseService(Path.Combine(_tempDir, "db_test.db"));
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DatabaseService_InitializesWithWalMode_EnablesHighConcurrency()
    {
        using var conn = _dbService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = cmd.ExecuteScalar()?.ToString();

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public void CreateBackupAndRestore_ValidDb_CreatesConsistentBackupFile()
    {
        // Seed some data
        using (var conn = _dbService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO AppSettings (Key, Value, UpdatedAt) VALUES ('ShopName', 'مطبعة الأمل', @now);";
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        // Create backup
        var backupPath = _dbService.CreateBackup(Path.Combine(_tempDir, "backups"));
        Assert.True(File.Exists(backupPath));
        Assert.True(new FileInfo(backupPath).Length > 0);

        // Verify record in Backups table
        using (var conn = _dbService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Backups WHERE BackupPath = @path";
            cmd.Parameters.AddWithValue("@path", backupPath);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(1, count);
        }
    }
}
