using System;
using System.Windows;
using System.Windows.Input;
using WindowsPrintAutomation.UI.ViewModels;

namespace WindowsPrintAutomation.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            TxtMaximizeIcon.Text = "🗖";
        }
        else
        {
            WindowState = WindowState.Maximized;
            TxtMaximizeIcon.Text = "🗗";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BtnMaximize_Click(sender, e);
        }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (TxtMaximizeIcon != null)
        {
            TxtMaximizeIcon.Text = WindowState == WindowState.Maximized ? "🗗" : "🗖";
        }
    }

    private void Nav_Dashboard_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Dashboard");
    private void Nav_Library_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Library");
    private void Nav_Customers_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Customers");
    private void Nav_Orders_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Orders");
    private void Nav_Printers_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Printers");
    private void Nav_Queue_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Queue");
    private void Nav_Recovery_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Recovery");
    private void Nav_ErrorLogs_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("ErrorLogs");
    private void Nav_Settings_Click(object sender, RoutedEventArgs e) => _viewModel.NavigateTo("Settings");
}
