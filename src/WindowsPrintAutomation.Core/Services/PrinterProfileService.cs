using Microsoft.Data.Sqlite;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Services;

public interface IPrinterProfileService
{
    Task<List<Printer>> GetPrintersAsync(bool activeOnly = false);
    Task<Printer?> GetPrinterByIdAsync(long id);
    Task<Printer?> GetPrinterByNameAsync(string name);
    Task<Printer> SaveOrUpdatePrinterAsync(Printer printer);
    Task SetPrinterActiveAsync(long id, bool isActive);
    Task SaveTestResultAsync(string printerName, bool isSuccess, string statusMessage, string? capabilitiesJson = null);
    Task<List<PrinterProfile>> GetProfilesForPrinterAsync(long printerId);
    Task<PrinterProfile> SaveProfileAsync(PrinterProfile profile);
    Task<bool> DeleteProfileAsync(long profileId);
    Task<Printer?> MatchBestPrinterAsync(PaperSizeOption paperSize, PrintDuplexOption duplex, PrintColorOption color, string? bookType = null);
    Task UpdatePrinterStatusAsync(string printerName, bool isOnline, bool hasError, string statusText, string? error = null);
    Task DeletePrinterAsync(long printerId);
}

public class PrinterProfileService : IPrinterProfileService
{
    private readonly DatabaseService _databaseService;

    public PrinterProfileService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<List<Printer>> GetPrintersAsync(bool activeOnly = false)
    {
        var list = new List<Printer>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();

        var query = @"
            SELECT Id, WindowsPrinterName, Manufacturer, Model, DriverName, PortName, AdapterFamily, ConnectionAddress, 
                   IsOnline, HasError, IsActive, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, 
                   AvailableTrays, CapabilitiesJson, LastSeenAt, LastTestedAt, TestResultStatus, LastErrorMessage, IsPaused 
            FROM Printers";

        if (activeOnly)
        {
            query += " WHERE IsActive = 1";
        }

        query += " ORDER BY WindowsPrinterName ASC";
        cmd.CommandText = query;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadPrinter(reader));
        }
        return list;
    }

    public async Task<Printer?> GetPrinterByIdAsync(long id)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WindowsPrinterName, Manufacturer, Model, DriverName, PortName, AdapterFamily, ConnectionAddress, 
                   IsOnline, HasError, IsActive, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, 
                   AvailableTrays, CapabilitiesJson, LastSeenAt, LastTestedAt, TestResultStatus, LastErrorMessage, IsPaused 
            FROM Printers WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadPrinter(reader);
        }
        return null;
    }

    public async Task<Printer?> GetPrinterByNameAsync(string name)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, WindowsPrinterName, Manufacturer, Model, DriverName, PortName, AdapterFamily, ConnectionAddress, 
                   IsOnline, HasError, IsActive, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, 
                   AvailableTrays, CapabilitiesJson, LastSeenAt, LastTestedAt, TestResultStatus, LastErrorMessage, IsPaused 
            FROM Printers WHERE WindowsPrinterName = @name";
        cmd.Parameters.AddWithValue("@name", name);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadPrinter(reader);
        }
        return null;
    }

    private static Printer ReadPrinter(SqliteDataReader reader)
    {
        return new Printer
        {
            Id = reader.GetInt64(0),
            WindowsPrinterName = reader.GetString(1),
            Manufacturer = reader.GetString(2),
            Model = reader.GetString(3),
            DriverName = reader.GetString(4),
            PortName = reader.GetString(5),
            AdapterFamily = reader.GetString(6),
            ConnectionAddress = reader.IsDBNull(7) ? null : reader.GetString(7),
            IsOnline = reader.GetInt32(8) == 1,
            HasError = reader.GetInt32(9) == 1,
            IsActive = reader.GetInt32(10) == 1,
            CurrentStatusText = reader.GetString(11),
            SupportedPaperSizes = reader.GetString(12),
            SupportsDuplex = reader.GetInt32(13) == 1,
            SupportsColor = reader.GetInt32(14) == 1,
            AvailableTrays = reader.GetString(15),
            CapabilitiesJson = reader.IsDBNull(16) ? null : reader.GetString(16),
            LastSeenAt = DateTime.Parse(reader.GetString(17)),
            LastTestedAt = reader.IsDBNull(18) ? null : DateTime.Parse(reader.GetString(18)),
            TestResultStatus = reader.IsDBNull(19) ? null : reader.GetString(19),
            LastErrorMessage = reader.IsDBNull(20) ? null : reader.GetString(20),
            IsPaused = reader.GetInt32(21) == 1
        };
    }

    public async Task<Printer> SaveOrUpdatePrinterAsync(Printer printer)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Printers (WindowsPrinterName, Manufacturer, Model, DriverName, PortName, AdapterFamily, ConnectionAddress, IsOnline, HasError, IsActive, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, AvailableTrays, CapabilitiesJson, LastSeenAt, LastTestedAt, TestResultStatus, LastErrorMessage, IsPaused)
            VALUES (@name, @man, @model, @driver, @port, @adapter, @conn, @online, @err, @active, @status, @sizes, @duplex, @color, @trays, @caps, @seen, @tested, @testSt, @errMsg, @paused)
            ON CONFLICT(WindowsPrinterName) DO UPDATE SET
                Manufacturer = @man,
                Model = @model,
                DriverName = @driver,
                PortName = @port,
                AdapterFamily = @adapter,
                ConnectionAddress = @conn,
                IsOnline = @online,
                HasError = @err,
                IsActive = @active,
                CurrentStatusText = @status,
                SupportedPaperSizes = @sizes,
                SupportsDuplex = @duplex,
                SupportsColor = @color,
                AvailableTrays = @trays,
                CapabilitiesJson = COALESCE(@caps, CapabilitiesJson),
                LastSeenAt = @seen,
                LastTestedAt = COALESCE(@tested, LastTestedAt),
                TestResultStatus = COALESCE(@testSt, TestResultStatus),
                LastErrorMessage = @errMsg,
                IsPaused = @paused
            RETURNING Id;";
        cmd.Parameters.AddWithValue("@name", printer.WindowsPrinterName);
        cmd.Parameters.AddWithValue("@man", printer.Manufacturer);
        cmd.Parameters.AddWithValue("@model", printer.Model);
        cmd.Parameters.AddWithValue("@driver", printer.DriverName ?? string.Empty);
        cmd.Parameters.AddWithValue("@port", printer.PortName ?? string.Empty);
        cmd.Parameters.AddWithValue("@adapter", printer.AdapterFamily ?? "GenericWindowsSpooler");
        cmd.Parameters.AddWithValue("@conn", (object?)printer.ConnectionAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@online", printer.IsOnline ? 1 : 0);
        cmd.Parameters.AddWithValue("@err", printer.HasError ? 1 : 0);
        cmd.Parameters.AddWithValue("@active", printer.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", printer.CurrentStatusText);
        cmd.Parameters.AddWithValue("@sizes", printer.SupportedPaperSizes);
        cmd.Parameters.AddWithValue("@duplex", printer.SupportsDuplex ? 1 : 0);
        cmd.Parameters.AddWithValue("@color", printer.SupportsColor ? 1 : 0);
        cmd.Parameters.AddWithValue("@trays", printer.AvailableTrays);
        cmd.Parameters.AddWithValue("@caps", (object?)printer.CapabilitiesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seen", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@tested", (object?)printer.LastTestedAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@testSt", (object?)printer.TestResultStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@errMsg", (object?)printer.LastErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paused", printer.IsPaused ? 1 : 0);

        var id = await cmd.ExecuteScalarAsync();
        printer.Id = Convert.ToInt64(id);
        return printer;
    }

    public async Task SetPrinterActiveAsync(long id, bool isActive)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Printers SET IsActive = @active WHERE Id = @id";
        cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveTestResultAsync(string printerName, bool isSuccess, string statusMessage, string? capabilitiesJson = null)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Printers SET
                LastTestedAt = @tested,
                TestResultStatus = @st,
                CapabilitiesJson = COALESCE(@caps, CapabilitiesJson)
            WHERE WindowsPrinterName = @name";
        cmd.Parameters.AddWithValue("@tested", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@st", isSuccess ? $"ناجح ({statusMessage})" : $"فشل ({statusMessage})");
        cmd.Parameters.AddWithValue("@caps", (object?)capabilitiesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", printerName);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdatePrinterStatusAsync(string printerName, bool isOnline, bool hasError, string statusText, string? error = null)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Printers SET
                IsOnline = @online,
                HasError = @err,
                CurrentStatusText = @status,
                LastErrorMessage = @errMsg,
                LastSeenAt = @seen
            WHERE WindowsPrinterName = @name";
        cmd.Parameters.AddWithValue("@name", printerName);
        cmd.Parameters.AddWithValue("@online", isOnline ? 1 : 0);
        cmd.Parameters.AddWithValue("@err", hasError ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", statusText);
        cmd.Parameters.AddWithValue("@errMsg", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seen", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PrinterProfile>> GetProfilesForPrinterAsync(long printerId)
    {
        var list = new List<PrinterProfile>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, PrinterId, ProfileName, TargetBookType, PaperSize, Duplex, ColorMode, PaperTray, PrintQuality, IsDefault FROM PrinterProfiles WHERE PrinterId = @pId";
        cmd.Parameters.AddWithValue("@pId", printerId);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PrinterProfile
            {
                Id = reader.GetInt64(0),
                PrinterId = reader.GetInt64(1),
                ProfileName = reader.GetString(2),
                TargetBookType = reader.GetString(3),
                PaperSize = (PaperSizeOption)reader.GetInt32(4),
                Duplex = (PrintDuplexOption)reader.GetInt32(5),
                ColorMode = (PrintColorOption)reader.GetInt32(6),
                PaperTray = reader.IsDBNull(7) ? null : reader.GetString(7),
                PrintQuality = reader.IsDBNull(8) ? null : reader.GetString(8),
                IsDefault = reader.GetInt32(9) == 1
            });
        }
        return list;
    }

    public async Task<PrinterProfile> SaveProfileAsync(PrinterProfile profile)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        if (profile.Id == 0)
        {
            cmd.CommandText = @"
                INSERT INTO PrinterProfiles (PrinterId, ProfileName, TargetBookType, PaperSize, Duplex, ColorMode, PaperTray, PrintQuality, IsDefault)
                VALUES (@pId, @name, @target, @size, @duplex, @color, @tray, @quality, @def)
                RETURNING Id;";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE PrinterProfiles SET
                    ProfileName = @name,
                    TargetBookType = @target,
                    PaperSize = @size,
                    Duplex = @duplex,
                    ColorMode = @color,
                    PaperTray = @tray,
                    PrintQuality = @quality,
                    IsDefault = @def
                WHERE Id = @id
                RETURNING Id;";
            cmd.Parameters.AddWithValue("@id", profile.Id);
        }

        cmd.Parameters.AddWithValue("@pId", profile.PrinterId);
        cmd.Parameters.AddWithValue("@name", profile.ProfileName);
        cmd.Parameters.AddWithValue("@target", profile.TargetBookType ?? "عام");
        cmd.Parameters.AddWithValue("@size", (int)profile.PaperSize);
        cmd.Parameters.AddWithValue("@duplex", (int)profile.Duplex);
        cmd.Parameters.AddWithValue("@color", (int)profile.ColorMode);
        cmd.Parameters.AddWithValue("@tray", (object?)profile.PaperTray ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@quality", (object?)profile.PrintQuality ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@def", profile.IsDefault ? 1 : 0);

        var id = await cmd.ExecuteScalarAsync();
        profile.Id = Convert.ToInt64(id);
        return profile;
    }

    public async Task<bool> DeleteProfileAsync(long profileId)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PrinterProfiles WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", profileId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task DeletePrinterAsync(long printerId)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Printers WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", printerId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Printer?> MatchBestPrinterAsync(PaperSizeOption paperSize, PrintDuplexOption duplex, PrintColorOption color, string? bookType = null)
    {
        var allPrinters = await GetPrintersAsync(activeOnly: true);
        var available = allPrinters.Where(p => p.IsOnline && !p.HasError && !p.IsPaused).ToList();

        if (available.Count == 0)
        {
            return null;
        }

        var sizeStr = paperSize.ToString();

        // 1. Look for matching profile if bookType specified
        if (!string.IsNullOrWhiteSpace(bookType))
        {
            foreach (var p in available)
            {
                var profiles = await GetProfilesForPrinterAsync(p.Id);
                if (profiles.Any(pr => pr.TargetBookType.Equals(bookType, StringComparison.OrdinalIgnoreCase) &&
                                       pr.PaperSize == paperSize &&
                                       (duplex == PrintDuplexOption.Simplex || pr.Duplex == duplex)))
                {
                    return p;
                }
            }
        }

        // 2. Filter compatible printers by verified capability
        var candidates = available.Where(p =>
        {
            if (!p.SupportedPaperSizes.Contains(sizeStr, StringComparison.OrdinalIgnoreCase)) return false;
            if (duplex != PrintDuplexOption.Simplex && !p.SupportsDuplex) return false;
            if (color == PrintColorOption.Color && !p.SupportsColor) return false;
            return true;
        }).ToList();

        if (candidates.Count > 0)
        {
            return candidates.First();
        }

        // 3. Fallback
        return available.FirstOrDefault();
    }
}
