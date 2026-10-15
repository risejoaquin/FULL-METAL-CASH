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
        if (DataContext is AppShellViewModel shell)
        {
            shell.ProductionCart?.SessionChanged();
            shell.ProductionCheckout?.SessionChanged();
            if (shell.ProductionLogin?.ActiveSession is not null && shell.ProductionCheckout is { } checkout)
                await checkout.RefreshShiftAsync();
        }
    }

    private async void ProductionAddSku_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCart: { } cart })
            await cart.AddBySkuAsync();
    }

    private void ProductionClearCart_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCart: { } cart } && cart.CanEdit)
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
            shell.ProductionCart?.SessionChanged();
            shell.ProductionCheckout?.SessionChanged();
        }
    }

    private async void ProductionResolveUncommitted_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCheckout: { } checkout })
            await checkout.ResolveUncommittedAsync();
    }

    private async void ProductionVerifyRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCheckout: { } checkout })
            await checkout.VerifyRecoveryAsync();
    }

    private async void ProductionOpenShift_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCheckout: { } checkout })
            await checkout.OpenShiftAsync();
    }

    private async void ProductionCheckout_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCheckout: { } checkout })
            await checkout.CheckoutCashAsync();
    }

    private async void ProductionCloseShift_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppShellViewModel { ProductionCheckout: { } checkout })
            await checkout.CloseShiftAsync();
    }
}
