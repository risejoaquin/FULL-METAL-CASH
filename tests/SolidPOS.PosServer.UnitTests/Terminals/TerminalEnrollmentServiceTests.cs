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
