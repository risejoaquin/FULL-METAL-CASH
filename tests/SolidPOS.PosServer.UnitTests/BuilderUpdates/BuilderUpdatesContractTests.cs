using System.Text.Json;
using SolidPOS.PosServer.Contracts.BuilderUpdates;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.BuilderUpdates;

public sealed class BuilderUpdatesContractTests
{
    [Fact]
    public void Builder_project_response_preserves_branding_snapshot()
    {
        Guid tenantId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        JsonElement config = JsonDocument.Parse("""{"posCore":"1.0.0","offline":true}""").RootElement.Clone();
        JsonElement branding = JsonDocument.Parse("""{"name":"Demo Cafe","primaryColor":"#111827"}""").RootElement.Clone();

        var response = new BuilderProjectResponse(
            projectId,
            tenantId,
            "Demo Cafe POS",
            null,
            "velopack",
            config,
            branding,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(projectId, response.Id);
        Assert.Equal("velopack", response.PackageType);
        Assert.Equal("Demo Cafe", response.Branding.GetProperty("name").GetString());
    }

    [Fact]
    public void Update_check_response_exposes_preserve_local_branding_policy()
    {
        var release = new UpdateReleaseResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "1.2.0",
            "stable",
            "velopack",
            "https://updates.solidpos.local/solidpos-1.2.0.nupkg",
            "sha256-demo",
            "signature-demo",
            "1.1.0",
            false,
            true,
            DateTimeOffset.UtcNow,
            null);

        var response = new UpdateCheckResponse(
            true,
            "1.1.0",
            "stable",
            "velopack",
            release,
            "update_available",
            "preserve_local_branding");

        Assert.True(response.UpdateAvailable);
        Assert.Equal("preserve_local_branding", response.BrandingPolicy);
        Assert.Same(release, response.Release);
    }
}
