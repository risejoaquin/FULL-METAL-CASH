using System.Windows;
using SolidPOS.PosCore.Wpf.ViewModels;

namespace SolidPOS.PosCore.Wpf;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void ProductionLogin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AppShellViewModel { ProductionLogin: { } login }) return;
        string password = ProductionPassword.Password;
        ProductionPassword.Clear();
        await login.LoginAsync(password);
    }

    private async void ProductionAddSku_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCart: { } cart })
            await cart.AddBySkuAsync();
    }

    private void ProductionClearCart_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCart: { } cart })
            cart.Clear();
    }

    private async void ProductionLogout_Click(object sender, RoutedEventArgs e)
    {
        ProductionPassword.Clear();
        if (DataContext is AppShellViewModel shell)
        {
            shell.ProductionCart?.Clear();
            if (shell.ProductionLogin is { } login)
                await login.LogoutAsync();
        }
    }
}
