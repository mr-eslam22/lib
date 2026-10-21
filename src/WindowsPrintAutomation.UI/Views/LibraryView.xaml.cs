using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private async void BtnImportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel vm)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "اختر المجلد الرئيسي للكتب المدرسية",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                await vm.ImportFolderAsync(dialog.FolderName);
            }
        }
    }
}
