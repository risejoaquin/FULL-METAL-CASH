using SolidPOS.PosCore.Application.Branding;
using SolidPOS.PosCore.Domain;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class TenantBrandingPackageServiceTests
{
    [Fact]
    public void Create_builds_valid_branding_package()
    {
        var service = new TenantBrandingPackageService(new InMemoryBrandingPackageStore());
        var package = service.Create(
            Guid.Parse("0ce5bbd0-528b-4aee-9fe3-93df001a4fde"),
            "Mi Cafeteria",
            "Mi Cafeteria POS",
            "#20242A",
            "#2F80ED",
            "assets/logo.png",
            "Mi Cafeteria",
            "Gracias por su compra.",
            DateTimeOffset.Parse("2026-08-20T00:00:00Z"));

        var validation = service.Validate(package);

        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Equal("1.0", package.PackageVersion);
    }

    [Fact]
    public void Validate_rejects_invalid_colors()
    {
        var service = new TenantBrandingPackageService(new InMemoryBrandingPackageStore());
        var package = new TenantBrandingPackage(Guid.NewGuid(), "Tenant", "App", "blue", "#FFFFFF", string.Empty, "Header", string.Empty, "1.0", DateTimeOffset.UtcNow);

        var validation = service.Validate(package);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, x => x.Contains("primaryColorHex", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Save_and_load_roundtrips_valid_package()
    {
        var store = new InMemoryBrandingPackageStore();
        var service = new TenantBrandingPackageService(store);
        var package = service.Create(Guid.NewGuid(), "Tenant", "Tenant POS", "#111111", "#222222", string.Empty, "Tenant", "Gracias", DateTimeOffset.UtcNow);

        await service.SaveValidatedAsync(package, "branding.json");
        var loaded = await service.LoadValidatedAsync("branding.json");

        Assert.Equal(package.TenantId, loaded.TenantId);
        Assert.Equal(package.AppName, loaded.AppName);
    }

    private sealed class InMemoryBrandingPackageStore : ITenantBrandingPackageStore
    {
        private readonly Dictionary<string, TenantBrandingPackage> packages = new();

        public Task SaveAsync(TenantBrandingPackage package, string path, CancellationToken cancellationToken = default)
        {
            packages[path] = package;
            return Task.CompletedTask;
        }

        public Task<TenantBrandingPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(packages[path]);
        }
    }
}
