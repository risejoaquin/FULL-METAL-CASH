using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class BrandingViewModel : ViewModelBase
{
    private string tenantName = "SolidPOS Demo";
    private string appName = "SolidPOS PosCore";
    private string primaryColorHex = "#20242A";
    private string accentColorHex = "#2F80ED";
    private string logoPath = string.Empty;
    private string receiptHeader = "SolidPOS";
    private string receiptFooter = "Gracias por su compra.";
    private string status = "Branding default cargado.";

    public string TenantName { get => tenantName; set => SetProperty(ref tenantName, value); }
    public string AppName { get => appName; set => SetProperty(ref appName, value); }
    public string PrimaryColorHex { get => primaryColorHex; set => SetProperty(ref primaryColorHex, value); }
    public string AccentColorHex { get => accentColorHex; set => SetProperty(ref accentColorHex, value); }
    public string LogoPath { get => logoPath; set => SetProperty(ref logoPath, value); }
    public string ReceiptHeader { get => receiptHeader; set => SetProperty(ref receiptHeader, value); }
    public string ReceiptFooter { get => receiptFooter; set => SetProperty(ref receiptFooter, value); }
    public string Status { get => status; set => SetProperty(ref status, value); }

    public string HeaderSummary => $"{AppName} / {TenantName}";
    public string ReceiptSummary => $"{ReceiptHeader} | {ReceiptFooter}";

    public void Apply(TenantBrandingPackage package)
    {
        TenantName = package.TenantName;
        AppName = package.AppName;
        PrimaryColorHex = package.PrimaryColorHex;
        AccentColorHex = package.AccentColorHex;
        LogoPath = package.LogoPath;
        ReceiptHeader = package.ReceiptHeader;
        ReceiptFooter = package.ReceiptFooter;
        Status = $"Branding package aplicado. version={package.PackageVersion}";
        RaisePropertyChanged(nameof(HeaderSummary));
        RaisePropertyChanged(nameof(ReceiptSummary));
    }
}
