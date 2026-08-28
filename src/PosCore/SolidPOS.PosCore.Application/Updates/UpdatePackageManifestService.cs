using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Updates;

public interface IUpdatePackageManifestStore
{
    Task SaveAsync(UpdatePackageManifest manifest, string path, CancellationToken cancellationToken = default);
    Task<UpdatePackageManifest> LoadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class UpdatePackageManifestService
{
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedChannels = new(StringComparer.OrdinalIgnoreCase) { "stable", "dev" };
    private readonly IUpdatePackageManifestStore store;

    public UpdatePackageManifestService(IUpdatePackageManifestStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public UpdatePackageManifest CreateFromPackageFile(
        Guid tenantId,
        string tenantName,
        string appName,
        string releaseVersion,
        string channel,
        string packageKind,
        string packagePath,
        string minimumPosCoreVersion,
        string minimumPosBuilderVersion,
        string brandingPackageVersion,
        DateTimeOffset generatedAtUtc,
        string notes)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Update package file not found.", packagePath);

        var fileInfo = new FileInfo(packagePath);
        return new UpdatePackageManifest(
            tenantId,
            NormalizeRequired(tenantName, nameof(tenantName)),
            NormalizeRequired(appName, nameof(appName)),
            NormalizeRequired(releaseVersion, nameof(releaseVersion)),
            NormalizeChannel(channel),
            NormalizeRequired(packageKind, nameof(packageKind)),
            fileInfo.Name,
            fileInfo.Length,
            ComputeSha256(packagePath),
            NormalizeRequired(minimumPosCoreVersion, nameof(minimumPosCoreVersion)),
            NormalizeRequired(minimumPosBuilderVersion, nameof(minimumPosBuilderVersion)),
            NormalizeRequired(brandingPackageVersion, nameof(brandingPackageVersion)),
            generatedAtUtc,
            NormalizeOptional(notes));
    }

    public UpdatePackageValidationResult Validate(UpdatePackageManifest manifest, string? packagePath = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (manifest.TenantId == Guid.Empty) errors.Add("tenantId is required.");
        if (string.IsNullOrWhiteSpace(manifest.TenantName)) errors.Add("tenantName is required.");
        if (string.IsNullOrWhiteSpace(manifest.AppName)) errors.Add("appName is required.");
        if (string.IsNullOrWhiteSpace(manifest.ReleaseVersion)) errors.Add("releaseVersion is required.");
        if (!SupportedChannels.Contains(manifest.Channel)) errors.Add("channel must be stable or dev.");
        if (string.IsNullOrWhiteSpace(manifest.PackageKind)) errors.Add("packageKind is required.");
        if (string.IsNullOrWhiteSpace(manifest.PackageFileName)) errors.Add("packageFileName is required.");
        if (manifest.PackageSizeBytes <= 0) errors.Add("packageSizeBytes must be greater than zero.");
        if (!Sha256Hex.IsMatch(manifest.Sha256)) errors.Add("sha256 must be a 64 character hexadecimal string.");
        if (string.IsNullOrWhiteSpace(manifest.MinimumPosCoreVersion)) errors.Add("minimumPosCoreVersion is required.");
        if (string.IsNullOrWhiteSpace(manifest.MinimumPosBuilderVersion)) errors.Add("minimumPosBuilderVersion is required.");
        if (string.IsNullOrWhiteSpace(manifest.BrandingPackageVersion)) errors.Add("brandingPackageVersion is required.");
        if (manifest.GeneratedAtUtc == default) errors.Add("generatedAtUtc is required.");

        if (!string.IsNullOrWhiteSpace(packagePath))
        {
            if (!File.Exists(packagePath))
            {
                errors.Add("package file referenced for validation does not exist.");
            }
            else
            {
                var actualSha256 = ComputeSha256(packagePath);
                var actualSize = new FileInfo(packagePath).Length;
                if (!string.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add("package sha256 does not match manifest.");
                if (actualSize != manifest.PackageSizeBytes) errors.Add("package size does not match manifest.");
            }
        }
        else
        {
            warnings.Add("package file was not supplied for hash verification.");
        }

        return new UpdatePackageValidationResult(errors.Count == 0, errors, warnings);
    }

    public async Task SaveValidatedAsync(UpdatePackageManifest manifest, string manifestPath, string? packagePath = null, CancellationToken cancellationToken = default)
    {
        var validation = Validate(manifest, packagePath);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Update package manifest is invalid: " + string.Join("; ", validation.Errors));
        }

        await store.SaveAsync(manifest, manifestPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UpdatePackageManifest> LoadValidatedAsync(string manifestPath, string? packagePath = null, CancellationToken cancellationToken = default)
    {
        var manifest = await store.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var validation = Validate(manifest, packagePath);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Update package manifest is invalid: " + string.Join("; ", validation.Errors));
        }

        return manifest;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        return value.Trim();
    }

    private static string NormalizeChannel(string channel)
    {
        var normalized = NormalizeRequired(channel, nameof(channel)).ToLowerInvariant();
        if (!SupportedChannels.Contains(normalized)) throw new ArgumentException("channel must be stable or dev.", nameof(channel));
        return normalized;
    }

    private static string NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
