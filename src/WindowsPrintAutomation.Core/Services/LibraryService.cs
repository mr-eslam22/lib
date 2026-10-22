using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;

namespace WindowsPrintAutomation.Core.Services;

public class ScanResult
{
    public int TotalFilesFound { get; set; }
    public int ImportedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int CorruptedCount { get; set; }
    public int UnsupportedCount { get; set; }
    public List<string> CorruptedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public interface ILibraryService
{
    Task<ScanResult> ImportFolderAsync(string rootFolderPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<List<Book>> GetBooksAsync(string? grade = null, string? subject = null, string? searchQuery = null);
    Task<List<string>> GetGradesAsync();
    Task<List<string>> GetSubjectsAsync(string? grade = null);
    Task<Book?> GetBookByIdAsync(long id);
    Task<bool> UpdateBookAsync(Book book);
    Task<List<LibraryFolder>> GetFoldersAsync();
    Task<bool> RemoveFolderAsync(long folderId);
}

public class LibraryService : ILibraryService
{
    private readonly DatabaseService _databaseService;

    public LibraryService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<ScanResult> ImportFolderAsync(string rootFolderPath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new ScanResult();

        if (!Directory.Exists(rootFolderPath))
        {
            result.Errors.Add($"المجلد غير موجود: {rootFolderPath}");
            return result;
        }

        long folderId;
        using (var conn = _databaseService.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO LibraryFolders (FolderPath, AddedAt, LastScanAt, BookCount, IsActive)
                VALUES (@path, @addedAt, @scanAt, 0, 1)
                ON CONFLICT(FolderPath) DO UPDATE SET
                    LastScanAt = @scanAt,
                    IsActive = 1
                RETURNING Id;";
            cmd.Parameters.AddWithValue("@path", Path.GetFullPath(rootFolderPath));
            cmd.Parameters.AddWithValue("@addedAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@scanAt", DateTime.UtcNow.ToString("o"));
            var returned = await cmd.ExecuteScalarAsync(cancellationToken);
            folderId = Convert.ToInt64(returned);
        }

        progress?.Report("جاري حصر وفهرسة الملفات داخل المجلد...");
        var allFiles = Directory.EnumerateFiles(rootFolderPath, "*.*", SearchOption.AllDirectories).ToList();
        result.TotalFilesFound = allFiles.Count;

        var pdfFiles = allFiles.Where(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".db" or ".db-wal" or ".db-shm" or ".tmp" or ".lock" or ".ds_store")
            {
                return false;
            }
            if (ext != ".pdf")
            {
                result.UnsupportedCount++;
                return false;
            }
            return true;
        }).ToList();

        progress?.Report($"تم العثور على {pdfFiles.Count} ملف PDF، جاري الفحص السريع...");

        using (var conn = _databaseService.CreateConnection())
        {
            using var transaction = conn.BeginTransaction();
            int processedIndex = 0;

            foreach (var filePath in pdfFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedIndex++;

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Exists || fileInfo.Length == 0)
                    {
                        result.CorruptedCount++;
                        result.CorruptedFiles.Add(filePath);
                        continue;
                    }

                    if (processedIndex % 5 == 0 || processedIndex == pdfFiles.Count)
                    {
                        progress?.Report($"جاري فحص ({processedIndex} من {pdfFiles.Count}): {Path.GetFileName(filePath)}");
                    }

                    // Parse Grade, Subject, Title from hierarchy (supports 4 levels, 3 levels, etc.)
                    var (grade, subject, title) = ExtractMetadataFromPath(rootFolderPath, filePath);

                    // High-speed composite hash (microseconds instead of seconds)
                    string fileHash = ComputeQuickHash(filePath, fileInfo.Length);

                    // High-speed page count
                    int pageCount = GetPdfPageCount(filePath);

                    // Check if already in DB
                    long? existingId = null;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = "SELECT Id FROM Books WHERE FilePath = @path OR FileHash = @hash LIMIT 1";
                        cmd.Parameters.AddWithValue("@path", filePath);
                        cmd.Parameters.AddWithValue("@hash", fileHash);
                        var existing = await cmd.ExecuteScalarAsync(cancellationToken);
                        if (existing != null)
                        {
                            existingId = Convert.ToInt64(existing);
                        }
                    }

                    if (existingId == null)
                    {
                        using var insertCmd = conn.CreateCommand();
                        insertCmd.Transaction = transaction;
                        insertCmd.CommandText = @"
                            INSERT INTO Books (Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, IsAvailable, LastScannedAt, FolderId)
                            VALUES (@title, @grade, @subject, @path, @fileName, @hash, @size, @pages, 1, @scanned, @folderId)";
                        insertCmd.Parameters.AddWithValue("@title", title);
                        insertCmd.Parameters.AddWithValue("@grade", grade);
                        insertCmd.Parameters.AddWithValue("@subject", subject);
                        insertCmd.Parameters.AddWithValue("@path", filePath);
                        insertCmd.Parameters.AddWithValue("@fileName", Path.GetFileName(filePath));
                        insertCmd.Parameters.AddWithValue("@hash", fileHash);
                        insertCmd.Parameters.AddWithValue("@size", fileInfo.Length);
                        insertCmd.Parameters.AddWithValue("@pages", pageCount);
                        insertCmd.Parameters.AddWithValue("@scanned", DateTime.UtcNow.ToString("o"));
                        insertCmd.Parameters.AddWithValue("@folderId", folderId);
                        await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                        result.ImportedCount++;
                    }
                    else
                    {
                        using var updateCmd = conn.CreateCommand();
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText = @"
                            UPDATE Books SET 
                                Title = @title,
                                Grade = @grade,
                                Subject = @subject,
                                FilePath = @path,
                                FileName = @fileName,
                                FileSizeBytes = @size,
                                PageCount = @pages,
                                IsAvailable = 1,
                                LastScannedAt = @scanned,
                                FolderId = @folderId
                            WHERE Id = @id";
                        updateCmd.Parameters.AddWithValue("@id", existingId.Value);
                        updateCmd.Parameters.AddWithValue("@title", title);
                        updateCmd.Parameters.AddWithValue("@grade", grade);
                        updateCmd.Parameters.AddWithValue("@subject", subject);
                        updateCmd.Parameters.AddWithValue("@path", filePath);
                        updateCmd.Parameters.AddWithValue("@fileName", Path.GetFileName(filePath));
                        updateCmd.Parameters.AddWithValue("@size", fileInfo.Length);
                        updateCmd.Parameters.AddWithValue("@pages", pageCount);
                        updateCmd.Parameters.AddWithValue("@scanned", DateTime.UtcNow.ToString("o"));
                        updateCmd.Parameters.AddWithValue("@folderId", folderId);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                        result.UpdatedCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"خطأ في معالجة الملف '{filePath}': {ex.Message}");
                }
            }

            transaction.Commit();
        }

        // Update Folder book count
        using (var conn = _databaseService.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE LibraryFolders 
                SET BookCount = (SELECT COUNT(*) FROM Books WHERE FolderId = @folderId AND IsAvailable = 1)
                WHERE Id = @folderId";
            cmd.Parameters.AddWithValue("@folderId", folderId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return result;
    }

    public static (string Grade, string Subject, string Title) ExtractMetadataFromPath(string rootPath, string filePath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        var title = Path.GetFileNameWithoutExtension(filePath);
        var grade = "غير محدد";
        var subject = "عام";

        if (parts.Length >= 4)
        {
            // Structure: Stage (e.g. ابتدائي/اعدادي/ثانوي) / Grade (الصف الأول) / Subject (عربي) / Book.pdf
            var stage = parts[0];
            var gradePart = parts[1];
            subject = parts[2];

            if (gradePart.Contains(stage, StringComparison.OrdinalIgnoreCase))
            {
                grade = gradePart;
            }
            else
            {
                grade = $"{stage} - {gradePart}";
            }
        }
        else if (parts.Length == 3)
        {
            // Structure: Grade / Subject / Title.pdf
            grade = parts[0];
            subject = parts[1];
        }
        else if (parts.Length == 2)
        {
            // Structure: Subject / Title.pdf
            subject = parts[0];
        }

        return (grade, subject, title);
    }

    private static string ComputeQuickHash(string filePath, long fileSize)
    {
        using var sha256 = SHA256.Create();
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        if (fileSize <= 1024 * 1024)
        {
            return Convert.ToHexString(sha256.ComputeHash(fs));
        }

        using var ms = new MemoryStream();
        var sizeBytes = BitConverter.GetBytes(fileSize);
        ms.Write(sizeBytes, 0, sizeBytes.Length);

        byte[] buffer = new byte[64 * 1024];

        // Read first 64 KB
        int read = fs.Read(buffer, 0, buffer.Length);
        if (read > 0) ms.Write(buffer, 0, read);

        // Read middle 64 KB
        if (fileSize > 256 * 1024)
        {
            fs.Seek(fileSize / 2, SeekOrigin.Begin);
            read = fs.Read(buffer, 0, buffer.Length);
            if (read > 0) ms.Write(buffer, 0, read);
        }

        // Read last 64 KB
        if (fileSize > 128 * 1024)
        {
            fs.Seek(Math.Max(0, fileSize - buffer.Length), SeekOrigin.Begin);
            read = fs.Read(buffer, 0, buffer.Length);
            if (read > 0) ms.Write(buffer, 0, read);
        }

        ms.Position = 0;
        return Convert.ToHexString(sha256.ComputeHash(ms));
    }

    private static int GetPdfPageCount(string filePath)
    {
        try
        {
            int fastCount = TryQuickGetPageCount(filePath);
            if (fastCount > 0)
            {
                return fastCount;
            }
        }
        catch
        {
            // Ignore quick scan error and fall back to PdfPig
        }

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var pdfDoc = PdfDocument.Open(fs);
            return pdfDoc.NumberOfPages;
        }
        catch
        {
            return 0;
        }
    }

    private static int TryQuickGetPageCount(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return 0;

        long scanSize = Math.Min(fs.Length, 64 * 1024);
        fs.Seek(fs.Length - scanSize, SeekOrigin.Begin);
        byte[] buffer = new byte[scanSize];
        int read = fs.Read(buffer, 0, buffer.Length);
        string content = System.Text.Encoding.Latin1.GetString(buffer, 0, read);

        var matches = System.Text.RegularExpressions.Regex.Matches(content, @"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)");
        if (matches.Count > 0)
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                if (int.TryParse(matches[i].Groups[1].Value, out int count) && count > 0)
                {
                    return count;
                }
            }
        }

        return 0;
    }

    public async Task<List<Book>> GetBooksAsync(string? grade = null, string? subject = null, string? searchQuery = null)
    {
        var list = new List<Book>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();

        var query = "SELECT Id, Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, IsAvailable, LastScannedAt, FolderId FROM Books WHERE IsAvailable = 1";

        if (!string.IsNullOrWhiteSpace(grade) && grade != "الكل")
        {
            query += " AND Grade = @grade";
            cmd.Parameters.AddWithValue("@grade", grade);
        }

        if (!string.IsNullOrWhiteSpace(subject) && subject != "الكل")
        {
            query += " AND Subject = @subject";
            cmd.Parameters.AddWithValue("@subject", subject);
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            query += " AND (Title LIKE @search OR FileName LIKE @search OR Subject LIKE @search OR Grade LIKE @search)";
            cmd.Parameters.AddWithValue("@search", $"%{searchQuery}%");
        }

        query += " ORDER BY Grade ASC, Subject ASC, Title ASC";
        cmd.CommandText = query;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Book
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Grade = reader.GetString(2),
                Subject = reader.GetString(3),
                FilePath = reader.GetString(4),
                FileName = reader.GetString(5),
                FileHash = reader.GetString(6),
                FileSizeBytes = reader.GetInt64(7),
                PageCount = reader.GetInt32(8),
                IsAvailable = reader.GetInt32(9) == 1,
                LastScannedAt = DateTime.Parse(reader.GetString(10)),
                FolderId = reader.IsDBNull(11) ? null : reader.GetInt64(11)
            });
        }

        return list;
    }

    public async Task<List<string>> GetGradesAsync()
    {
        var list = new List<string>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT Grade FROM Books WHERE IsAvailable = 1 ORDER BY Grade ASC";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task<List<string>> GetSubjectsAsync(string? grade = null)
    {
        var list = new List<string>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(grade) || grade == "الكل")
        {
            cmd.CommandText = "SELECT DISTINCT Subject FROM Books WHERE IsAvailable = 1 ORDER BY Subject ASC";
        }
        else
        {
            cmd.CommandText = "SELECT DISTINCT Subject FROM Books WHERE IsAvailable = 1 AND Grade = @grade ORDER BY Subject ASC";
            cmd.Parameters.AddWithValue("@grade", grade);
        }

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task<Book?> GetBookByIdAsync(long id)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Grade, Subject, FilePath, FileName, FileHash, FileSizeBytes, PageCount, IsAvailable, LastScannedAt, FolderId FROM Books WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Book
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Grade = reader.GetString(2),
                Subject = reader.GetString(3),
                FilePath = reader.GetString(4),
                FileName = reader.GetString(5),
                FileHash = reader.GetString(6),
                FileSizeBytes = reader.GetInt64(7),
                PageCount = reader.GetInt32(8),
                IsAvailable = reader.GetInt32(9) == 1,
                LastScannedAt = DateTime.Parse(reader.GetString(10)),
                FolderId = reader.IsDBNull(11) ? null : reader.GetInt64(11)
            };
        }
        return null;
    }

    public async Task<bool> UpdateBookAsync(Book book)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Books SET
                Title = @title,
                Grade = @grade,
                Subject = @subject
            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", book.Id);
        cmd.Parameters.AddWithValue("@title", book.Title);
        cmd.Parameters.AddWithValue("@grade", book.Grade);
        cmd.Parameters.AddWithValue("@subject", book.Subject);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<LibraryFolder>> GetFoldersAsync()
    {
        var list = new List<LibraryFolder>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FolderPath, AddedAt, LastScanAt, BookCount, IsActive FROM LibraryFolders ORDER BY AddedAt DESC";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new LibraryFolder
            {
                Id = reader.GetInt64(0),
                FolderPath = reader.GetString(1),
                AddedAt = DateTime.Parse(reader.GetString(2)),
                LastScanAt = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3)),
                BookCount = reader.GetInt32(4),
                IsActive = reader.GetInt32(5) == 1
            });
        }
        return list;
    }

    public async Task<bool> RemoveFolderAsync(long folderId)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM LibraryFolders WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", folderId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
