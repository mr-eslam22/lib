using System.IO;
using UglyToad.PdfPig.Writer;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Services;
using Xunit;

namespace WindowsPrintAutomation.Tests;

public class LibraryImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _booksDir;
    private readonly DatabaseService _dbService;
    private readonly LibraryService _libraryService;

    public LibraryImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinPrintTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _booksDir = Path.Combine(_tempDir, "Books");
        Directory.CreateDirectory(_booksDir);
        _dbService = new DatabaseService(Path.Combine(_tempDir, "test.db"));
        _libraryService = new LibraryService(_dbService);
    }

    public void Dispose()
    {
        _dbService.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string CreateMinimalPdf(string path, int pageCount = 3)
    {
        var builder = new PdfDocumentBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        }
        var bytes = builder.Build();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ExtractMetadataFromPath_StandardHierarchy_ParsesGradeSubjectTitle()
    {
        var root = @"C:\Books";
        var filePath = @"C:\Books\الصف الأول الثانوي\اللغة العربية\كتاب الطالب.pdf";

        var (grade, subject, title) = LibraryService.ExtractMetadataFromPath(root, filePath);

        Assert.Equal("الصف الأول الثانوي", grade);
        Assert.Equal("اللغة العربية", subject);
        Assert.Equal("كتاب الطالب", title);
    }

    [Fact]
    public async Task ImportFolderAsync_NestedHierarchy_ImportsBooksAndExtractsPages()
    {
        // Setup folder structure
        var book1 = Path.Combine(_booksDir, "الصف الأول الثانوي", "اللغة العربية", "كتاب الطالب.pdf");
        var book2 = Path.Combine(_booksDir, "الصف الأول الثانوي", "اللغة الإنجليزية", "Student Book.pdf");
        var book3 = Path.Combine(_booksDir, "الصف الثاني الثانوي", "الرياضيات", "الجبر والهندسة.pdf");

        CreateMinimalPdf(book1, pageCount: 15);
        CreateMinimalPdf(book2, pageCount: 22);
        CreateMinimalPdf(book3, pageCount: 8);

        // Add an unsupported file
        File.WriteAllText(Path.Combine(_booksDir, "notes.txt"), "ملاحظات خارجية");

        var result = await _libraryService.ImportFolderAsync(_booksDir);

        Assert.Equal(3, result.ImportedCount);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.Equal(0, result.CorruptedCount);

        var allBooks = await _libraryService.GetBooksAsync();
        Assert.Equal(3, allBooks.Count);

        var arabicBook = allBooks.First(b => b.Title == "كتاب الطالب");
        Assert.Equal("الصف الأول الثانوي", arabicBook.Grade);
        Assert.Equal("اللغة العربية", arabicBook.Subject);
        Assert.Equal(15, arabicBook.PageCount);
        Assert.False(string.IsNullOrWhiteSpace(arabicBook.FileHash));
    }

    [Fact]
    public async Task ImportFolderAsync_DuplicateFiles_UpdatesRatherThanDuplicates()
    {
        var bookPath = Path.Combine(_booksDir, "الصف الأول الثانوي", "اللغة العربية", "كتاب النحو.pdf");
        CreateMinimalPdf(bookPath, pageCount: 10);

        var firstScan = await _libraryService.ImportFolderAsync(_booksDir);
        Assert.Equal(1, firstScan.ImportedCount);

        // Scan second time
        var secondScan = await _libraryService.ImportFolderAsync(_booksDir);
        Assert.Equal(0, secondScan.ImportedCount);
        Assert.Equal(1, secondScan.UpdatedCount);

        var allBooks = await _libraryService.GetBooksAsync();
        Assert.Single(allBooks);
    }

    [Fact]
    public async Task ImportFolderAsync_CorruptedZeroBytePdf_HandledSafelyWithoutCrash()
    {
        var corruptedPath = Path.Combine(_booksDir, "الصف الثالث الثانوي", "الكيمياء", "كتاب_تالف.pdf");
        var dir = Path.GetDirectoryName(corruptedPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(corruptedPath, Array.Empty<byte>()); // 0-byte file

        var scan = await _libraryService.ImportFolderAsync(_booksDir);
        Assert.Equal(1, scan.CorruptedCount);
        Assert.Single(scan.CorruptedFiles);
    }
}
