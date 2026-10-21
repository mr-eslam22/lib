using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class PrintersView : UserControl
{
    public PrintersView()
    {
        InitializeComponent();
    }

    private async void BtnTogglePause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Printer printer && DataContext is PrintersViewModel vm)
        {
            await vm.TogglePausePrinterAsync(printer);
        }
    }
}
