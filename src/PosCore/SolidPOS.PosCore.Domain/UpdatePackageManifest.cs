namespace SolidPOS.PosCore.Domain;

public sealed record UpdatePackageManifest(
    Guid TenantId,
    string TenantName,
    string AppName,
    string ReleaseVersion,
    string Channel,
    string PackageKind,
    string PackageFileName,
    long PackageSizeBytes,
    string Sha256,
    string MinimumPosCoreVersion,
    string MinimumPosBuilderVersion,
    string BrandingPackageVersion,
    DateTimeOffset GeneratedAtUtc,
    string Notes)
{
    public const string CurrentManifestVersion = "1.0";
}

public sealed record UpdatePackageValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
