using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.BuilderUpdates;
using SolidPOS.PosServer.Contracts.BuilderUpdates;
using SolidPOS.PosServer.Contracts.Terminals;
using SolidPOS.PosServer.Infrastructure.BuilderUpdates;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.BuilderUpdates;

public sealed class StagedRolloutChannelTests
{
    [Fact]
    public async Task Staged_rollout_returns_update_for_targeted_terminal()
    {
        Guid tenantId = Guid.NewGuid();
        Guid targetTerminalId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();

        var stagedRelease = new UpdateReleaseResponse(
            Guid.NewGuid(),
            tenantId,
            "1.2.0-beta.1",
            "beta",
            "velopack",
            "https://updates.solidpos.local/beta/solidpos-1.2.0-beta.1.nupkg",
            "sha256-dummy-hash",
            "sig-dummy-thumbprint",
            "1.1.0",
            false,
            true,
            DateTimeOffset.UtcNow,
            null);

        repository.Setup(x => x.CheckForUpdateAsync(tenantId, "1.1.0", "beta", "velopack", targetTerminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResponse(true, "1.1.0", "beta", "velopack", stagedRelease, "update_available", "preserve_local_branding"));

        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);

        var result = await service.CheckForUpdateAsync("1.1.0", "beta", "velopack", targetTerminalId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.UpdateAvailable);
        Assert.Equal("1.2.0-beta.1", result.Release!.Version);
        Assert.Equal("beta", result.Release.Channel);
    }

    [Fact]
    public async Task Staged_rollout_excludes_non_targeted_terminal()
    {
        Guid tenantId = Guid.NewGuid();
        Guid nonTargetTerminalId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();

        repository.Setup(x => x.CheckForUpdateAsync(tenantId, "1.1.0", "beta", "velopack", nonTargetTerminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResponse(false, "1.1.0", "beta", "velopack", null, "already_current_no_release_or_outside_cohort", "preserve_local_branding"));

        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);

        var result = await service.CheckForUpdateAsync("1.1.0", "beta", "velopack", nonTargetTerminalId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.UpdateAvailable);
        Assert.Null(result.Release);
        Assert.Equal("already_current_no_release_or_outside_cohort", result.Decision);
    }

    [Fact]
    public async Task Channel_isolation_ensures_stable_request_does_not_receive_beta_release()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();

        // Repository returns null because channel 'stable' has no newer release
        repository.Setup(x => x.CheckForUpdateAsync(tenantId, "1.1.0", "stable", "velopack", terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResponse(false, "1.1.0", "stable", "velopack", null, "already_current_no_release_or_outside_cohort", "preserve_local_branding"));

        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);

        var result = await service.CheckForUpdateAsync("1.1.0", "stable", "velopack", terminalId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.UpdateAvailable);
        Assert.Null(result.Release);
    }

    [Fact]
    public void TerminalDeviceHealthDto_with_UpdateHealthEvidenceDto_roundtrips_json()
    {
        var evidence = new UpdateHealthEvidenceDto(
            State: "applied",
            CurrentVersion: "1.1.0",
            TargetVersion: "1.1.0",
            Channel: "stable",
            PackageFileName: "solidpos-1.1.0.pkg",
            PackageSha256: new string('a', 64),
            IsSigned: true,
            SigningThumbprint: "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
            ErrorMessage: null,
            AttemptedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc: DateTimeOffset.UtcNow,
            RollbackVersion: null,
            RollbackReason: null);

        var health = new TerminalDeviceHealthDto(
            BatteryStatus: "charging",
            BatteryLevelPercent: 98,
            AvailableDiskSpaceBytes: 50000000000,
            TotalDiskSpaceBytes: 100000000000,
            MemoryUsageBytes: 4000000000,
            CpuArchitecture: "X64",
            OsVersion: "Windows 11 Pro",
            IsStorageHealthy: true,
            ReportedAtUtc: DateTimeOffset.UtcNow,
            UpdateHealth: evidence);

        var json = JsonSerializer.Serialize(health);
        var deserialized = JsonSerializer.Deserialize<TerminalDeviceHealthDto>(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.UpdateHealth);
        Assert.Equal("applied", deserialized.UpdateHealth!.State);
        Assert.Equal("1.1.0", deserialized.UpdateHealth.CurrentVersion);
        Assert.Equal("stable", deserialized.UpdateHealth.Channel);
        Assert.True(deserialized.UpdateHealth.IsSigned);
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", deserialized.UpdateHealth.SigningThumbprint);
    }
}
