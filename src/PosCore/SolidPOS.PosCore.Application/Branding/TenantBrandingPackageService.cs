using System.Text.RegularExpressions;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.Branding;

public interface ITenantBrandingPackageStore
{
    Task SaveAsync(TenantBrandingPackage package, string path, CancellationToken cancellationToken = default);
    Task<TenantBrandingPackage> LoadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class TenantBrandingPackageService
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private readonly ITenantBrandingPackageStore store;

    public TenantBrandingPackageService(ITenantBrandingPackageStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public TenantBrandingPackage Create(
        Guid tenantId,
        string tenantName,
        string appName,
        string primaryColorHex,
        string accentColorHex,
        string logoPath,
        string receiptHeader,
        string receiptFooter,
        DateTimeOffset generatedAtUtc)
    {
        return new TenantBrandingPackage(
            tenantId,
            NormalizeRequired(tenantName, nameof(tenantName)),
            NormalizeRequired(appName, nameof(appName)),
            NormalizeRequired(primaryColorHex, nameof(primaryColorHex)),
            NormalizeRequired(accentColorHex, nameof(accentColorHex)),
            NormalizeOptional(logoPath),
            NormalizeRequired(receiptHeader, nameof(receiptHeader)),
            NormalizeOptional(receiptFooter),
            TenantBrandingPackage.CurrentPackageVersion,
            generatedAtUtc);
    }

    public TenantBrandingValidationResult Validate(TenantBrandingPackage package)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (package.TenantId == Guid.Empty) errors.Add("tenantId is required.");
        if (string.IsNullOrWhiteSpace(package.TenantName)) errors.Add("tenantName is required.");
        if (string.IsNullOrWhiteSpace(package.AppName)) errors.Add("appName is required.");
        if (!HexColor.IsMatch(package.PrimaryColorHex)) errors.Add("primaryColorHex must be #RRGGBB.");
        if (!HexColor.IsMatch(package.AccentColorHex)) errors.Add("accentColorHex must be #RRGGBB.");
        if (!string.Equals(package.PackageVersion, TenantBrandingPackage.CurrentPackageVersion, StringComparison.Ordinal)) errors.Add("packageVersion is not supported.");
        if (string.IsNullOrWhiteSpace(package.ReceiptHeader)) warnings.Add("receiptHeader is empty.");
        if (package.GeneratedAtUtc == default) errors.Add("generatedAtUtc is required.");

        return new TenantBrandingValidationResult(errors.Count == 0, errors, warnings);
    }

    public async Task SaveValidatedAsync(TenantBrandingPackage package, string path, CancellationToken cancellationToken = default)
    {
        var validation = Validate(package);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Branding package is invalid: " + string.Join("; ", validation.Errors));
        }

        await store.SaveAsync(package, path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TenantBrandingPackage> LoadValidatedAsync(string path, CancellationToken cancellationToken = default)
    {
        var package = await store.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        var validation = Validate(package);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Branding package is invalid: " + string.Join("; ", validation.Errors));
        }

        return package;
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        return value.Trim();
    }

    private static string NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
