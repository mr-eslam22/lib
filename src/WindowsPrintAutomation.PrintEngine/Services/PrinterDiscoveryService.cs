using System.Management;
using System.Printing;
using WindowsPrintAutomation.Core.Domain.Entities;

namespace WindowsPrintAutomation.PrintEngine.Services;

public class DiscoveredPrinterInfo
{
    public string Name { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string PortName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = "Generic";
    public string Model { get; set; } = string.Empty;
    public string? ConnectionAddress { get; set; }
    public string RecommendedAdapterFamily { get; set; } = "GenericWindowsSpooler";
    public bool IsDefault { get; set; }
    public bool IsNetwork { get; set; }
}

public interface IPrinterDiscoveryService
{
    Task<List<DiscoveredPrinterInfo>> DiscoverPrintersAsync();
}

public class PrinterDiscoveryService : IPrinterDiscoveryService
{
    public Task<List<DiscoveredPrinterInfo>> DiscoverPrintersAsync()
    {
        var list = new List<DiscoveredPrinterInfo>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Query via WMI Win32_Printer
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverName, PortName, Local, Network, Default FROM Win32_Printer");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || seenNames.Contains(name)) continue;

                seenNames.Add(name);
                var driver = item["DriverName"]?.ToString() ?? string.Empty;
                var port = item["PortName"]?.ToString() ?? string.Empty;
                bool isNetwork = item["Network"] != null && Convert.ToBoolean(item["Network"]);
                bool isDefault = item["Default"] != null && Convert.ToBoolean(item["Default"]);

                var info = BuildDiscoveredInfo(name, driver, port, isNetwork, isDefault);
                list.Add(info);
            }
        }
        catch
        {
            // Fallback to System.Printing
        }

        // 2. Fallback / Augment via LocalPrintServer
        try
        {
            using var server = new LocalPrintServer();
            string defaultName = string.Empty;
            try { defaultName = server.DefaultPrintQueue?.FullName ?? string.Empty; } catch { }

            var queues = server.GetPrintQueues();
            foreach (var q in queues)
            {
                if (seenNames.Contains(q.FullName)) continue;

                seenNames.Add(q.FullName);
                var driver = q.QueueDriver?.Name ?? string.Empty;
                var port = q.QueuePort?.Name ?? string.Empty;
                bool isDefault = !string.IsNullOrEmpty(defaultName) && string.Equals(q.FullName, defaultName, StringComparison.OrdinalIgnoreCase);
                var info = BuildDiscoveredInfo(q.FullName, driver, port, false, isDefault);
                list.Add(info);
            }
        }
        catch
        {
            // Ignore if permissions limited
        }

        return Task.FromResult(list);
    }

    private static DiscoveredPrinterInfo BuildDiscoveredInfo(string name, string driver, string port, bool isNetwork, bool isDefault)
    {
        var manufacturer = DetectManufacturer(name, driver);
        var adapterFamily = DetectAdapterFamily(manufacturer, port);
        string? ipAddress = ExtractIpFromPort(port);

        return new DiscoveredPrinterInfo
        {
            Name = name,
            DriverName = driver,
            PortName = port,
            Manufacturer = manufacturer,
            Model = string.IsNullOrWhiteSpace(driver) ? name : driver,
            ConnectionAddress = ipAddress ?? (port.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? "USB" : port),
            RecommendedAdapterFamily = adapterFamily,
            IsDefault = isDefault,
            IsNetwork = isNetwork || !string.IsNullOrEmpty(ipAddress)
        };
    }

    public static string DetectManufacturer(string name, string driver)
    {
        var text = $"{name} {driver}".ToLowerInvariant();

        if (text.Contains("hp") || text.Contains("hewlett") || text.Contains("laserjet")) return "HP";
        if (text.Contains("kyocera") || text.Contains("taskalfa") || text.Contains("ecosys") || text.Contains("kpdl")) return "Kyocera";
        if (text.Contains("canon") || text.Contains("imagerunner") || text.Contains("lbp")) return "Canon";
        if (text.Contains("ricoh") || text.Contains("aficio")) return "Ricoh";
        if (text.Contains("xerox") || text.Contains("workcentre") || text.Contains("versalink")) return "Xerox";
        if (text.Contains("brother")) return "Brother";
        if (text.Contains("epson")) return "Epson";
        if (text.Contains("konica") || text.Contains("minolta") || text.Contains("bizhub")) return "Konica Minolta";
        if (text.Contains("sharp")) return "Sharp";

        return "Generic";
    }

    public static string DetectAdapterFamily(string manufacturer, string port)
    {
        if (manufacturer == "HP") return "HpEnterprise";
        if (manufacturer == "Kyocera") return "KyoceraTaskAlfa";

        if (!string.IsNullOrWhiteSpace(port) && (port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) || port.Contains(".")))
        {
            return "NetworkSnmp";
        }

        return "GenericWindowsSpooler";
    }

    private static string? ExtractIpFromPort(string port)
    {
        if (string.IsNullOrWhiteSpace(port)) return null;

        if (port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
        {
            return port.Substring(3);
        }

        var parts = port.Split('.');
        if (parts.Length == 4 && parts.All(p => int.TryParse(p, out _)))
        {
            return port;
        }

        return null;
    }
}
