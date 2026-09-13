using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.Terminals;
using SolidPOS.PosServer.Infrastructure.Terminals;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Terminals;

public sealed class TerminalEnrollmentServiceTests
{
    [Fact]
    public async Task Register_writes_terminal_updated_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-15T23:00:00Z");

        Mock<ITerminalRepository> repository = new();
        repository
            .Setup(x => x.RegisterTerminalAsync("enrollment-token-hash", "Caja 01", "DEVICE-001", "0.1.0-dev", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticatedTerminal(terminalId, tenantId, storeId, "Caja 01", "active"));

        TerminalEnrollmentService service = CreateService(
            Mock.Of<ITenantContext>(),
            repository.Object,
            clockNow: now,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out Mock<ITokenService> tokenService);

        tokenService.Setup(x => x.HashToken("enrollment-token")).Returns("enrollment-token-hash");

        TerminalSessionResponse? result = await service.RegisterTerminalAsync(
            new RegisterTerminalRequest("enrollment-token", "Caja 01", "DEVICE-001", "0.1.0-dev"),
            CancellationToken.None);

        Assert.NotNull(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                storeId,
                "terminal.updated",
                terminalId,
                "update",
                1,
                It.IsAny<JsonElement>(),
                terminalId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Revoke_writes_terminal_updated_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITerminalRepository> repository = new();
        repository
            .Setup(x => x.RevokeTerminalAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.Parse("2026-08-15T23:05:00Z"),
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out _);

        bool result = await service.RevokeTerminalAsync(terminalId, CancellationToken.None);

        Assert.True(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "terminal.updated",
                terminalId,
                "update",
                1,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTerminal_returns_details_when_terminal_exists()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        TerminalDetailResponse expected = new(
            terminalId,
            tenantId,
            storeId,
            "Caja 01",
            "DEVICE-001",
            "active",
            "1.1.0",
            DateTimeOffset.UtcNow,
            null,
            null,
            new TerminalDeviceHealthDto("charging", 95, 50000000000, 100000000000, 4000000000, "X64", "Windows 11", true, DateTimeOffset.UtcNow),
            new TerminalRemoteConfigMetadata(60, true, "Information", 30, 1440, DateTimeOffset.UtcNow));

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.GetTerminalAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.UtcNow,
            out _,
            out _);

        TerminalDetailResponse? result = await service.GetTerminalAsync(terminalId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(terminalId, result.Id);
        Assert.Equal("active", result.Status);
        Assert.Equal("1.1.0", result.AppVersion);
        Assert.NotNull(result.DeviceHealth);
        Assert.Equal(95, result.DeviceHealth.BatteryLevelPercent);
        Assert.NotNull(result.RemoteConfig);
        Assert.Equal(60, result.RemoteConfig.HeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task GetTerminal_returns_null_when_tenant_context_is_missing()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            Mock.Of<ITerminalRepository>(),
            clockNow: DateTimeOffset.UtcNow,
            out _,
            out _);

        TerminalDetailResponse? result = await service.GetTerminalAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AssignStore_reassigns_store_and_writes_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid oldStoreId = Guid.NewGuid();
        Guid newStoreId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.StoreExistsAsync(tenantId, newStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repository.Setup(x => x.AssignTerminalStoreAsync(tenantId, terminalId, newStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        TerminalDetailResponse reassignedTerminal = new(
            terminalId,
            tenantId,
            newStoreId,
            "Caja 01",
            "DEVICE-001",
            "active",
            "1.1.0",
            DateTimeOffset.UtcNow);

        repository.Setup(x => x.GetTerminalAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reassignedTerminal);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.UtcNow,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out _);

        TerminalDetailResponse? result = await service.AssignStoreAsync(terminalId, newStoreId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(newStoreId, result.StoreId);

        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                newStoreId,
                "terminal.updated",
                terminalId,
                "update",
                1,
                It.IsAny<JsonElement>(),
                terminalId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AssignStore_rejects_cross_tenant_store_assignment()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        Guid foreignStoreId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.StoreExistsAsync(tenantId, foreignStoreId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Store does NOT exist in this tenant

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.UtcNow,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out _);

        TerminalDetailResponse? result = await service.AssignStoreAsync(terminalId, foreignStoreId, CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.AssignTerminalStoreAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        syncChangeWriter.Verify(x => x.AppendAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Disable_disables_terminal_and_writes_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.DisableTerminalAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.UtcNow,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out _);

        bool result = await service.DisableTerminalAsync(terminalId, CancellationToken.None);

        Assert.True(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "terminal.updated",
                terminalId,
                "update",
                1,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Enable_enables_disabled_terminal_and_writes_sync_change()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.EnableTerminalAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.UtcNow,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out _);

        bool result = await service.EnableTerminalAsync(terminalId, CancellationToken.None);

        Assert.True(result);
        syncChangeWriter.Verify(
            x => x.AppendAsync(
                tenantId,
                null,
                "terminal.updated",
                terminalId,
                "update",
                1,
                It.IsAny<JsonElement>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Enable_fails_when_terminal_is_permanently_revoked()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.EnableTerminalAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Revoked terminals cannot be re-enabled

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: DateTimeOffset.UtcNow,
            out Mock<ISyncChangeWriter> syncChangeWriter,
            out _);

        bool result = await service.EnableTerminalAsync(terminalId, CancellationToken.None);

        Assert.False(result);
        syncChangeWriter.Verify(x => x.AppendAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<JsonElement>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordHeartbeat_records_activity_health_and_returns_response()
    {
        Guid tenantId = Guid.NewGuid();
        Guid storeId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        TerminalRemoteConfigMetadata remoteConfig = new(60, true, "Information", 30, 1440, now);
        TerminalHeartbeatResponse expectedResponse = new(terminalId, tenantId, storeId, "active", now, remoteConfig);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.RecordHeartbeatAsync(
            tenantId,
            terminalId,
            "1.1.0",
            4,
            "cursor-123",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: now,
            out _,
            out _);

        TerminalHeartbeatRequest request = new(
            "1.1.0",
            4,
            0,
            "cursor-123",
            new TerminalDeviceHealthDto("ac", 100, 50000000, 100000000, 2000000, "X64", "Windows 11", true, now));

        TerminalHeartbeatResponse? result = await service.RecordHeartbeatAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(terminalId, result.TerminalId);
        Assert.Equal("active", result.Status);
        Assert.Equal(60, result.RemoteConfig.HeartbeatIntervalSeconds);
    }

    [Fact]
    public async Task GetDeviceHealth_returns_health_from_repository()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        TerminalDeviceHealthDto expected = new("charging", 80, 50000000, 100000000, 2000000, "X64", "Windows 11", true, now);
        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.GetDeviceHealthAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: now,
            out _,
            out _);

        TerminalDeviceHealthDto? result = await service.GetDeviceHealthAsync(terminalId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("charging", result.BatteryStatus);
        Assert.Equal(80, result.BatteryLevelPercent);
    }

    [Fact]
    public async Task RemoteConfig_get_and_update_lifecycle()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        Mock<ITerminalRepository> repository = new();
        TerminalRemoteConfigMetadata customConfig = new(120, false, "Debug", 60, 2880, now);

        repository.Setup(x => x.GetRemoteConfigAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(customConfig);
        repository.Setup(x => x.UpdateRemoteConfigAsync(tenantId, terminalId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        TerminalEnrollmentService service = CreateService(
            tenantContext.Object,
            repository.Object,
            clockNow: now,
            out _,
            out _);

        TerminalRemoteConfigMetadata? retrieved = await service.GetRemoteConfigAsync(terminalId, CancellationToken.None);
        Assert.NotNull(retrieved);
        Assert.Equal(120, retrieved.HeartbeatIntervalSeconds);
        Assert.Equal("Debug", retrieved.LogLevel);

        TerminalRemoteConfigMetadata? updated = await service.UpdateRemoteConfigAsync(terminalId, customConfig with { HeartbeatIntervalSeconds = 90 }, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(90, updated.HeartbeatIntervalSeconds);
    }

    private static TerminalEnrollmentService CreateService(
        ITenantContext tenantContext,
        ITerminalRepository repository,
        DateTimeOffset clockNow,
        out Mock<ISyncChangeWriter> syncChangeWriter,
        out Mock<ITokenService> tokenService)
    {
        tokenService = new Mock<ITokenService>();
        tokenService.Setup(x => x.CreateRefreshToken()).Returns("refresh-token");
        tokenService.Setup(x => x.HashToken("refresh-token")).Returns("refresh-token-hash");
        tokenService.Setup(x => x.HashToken("terminal-access-token")).Returns("terminal-access-token-hash");
        tokenService
            .Setup(x => x.CreateTerminalAccessToken(
                It.IsAny<AuthenticatedTerminal>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<DateTimeOffset>()))
            .Returns("terminal-access-token");

        syncChangeWriter = new Mock<ISyncChangeWriter>();

        Mock<IClock> clock = new();
        clock.SetupGet(x => x.UtcNow).Returns(clockNow);

        return new TerminalEnrollmentService(
            tenantContext,
            repository,
            tokenService.Object,
            syncChangeWriter.Object,
            clock.Object,
            Options.Create(new JwtOptions { TerminalAccessTokenDays = 7 }),
            Mock.Of<ILogger<TerminalEnrollmentService>>());
    }
}
