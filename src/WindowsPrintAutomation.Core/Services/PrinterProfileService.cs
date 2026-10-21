using Microsoft.Data.Sqlite;
using WindowsPrintAutomation.Core.Data;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;

namespace WindowsPrintAutomation.Core.Services;

public interface IPrinterProfileService
{
    Task<List<Printer>> GetPrintersAsync();
    Task<Printer?> GetPrinterByIdAsync(long id);
    Task<Printer?> GetPrinterByNameAsync(string name);
    Task<Printer> SaveOrUpdatePrinterAsync(Printer printer);
    Task<List<PrinterProfile>> GetProfilesForPrinterAsync(long printerId);
    Task<PrinterProfile> SaveProfileAsync(PrinterProfile profile);
    Task<bool> DeleteProfileAsync(long profileId);
    Task<Printer?> MatchBestPrinterAsync(PaperSizeOption paperSize, PrintDuplexOption duplex, PrintColorOption color);
    Task UpdatePrinterStatusAsync(string printerName, bool isOnline, bool hasError, string statusText, string? error = null);
}

public class PrinterProfileService : IPrinterProfileService
{
    private readonly DatabaseService _databaseService;

    public PrinterProfileService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<List<Printer>> GetPrintersAsync()
    {
        var list = new List<Printer>();
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, WindowsPrinterName, Manufacturer, Model, ConnectionAddress, IsOnline, HasError, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, AvailableTrays, LastSeenAt, LastErrorMessage, IsPaused FROM Printers ORDER BY WindowsPrinterName ASC";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Printer
            {
                Id = reader.GetInt64(0),
                WindowsPrinterName = reader.GetString(1),
                Manufacturer = reader.GetString(2),
                Model = reader.GetString(3),
                ConnectionAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsOnline = reader.GetInt32(5) == 1,
                HasError = reader.GetInt32(6) == 1,
                CurrentStatusText = reader.GetString(7),
                SupportedPaperSizes = reader.GetString(8),
                SupportsDuplex = reader.GetInt32(9) == 1,
                SupportsColor = reader.GetInt32(10) == 1,
                AvailableTrays = reader.GetString(11),
                LastSeenAt = DateTime.Parse(reader.GetString(12)),
                LastErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13),
                IsPaused = reader.GetInt32(14) == 1
            });
        }
        return list;
    }

    public async Task<Printer?> GetPrinterByIdAsync(long id)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, WindowsPrinterName, Manufacturer, Model, ConnectionAddress, IsOnline, HasError, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, AvailableTrays, LastSeenAt, LastErrorMessage, IsPaused FROM Printers WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Printer
            {
                Id = reader.GetInt64(0),
                WindowsPrinterName = reader.GetString(1),
                Manufacturer = reader.GetString(2),
                Model = reader.GetString(3),
                ConnectionAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsOnline = reader.GetInt32(5) == 1,
                HasError = reader.GetInt32(6) == 1,
                CurrentStatusText = reader.GetString(7),
                SupportedPaperSizes = reader.GetString(8),
                SupportsDuplex = reader.GetInt32(9) == 1,
                SupportsColor = reader.GetInt32(10) == 1,
                AvailableTrays = reader.GetString(11),
                LastSeenAt = DateTime.Parse(reader.GetString(12)),
                LastErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13),
                IsPaused = reader.GetInt32(14) == 1
            };
        }
        return null;
    }

    public async Task<Printer?> GetPrinterByNameAsync(string name)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, WindowsPrinterName, Manufacturer, Model, ConnectionAddress, IsOnline, HasError, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, AvailableTrays, LastSeenAt, LastErrorMessage, IsPaused FROM Printers WHERE WindowsPrinterName = @name";
        cmd.Parameters.AddWithValue("@name", name);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Printer
            {
                Id = reader.GetInt64(0),
                WindowsPrinterName = reader.GetString(1),
                Manufacturer = reader.GetString(2),
                Model = reader.GetString(3),
                ConnectionAddress = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsOnline = reader.GetInt32(5) == 1,
                HasError = reader.GetInt32(6) == 1,
                CurrentStatusText = reader.GetString(7),
                SupportedPaperSizes = reader.GetString(8),
                SupportsDuplex = reader.GetInt32(9) == 1,
                SupportsColor = reader.GetInt32(10) == 1,
                AvailableTrays = reader.GetString(11),
                LastSeenAt = DateTime.Parse(reader.GetString(12)),
                LastErrorMessage = reader.IsDBNull(13) ? null : reader.GetString(13),
                IsPaused = reader.GetInt32(14) == 1
            };
        }
        return null;
    }

    public async Task<Printer> SaveOrUpdatePrinterAsync(Printer printer)
    {
        using var conn = _databaseService.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Printers (WindowsPrinterName, Manufacturer, Model, ConnectionAddress, IsOnline, HasError, CurrentStatusText, SupportedPaperSizes, SupportsDuplex, SupportsColor, AvailableTrays, LastSeenAt, LastErrorMessage, IsPaused)
            VALUES (@name, @man, @model, @conn, @online, @err, @status, @sizes, @duplex, @color, @trays, @seen, @errMsg, @paused)
            ON CONFLICT(WindowsPrinterName) DO UPDATE SET
                Manufacturer = @man,
                Model = @model,
                ConnectionAddress = @conn,
                IsOnline = @online,
                HasError = @err,
                CurrentStatusText = @status,
                SupportedPaperSizes = @sizes,
                SupportsDuplex = @duplex,
                SupportsColor = @color,
                AvailableTrays = @trays,
                LastSeenAt = @seen,
                LastErrorMessage = @errMsg,
                IsPaused = @paused
            RETURNING Id;";
        cmd.Parameters.AddWithValue("@name", printer.WindowsPrinterName);
        cmd.Parameters.AddWithValue("@man", printer.Manufacturer);
        cmd.Parameters.AddWithValue("@model", printer.Model);
        cmd.Parameters.AddWithValue("@conn", (object?)printer.ConnectionAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@online", printer.IsOnline ? 1 : 0);
        cmd.Parameters.AddWithValue("@err", printer.HasError ? 1 : 0);
        cmd.Parameters.AddWithValue("@status", printer.CurrentStatusText);
        cmd.Parameters.AddWithValue("@sizes", printer.SupportedPaperSizes);
        cmd.Parameters.AddWithValue("@duplex", printer.SupportsDuplex ? 1 : 0);
        cmd.Parameters.AddWithValue("@color", printer.SupportsColor ? 1 : 0);
        cmd.Parameters.AddWithValue("@trays", printer.AvailableTrays);
        cmd.Parameters.AddWithValue("@seen", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@errMsg", (object?)printer.LastErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paused", printer.IsPaused ? 1 : 0);

        var id = await cmd.ExecuteScalarAsync();
        printer.Id = Convert.ToInt64(id);
        return printer;
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
        cmd.CommandText = "SELECT Id, PrinterId, ProfileName, PaperSize, Duplex, ColorMode, PaperTray, PrintQuality, IsDefault FROM PrinterProfiles WHERE PrinterId = @pId";
        cmd.Parameters.AddWithValue("@pId", printerId);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PrinterProfile
            {
                Id = reader.GetInt64(0),
                PrinterId = reader.GetInt64(1),
                ProfileName = reader.GetString(2),
                PaperSize = (PaperSizeOption)reader.GetInt32(3),
                Duplex = (PrintDuplexOption)reader.GetInt32(4),
                ColorMode = (PrintColorOption)reader.GetInt32(5),
                PaperTray = reader.IsDBNull(6) ? null : reader.GetString(6),
                PrintQuality = reader.IsDBNull(7) ? null : reader.GetString(7),
                IsDefault = reader.GetInt32(8) == 1
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
                INSERT INTO PrinterProfiles (PrinterId, ProfileName, PaperSize, Duplex, ColorMode, PaperTray, PrintQuality, IsDefault)
                VALUES (@pId, @name, @size, @duplex, @color, @tray, @quality, @def)
                RETURNING Id;";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE PrinterProfiles SET
                    ProfileName = @name,
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

    public async Task<Printer?> MatchBestPrinterAsync(PaperSizeOption paperSize, PrintDuplexOption duplex, PrintColorOption color)
    {
        var allPrinters = await GetPrintersAsync();
        var available = allPrinters.Where(p => p.IsOnline && !p.HasError && !p.IsPaused).ToList();

        if (available.Count == 0)
        {
            return null;
        }

        var sizeStr = paperSize.ToString();

        // Filter compatible printers
        var candidates = available.Where(p =>
        {
            // Paper size check
            if (!p.SupportedPaperSizes.Contains(sizeStr, StringComparison.OrdinalIgnoreCase)) return false;
            // Duplex check
            if (duplex != PrintDuplexOption.Simplex && !p.SupportsDuplex) return false;
            // Color check
            if (color == PrintColorOption.Color && !p.SupportsColor) return false;
            return true;
        }).ToList();

        if (candidates.Count == 0)
        {
            // Fallback to any online printer that at least supports duplex/color if requested
            candidates = available.Where(p =>
                (duplex == PrintDuplexOption.Simplex || p.SupportsDuplex) &&
                (color != PrintColorOption.Color || p.SupportsColor)
            ).ToList();
        }

        return candidates.FirstOrDefault() ?? available.FirstOrDefault();
    }
}
