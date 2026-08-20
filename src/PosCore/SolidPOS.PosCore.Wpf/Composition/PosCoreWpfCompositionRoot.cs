using SolidPOS.PosCore.Wpf.ViewModels;

namespace SolidPOS.PosCore.Wpf.Composition;

public static class PosCoreWpfCompositionRoot
{
    public static AppShellViewModel CreateShellViewModel(string databasePath)
    {
        var viewModel = new AppShellViewModel(databasePath);
        viewModel.InitializeForShell();
        return viewModel;
    }
}
