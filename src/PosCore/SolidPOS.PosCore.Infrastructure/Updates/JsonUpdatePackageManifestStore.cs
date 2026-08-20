using System.Text.Json;
using SolidPOS.PosCore.Application.Updates;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Infrastructure.Updates;

public sealed class JsonUpdatePackageManifestStore : IUpdatePackageManifestStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveAsync(UpdatePackageManifest manifest, string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UpdatePackageManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Update package manifest not found.", path);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<UpdatePackageManifest>(stream, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Update package manifest JSON is empty or invalid.");
    }
}
