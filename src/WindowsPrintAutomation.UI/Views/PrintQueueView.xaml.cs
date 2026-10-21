using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class PrintQueueView : UserControl
{
    public PrintQueueView()
    {
        InitializeComponent();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrintQueueViewModel vm)
        {
            await vm.LoadJobsAsync();
        }
    }

    private void BtnVerify_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PrintJob job)
        {
            var parent = Application.Current.MainWindow?.DataContext as MainViewModel;
            if (parent != null)
            {
                parent.RecoveryVM.SelectedJob = job;
                parent.NavigateTo("Recovery");
            }
        }
    }

    private async void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PrintJob job && DataContext is PrintQueueViewModel vm)
        {
            if (MessageBox.Show($"هل أنت متأكد من رغبتك في إلغاء المهمة #{job.JobNumber}؟", "تأكيد الإلغاء", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await vm.CancelJobAsync(job);
            }
        }
    }
}
