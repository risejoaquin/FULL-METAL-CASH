using System.Windows.Input;
using SolidPOS.PosCore.Application.Branding;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosBuilder.Wpf.ViewModels;

public sealed class BuilderShellViewModel : ViewModelBase
{
    private readonly TenantBrandingPackageService service;
    private string tenantId = "0ce5bbd0-528b-4aee-9fe3-93df001a4fde";
    private string tenantName = "Mi Cafeteria";
    private string appName = "Mi Cafeteria POS";
    private string primaryColorHex = "#20242A";
    private string accentColorHex = "#2F80ED";
    private string logoPath = "assets/logo-placeholder.png";
    private string receiptHeader = "Mi Cafeteria";
    private string receiptFooter = "Gracias por su compra.";
    private string outputPath = ".\\.runtime\\tenant-branding-package.json";
    private string status = "PosBuilder listo para generar paquete de branding.";
    private bool isValid;
    private int errorCount;
    private int warningCount;

    public BuilderShellViewModel(TenantBrandingPackageService service)
    {
        this.service = service;
        GeneratePackageCommand = new RelayCommand(GeneratePackage);
        ValidatePackageCommand = new RelayCommand(ValidatePackage);
    }

    public string TenantId { get => tenantId; set => SetProperty(ref tenantId, value); }
    public string TenantName { get => tenantName; set => SetProperty(ref tenantName, value); }
    public string AppName { get => appName; set => SetProperty(ref appName, value); }
    public string PrimaryColorHex { get => primaryColorHex; set => SetProperty(ref primaryColorHex, value); }
    public string AccentColorHex { get => accentColorHex; set => SetProperty(ref accentColorHex, value); }
    public string LogoPath { get => logoPath; set => SetProperty(ref logoPath, value); }
    public string ReceiptHeader { get => receiptHeader; set => SetProperty(ref receiptHeader, value); }
    public string ReceiptFooter { get => receiptFooter; set => SetProperty(ref receiptFooter, value); }
    public string OutputPath { get => outputPath; set => SetProperty(ref outputPath, value); }
    public string Status { get => status; set => SetProperty(ref status, value); }
    public bool IsValid { get => isValid; private set => SetProperty(ref isValid, value); }
    public int ErrorCount { get => errorCount; private set => SetProperty(ref errorCount, value); }
    public int WarningCount { get => warningCount; private set => SetProperty(ref warningCount, value); }

    public ICommand GeneratePackageCommand { get; }
    public ICommand ValidatePackageCommand { get; }

    public void GeneratePackage()
    {
        var package = service.Create(Guid.Parse(TenantId), TenantName, AppName, PrimaryColorHex, AccentColorHex, LogoPath, ReceiptHeader, ReceiptFooter, DateTimeOffset.UtcNow);
        service.SaveValidatedAsync(package, OutputPath).GetAwaiter().GetResult();
        Status = $"Branding package generado para {TenantName}.";
    }

    public void ValidatePackage()
    {
        var package = service.LoadValidatedAsync(OutputPath).GetAwaiter().GetResult();
        var validation = service.Validate(package);
        IsValid = validation.IsValid;
        ErrorCount = validation.Errors.Count;
        WarningCount = validation.Warnings.Count;
        Status = validation.IsValid ? $"Branding package valido para {package.AppName}." : "Branding package invalido.";
    }
}
