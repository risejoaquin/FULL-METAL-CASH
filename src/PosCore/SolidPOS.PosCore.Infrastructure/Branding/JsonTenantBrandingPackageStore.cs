using System.Text.Json;
using SolidPOS.PosCore.Application.Branding;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.Branding;

public sealed class JsonTenantBrandingPackageStore : ITenantBrandingPackageStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveAsync(TenantBrandingPackage package, string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, package, Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TenantBrandingPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Branding package not found.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TenantBrandingPackage>(stream, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Branding package JSON is empty or invalid.");
    }
}
