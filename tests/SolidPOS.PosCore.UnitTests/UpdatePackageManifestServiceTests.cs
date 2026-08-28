using SolidPOS.PosCore.Application.Updates;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class UpdatePackageManifestServiceTests
{
    [Fact]
    public async Task Create_from_package_file_builds_valid_manifest()
    {
        var tempPackage = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPackage, "SolidPOS package content");
            var service = new UpdatePackageManifestService(new InMemoryUpdatePackageManifestStore());

            var manifest = service.CreateFromPackageFile(
                Guid.Parse("0ce5bbd0-528b-4aee-9fe3-93df001a4fde"),
                "Mi Cafeteria",
                "Mi Cafeteria POS",
                "1.0.0",
                "stable",
                "local-poscore-package",
                tempPackage,
                "1.0.0",
                "1.0.0",
                "1.0",
                DateTimeOffset.Parse("2026-08-20T00:00:00Z"),
                "release notes");

            var validation = service.Validate(manifest, tempPackage);

            Assert.True(validation.IsValid);
            Assert.Empty(validation.Errors);
            Assert.Equal("stable", manifest.Channel);
            Assert.Equal(64, manifest.Sha256.Length);
            Assert.True(manifest.PackageSizeBytes > 0);
        }
        finally
        {
            if (File.Exists(tempPackage)) File.Delete(tempPackage);
        }
    }

    [Fact]
    public void Validate_rejects_wrong_sha256()
    {
        var tempPackage = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPackage, "SolidPOS package content");
            var manifest = new UpdatePackageManifest(
                Guid.NewGuid(),
                "Tenant",
                "Tenant POS",
                "1.0.0",
                "stable",
                "local-poscore-package",
                Path.GetFileName(tempPackage),
                new FileInfo(tempPackage).Length,
                new string('0', 64),
                "1.0.0",
                "1.0.0",
                "1.0",
                DateTimeOffset.UtcNow,
                string.Empty);
            var service = new UpdatePackageManifestService(new InMemoryUpdatePackageManifestStore());

            var validation = service.Validate(manifest, tempPackage);

            Assert.False(validation.IsValid);
            Assert.Contains(validation.Errors, x => x.Contains("sha256", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempPackage)) File.Delete(tempPackage);
        }
    }

    [Fact]
    public async Task Save_and_load_roundtrips_valid_manifest()
    {
        var tempPackage = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPackage, "SolidPOS package content");
            var store = new InMemoryUpdatePackageManifestStore();
            var service = new UpdatePackageManifestService(store);
            var manifest = service.CreateFromPackageFile(Guid.NewGuid(), "Tenant", "Tenant POS", "1.0.0", "dev", "local-poscore-package", tempPackage, "1.0.0", "1.0.0", "1.0", DateTimeOffset.UtcNow, string.Empty);

            await service.SaveValidatedAsync(manifest, "manifest.json", tempPackage);
            var loaded = await service.LoadValidatedAsync("manifest.json");

            Assert.Equal(manifest.Sha256, loaded.Sha256);
            Assert.Equal("dev", loaded.Channel);
        }
        finally
        {
            if (File.Exists(tempPackage)) File.Delete(tempPackage);
        }
    }

    private sealed class InMemoryUpdatePackageManifestStore : IUpdatePackageManifestStore
    {
        private readonly Dictionary<string, UpdatePackageManifest> manifests = new();

        public Task SaveAsync(UpdatePackageManifest manifest, string path, CancellationToken cancellationToken = default)
        {
            manifests[path] = manifest;
            return Task.CompletedTask;
        }

        public Task<UpdatePackageManifest> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(manifests[path]);
        }
    }
}
