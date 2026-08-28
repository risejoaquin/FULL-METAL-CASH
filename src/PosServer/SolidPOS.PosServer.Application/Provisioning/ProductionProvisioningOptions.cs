namespace SolidPOS.PosServer.Application.Provisioning;

public sealed class ProductionProvisioningOptions
{
    public const string SectionName = "Provisioning";

    public string? BootstrapKey { get; set; }

    public bool AllowProductionBootstrap { get; set; } = true;

    public bool DisableDemoUserByDefault { get; set; } = true;
}
