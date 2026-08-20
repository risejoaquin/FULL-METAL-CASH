using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Wpf.ViewModels;

namespace SolidPOS.PosCore.Wpf.Composition;

public static class PosCoreWpfCompositionRoot
{
    public static AppShellViewModel CreateShellViewModel(string databasePath, TenantBrandingPackage? brandingPackage = null)
    {
        var viewModel = new AppShellViewModel(databasePath);
        if (brandingPackage is not null)
        {
            viewModel.ApplyBranding(brandingPackage);
        }
        viewModel.InitializeForShell();
        return viewModel;
    }
}
