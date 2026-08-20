using SolidPOS.PosBuilder.Wpf.ViewModels;
using SolidPOS.PosCore.Application.Branding;
using SolidPOS.PosCore.Infrastructure.Branding;

namespace SolidPOS.PosBuilder.Wpf.Composition;

public static class PosBuilderCompositionRoot
{
    public static BuilderShellViewModel CreateBuilderViewModel()
    {
        return new BuilderShellViewModel(new TenantBrandingPackageService(new JsonTenantBrandingPackageStore()));
    }
}
