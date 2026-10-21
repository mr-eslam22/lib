using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void BtnBackup_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CreateBackup();
        }
    }
}
