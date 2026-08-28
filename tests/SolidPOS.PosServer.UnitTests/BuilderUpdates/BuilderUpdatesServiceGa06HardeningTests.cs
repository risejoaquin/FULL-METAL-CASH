using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.BuilderUpdates;
using SolidPOS.PosServer.Contracts.BuilderUpdates;
using SolidPOS.PosServer.Infrastructure.BuilderUpdates;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.BuilderUpdates;

public sealed class BuilderUpdatesServiceGa06HardeningTests
{
    [Fact]
    public async Task Create_release_reports_invalid_release_fields_instead_of_generic_null()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();
        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);

        var request = new CreateUpdateReleaseRequest(
            "1.0.0-rc.1", "stable", "velopack", "https://updates.solidpos.local/setup.exe",
            "hash", "signature", "0.9.0", false, true, false, new[] { Guid.NewGuid() });

        UpdateReleaseCreationConflictException error = await Assert.ThrowsAsync<UpdateReleaseCreationConflictException>(
            () => service.CreateReleaseAsync(request, CancellationToken.None));

        Assert.Equal("INVALID_RELEASE_REQUEST", error.ErrorCode);
        Assert.Contains("tenantScoped", error.ConflictingFields);
        repository.Verify(x => x.CreateReleaseAsync(It.IsAny<Guid>(), It.IsAny<CreateUpdateReleaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reconciliation_audits_created_only_when_repository_created_row_and_targeted_only_when_inserted()
    {
        Guid tenantId = Guid.NewGuid();
        Guid releaseId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();
        var release = new UpdateReleaseResponse(releaseId, tenantId, "1.0.0-rc.1", "stable", "velopack", "https://updates.solidpos.local/setup.exe", "hash", "signature", "0.9.0", false, true, DateTimeOffset.UtcNow, null);
        repository.Setup(x => x.CreateReleaseAsync(tenantId, It.IsAny<CreateUpdateReleaseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateReleaseWriteResult(release, false, 1));
        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);
        var request = new CreateUpdateReleaseRequest("1.0.0-rc.1", "stable", "velopack", release.ArtifactUrl, release.ArtifactHash, release.Signature, release.RollbackVersion, false, true, true, new[] { terminalId });

        UpdateReleaseResponse? response = await service.CreateReleaseAsync(request, CancellationToken.None);

        Assert.Equal(releaseId, response!.Id);
        audit.Verify(x => x.AppendAsync(tenantId, "updates.release.created", "update_release", releaseId, null, It.IsAny<System.Text.Json.JsonElement?>(), It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(x => x.AppendAsync(tenantId, "updates.release.reconciled", "update_release", releaseId, null, It.IsAny<System.Text.Json.JsonElement?>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.AppendAsync(tenantId, "updates.release.cohort.targeted", "update_release", releaseId, null, It.IsAny<System.Text.Json.JsonElement?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_check_fails_closed_when_candidate_is_not_newer_than_current_version()
    {
        Guid tenantId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();
        var oldRelease = new UpdateReleaseResponse(Guid.NewGuid(), tenantId, "1.0.0-rc.1", "stable", "velopack", "https://updates.solidpos.local/setup.exe", "hash", "signature", "0.9.0", false, true, DateTimeOffset.UtcNow, null);
        repository.Setup(x => x.CheckForUpdateAsync(tenantId, "1.1.0", "stable", "velopack", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResponse(true, "1.1.0", "stable", "velopack", oldRelease, "update_available", "preserve_local_branding"));
        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);

        UpdateCheckResponse? response = await service.CheckForUpdateAsync("1.1.0", "stable", "velopack", Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response!.UpdateAvailable);
        Assert.Null(response.Release);
        Assert.Equal("already_current_or_newer", response.Decision);
    }
    [Fact]
    public async Task Update_check_keeps_ga06_rc_available_for_older_expansion_version()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Mock<ITenantContext> tenant = new();
        tenant.SetupGet(x => x.TenantId).Returns(tenantId);
        Mock<IBuilderUpdatesRepository> repository = new();
        Mock<IAuditEventWriter> audit = new();
        Mock<ILogger<BuilderUpdatesService>> logger = new();
        var candidate = new UpdateReleaseResponse(Guid.NewGuid(), tenantId, "1.0.0-rc.1", "stable", "velopack", "https://updates.solidpos.local/setup.exe", "hash", "signature", "0.10.0-exp09.20260821193853", false, true, DateTimeOffset.UtcNow, null);
        repository.Setup(x => x.CheckForUpdateAsync(tenantId, "0.10.0-exp09.20260821193853", "stable", "velopack", terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCheckResponse(true, "0.10.0-exp09.20260821193853", "stable", "velopack", candidate, "update_available", "preserve_local_branding"));
        var service = new BuilderUpdatesService(tenant.Object, repository.Object, audit.Object, logger.Object);

        UpdateCheckResponse? response = await service.CheckForUpdateAsync("0.10.0-exp09.20260821193853", "stable", "velopack", terminalId, CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response!.UpdateAvailable);
        Assert.Equal(candidate.Id, response.Release!.Id);
    }

}
