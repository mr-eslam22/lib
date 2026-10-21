using System;
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
            NewOrderModal.Visibility = Visibility.Visible;
        }
    }

    private void BtnCloseModal_Click(object sender, RoutedEventArgs e)
    {
        NewOrderModal.Visibility = Visibility.Collapsed;
    }

    private void BtnAddBookItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Book book && DataContext is OrdersViewModel vm)
        {
            vm.AddBookToDraft(book);
        }
    }

    private void BtnAddAllFiltered_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrdersViewModel vm)
        {
            vm.AddAllFilteredBooksToDraft();
        }
    }

    private void BtnIncrementCopies_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDraft draft && DataContext is OrdersViewModel vm)
        {
            draft.IncrementCopies();
            vm.NotifyDraftTotals();
        }
    }

    private void BtnDecrementCopies_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OrderItemDraft draft && DataContext is OrdersViewModel vm)
        {
            draft.DecrementCopies();
            vm.NotifyDraftTotals();
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

    private async void BtnDeleteOrder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Order order && DataContext is OrdersViewModel vm)
        {
            var result = MessageBox.Show(
                $"هل أنت متأكد من رغبتك في حذف هذا الطلب نهائياً؟\n\nرقم الطلب: {order.OrderNumber}\nاسم العميل: {order.CustomerName}\nعدد الكتب: {order.TotalItemsCount}\n\nتنبيه: سيتم مسح الطلب وجميع مهام الطباعة التابعة له من النظام.",
                "تأكيد حذف الطلب",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var deleted = await vm.DeleteOrderAsync(order);
                    if (deleted)
                    {
                        MessageBox.Show($"تم حذف الطلب {order.OrderNumber} بنجاح.", "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("تعذر حذف الطلب، قد يكون قد تم حذفه مسبقاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"حدث خطأ أثناء محاولة مسح الطلب: {ex.Message}", "خطأ في الحذف", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
