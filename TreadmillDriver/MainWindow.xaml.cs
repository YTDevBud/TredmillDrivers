using System.Windows;
using System.Windows.Input;
using TreadmillDriver.Models;
using TreadmillDriver.ViewModels;

namespace TreadmillDriver;

/// <summary>
/// Main application window code-behind.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var vm = new MainViewModel();
        vm.SetWindow(this);
        DataContext = vm;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SaveSettings();
            vm.Dispose();
        }
    }

    // ─── Custom Title Bar Handlers ───────────────────────────────────

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaxRestoreButton.Content = "□";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaxRestoreButton.Content = "❐";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ─── Output Mode Card Click Handlers ─────────────────────────────
    // (WPF Border doesn't support Command binding, so we use code-behind events)

    private void KeyboardMode_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedOutputMode = OutputMode.Keyboard;
    }

    private void XboxMode_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedOutputMode = OutputMode.XboxController;
    }

    private void VRMode_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedOutputMode = OutputMode.VRController;
    }
}
