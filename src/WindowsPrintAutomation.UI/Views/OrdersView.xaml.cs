using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.Core.Domain.Entities;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class OrdersView : UserControl
{
    public OrdersView()
    {
        InitializeComponent();
    }

    private async void BtnNewOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrdersViewModel vm)
        {
            await vm.PrepareNewOrderAsync();

            // Load available books for the combo
            var parent = Application.Current.MainWindow?.DataContext as MainViewModel;
            if (parent != null)
            {
                await parent.LibraryVM.LoadBooksAsync();
                CmbAvailableBooks.ItemsSource = parent.LibraryVM.Books;
                if (parent.LibraryVM.Books.Count > 0)
                {
                    CmbAvailableBooks.SelectedIndex = 0;
                }
            }

            NewOrderModal.Visibility = Visibility.Visible;
        }
    }

    private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
    {
        NewOrderModal.Visibility = Visibility.Collapsed;
    }

    private void BtnAddBookToDraft_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrdersViewModel vm && CmbAvailableBooks.SelectedItem is Book selectedBook)
        {
            vm.AddBookToDraft(selectedBook);
        }
    }

    private void BtnRemoveDraftItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDraft draft && DataContext is OrdersViewModel vm)
        {
            vm.RemoveDraftItem(draft);
        }
    }

    private async void BtnConfirmOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrdersViewModel vm)
        {
            try
            {
                var created = await vm.SubmitOrderAsync();
                NewOrderModal.Visibility = Visibility.Collapsed;
                MessageBox.Show($"تم إنشاء وتأكيد الطلب #{created.OrderNumber} بنجاح، وإدراج مهام الطباعة في الطابور.", "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"فشل إنشاء الطلب: {ex.Message}", "خطأ في التأكيد", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
