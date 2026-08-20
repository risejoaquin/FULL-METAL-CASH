namespace SolidPOS.PosCore.Domain;

public sealed record TenantBrandingPackage(
    Guid TenantId,
    string TenantName,
    string AppName,
    string PrimaryColorHex,
    string AccentColorHex,
    string LogoPath,
    string ReceiptHeader,
    string ReceiptFooter,
    string PackageVersion,
    DateTimeOffset GeneratedAtUtc)
{
    public const string CurrentPackageVersion = "1.0";
}

public sealed record TenantBrandingValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
