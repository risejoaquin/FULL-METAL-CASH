using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Contracts.Cash;
using SolidPOS.PosServer.Infrastructure.Cash;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Cash;

public sealed class CashShiftServiceTests
{
    [Fact]
    public async Task Open_rejects_request_without_terminal_runtime_context()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ICashShiftRepository> repository = new();
        CashShiftService service = CreateService(tenantContext.Object, repository.Object);

        CashShiftResponse? result = await service.OpenAsync(new OpenCashShiftRequest(null, null, Guid.NewGuid(), 10000), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.OpenAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Create_movement_rejects_unknown_movement_type()
    {
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        Mock<ICashShiftRepository> repository = new();
        CashShiftService service = CreateService(tenantContext.Object, repository.Object);

        CashMovementResponse? result = await service.CreateMovementAsync(
            Guid.NewGuid(),
            new CreateCashMovementRequest("invalid", 1000, "test", Guid.NewGuid(), null),
            CancellationToken.None);

        Assert.Null(result);
        repository.Verify(
            x => x.CreateMovementAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static CashShiftService CreateService(ITenantContext tenantContext, ICashShiftRepository repository)
    {
        Mock<ILogger<CashShiftService>> logger = new();
        Mock<IAuditEventWriter> auditEventWriter = CreateAuditEventWriter();
        return new CashShiftService(tenantContext, repository, auditEventWriter.Object, logger.Object);
    }

    private static Mock<IAuditEventWriter> CreateAuditEventWriter()
    {
        Mock<IAuditEventWriter> auditEventWriter = new();
        auditEventWriter
            .Setup(x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<System.Text.Json.JsonElement?>(),
                It.IsAny<System.Text.Json.JsonElement?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return auditEventWriter;
    }
}
