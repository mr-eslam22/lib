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

        var allFiles = Directory.EnumerateFiles(rootFolderPath, "*.*", SearchOption.AllDirectories).ToList();
        result.TotalFilesFound = allFiles.Count;

        foreach (var filePath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".pdf")
            {
                result.UnsupportedCount++;
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    result.CorruptedCount++;
                    result.CorruptedFiles.Add(filePath);
                    continue;
                }

                progress?.Report($"جاري فحص: {Path.GetFileName(filePath)}");

                // Parse Grade, Subject, Title from hierarchy
                var (grade, subject, title) = ExtractMetadataFromPath(rootFolderPath, filePath);

                // Compute hash and page count
                string fileHash;
                int pageCount = 0;

                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using var sha256 = SHA256.Create();
                        var hashBytes = sha256.ComputeHash(fs);
                        fileHash = Convert.ToHexString(hashBytes);

                        fs.Position = 0;
                        using var pdfDoc = PdfDocument.Open(fs);
                        pageCount = pdfDoc.NumberOfPages;
                    }
                }
                catch (Exception ex)
                {
                    result.CorruptedCount++;
                    result.CorruptedFiles.Add($"{filePath}: {ex.Message}");
                    continue;
                }

                // Insert or Update in SQLite
                using (var conn = _databaseService.CreateConnection())
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT Id FROM Books WHERE FilePath = @path OR FileHash = @hash LIMIT 1";
                    cmd.Parameters.AddWithValue("@path", filePath);
                    cmd.Parameters.AddWithValue("@hash", fileHash);
                    var existingId = await cmd.ExecuteScalarAsync(cancellationToken);

                    if (existingId == null)
                    {
                        using var insertCmd = conn.CreateCommand();
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
                        updateCmd.Parameters.AddWithValue("@id", Convert.ToInt64(existingId));
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
            }
            catch (Exception ex)
            {
                result.Errors.Add($"خطأ في معالجة الملف '{filePath}': {ex.Message}");
            }
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

        if (parts.Length >= 3)
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
