using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class PrintersView : UserControl
{
    public PrintersView()
    {
        InitializeComponent();
    }

    private async void BtnDiscoverPrinters_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrintersViewModel vm)
        {
            await vm.DiscoverPrintersAsync();
        }
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrintersViewModel vm)
        {
            await vm.RunConnectionAndCapabilitiesTestAsync();
        }
    }

    private async void BtnTogglePause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Printer printer && DataContext is PrintersViewModel vm)
        {
            await vm.TogglePausePrinterAsync(printer);
        }
    }

    private async void BtnToggleActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Printer printer && DataContext is PrintersViewModel vm)
        {
            await vm.ToggleActivePrinterAsync(printer);
        }
    }

    private async void BtnSavePrinter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrintersViewModel vm && vm.SelectedPrinter != null)
        {
            await vm.SavePrinterAsync(vm.SelectedPrinter);
            MessageBox.Show("تم حفظ إعدادات الماكينة بنجاح.", "حفظ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnAddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrintersViewModel vm && vm.SelectedPrinter != null)
        {
            // Add a default A4 Duplex profile
            await vm.AddProfileAsync(
                $"ملف مخصص - {DateTime.Now:yyyyMMdd_HHmm}",
                "ملازم مدرسية A4",
                PaperSizeOption.A4,
                PrintDuplexOption.DuplexLongEdge,
                PrintColorOption.Monochrome,
                "درج تلقائي");
            MessageBox.Show("تم إضافة ملف تعريف جديد بنجاح.", "إضافة ملف", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
