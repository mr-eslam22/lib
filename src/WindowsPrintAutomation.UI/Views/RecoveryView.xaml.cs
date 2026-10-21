using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class RecoveryView : UserControl
{
    public RecoveryView()
    {
        InitializeComponent();
    }

    private async void BtnConfirmVerification_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryViewModel vm)
        {
            await vm.ConfirmVerificationAsync();
            var parent = Application.Current.MainWindow?.DataContext as MainViewModel;
            parent?.RefreshBadgesAsync();
        }
    }

    private async void BtnResumeContinuation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecoveryViewModel vm)
        {
            await vm.ResumeContinuationAsync();
            var parent = Application.Current.MainWindow?.DataContext as MainViewModel;
            parent?.RefreshBadgesAsync();
        }
    }
}
