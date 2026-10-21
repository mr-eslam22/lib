using System.Collections.Concurrent;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine;

public interface IPrinterAdapterRegistry
{
    void RegisterAdapter(IPrinterAdapter adapter);
    IPrinterAdapter GetAdapter(string adapterIdOrFamily);
    IReadOnlyList<IPrinterAdapter> GetAllAdapters();
    IPrinterAdapter ResolveAdapterForPrinter(Printer printer);
}

public class PrinterAdapterRegistry : IPrinterAdapterRegistry
{
    private readonly ConcurrentDictionary<string, IPrinterAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPrinterAdapter _fallbackAdapter;

    public PrinterAdapterRegistry(IPrinterAdapter defaultFallback)
    {
        _fallbackAdapter = defaultFallback ?? throw new ArgumentNullException(nameof(defaultFallback));
        RegisterAdapter(defaultFallback);
    }

    public void RegisterAdapter(IPrinterAdapter adapter)
    {
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));
        _adapters[adapter.AdapterId] = adapter;
        _adapters[adapter.VendorFamily] = adapter;
    }

    public IPrinterAdapter GetAdapter(string adapterIdOrFamily)
    {
        if (string.IsNullOrWhiteSpace(adapterIdOrFamily))
        {
            return _fallbackAdapter;
        }

        if (_adapters.TryGetValue(adapterIdOrFamily, out var adapter))
        {
            return adapter;
        }

        return _fallbackAdapter;
    }

    public IReadOnlyList<IPrinterAdapter> GetAllAdapters()
    {
        return _adapters.Values.Distinct().ToList();
    }

    public IPrinterAdapter ResolveAdapterForPrinter(Printer printer)
    {
        if (printer == null) return _fallbackAdapter;

        // 1. Try explicit AdapterFamily
        if (!string.IsNullOrWhiteSpace(printer.AdapterFamily) && _adapters.TryGetValue(printer.AdapterFamily, out var adapter))
        {
            return adapter;
        }

        // 2. Auto-match by Manufacturer keyword
        var man = printer.Manufacturer ?? string.Empty;
        var model = printer.Model ?? string.Empty;
        var name = printer.WindowsPrinterName ?? string.Empty;

        if (man.Contains("HP", StringComparison.OrdinalIgnoreCase) || model.Contains("LaserJet", StringComparison.OrdinalIgnoreCase) || name.Contains("HP", StringComparison.OrdinalIgnoreCase))
        {
            if (_adapters.TryGetValue("HpEnterprise", out var hpAdapter)) return hpAdapter;
        }

        if (man.Contains("Kyocera", StringComparison.OrdinalIgnoreCase) || model.Contains("TASKalfa", StringComparison.OrdinalIgnoreCase) || name.Contains("Kyocera", StringComparison.OrdinalIgnoreCase))
        {
            if (_adapters.TryGetValue("KyoceraTaskAlfa", out var kyoAdapter)) return kyoAdapter;
        }

        if (!string.IsNullOrWhiteSpace(printer.ConnectionAddress) && (printer.ConnectionAddress.StartsWith("192.") || printer.ConnectionAddress.StartsWith("10.") || printer.ConnectionAddress.StartsWith("172.")))
        {
            if (_adapters.TryGetValue("NetworkSnmp", out var snmpAdapter)) return snmpAdapter;
        }

        return _fallbackAdapter;
    }
}
