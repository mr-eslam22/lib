using System.Windows;
using System.Windows.Controls;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
    }

    private async void BtnSaveCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CustomersViewModel vm)
        {
            var name = TxtName.Text.Trim();
            var phone = TxtPhone.Text.Trim();
            var notes = TxtNotes.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("يرجى إدخال اسم العميل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                MessageBox.Show("يرجى إدخال رقم الهاتف.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await vm.AddCustomerAsync(name, phone, notes);
            TxtName.Text = string.Empty;
            TxtPhone.Text = string.Empty;
            TxtNotes.Text = string.Empty;
            MessageBox.Show("تم حفظ العميل بنجاح.", "تمت العملية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
