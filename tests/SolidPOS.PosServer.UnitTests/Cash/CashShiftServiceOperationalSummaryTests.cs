using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Contracts.Cash;
using SolidPOS.PosServer.Infrastructure.Cash;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Cash;

public sealed class CashShiftServiceOperationalSummaryTests
{
    [Fact]
    public async Task GetOperationalSummary_rejects_without_tenant_context()
    {
        Mock<ITenantContext> tenantContext = new();
        Mock<ICashShiftRepository> repository = new();
        CashShiftService service = CreateService(tenantContext.Object, repository.Object);

        CashShiftOperationalSummaryResponse? result = await service.GetOperationalSummaryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
        repository.Verify(x => x.GetOperationalSummaryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOperationalSummary_reads_summary_for_tenant_shift()
    {
        Guid tenantId = Guid.NewGuid();
        Guid shiftId = Guid.NewGuid();
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

        CashShiftOperationalSummaryResponse summary = new(
            shiftId,
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "closed",
            10000,
            15000,
            15000,
            0,
            6500,
            8200,
            0,
            0,
            500,
            200,
            1,
            2,
            0,
            3,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow);

        Mock<ICashShiftRepository> repository = new();
        repository
            .Setup(x => x.GetOperationalSummaryAsync(tenantId, shiftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        CashShiftService service = CreateService(tenantContext.Object, repository.Object);

        CashShiftOperationalSummaryResponse? result = await service.GetOperationalSummaryAsync(shiftId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(shiftId, result.ShiftId);
        Assert.Equal(6500, result.CashSalesCents);
        Assert.Equal(2, result.SalesCount);
    }

    private static CashShiftService CreateService(ITenantContext tenantContext, ICashShiftRepository repository)
    {
        Mock<IAuditEventWriter> audit = new();
        audit
            .Setup(x => x.AppendAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<JsonElement?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CashShiftService(
            tenantContext,
            repository,
            audit.Object,
            Mock.Of<ILogger<CashShiftService>>());
    }
}
