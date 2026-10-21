using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class ErrorLogsView : UserControl
{
    public ErrorLogsView()
    {
        InitializeComponent();
    }

    private async void BtnResolve_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ErrorLogsViewModel vm)
        {
            await vm.ResolveSelectedErrorAsync();
            var parent = Application.Current.MainWindow?.DataContext as MainViewModel;
            parent?.RefreshBadgesAsync();
        }
    }
}
